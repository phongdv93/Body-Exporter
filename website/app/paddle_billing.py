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
    }


def paddle_checkout_settings() -> dict[str, str]:
    env = (config.PADDLE_ENV or "sandbox").strip().lower()
    return {
        "client_token": (config.PADDLE_CLIENT_TOKEN or "").strip(),
        "price_id": (config.PADDLE_PRICE_ID or "").strip(),
        "environment": "sandbox" if env == "sandbox" else "production",
        "success_url": f"{config.SITE_URL}/buy/success",
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
