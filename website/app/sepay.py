"""SePay QR URL helpers (matches SolidWorks Body Exporter SepayQrHelper)."""

from __future__ import annotations

import base64
import hashlib
import hmac
import time
from urllib.parse import parse_qsl, quote, urlencode, urlparse, urlunparse


def build_transfer_memo(email: str) -> str:
    trimmed = (email or "").strip()
    if not trimmed:
        return "Body Export License"
    return f"BE {trimmed}"


def build_qr_image_url(base_url: str, email: str) -> str:
    if not base_url or not base_url.strip():
        return ""
    parsed = urlparse(base_url.strip())
    query = dict(parse_qsl(parsed.query, keep_blank_values=True))
    query["des"] = build_transfer_memo(email)
    new_query = urlencode(query, quote_via=quote)
    return urlunparse(parsed._replace(query=new_query))


def parse_qr_base(base_url: str) -> dict:
    parsed = urlparse(base_url.strip())
    q = dict(parse_qsl(parsed.query, keep_blank_values=True))
    amount_raw = q.get("amount", "")
    try:
        amount = int(amount_raw) if amount_raw else None
    except ValueError:
        amount = None
    return {
        "bank": q.get("bank", ""),
        "account": q.get("acc", ""),
        "amount_vnd": amount,
        "description": q.get("des", ""),
    }


def pg_credentials_for_content(content) -> tuple[str, str, str]:
    """Merchant id, secret key, env — site row overrides env."""
    from app import config

    mid = (getattr(content, "sepay_pg_merchant_id", None) or "").strip() or config.SEPAY_PG_MERCHANT_ID
    sk = (getattr(content, "sepay_pg_secret_key", None) or "").strip() or config.SEPAY_PG_SECRET_KEY
    envname = (getattr(content, "sepay_pg_env", None) or "").strip().lower() or config.SEPAY_PG_ENV
    return mid, sk, envname


def pg_checkout_available_for_content(content) -> bool:
    mid, sk, _ = pg_credentials_for_content(content)
    return bool(mid and sk)


def pg_checkout_available() -> bool:
    from app import config

    return bool(config.SEPAY_PG_MERCHANT_ID and config.SEPAY_PG_SECRET_KEY)


def pg_checkout_init_url_for_env(env_name: str) -> str:
    if (env_name or "").lower() == "production":
        return "https://pay.sepay.vn/v1/checkout/init"
    return "https://pay-sandbox.sepay.vn/v1/checkout/init"


def pg_checkout_init_url() -> str:
    from app import config

    return pg_checkout_init_url_for_env(config.SEPAY_PG_ENV)


def sign_checkout_fields(fields: dict[str, str], secret_key: str) -> str:
    allowed = [
        "merchant",
        "operation",
        "payment_method",
        "order_amount",
        "currency",
        "order_invoice_number",
        "order_description",
        "customer_id",
        "success_url",
        "error_url",
        "cancel_url",
    ]
    parts = []
    for key in allowed:
        if key in fields and fields[key] is not None:
            parts.append(f"{key}={fields[key]}")
    signed_string = ",".join(parts)
    digest = hmac.new(
        secret_key.encode("utf-8"),
        signed_string.encode("utf-8"),
        hashlib.sha256,
    ).digest()
    return base64.b64encode(digest).decode("ascii")


def build_pg_checkout_fields(
    *,
    merchant_id: str,
    secret_key: str,
    email: str,
    amount_vnd: int,
    invoice: str,
    description: str,
    success_url: str,
    error_url: str,
    cancel_url: str,
) -> dict[str, str]:
    fields = {
        "merchant": merchant_id,
        "currency": "VND",
        "order_amount": str(amount_vnd),
        "operation": "PURCHASE",
        "order_description": description,
        "order_invoice_number": invoice,
        "customer_id": email.strip() or "guest",
        "success_url": success_url,
        "error_url": error_url,
        "cancel_url": cancel_url,
    }
    fields["signature"] = sign_checkout_fields(fields, secret_key)
    return fields


def new_invoice_number() -> str:
    return f"BE-{int(time.time())}"
