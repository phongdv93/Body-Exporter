"""Admin CRUD for issued licenses (Postgres)."""

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy import select
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db, require_admin
from app.license_service import issue_license_record
from app.models import AdminUser, License
from app.database import get_content

router = APIRouter(prefix="/admin/licenses")
templates = Jinja2Templates(directory=str(config.TEMPLATES_DIR))


@router.get("", response_class=HTMLResponse)
def list_licenses(
    request: Request,
    db: Session = Depends(get_db),
    _user: AdminUser = Depends(require_admin),
):
    rows = db.scalars(select(License).order_by(License.purchased_at.desc())).all()
    content = get_content(db)
    return templates.TemplateResponse(
        "admin/licenses.html",
        {
            "request": request,
            "licenses": rows,
            "content": content,
            "saved": False,
            "error": None,
        },
    )


@router.post("", response_class=HTMLResponse)
def create_license(
    request: Request,
    buyer_email: str = Form(...),
    plan: str = Form("personal"),
    days: int = Form(365),
    notes: str = Form(""),
    db: Session = Depends(get_db),
    _user: AdminUser = Depends(require_admin),
):
    err = None
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
        err = str(ex)

    rows = db.scalars(select(License).order_by(License.purchased_at.desc())).all()
    content = get_content(db)
    return templates.TemplateResponse(
        "admin/licenses.html",
        {
            "request": request,
            "licenses": rows,
            "content": content,
            "saved": err is None,
            "error": err,
        },
    )


@router.post("/edit", response_class=HTMLResponse)
def edit_license(
    request: Request,
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

    rows = db.scalars(select(License).order_by(License.purchased_at.desc())).all()
    content = get_content(db)
    return templates.TemplateResponse(
        "admin/licenses.html",
        {
            "request": request,
            "licenses": rows,
            "content": content,
            "saved": True,
            "error": None,
        },
    )
