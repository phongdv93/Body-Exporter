"""Admin CRUD for issued licenses (Postgres)."""

from urllib.parse import quote

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy import select
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db, require_admin
from app.license_service import issue_license_record
from app.models import AdminUser, License
from app.database import get_content
from app.template_response import html_response

router = APIRouter(prefix="/admin/licenses")
templates = Jinja2Templates(directory=str(config.TEMPLATES_DIR))


def _env_status(content) -> dict:
    wh = bool(
        (content.sepay_webhook_secret or "").strip()
        or config.SEPAY_WEBHOOK_SECRET
        or (content.sepay_webhook_api_key or "").strip()
        or config.SEPAY_WEBHOOK_API_KEY
    )
    return {
        "worker": bool(config.WORKER_API_BASE_URL and config.WORKER_ADMIN_TOKEN),
        "resend": bool(config.RESEND_API_KEY),
        "webhook": wh,
        "webhook_url": f"{config.SITE_URL.rstrip('/')}/webhook/sepay",
    }


def _licenses_ctx(request: Request, db: Session, *, saved: bool, error: str | None):
    rows = db.scalars(select(License).order_by(License.purchased_at.desc())).all()
    content = get_content(db)
    return {
        "request": request,
        "licenses": rows,
        "content": content,
        "saved": saved,
        "error": error,
        "env": _env_status(content),
    }


@router.get("")
def list_licenses(
    request: Request,
    saved: int = 0,
    err: str = "",
    db: Session = Depends(get_db),
    _user: AdminUser = Depends(require_admin),
):
    return html_response(
        templates,
        "admin/licenses.html",
        _licenses_ctx(request, db, saved=bool(saved), error=err or None),
    )


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
    return RedirectResponse("/admin/licenses?saved=1", status_code=303)


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
    return RedirectResponse("/admin/licenses?saved=1", status_code=303)
