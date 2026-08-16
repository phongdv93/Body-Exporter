"""Admin CRUD for issued licenses (Postgres)."""

from datetime import datetime
from urllib.parse import quote, unquote

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy import select
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db, require_admin
from app.license_service import issue_license_record
from app.license_sync import sync_fingerprints_from_worker
from app.models import AdminUser, License
from app.database import get_content
from app.template_response import html_response

router = APIRouter(prefix="/admin/licenses")
templates = Jinja2Templates(directory=str(config.TEMPLATES_DIR))


def _env_status(content) -> dict:
    hmac_s = config.SEPAY_WEBHOOK_SECRET or (content.sepay_webhook_secret or "").strip()
    api_k = config.SEPAY_WEBHOOK_API_KEY or (content.sepay_webhook_api_key or "").strip()
    wh = bool(hmac_s or api_k)
    return {
        "worker": bool(config.WORKER_API_BASE_URL and config.WORKER_ADMIN_TOKEN),
        "resend": bool(config.RESEND_API_KEY),
        "webhook": wh,
        "webhook_url": f"{config.SITE_URL.rstrip('/')}/webhook/sepay",
    }


def _licenses_ctx(
    request: Request,
    db: Session,
    *,
    saved: bool,
    error: str | None,
    sync_msg: str | None = None,
):
    rows = db.scalars(select(License).order_by(License.purchased_at.desc())).all()
    content = get_content(db)
    now = datetime.utcnow()
    license_rows = []
    for lic in rows:
        days_left = None
        expiry_state = "none"
        if lic.expires_at is not None:
            delta = lic.expires_at - now
            days_left = int(delta.total_seconds() // 86400)
            if delta.total_seconds() <= 0:
                expiry_state = "expired"
                days_left = 0
            elif days_left <= 14:
                expiry_state = "soon"
            else:
                expiry_state = "ok"
        license_rows.append(
            {
                "lic": lic,
                "days_left": days_left,
                "expiry_state": expiry_state,
            }
        )
    return {
        "request": request,
        "licenses": rows,
        "license_rows": license_rows,
        "content": content,
        "saved": saved,
        "error": error,
        "sync_msg": sync_msg,
        "env": _env_status(content),
    }


def _run_worker_fingerprint_sync(db: Session) -> str | None:
    result = sync_fingerprints_from_worker(db)
    if not result.get("ok"):
        return f"Đồng bộ Worker thất bại: {result.get('error')}"
    updated = int(result.get("updated") or 0)
    matched = int(result.get("matched") or 0)
    with_fp = int(result.get("with_fingerprint") or 0)
    if updated:
        return f"Đã đồng bộ {updated} fingerprint từ Worker ({with_fp} key đã kích hoạt máy / {matched} khớp Postgres)."
    return f"Đã kiểm tra Worker — {with_fp} key có fingerprint, không có thay đổi mới."


@router.get("")
def list_licenses(
    request: Request,
    saved: int = 0,
    err: str = "",
    sync: str = "",
    nosync: int = 0,
    db: Session = Depends(get_db),
    _user: AdminUser = Depends(require_admin),
):
    sync_msg = unquote(sync).strip() if sync else None
    if not sync_msg and not nosync and config.WORKER_API_BASE_URL and config.WORKER_ADMIN_TOKEN:
        sync_msg = _run_worker_fingerprint_sync(db)
    return html_response(
        templates,
        "admin/licenses.html",
        _licenses_ctx(request, db, saved=bool(saved), error=err or None, sync_msg=sync_msg),
    )


@router.post("/sync-worker")
def sync_worker_fingerprints(
    db: Session = Depends(get_db),
    _user: AdminUser = Depends(require_admin),
):
    sync_msg = _run_worker_fingerprint_sync(db)
    q = f"sync={quote(sync_msg or '', safe='')}"
    return RedirectResponse(f"/admin/licenses?{q}&nosync=1", status_code=303)


@router.post("")
def create_license(
    buyer_email: str = Form(...),
    plan: str = Form("personal"),
    days: int = Form(365),
    notes: str = Form(""),
    db: Session = Depends(get_db),
    _user: AdminUser = Depends(require_admin),
):
    try:
        issue_license_record(
            db,
            buyer_email=buyer_email.strip(),
            plan=plan.strip() or "personal",
            days=max(1, int(days)),
            sepay_transaction_id=None,
            notes=notes.strip(),
            send_email=True,
            order_id_suffix=f"admin-{buyer_email.strip()[:20]}",
        )
    except Exception as ex:
        return RedirectResponse(
            f"/admin/licenses?err={quote(str(ex), safe='')}",
            status_code=303,
        )
    return RedirectResponse("/admin/licenses?saved=1&nosync=1", status_code=303)


@router.post("/edit")
def edit_license(
    license_id: int = Form(...),
    buyer_email: str = Form(""),
    machine_fingerprint: str = Form(""),
    notes: str = Form(""),
    revoked: str = Form("0"),
    db: Session = Depends(get_db),
    _user: AdminUser = Depends(require_admin),
):
    lic = db.get(License, license_id)
    if lic:
        if buyer_email.strip():
            lic.buyer_email = buyer_email.strip()
        fp = machine_fingerprint.strip()
        lic.machine_fingerprint = fp if fp else None
        lic.notes = notes.strip()
        lic.revoked = revoked == "1"
        db.commit()
    return RedirectResponse("/admin/licenses?saved=1&nosync=1", status_code=303)
