"""Call Cloudflare Worker admin API to mint a license (KV)."""

from __future__ import annotations

import logging
import uuid
from datetime import datetime, timedelta

import httpx

from app import config

log = logging.getLogger("uvicorn.error")


def mint_license_via_worker(*, owner: str, plan: str, days: int) -> tuple[str, datetime]:
    """Return (license_key, expires_at_utc_naive). Falls back to local UUID if Worker not configured."""
    base = config.WORKER_API_BASE_URL.strip().rstrip("/")
    token = config.WORKER_ADMIN_TOKEN.strip()
    if not base or not token:
        log.warning(
            "WORKER_API_BASE_URL / WORKER_ADMIN_TOKEN not set — minting local UUID only "
            "(plugin online validation will NOT work until key exists on Worker)."
        )
        key = str(uuid.uuid4())
        exp = datetime.utcnow() + timedelta(days=max(1, days))
        return key, exp

    url = f"{base}/admin/license/issue"
    try:
        r = httpx.post(
            url,
            headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
            json={"owner": owner.strip(), "plan": plan, "days": int(days)},
            timeout=45.0,
        )
        if not r.is_success:
            log.error("Worker issue failed %s: %s", r.status_code, r.text[:500])
            r.raise_for_status()
        data = r.json()
        key = (data.get("key") or "").strip()
        exp_raw = (data.get("expiresAt") or "").strip()
        if not key:
            raise ValueError("Worker response missing key")
        exp = _parse_iso_to_naive_utc(exp_raw)
        return key, exp
    except Exception:
        log.exception("Worker mint failed")
        raise


def _parse_iso_to_naive_utc(s: str) -> datetime:
    if not s:
        return datetime.utcnow() + timedelta(days=365)
    s2 = s.replace("Z", "+00:00")
    dt = datetime.fromisoformat(s2)
    if dt.tzinfo is not None:
        dt = dt.replace(tzinfo=None)
    return dt
