"""Log plugin ZIP downloads from /download/go for admin dashboard."""

from __future__ import annotations

import hashlib
import logging
from datetime import datetime, timedelta

from fastapi import Request
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.i18n import resolve_lang
from app.models import DownloadEvent, SiteContent

log = logging.getLogger("uvicorn.error")


def _client_ip(request: Request) -> str | None:
    forwarded = (request.headers.get("x-forwarded-for") or "").strip()
    if forwarded:
        return forwarded.split(",")[0].strip()[:45]
    if request.client and request.client.host:
        return request.client.host[:45]
    return None


def _visitor_hash(request: Request) -> str:
    ip = _client_ip(request) or ""
    ua = (request.headers.get("user-agent") or "")[:500]
    raw = f"{ip}|{ua}".encode("utf-8", errors="replace")
    return hashlib.sha256(raw).hexdigest()[:64]


def record_plugin_download(request: Request, db: Session, content: SiteContent) -> None:
    """Persist one download event; never raises (redirect must succeed)."""
    try:
        ua = (request.headers.get("user-agent") or "")[:300]
        row = DownloadEvent(
            downloaded_at=datetime.utcnow(),
            ip=_client_ip(request),
            visitor_hash=_visitor_hash(request),
            plugin_version=(content.download_version or "").strip()[:40],
            lang=resolve_lang(request)[:8],
            user_agent=ua,
        )
        db.add(row)
        db.commit()
    except Exception:
        log.exception("record_plugin_download failed")
        db.rollback()


def download_stats_for_admin(db: Session) -> dict:
    now = datetime.utcnow()
    since_7 = now - timedelta(days=7)
    since_30 = now - timedelta(days=30)

    total = db.scalar(select(func.count()).select_from(DownloadEvent)) or 0
    unique_all = (
        db.scalar(select(func.count(func.distinct(DownloadEvent.visitor_hash))).select_from(DownloadEvent))
        or 0
    )
    clicks_7 = (
        db.scalar(select(func.count()).select_from(DownloadEvent).where(DownloadEvent.downloaded_at >= since_7))
        or 0
    )
    unique_7 = (
        db.scalar(
            select(func.count(func.distinct(DownloadEvent.visitor_hash)))
            .select_from(DownloadEvent)
            .where(DownloadEvent.downloaded_at >= since_7)
        )
        or 0
    )
    clicks_30 = (
        db.scalar(select(func.count()).select_from(DownloadEvent).where(DownloadEvent.downloaded_at >= since_30))
        or 0
    )
    unique_30 = (
        db.scalar(
            select(func.count(func.distinct(DownloadEvent.visitor_hash)))
            .select_from(DownloadEvent)
            .where(DownloadEvent.downloaded_at >= since_30)
        )
        or 0
    )
    recent = db.scalars(
        select(DownloadEvent).order_by(DownloadEvent.downloaded_at.desc()).limit(20)
    ).all()

    return {
        "total_clicks": total,
        "unique_visitors": unique_all,
        "clicks_7d": clicks_7,
        "unique_7d": unique_7,
        "clicks_30d": clicks_30,
        "unique_30d": unique_30,
        "recent": recent,
    }
