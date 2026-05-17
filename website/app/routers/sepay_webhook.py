"""SePay incoming-transfer webhook (bank QR) — same rules as Cloudflare Worker."""

from __future__ import annotations

import hashlib
import hmac
import json
import logging
import re
from typing import Any

from fastapi import APIRouter, Request, Response
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app import config
from app.database import SessionLocal, get_content
from app.email_notify import send_license_key_email
from app.license_service import find_license_by_sepay_tx, issue_license_record
from app.sepay import parse_qr_base

log = logging.getLogger("uvicorn.error")

router = APIRouter(tags=["webhook"])


def _timing_safe_eq(a: str, b: str) -> bool:
    try:
        return hmac.compare_digest(a.encode("utf-8"), b.encode("utf-8"))
    except Exception:
        return False


def _verify_sepay_api_key_header(authorization: str, api_key: str) -> bool:
    auth = (authorization or "").strip()
    if not auth.lower().startswith("apikey "):
        return False
    got = auth[7:].strip()
    return _timing_safe_eq(got, api_key)


def _verify_sepay_hmac(secret: str, raw_body: str, timestamp: str, signature_header: str) -> bool:
    if not timestamp or not signature_header:
        return False
    sig_hex = signature_header.strip()
    if sig_hex.lower().startswith("sha256="):
        sig_hex = sig_hex[7:].strip()
    sig_hex = sig_hex.lower()
    message = f"{timestamp}.{raw_body}"
    mac = hmac.new(secret.encode("utf-8"), message.encode("utf-8"), hashlib.sha256).hexdigest()
    expected_hdr = f"sha256={mac}"
    return _timing_safe_eq(mac.lower(), sig_hex.lower()) or _timing_safe_eq(
        expected_hdr.lower(), signature_header.strip().lower()
    )


def _webhook_auth_ok(request: Request, raw_body: str, hmac_secret: str, api_key: str) -> bool:
    if not hmac_secret and not api_key:
        log.error("SePay webhook: set sepay_webhook_secret or sepay_webhook_api_key in admin / .env")
        return False
    sig = request.headers.get("X-SePay-Signature") or request.headers.get("X-Sepay-Signature") or ""
    ts = request.headers.get("X-SePay-Timestamp") or request.headers.get("X-Sepay-Timestamp") or ""
    if sig and ts:
        if not hmac_secret:
            return False
        return _verify_sepay_hmac(hmac_secret, raw_body, ts, sig)
    if api_key and _verify_sepay_api_key_header(request.headers.get("Authorization") or "", api_key):
        return True
    return False


def extract_email_from_transfer_text(*parts: str | None) -> str | None:
    combined = " ".join(p for p in parts if p and str(p).strip())
    if not combined:
        return None
    m = re.search(r"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", combined)
    if m:
        return m.group(0).lower()
    m2 = re.search(r"\bBE\s+([a-zA-Z0-9._+-]+)gmailcom\b", combined, re.I)
    if m2:
        return f"{m2.group(1)}@gmail.com".lower()
    m3 = re.search(r"\b([a-zA-Z0-9._+-]{2,40})(gmail|yahoo|hotmail|outlook)com\b", combined, re.I)
    if m3:
        return f"{m3.group(1)}@{m3.group(2)}.com".lower()
    return None


def _allowed_amounts_vnd(content) -> list[int]:
    amounts: set[int] = set()
    base = (content.sepay_qr_base_url or "").strip() or config.SEPAY_QR_BASE_URL
    info = parse_qr_base(base)
    n = info.get("amount_vnd")
    if isinstance(n, int) and n > 0:
        amounts.add(n)
    price = int(content.license_price_vnd or config.LICENSE_PRICE_VND or 0)
    if price > 0:
        amounts.add(price)
    if not amounts:
        amounts.add(990000)
    return sorted(amounts)


def _parse_payload(raw: str) -> dict[str, Any]:
    return json.loads(raw)


@router.post("/webhook/sepay")
async def sepay_webhook(request: Request):
    raw_body = (await request.body()).decode("utf-8", errors="replace")
    db: Session = SessionLocal()
    try:
        content = get_content(db)
        hmac_s = (content.sepay_webhook_secret or "").strip() or config.SEPAY_WEBHOOK_SECRET
        api_k = (content.sepay_webhook_api_key or "").strip() or config.SEPAY_WEBHOOK_API_KEY

        if not _webhook_auth_ok(request, raw_body, hmac_s, api_k):
            return Response("Unauthorized", status_code=401)

        try:
            payload = _parse_payload(raw_body)
        except json.JSONDecodeError:
            return Response("Bad Request", status_code=400)

        if str(payload.get("transferType") or "").lower() != "in":
            return Response(json.dumps({"success": True}), media_type="application/json")

        tx_id = payload.get("id")
        try:
            tx_int = int(tx_id)  # type: ignore[arg-type]
        except (TypeError, ValueError):
            return Response(json.dumps({"success": True}), media_type="application/json")

        existing = find_license_by_sepay_tx(db, tx_int)
        if existing:
            buyer = extract_email_from_transfer_text(
                payload.get("content"),
                payload.get("description"),
                payload.get("code"),
            ) or existing.buyer_email
            if buyer:
                send_license_key_email(
                    to=buyer,
                    license_key=existing.license_key,
                    order_id=f"sepay-{tx_int}",
                )
            return Response(json.dumps({"success": True}), media_type="application/json")

        allowed = _allowed_amounts_vnd(content)
        amount = float(payload.get("transferAmount") or 0)
        if not any(float(a) == amount for a in allowed):
            log.info("SePay tx %s ignored: amount %s not in %s", tx_int, amount, allowed)
            return Response(json.dumps({"success": True}), media_type="application/json")

        buyer_email = extract_email_from_transfer_text(
            payload.get("content"),
            payload.get("description"),
            payload.get("code"),
        )
        if not buyer_email:
            log.info("SePay tx %s ignored: no email in memo", tx_int)
            return Response(json.dumps({"success": True}), media_type="application/json")

        days = max(1, int(content.license_term_days or config.SEPAY_LICENSE_DAYS))
        try:
            issue_license_record(
                db,
                buyer_email=buyer_email,
                plan="personal",
                days=days,
                sepay_transaction_id=tx_int,
                notes="SePay bank transfer",
                send_email=True,
                order_id_suffix=f"sepay-{tx_int}",
            )
            log.info("SePay tx %s minted for %s", tx_int, buyer_email)
        except IntegrityError:
            db.rollback()
            log.warning("SePay tx %s duplicate insert (ignored)", tx_int)
        return Response(json.dumps({"success": True}), media_type="application/json")
    except Exception:
        log.exception("SePay webhook error")
        return Response("Internal Server Error", status_code=500)
    finally:
        db.close()
