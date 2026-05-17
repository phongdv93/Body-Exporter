from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db, login_session, logout_session, require_admin, verify_admin
from app.client_telemetry import list_machines_for_admin
from app.database import get_content
from app.sepay import pg_checkout_available_for_content
from app.template_response import html_response
from app.worker_client import sync_client_config_from_site

router = APIRouter(prefix="/admin")
templates = Jinja2Templates(directory=str(config.TEMPLATES_DIR))


@router.get("/login")
def login_page(request: Request):
    return html_response(templates, "admin/login.html", {"request": request, "error": None})


@router.post("/login")
def login_post(
    request: Request,
    username: str = Form(...),
    password: str = Form(...),
    db: Session = Depends(get_db),
):
    if verify_admin(username.strip(), password, db):
        login_session(request, username.strip())
        return RedirectResponse("/admin", status_code=303)
    return html_response(
        templates,
        "admin/login.html",
        {"request": request, "error": "Sai tên đăng nhập hoặc mật khẩu."},
        status_code=401,
    )


@router.get("/logout")
def logout(request: Request):
    logout_session(request)
    return RedirectResponse("/admin/login", status_code=303)


def _env_status(content) -> dict:
    hmac_s = config.SEPAY_WEBHOOK_SECRET or (content.sepay_webhook_secret or "").strip()
    api_k = config.SEPAY_WEBHOOK_API_KEY or (content.sepay_webhook_api_key or "").strip()
    wh = bool(hmac_s or api_k)
    return {
        "worker": bool(config.WORKER_API_BASE_URL and config.WORKER_ADMIN_TOKEN),
        "resend": bool(config.RESEND_API_KEY),
        "webhook": wh,
    }


@router.get("")
def dashboard(request: Request, db: Session = Depends(get_db), _user=Depends(require_admin)):
    content = get_content(db)
    machines = list_machines_for_admin(db)
    return html_response(
        templates,
        "admin/dashboard.html",
        {
            "request": request,
            "content": content,
            "machines": machines,
            "machine_count": len(machines),
            "pg_available": pg_checkout_available_for_content(content),
            "site_url": config.SITE_URL.rstrip("/"),
            "env": _env_status(content),
            "telemetry_active_days": config.TELEMETRY_ACTIVE_DAYS,
            "telemetry_inactive_days": config.TELEMETRY_INACTIVE_DAYS,
        },
    )


@router.get("/content")
def edit_content(
    request: Request,
    saved: int = 0,
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    return html_response(
        templates,
        "admin/content.html",
        {
            "request": request,
            "content": get_content(db),
            "saved": bool(saved),
            "site_url": config.SITE_URL.rstrip("/"),
        },
    )


@router.post("/content")
def save_content(
    request: Request,
    hero_title: str = Form(""),
    hero_subtitle: str = Form(""),
    hero_bullets: str = Form(""),
    about_html: str = Form(""),
    download_version: str = Form(""),
    download_url: str = Form(""),
    download_notes: str = Form(""),
    buy_intro: str = Form(""),
    buy_footer: str = Form(""),
    sepay_qr_base_url: str = Form(""),
    license_price_vnd: int = Form(1590000),
    license_term_days: int = Form(365),
    support_email: str = Form(""),
    author_name: str = Form(""),
    sepay_pg_merchant_id: str = Form(""),
    sepay_pg_secret_key: str = Form(""),
    sepay_pg_env: str = Form("sandbox"),
    sepay_webhook_secret: str = Form(""),
    sepay_webhook_api_key: str = Form(""),
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    c = get_content(db)
    c.hero_title = hero_title.strip()
    c.hero_subtitle = hero_subtitle.strip()
    c.hero_bullets = hero_bullets.strip()
    c.about_html = about_html.strip()
    c.download_version = download_version.strip()
    c.download_url = download_url.strip()
    c.download_notes = download_notes.strip()
    c.buy_intro = buy_intro.strip()
    c.buy_footer = buy_footer.strip()
    c.sepay_qr_base_url = sepay_qr_base_url.strip()
    c.license_price_vnd = max(1, license_price_vnd)
    c.license_term_days = max(1, int(license_term_days))
    c.support_email = support_email.strip() or config.SUPPORT_EMAIL
    c.author_name = author_name.strip() or config.AUTHOR_NAME
    c.sepay_pg_merchant_id = sepay_pg_merchant_id.strip()
    if sepay_pg_secret_key.strip():
        c.sepay_pg_secret_key = sepay_pg_secret_key.strip()
    env_pg = (sepay_pg_env or "sandbox").strip().lower()
    if env_pg in ("sandbox", "production"):
        c.sepay_pg_env = env_pg
    wh = sepay_webhook_secret.strip()
    if wh:
        c.sepay_webhook_secret = wh
    wh_api = sepay_webhook_api_key.strip()
    if wh_api:
        c.sepay_webhook_api_key = wh_api
    db.commit()
    db.refresh(c)
    try:
        sync_client_config_from_site(
            support_email=c.support_email,
            site_url=config.SITE_URL,
            sepay_qr_base_url=c.sepay_qr_base_url,
            author_name=c.author_name,
        )
    except Exception as ex:
        import logging

        logging.getLogger("uvicorn.error").warning(
            "Saved site content but Worker client-config not synced (plugin About unchanged): %s",
            ex,
        )
    return RedirectResponse("/admin/content?saved=1", status_code=303)
