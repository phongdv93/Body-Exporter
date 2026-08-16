"""Record plugin installs / heartbeats for admin dashboard."""

from __future__ import annotations

import logging
import math
from datetime import datetime, timedelta
from typing import Any

import httpx
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app import config
from app.models import ClientMachine, License

log = logging.getLogger("uvicorn.error")

LICENSE_STATUS_VALUES = frozenset({"trial", "licensed", "expired", "none", "unknown", "error"})


def _client_ip(request) -> str | None:
    if request is None:
        return None
    forwarded = (request.headers.get("x-forwarded-for") or "").split(",")[0].strip()
    if forwarded:
        return forwarded[:45]
    if request.client and request.client.host:
        return str(request.client.host)[:45]
    return None


def _is_private_ip(ip: str) -> bool:
    if not ip:
        return True
    if ip in ("127.0.0.1", "::1", "localhost"):
        return True
    if ip.startswith("10.") or ip.startswith("192.168.") or ip.startswith("172."):
        return True
    return False


def lookup_geo(ip: str | None) -> dict[str, str | None]:
    if not ip or _is_private_ip(ip):
        return {}
    try:
        r = httpx.get(
            f"http://ip-api.com/json/{ip}",
            params={"fields": "status,country,countryCode,regionName,city"},
            timeout=2.5,
        )
        if not r.is_success:
            return {}
        data = r.json()
        if data.get("status") != "success":
            return {}
        return {
            "country_code": (data.get("countryCode") or "")[:8] or None,
            "country_name": (data.get("country") or "")[:80] or None,
            "region": (data.get("regionName") or "")[:80] or None,
            "city": (data.get("city") or "")[:80] or None,
        }
    except Exception as ex:
        log.debug("GeoIP lookup failed for %s: %s", ip, ex)
        return {}


def _machine_has_purchased_license(db: Session, machine_id: str) -> bool:
    mid = (machine_id or "").strip()
    if not mid:
        return False
    q = (
        select(func.count())
        .select_from(License)
        .where(
            License.revoked.is_(False),
            func.lower(License.machine_fingerprint) == mid.lower(),
        )
    )
    return (db.scalar(q) or 0) > 0


def record_client_ping(
    db: Session,
    *,
    machine_id: str,
    hostname: str = "",
    plugin_version: str = "",
    sw_version: str = "",
    license_status: str = "unknown",
    event: str = "ping",
    client_ip: str | None = None,
) -> ClientMachine:
    mid = (machine_id or "").strip()
    if not mid or len(mid) < 8:
        raise ValueError("invalid machine_id")

    status = (license_status or "unknown").strip().lower()
    if status not in LICENSE_STATUS_VALUES:
        status = "unknown"

    now = datetime.utcnow()
    row = db.scalar(select(ClientMachine).where(ClientMachine.machine_id == mid))
    geo = lookup_geo(client_ip)

    purchased = _machine_has_purchased_license(db, mid)
    if status == "licensed":
        purchased = True

    if row is None:
        row = ClientMachine(
            machine_id=mid,
            hostname=(hostname or "")[:128],
            plugin_version=(plugin_version or "")[:40],
            sw_version=(sw_version or "")[:40],
            first_seen_at=now,
            last_seen_at=now,
            last_ip=client_ip,
            license_status=status,
            has_purchased_license=purchased,
            last_event=(event or "ping")[:32],
            **{k: v for k, v in geo.items() if v},
        )
        db.add(row)
    else:
        if hostname:
            row.hostname = hostname[:128]
        if plugin_version:
            row.plugin_version = plugin_version[:40]
        if sw_version:
            row.sw_version = sw_version[:40]
        row.last_seen_at = now
        row.license_status = status
        row.has_purchased_license = purchased or row.has_purchased_license
        row.last_event = (event or "ping")[:32]
        if client_ip:
            row.last_ip = client_ip
            if geo:
                row.country_code = geo.get("country_code")
                row.country_name = geo.get("country_name")
                row.region = geo.get("region")
                row.city = geo.get("city")

    db.commit()
    db.refresh(row)
    return row


def machine_usage_label(last_seen: datetime | None, now: datetime | None = None) -> str:
    """active | inactive | likely_removed"""
    if not last_seen:
        return "unknown"
    now = now or datetime.utcnow()
    age = now - last_seen
    active = timedelta(days=config.TELEMETRY_ACTIVE_DAYS)
    inactive = timedelta(days=config.TELEMETRY_INACTIVE_DAYS)
    if age <= active:
        return "active"
    if age <= inactive:
        return "inactive"
    return "likely_removed"


def machine_usage_label_vi(label: str) -> str:
    return {
        "active": "Đang dùng",
        "inactive": "Ít dùng",
        "likely_removed": "Có thể đã gỡ",
        "unknown": "—",
    }.get(label, label)


def _upsert_machine_from_crm(
    db: Session,
    *,
    machine_id: str,
    license_status: str = "licensed",
    has_purchased: bool = True,
    event: str = "license_sync",
) -> None:
    mid = (machine_id or "").strip()
    if not mid or len(mid) < 8:
        return
    now = datetime.utcnow()
    row = db.scalar(select(ClientMachine).where(ClientMachine.machine_id == mid))
    if row is None:
        row = ClientMachine(
            machine_id=mid,
            first_seen_at=now,
            last_seen_at=now,
            license_status=license_status[:32],
            has_purchased_license=has_purchased,
            last_event=event[:32],
        )
        db.add(row)
        db.flush()
    else:
        if has_purchased:
            row.has_purchased_license = True
        if license_status == "licensed":
            row.license_status = "licensed"
        elif license_status == "expired" and row.license_status not in ("licensed",):
            row.license_status = "expired"
        elif license_status and row.license_status in ("unknown", "none"):
            row.license_status = license_status[:32]
        row.last_event = event[:32]


def sync_known_machines_from_crm(db: Session) -> int:
    """Backfill dashboard from Postgres licenses + Worker KV (one row per machine_id)."""
    before = db.scalar(select(func.count()).select_from(ClientMachine)) or 0
    # Dedupe trong một lần sync — tránh INSERT trùng machine_id trước khi flush.
    pending: dict[str, dict[str, Any]] = {}

    def _merge(mid: str, license_status: str, event: str) -> None:
        if mid not in pending:
            pending[mid] = {"license_status": license_status, "event": event}
            return
        cur = pending[mid]
        if license_status == "licensed":
            cur["license_status"] = "licensed"
        elif license_status == "expired" and cur["license_status"] != "licensed":
            cur["license_status"] = "expired"

    for lic in db.scalars(select(License).where(License.machine_fingerprint.isnot(None))).all():
        fp = (lic.machine_fingerprint or "").strip()
        if not fp:
            continue
        _merge(fp, "licensed" if not lic.revoked else "expired", "postgres_license")

    try:
        from app.worker_client import fetch_worker_license_records

        for rec in fetch_worker_license_records():
            fp = (rec.get("boundMachineId") or "").strip()
            if not fp:
                continue
            _merge(fp, "expired" if rec.get("revoked") else "licensed", "worker_kv")
    except Exception as ex:
        log.warning("sync_known_machines_from_crm: Worker list failed: %s", ex)

    for mid, info in pending.items():
        _upsert_machine_from_crm(
            db,
            machine_id=mid,
            license_status=info["license_status"],
            has_purchased=True,
            event=info["event"],
        )

    try:
        db.commit()
    except Exception:
        db.rollback()
        raise

    after = db.scalar(select(func.count()).select_from(ClientMachine)) or 0
    return max(0, after - before)


def list_machines_for_admin(db: Session) -> list[dict[str, Any]]:
    rows = db.scalars(select(ClientMachine).order_by(ClientMachine.last_seen_at.desc())).all()
    now = datetime.utcnow()

    # Latest non-revoked CRM license per fingerprint — same number the plugin badge should track.
    licenses = db.scalars(
        select(License).where(
            License.machine_fingerprint.is_not(None),
            License.revoked.is_(False),
        )
    ).all()
    best_by_fp: dict[str, License] = {}
    for lic in licenses:
        fp = (lic.machine_fingerprint or "").strip().lower()
        if not fp:
            continue
        prev = best_by_fp.get(fp)
        if prev is None:
            best_by_fp[fp] = lic
            continue
        prev_exp = prev.expires_at or datetime.min
        lic_exp = lic.expires_at or datetime.min
        if lic_exp >= prev_exp:
            best_by_fp[fp] = lic

    out: list[dict[str, Any]] = []
    for m in rows:
        usage = machine_usage_label(m.last_seen_at, now)
        loc_parts = [p for p in (m.city, m.region, m.country_name) if p]
        fp = (m.machine_id or "").strip().lower()
        lic = best_by_fp.get(fp)
        expires_at = lic.expires_at if lic else None
        days_left: int | None = None
        expiry_state = "none"
        if expires_at is not None:
            delta = expires_at - now
            days_left = max(0, math.ceil(delta.total_seconds() / 86400))
            if delta.total_seconds() <= 0:
                expiry_state = "expired"
                days_left = 0
            elif days_left <= 14:
                expiry_state = "soon"
            else:
                expiry_state = "ok"

        out.append(
            {
                "machine": m,
                "usage": usage,
                "usage_label": machine_usage_label_vi(usage),
                "location": ", ".join(loc_parts) if loc_parts else "—",
                "license_label": {
                    "licensed": "Có license",
                    "trial": "Trial",
                    "expired": "Hết hạn",
                    "none": "Chưa kích hoạt",
                    "error": "Lỗi",
                }.get(m.license_status, m.license_status),
                "crm_email": (lic.buyer_email if lic else "") or "",
                "crm_plan": (lic.plan if lic else "") or "",
                "crm_expires_at": expires_at,
                "crm_days_left": days_left,
                "crm_expiry_state": expiry_state,
                "crm_key": (lic.license_key if lic else "") or "",
            }
        )
    return out
