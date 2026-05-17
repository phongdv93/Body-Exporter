"""Call Cloudflare Worker admin API (mint license, client-config KV)."""

from __future__ import annotations

import json
import logging
import uuid
from datetime import datetime, timedelta
from typing import Any

import httpx

from app import config

log = logging.getLogger("uvicorn.error")


def _worker_admin_headers() -> dict[str, str]:
    token = config.WORKER_ADMIN_TOKEN.strip()
    if not token:
        raise ValueError("WORKER_ADMIN_TOKEN not set")
    return {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}


def _worker_base() -> str:
    base = config.WORKER_API_BASE_URL
    if not base:
        raise ValueError("WORKER_API_BASE_URL not set")
    return base.rstrip("/")


# UTF-8 copy for plugin License window (avoid PowerShell mojibake on push).
_CLIENT_CONFIG_TEXT: dict[str, str] = {
    "authorName": "Gió",
    "paymentWebTitle": "Mở trang thanh toán",
    "paymentWebBody": "Chọn QR chuyển khoản hoặc thẻ trên web. Nhập email để nhận license tự động.",
    "paymentVnTitle": "Thanh toán Việt Nam (Sepay)",
    "paymentVnBody": (
        "Quét QR bên dưới và chuyển đúng số tiền. "
        "Ghi email trong nội dung CK (vd. BE email@ban.com) để nhận license tự động."
    ),
}


def sync_client_config_from_site(
    *,
    support_email: str,
    site_url: str,
    sepay_qr_base_url: str = "",
    author_name: str = "",
) -> None:
    """Push support + payment URLs to Worker KV so the SolidWorks plugin About section updates."""
    base = _worker_base()
    headers_get = {"Accept": "application/json"}
    try:
        r = httpx.get(f"{base}/v1/client-config", headers=headers_get, timeout=20.0)
        r.raise_for_status()
        cfg: dict[str, Any] = r.json()
    except Exception:
        log.exception("Failed to read Worker client-config before sync")
        cfg = {}

    site = (site_url or config.SITE_URL).rstrip("/")
    author = (author_name or config.AUTHOR_NAME).strip() or config.AUTHOR_NAME
    cfg["authorName"] = author
    cfg["supportEmail"] = (support_email or config.SUPPORT_EMAIL).strip()
    cfg["supportUrl"] = site
    cfg["paymentWebUrl"] = f"{site}/buy"
    for k, v in _CLIENT_CONFIG_TEXT.items():
        if k != "authorName":
            cfg[k] = v
    cfg["authorName"] = author
    qr = (sepay_qr_base_url or "").strip()
    if qr:
        cfg["paymentVnSepayUrl"] = qr

    body = json.dumps(cfg, ensure_ascii=False)
    r2 = httpx.put(
        f"{base}/admin/client-config",
        headers=_worker_admin_headers(),
        content=body.encode("utf-8"),
        timeout=30.0,
    )
    if not r2.is_success:
        log.error("Worker client-config PUT failed %s: %s", r2.status_code, r2.text[:500])
        r2.raise_for_status()
    log.info("Worker client-config synced: supportEmail=%s supportUrl=%s", cfg["supportEmail"], site)


def mint_license_via_worker(*, owner: str, plan: str, days: int) -> tuple[str, datetime]:
    """Return (license_key, expires_at_utc_naive). Falls back to local UUID if Worker not configured."""
    base = config.WORKER_API_BASE_URL
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
