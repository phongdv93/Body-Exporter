"""Paddle Billing — checkout config + webhook → license email."""

from __future__ import annotations

import hashlib
import hmac
import json
import logging
import time
from typing import Any

import httpx
from sqlalchemy.orm import Session

import os

from app import config
from app.license_service import find_license_by_paddle_tx, issue_license_record

log = logging.getLogger("uvicorn.error")

_COMPLETED_EVENTS = frozenset(
    {
        "transaction.completed",
        "transaction.paid",
    }
)
_SIGNATURE_MAX_SKEW_SEC = 300


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
    ready = paddle_configured()
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
        "paddle_checkout_page": f"{config.SITE_URL.rstrip('/')}/buy/paddle",
        "checkout_service_reachable": checkout_svc_ok,
    }


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


def create_paddle_checkout_transaction(email: str) -> dict[str, str | None]:
    """Create Paddle transaction server-side."""
    api_key = (config.PADDLE_API_KEY or "").strip()
    price_id = (config.PADDLE_PRICE_ID or "").strip()
    if not api_key or not price_id:
        return {"transaction_id": None, "checkout_url": None, "error": "paddle_api_not_configured", "error_code": None}
    email = (email or "").strip()
    if not email or "@" not in email:
        return {"transaction_id": None, "checkout_url": None, "error": "invalid_email", "error_code": None}
    site = config.SITE_URL.rstrip("/")
    paddle_page = f"{site}/buy/paddle"
    body = {
        "items": [{"price_id": price_id, "quantity": 1}],
        "customer": {"email": email},
        "custom_data": {"buyer_email": email},
        "collection_mode": "automatic",
        # Dedicated page with inline Paddle checkout only (not /buy/success).
        "checkout": {"url": paddle_page},
    }
    try:
        r = httpx.post(
            f"{_paddle_api_base()}/transactions",
            headers={
                "Authorization": f"Bearer {api_key}",
                "Content-Type": "application/json",
            },
            json=body,
            timeout=20.0,
        )
        if not r.is_success:
            code = _paddle_error_code(r)
            log.warning("Paddle create transaction %s code=%s: %s", r.status_code, code, r.text[:500])
            return {
                "transaction_id": None,
                "checkout_url": None,
                "error": code or f"paddle_api_{r.status_code}",
                "error_code": code or None,
            }
        data = r.json().get("data") or {}
        txn_id = (data.get("id") or "").strip() or None
        checkout = data.get("checkout") or {}
        checkout_url = (checkout.get("url") or "").strip() or None
        if txn_id and not checkout_url:
            checkout_url = f"{paddle_page}?_ptxn={txn_id}"
        if txn_id:
            return {"transaction_id": txn_id, "checkout_url": checkout_url, "error": None, "error_code": None}
        return {"transaction_id": None, "checkout_url": checkout_url, "error": "no_transaction_id", "error_code": None}
    except Exception as ex:
        log.exception("Paddle create transaction failed")
        return {"transaction_id": None, "checkout_url": None, "error": str(ex)[:120], "error_code": None}


def paddle_checkout_settings(*, display_mode: str = "overlay") -> dict[str, str]:
    env = (config.PADDLE_ENV or "sandbox").strip().lower()
    mode = (display_mode or "overlay").strip().lower()
    if mode not in ("overlay", "inline"):
        mode = "overlay"
    return {
        "client_token": (config.PADDLE_CLIENT_TOKEN or "").strip(),
        "price_id": (config.PADDLE_PRICE_ID or "").strip(),
        "environment": "sandbox" if env == "sandbox" else "production",
        "display_mode": mode,
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
                return val
    customer = data.get("customer") or {}
    if isinstance(customer, dict):
        val = (customer.get("email") or "").strip()
        if val and "@" in val:
            return val
    details = data.get("details") or {}
    if isinstance(details, dict):
        for block in details.values():
            if isinstance(block, dict):
                val = (block.get("email") or "").strip()
                if val and "@" in val:
                    return val
    return ""


def _transaction_paid_ok(data: dict[str, Any], event_type: str) -> bool:
    status = (data.get("status") or "").strip().lower()
    if event_type == "transaction.completed":
        return status in ("", "completed", "paid", "billed")
    if event_type == "transaction.paid":
        return status in ("paid", "completed", "billed")
    return False


def handle_paddle_webhook(db: Session, payload: dict[str, Any]) -> dict[str, str]:
    event_type = (payload.get("event_type") or "").strip()
    if event_type not in _COMPLETED_EVENTS:
        return {"status": "ignored", "reason": event_type or "unknown"}

    data = payload.get("data") or {}
    if not isinstance(data, dict):
        return {"status": "ignored", "reason": "no_data"}

    if not _transaction_paid_ok(data, event_type):
        return {"status": "ignored", "reason": data.get("status") or "not_paid"}

    txn_id = (data.get("id") or "").strip()
    if not txn_id:
        return {"status": "ignored", "reason": "no_txn_id"}

    if find_license_by_paddle_tx(db, txn_id):
        return {"status": "ok", "reason": "duplicate"}

    email = _extract_email(data)
    if not email:
        log.warning("Paddle %s: no buyer email in custom_data", txn_id)
        return {"status": "ignored", "reason": "no_email"}

    try:
        issue_license_record(
            db,
            buyer_email=email,
            paddle_transaction_id=txn_id,
            notes="Paddle checkout",
            order_id_suffix=f"paddle-{txn_id}",
        )
        log.info("Paddle %s: license issued for %s", txn_id, email)
        return {"status": "ok"}
    except Exception:
        log.exception("Paddle webhook issue_license failed for %s", txn_id)
        return {"status": "error"}


def parse_webhook_json(raw_body: bytes) -> dict[str, Any]:
    return json.loads(raw_body.decode("utf-8"))
