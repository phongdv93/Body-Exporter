"""Create / renew license rows + Worker mint/extend + optional Resend."""

from __future__ import annotations

import logging
from datetime import datetime

from sqlalchemy import func, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app import config
from app.database import get_content
from app.email_notify import send_license_key_email, send_license_renewed_email
from app.models import License, LicensePayment
from app.worker_client import extend_license_via_worker, mint_license_via_worker

log = logging.getLogger("uvicorn.error")


def issue_license_record(
    db: Session,
    *,
    buyer_email: str,
    plan: str = "personal",
    days: int | None = None,
    sepay_transaction_id: int | None = None,
    paddle_transaction_id: str | None = None,
    notes: str = "",
    send_email: bool = True,
    order_id_suffix: str = "",
    email_lang: str = "vi",
) -> License:
    """Always mint a NEW key (admin gift/test). Prefer fulfill_paid_license for payments."""
    content = get_content(db)
    term = days if days is not None else (content.license_term_days or config.SEPAY_LICENSE_DAYS)
    term = max(1, int(term))
    plan = (plan or "personal").strip() or "personal"
    email = (buyer_email or "").strip()
    if not email:
        raise ValueError("buyer_email required")

    key, expires_at = mint_license_via_worker(owner=email, plan=plan, days=term)

    lic = License(
        license_key=key,
        buyer_email=email,
        plan=plan,
        purchased_at=datetime.utcnow(),
        expires_at=expires_at,
        machine_fingerprint=None,
        sepay_transaction_id=sepay_transaction_id,
        paddle_transaction_id=(paddle_transaction_id or "").strip() or None,
        revoked=False,
        notes=(notes or "").strip(),
    )
    db.add(lic)
    try:
        db.commit()
        db.refresh(lic)
    except IntegrityError:
        db.rollback()
        raise

    _record_payment(
        db,
        license_id=lic.id,
        license_key=key,
        buyer_email=email,
        days_added=term,
        previous_expires_at=None,
        new_expires_at=expires_at,
        renewed=False,
        sepay_transaction_id=sepay_transaction_id,
        paddle_transaction_id=paddle_transaction_id,
        notes=notes,
    )

    if send_email:
        oid = order_id_suffix.strip() or (f"manual-{lic.id}" if lic.id else "manual")
        out = send_license_key_email(
            to=email,
            license_key=key,
            order_id=oid,
            plan=plan,
            expires_at=expires_at,
            lang=email_lang,
        )
        if out.get("skipped"):
            log.warning(
                "RESEND_API_KEY not set — license %s saved for %s but email not sent",
                lic.license_key[:8] + "…",
                email,
            )
        elif not out.get("ok"):
            detail = out.get("detail") or "unknown"
            log.error("Resend license email failed for %s: %s", email, detail)
            raise ValueError(f"Gửi email thất bại: {detail}")

    return lic


def fulfill_paid_license(
    db: Session,
    *,
    buyer_email: str,
    plan: str = "personal",
    days: int,
    sepay_transaction_id: int | None = None,
    paddle_transaction_id: str | None = None,
    notes: str = "",
    send_email: bool = True,
    order_id_suffix: str = "",
    email_lang: str = "vi",
) -> tuple[License, bool]:
    """
    Payment webhook entry point.
    Same email with an existing non-revoked key → extend that key (auto-renew).
    Otherwise → mint a new key.
    Returns (license, renewed).
    """
    email = (buyer_email or "").strip()
    if not email:
        raise ValueError("buyer_email required")
    term = max(1, int(days))
    plan = (plan or "personal").strip() or "personal"

    if sepay_transaction_id is not None:
        existing_pay = find_payment_by_sepay_tx(db, sepay_transaction_id)
        if existing_pay:
            lic = db.get(License, existing_pay.license_id) if existing_pay.license_id else None
            if lic is None:
                lic = find_license_by_key(db, existing_pay.license_key)
            if lic is not None:
                return lic, bool(existing_pay.renewed)

    if paddle_transaction_id:
        existing_pay = find_payment_by_paddle_tx(db, paddle_transaction_id)
        if existing_pay:
            lic = db.get(License, existing_pay.license_id) if existing_pay.license_id else None
            if lic is None:
                lic = find_license_by_key(db, existing_pay.license_key)
            if lic is not None:
                return lic, bool(existing_pay.renewed)
        by_lic = find_license_by_paddle_tx(db, paddle_transaction_id)
        if by_lic is not None:
            return by_lic, False

    if sepay_transaction_id is not None:
        by_lic = find_license_by_sepay_tx(db, sepay_transaction_id)
        if by_lic is not None:
            return by_lic, False

    renewable = find_renewable_license(db, email)
    if renewable is not None:
        return _renew_existing_license(
            db,
            lic=renewable,
            days=term,
            sepay_transaction_id=sepay_transaction_id,
            paddle_transaction_id=paddle_transaction_id,
            notes=notes,
            send_email=send_email,
            order_id_suffix=order_id_suffix,
            email_lang=email_lang,
        )

    lic = issue_license_record(
        db,
        buyer_email=email,
        plan=plan,
        days=term,
        sepay_transaction_id=sepay_transaction_id,
        paddle_transaction_id=paddle_transaction_id,
        notes=notes,
        send_email=send_email,
        order_id_suffix=order_id_suffix,
        email_lang=email_lang,
    )
    return lic, False


def _renew_existing_license(
    db: Session,
    *,
    lic: License,
    days: int,
    sepay_transaction_id: int | None,
    paddle_transaction_id: str | None,
    notes: str,
    send_email: bool,
    order_id_suffix: str,
    email_lang: str,
) -> tuple[License, bool]:
    previous = lic.expires_at
    try:
        data = extend_license_via_worker(key=lic.license_key, days=days)
    except Exception:
        # Worker key missing but CRM has a row — fall back to extend by owner email.
        log.warning("Extend by key failed for %s — retrying by owner", lic.license_key[:8])
        data = extend_license_via_worker(owner=lic.buyer_email, days=days)

    new_exp = _parse_expires(data.get("expiresAt"))
    prev_exp = _parse_expires(data.get("previousExpiresAt")) or previous

    lic.expires_at = new_exp
    note_line = (notes or "").strip() or "Auto-renew"
    stamp = datetime.utcnow().strftime("%Y-%m-%d")
    extra = f"{stamp}: {note_line} (+{days}d → {new_exp.date() if new_exp else '?'})"
    lic.notes = ((lic.notes or "").strip() + ("\n" if lic.notes else "") + extra).strip()
    db.add(lic)
    db.commit()
    db.refresh(lic)

    _record_payment(
        db,
        license_id=lic.id,
        license_key=lic.license_key,
        buyer_email=lic.buyer_email,
        days_added=days,
        previous_expires_at=prev_exp,
        new_expires_at=new_exp,
        renewed=True,
        sepay_transaction_id=sepay_transaction_id,
        paddle_transaction_id=paddle_transaction_id,
        notes=note_line,
    )

    if send_email:
        oid = order_id_suffix.strip() or f"renew-{lic.id}"
        out = send_license_renewed_email(
            to=lic.buyer_email,
            license_key=lic.license_key,
            order_id=oid,
            plan=lic.plan,
            expires_at=new_exp,
            days_added=days,
            lang=email_lang,
        )
        if out.get("skipped"):
            log.warning("RESEND_API_KEY not set — renew email skipped for %s", lic.buyer_email)
        elif not out.get("ok"):
            detail = out.get("detail") or "unknown"
            log.error("Resend renew email failed for %s: %s", lic.buyer_email, detail)
            # Payment already fulfilled — do not fail the webhook over email.

    log.info(
        "License renewed for %s key=%s… +%sd → %s",
        lic.buyer_email,
        lic.license_key[:8],
        days,
        new_exp,
    )
    return lic, True


def find_renewable_license(db: Session, buyer_email: str) -> License | None:
    """Non-revoked license for this email with the latest expires_at (may already be expired)."""
    email = (buyer_email or "").strip().lower()
    if not email:
        return None
    rows = db.scalars(
        select(License)
        .where(func.lower(License.buyer_email) == email)
        .where(License.revoked.is_(False))
        .order_by(License.expires_at.desc().nulls_last(), License.purchased_at.desc())
    ).all()
    return rows[0] if rows else None


def find_license_by_key(db: Session, key: str) -> License | None:
    k = (key or "").strip()
    if not k:
        return None
    return db.scalar(select(License).where(License.license_key == k))


def find_license_by_sepay_tx(db: Session, tx_id: int) -> License | None:
    return db.scalar(select(License).where(License.sepay_transaction_id == tx_id))


def find_license_by_paddle_tx(db: Session, txn_id: str) -> License | None:
    tid = (txn_id or "").strip()
    if not tid:
        return None
    return db.scalar(select(License).where(License.paddle_transaction_id == tid))


def find_payment_by_sepay_tx(db: Session, tx_id: int) -> LicensePayment | None:
    return db.scalar(select(LicensePayment).where(LicensePayment.sepay_transaction_id == tx_id))


def find_payment_by_paddle_tx(db: Session, txn_id: str) -> LicensePayment | None:
    tid = (txn_id or "").strip()
    if not tid:
        return None
    return db.scalar(select(LicensePayment).where(LicensePayment.paddle_transaction_id == tid))


def _record_payment(
    db: Session,
    *,
    license_id: int | None,
    license_key: str,
    buyer_email: str,
    days_added: int,
    previous_expires_at: datetime | None,
    new_expires_at: datetime | None,
    renewed: bool,
    sepay_transaction_id: int | None,
    paddle_transaction_id: str | None,
    notes: str,
) -> None:
    if sepay_transaction_id is None and not (paddle_transaction_id or "").strip():
        return
    pay = LicensePayment(
        license_id=license_id,
        license_key=license_key,
        buyer_email=buyer_email,
        days_added=days_added,
        previous_expires_at=previous_expires_at,
        new_expires_at=new_expires_at,
        renewed=renewed,
        sepay_transaction_id=sepay_transaction_id,
        paddle_transaction_id=(paddle_transaction_id or "").strip() or None,
        notes=(notes or "").strip(),
    )
    db.add(pay)
    try:
        db.commit()
    except IntegrityError:
        db.rollback()
        log.warning("LicensePayment duplicate ignored for %s", buyer_email)


def _parse_expires(raw: object) -> datetime | None:
    if raw is None:
        return None
    if isinstance(raw, datetime):
        return raw.replace(tzinfo=None) if raw.tzinfo else raw
    s = str(raw).strip()
    if not s:
        return None
    s2 = s.replace("Z", "+00:00")
    try:
        dt = datetime.fromisoformat(s2)
    except ValueError:
        return None
    if dt.tzinfo is not None:
        dt = dt.replace(tzinfo=None)
    return dt
