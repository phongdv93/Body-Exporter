"""Paddle Billing — checkout config + webhook → license email."""

from __future__ import annotations

import hashlib
import hmac
import json
import logging
import time
from typing import Any
from urllib.parse import quote

import httpx
from sqlalchemy.orm import Session

import os

from app import config
from app.database import get_content
from app.license_service import find_license_by_paddle_tx, issue_license_record

log = logging.getLogger("uvicorn.error")

_COMPLETED_EVENTS = frozenset(
    {
        "transaction.completed",
        "transaction.paid",
    }
)
_SUBSCRIPTION_ACCESS_EVENTS = frozenset(
    {
        "subscription.created",
        "subscription.activated",
    }
)
_SUBSCRIPTION_ACCESS_STATUSES = frozenset({"trialing", "active"})
_SIGNATURE_MAX_SKEW_SEC = 300


def paddle_client_token_issue() -> str | None:
    """Return i18n error key if PADDLE_CLIENT_TOKEN format is wrong."""
    token = (config.PADDLE_CLIENT_TOKEN or "").strip()
    if not token:
        return "paddle_missing_client_token"
    if token.startswith("pdl_"):
        return "paddle_client_token_is_api_key"
    env = (config.PADDLE_ENV or "sandbox").strip().lower()
    if env == "production" and not token.startswith("live_"):
        return "paddle_client_token_wrong_env"
    if env == "sandbox" and not token.startswith("test_"):
        return "paddle_client_token_wrong_env"
    return None


def paddle_configured() -> bool:
    return bool(
        (config.PADDLE_CLIENT_TOKEN or "").strip()
        and (config.PADDLE_API_KEY or "").strip()
        and (config.PADDLE_PRICE_ID or "").strip()
        and (config.PADDLE_WEBHOOK_SECRET or "").strip()
    )


def _paddle_api_base() -> str:
    env = (config.PADDLE_ENV or "sandbox").strip().lower()
    if env == "production":
        return "https://api.paddle.com"
    return "https://sandbox-api.paddle.com"


def paddle_admin_status() -> dict[str, Any]:
    """Masked status for admin dashboard (no secrets)."""
    token = (config.PADDLE_CLIENT_TOKEN or "").strip()
    price_id = (config.PADDLE_PRICE_ID or "").strip()
    wh = bool((config.PADDLE_WEBHOOK_SECRET or "").strip())
    api_key = bool((config.PADDLE_API_KEY or "").strip())
    env = config.PADDLE_ENV
    env_invalid = (os.getenv("PADDLE_ENV") or "").strip().lower() not in ("", "sandbox", "production")
    token_env_mismatch = False
    if token:
        if token.startswith("live_") and env != "production":
            token_env_mismatch = True
        elif token.startswith("test_") and env != "sandbox":
            token_env_mismatch = True
    client_token_issue = paddle_client_token_issue()
    ready = paddle_configured() and not client_token_issue
    price_ok = None
    if ready and api_key and price_id:
        try:
            r = httpx.get(
                f"{_paddle_api_base()}/prices/{price_id}",
                headers={
                    "Authorization": f"Bearer {config.PADDLE_API_KEY}",
                    "Content-Type": "application/json",
                },
                timeout=12.0,
            )
            price_ok = r.is_success
        except Exception as ex:
            log.debug("Paddle price check failed: %s", ex)
            price_ok = False
    checkout_svc_ok = None
    try:
        r = httpx.get("https://checkout-service.paddle.com/", timeout=8.0, follow_redirects=True)
        checkout_svc_ok = r.status_code < 500
    except Exception:
        checkout_svc_ok = False
    return {
        "configured": ready,
        "environment": env,
        "has_client_token": bool(token),
        "client_token_prefix": (token[:8] + "…") if len(token) > 8 else "",
        "has_price_id": bool(price_id),
        "price_id": price_id[:20] + ("…" if len(price_id) > 20 else ""),
        "price_api_ok": price_ok,
        "has_webhook_secret": wh,
        "has_api_key": api_key,
        "webhook_url": f"{config.SITE_URL.rstrip('/')}/webhook/paddle",
        "env_invalid": env_invalid,
        "token_env_mismatch": token_env_mismatch,
        "client_token_issue": client_token_issue,
        "paddle_checkout_page": f"{config.SITE_URL.rstrip('/')}/buy/paddle",
        "checkout_service_reachable": checkout_svc_ok,
    }


def _paddle_headers(api_key: str) -> dict[str, str]:
    return {
        "Authorization": f"Bearer {api_key}",
        "Content-Type": "application/json",
    }


def _get_or_create_paddle_customer(api_key: str, email: str) -> str | None:
    """Return Paddle customer id (ctm_…) for checkout."""
    email = (email or "").strip()
    if not email or "@" not in email:
        return None
    base = _paddle_api_base()
    headers = _paddle_headers(api_key)
    try:
        r = httpx.get(
            f"{base}/customers",
            params={"email": email, "per_page": 1},
            headers=headers,
            timeout=15.0,
        )
        if r.is_success:
            for row in (r.json().get("data") or []):
                if isinstance(row, dict):
                    cid = (row.get("id") or "").strip()
                    if cid:
                        return cid
        r = httpx.post(
            f"{base}/customers",
            headers=headers,
            json={"email": email},
            timeout=15.0,
        )
        if r.is_success:
            return ((r.json().get("data") or {}).get("id") or "").strip() or None
        log.warning("Paddle create customer %s: %s", r.status_code, r.text[:300])
    except Exception as ex:
        log.debug("Paddle customer lookup failed: %s", ex)
    return None


def _paddle_error_code(response: httpx.Response) -> str:
    try:
        payload = response.json()
    except Exception:
        return ""
    err = payload.get("error")
    if isinstance(err, dict):
        return (err.get("code") or "").strip()
    for item in payload.get("errors") or []:
        if isinstance(item, dict) and item.get("code"):
            return str(item["code"]).strip()
    return ""


def paddle_customer_id_for_email(email: str) -> str | None:
    api_key = (config.PADDLE_API_KEY or "").strip()
    if not api_key:
        return None
    return _get_or_create_paddle_customer(api_key, email)


def paddle_customer_email_by_id(customer_id: str) -> str | None:
    """Webhook payloads often include only customer_id — fetch email from Paddle API."""
    cid = (customer_id or "").strip()
    api_key = (config.PADDLE_API_KEY or "").strip()
    if not cid or not api_key or not cid.startswith("ctm_"):
        return None
    try:
        r = httpx.get(
            f"{_paddle_api_base()}/customers/{cid}",
            headers=_paddle_headers(api_key),
            timeout=15.0,
        )
        if r.is_success:
            val = ((r.json().get("data") or {}).get("email") or "").strip()
            if val and "@" in val:
                return val.lower()
        else:
            log.warning("Paddle GET customer %s: %s", cid, r.status_code)
    except Exception as ex:
        log.debug("Paddle customer fetch failed: %s", ex)
    return None


def _clamp_license_years(years: int | str | None) -> int:
    try:
        y = int(years) if years is not None else 1
    except (TypeError, ValueError):
        y = 1
    return max(1, min(y, config.MAX_LICENSE_YEARS))


def create_paddle_checkout_transaction(
    email: str,
    *,
    currency_code: str | None = None,
    years: int | str | None = 1,
) -> dict[str, str | None]:
    """Create Paddle transaction server-side."""
    api_key = (config.PADDLE_API_KEY or "").strip()
    price_id = (config.PADDLE_PRICE_ID or "").strip()
    if not api_key or not price_id:
        return {
            "transaction_id": None,
            "checkout_url": None,
            "customer_id": None,
            "error": "paddle_api_not_configured",
            "error_code": None,
        }
    email = (email or "").strip()
    if not email or "@" not in email:
        return {
            "transaction_id": None,
            "checkout_url": None,
            "customer_id": None,
            "error": "invalid_email",
            "error_code": None,
        }
    customer_id = _get_or_create_paddle_customer(api_key, email)
    qty = _clamp_license_years(years)
    site = config.SITE_URL.rstrip("/")
    paddle_page = f"{site}/buy/paddle"
    body: dict[str, Any] = {
        "items": [{"price_id": price_id, "quantity": qty}],
        "custom_data": {"buyer_email": email, "license_years": str(qty)},
        "collection_mode": "automatic",
        "checkout": {"url": paddle_page},
    }
    if customer_id:
        body["customer_id"] = customer_id
    else:
        body["customer"] = {"email": email}
    cc = (currency_code or "").strip().upper()
    if cc in ("USD", "VND", "EUR", "GBP"):
        body["currency_code"] = cc
    try:
        r = httpx.post(
            f"{_paddle_api_base()}/transactions",
            headers=_paddle_headers(api_key),
            json=body,
            timeout=20.0,
        )
        if not r.is_success:
            code = _paddle_error_code(r)
            log.warning("Paddle create transaction %s code=%s: %s", r.status_code, code, r.text[:500])
            return {
                "transaction_id": None,
                "checkout_url": None,
                "customer_id": customer_id,
                "error": code or f"paddle_api_{r.status_code}",
                "error_code": code or None,
            }
        data = r.json().get("data") or {}
        txn_id = (data.get("id") or "").strip() or None
        checkout = data.get("checkout") or {}
        checkout_url = (checkout.get("url") or "").strip() or None
        if txn_id:
            # Internal page uses ?txn= (not ?_ptxn) so we control Checkout.open with customer info.
            checkout_url = f"{paddle_page}?txn={txn_id}&years={qty}"
            if email:
                checkout_url += f"&email={quote(email)}"
        if txn_id:
            return {
                "transaction_id": txn_id,
                "checkout_url": checkout_url,
                "customer_id": customer_id or (data.get("customer_id") or "").strip() or None,
                "error": None,
                "error_code": None,
            }
        return {
            "transaction_id": None,
            "checkout_url": checkout_url,
            "customer_id": customer_id,
            "error": "no_transaction_id",
            "error_code": None,
        }
    except Exception as ex:
        log.exception("Paddle create transaction failed")
        return {
            "transaction_id": None,
            "checkout_url": None,
            "customer_id": customer_id,
            "error": str(ex)[:120],
            "error_code": None,
        }


def paddle_checkout_settings(
    *,
    display_mode: str = "overlay",
    customer_id: str | None = None,
    customer_email: str | None = None,
) -> dict[str, str]:
    env = (config.PADDLE_ENV or "sandbox").strip().lower()
    mode = (display_mode or "overlay").strip().lower()
    if mode not in ("overlay", "inline"):
        mode = "overlay"
    return {
        "client_token": (config.PADDLE_CLIENT_TOKEN or "").strip(),
        "price_id": (config.PADDLE_PRICE_ID or "").strip(),
        "environment": "sandbox" if env == "sandbox" else "production",
        "display_mode": mode,
        "customer_id": (customer_id or "").strip(),
        "customer_email": (customer_email or "").strip(),
        "success_url": f"{config.SITE_URL.rstrip('/')}/buy/success",
        "buy_page_url": f"{config.SITE_URL.rstrip('/')}/buy",
        "paddle_page_url": f"{config.SITE_URL.rstrip('/')}/buy/paddle",
        "support_email": config.SUPPORT_EMAIL,
    }


def verify_paddle_signature(raw_body: bytes, signature_header: str, secret: str) -> bool:
    if not secret or not signature_header:
        return False
    parts: dict[str, str] = {}
    for piece in signature_header.split(";"):
        if "=" in piece:
            k, v = piece.split("=", 1)
            parts[k.strip()] = v.strip()
    ts = parts.get("ts")
    h1 = parts.get("h1")
    if not ts or not h1:
        return False
    try:
        ts_int = int(ts)
        if abs(time.time() - ts_int) > _SIGNATURE_MAX_SKEW_SEC:
            log.warning("Paddle webhook: timestamp outside allowed window")
            return False
    except ValueError:
        return False
    try:
        payload = f"{ts}:{raw_body.decode('utf-8')}"
    except UnicodeDecodeError:
        return False
    expected = hmac.new(secret.encode("utf-8"), payload.encode("utf-8"), hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, h1)


def _extract_email(data: dict[str, Any]) -> str:
    custom = data.get("custom_data") or data.get("customData") or {}
    if isinstance(custom, dict):
        for key in ("buyer_email", "email", "customer_email"):
            val = (custom.get(key) or "").strip()
            if val and "@" in val:
                return val.lower()
    customer = data.get("customer") or {}
    if isinstance(customer, dict):
        val = (customer.get("email") or "").strip()
        if val and "@" in val:
            return val.lower()
        cid = (customer.get("id") or "").strip()
        if cid:
            fetched = paddle_customer_email_by_id(cid)
            if fetched:
                return fetched
    details = data.get("details") or {}
    if isinstance(details, dict):
        for block in details.values():
            if isinstance(block, dict):
                val = (block.get("email") or "").strip()
                if val and "@" in val:
                    return val.lower()
    cid = (data.get("customer_id") or "").strip()
    if cid:
        fetched = paddle_customer_email_by_id(cid)
        if fetched:
            return fetched
    return ""


def _license_years_from_transaction(data: dict[str, Any]) -> int:
    custom = data.get("custom_data") or data.get("customData") or {}
    if isinstance(custom, dict):
        raw = (custom.get("license_years") or custom.get("years") or "").strip()
        if raw.isdigit():
            return _clamp_license_years(int(raw))
    qty = 0
    for item in data.get("items") or []:
        if isinstance(item, dict):
            try:
                qty = max(qty, int(item.get("quantity") or 0))
            except (TypeError, ValueError):
                pass
    if qty > 0:
        return _clamp_license_years(qty)
    return 1


def _transaction_paid_ok(data: dict[str, Any], event_type: str) -> bool:
    status = (data.get("status") or "").strip().lower()
    if event_type == "transaction.completed":
        return status in ("", "completed", "paid", "billed", "ready")
    if event_type == "transaction.paid":
        return status in ("paid", "completed", "billed")
    return False


def _subscription_dedup_id(data: dict[str, Any]) -> str:
    sub_id = (data.get("id") or data.get("subscription_id") or "").strip()
    if sub_id.startswith("sub_"):
        return sub_id
    return ""


def _issue_paddle_license(
    db: Session,
    *,
    dedup_id: str,
    email: str,
    years: int,
    notes: str,
) -> dict[str, str]:
    if find_license_by_paddle_tx(db, dedup_id):
        return {"status": "ok", "reason": "duplicate"}
    content = get_content(db)
    term_days = max(1, int(content.license_term_days or config.SEPAY_LICENSE_DAYS)) * years
    try:
        issue_license_record(
            db,
            buyer_email=email,
            days=term_days,
            paddle_transaction_id=dedup_id,
            notes=notes,
            order_id_suffix=f"paddle-{dedup_id}",
            email_lang="en",
        )
        log.info("Paddle %s: license issued for %s", dedup_id, email)
        return {"status": "ok"}
    except ValueError as ex:
        log.error("Paddle %s: %s", dedup_id, ex)
        return {"status": "error", "reason": str(ex)[:200]}
    except Exception:
        log.exception("Paddle webhook issue_license failed for %s", dedup_id)
        return {"status": "error"}


def _handle_subscription_webhook(db: Session, data: dict[str, Any]) -> dict[str, str]:
    status = (data.get("status") or "").strip().lower()
    if status not in _SUBSCRIPTION_ACCESS_STATUSES:
        return {"status": "ignored", "reason": status or "subscription_not_active"}

    dedup_id = _subscription_dedup_id(data)
    if not dedup_id:
        return {"status": "ignored", "reason": "no_subscription_id"}

    email = _extract_email(data)
    if not email:
        log.warning("Paddle %s: no customer email (subscription)", dedup_id)
        return {"status": "ignored", "reason": "no_email"}

    years = _license_years_from_transaction(data)
    return _issue_paddle_license(
        db,
        dedup_id=dedup_id,
        email=email,
        years=years,
        notes=f"Paddle subscription ({years}y, {status})",
    )


def _handle_transaction_webhook(db: Session, data: dict[str, Any], event_type: str) -> dict[str, str]:
    if not _transaction_paid_ok(data, event_type):
        return {"status": "ignored", "reason": data.get("status") or "not_paid"}

    txn_id = (data.get("id") or "").strip()
    if not txn_id:
        return {"status": "ignored", "reason": "no_txn_id"}

    sub_dedup = _subscription_dedup_id({"subscription_id": data.get("subscription_id")})
    if sub_dedup and find_license_by_paddle_tx(db, sub_dedup):
        return {"status": "ok", "reason": "duplicate_subscription"}

    email = _extract_email(data)
    if not email:
        log.warning("Paddle %s: no buyer email (transaction)", txn_id)
        return {"status": "ignored", "reason": "no_email"}

    years = _license_years_from_transaction(data)
    return _issue_paddle_license(
        db,
        dedup_id=txn_id,
        email=email,
        years=years,
        notes=f"Paddle checkout ({years}y)",
    )


def handle_paddle_webhook(db: Session, payload: dict[str, Any]) -> dict[str, str]:
    event_type = (payload.get("event_type") or "").strip()
    data = payload.get("data") or {}
    if not isinstance(data, dict):
        return {"status": "ignored", "reason": "no_data"}

    if event_type in _SUBSCRIPTION_ACCESS_EVENTS:
        return _handle_subscription_webhook(db, data)
    if event_type in _COMPLETED_EVENTS:
        return _handle_transaction_webhook(db, data, event_type)
    return {"status": "ignored", "reason": event_type or "unknown"}


def parse_webhook_json(raw_body: bytes) -> dict[str, Any]:
    return json.loads(raw_body.decode("utf-8"))
