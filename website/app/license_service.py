"""Create license rows + Worker mint + optional Resend."""

from __future__ import annotations

import logging
from datetime import datetime

from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app import config
from app.database import get_content
from app.email_notify import send_license_key_email
from app.models import License
from app.worker_client import mint_license_via_worker

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
) -> License:
    """Mint on Worker (if configured), persist in Postgres, email buyer."""
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

    if send_email:
        oid = order_id_suffix.strip() or (f"manual-{lic.id}" if lic.id else "manual")
        out = send_license_key_email(
            to=email,
            license_key=key,
            order_id=oid,
            plan=plan,
            expires_at=expires_at,
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


def find_license_by_sepay_tx(db: Session, tx_id: int) -> License | None:
    return db.scalar(select(License).where(License.sepay_transaction_id == tx_id))


def find_license_by_paddle_tx(db: Session, txn_id: str) -> License | None:
    tid = (txn_id or "").strip()
    if not tid:
        return None
    return db.scalar(select(License).where(License.paddle_transaction_id == tid))
