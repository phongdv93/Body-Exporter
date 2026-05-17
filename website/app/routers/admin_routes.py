from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db, login_session, logout_session, require_admin, verify_admin
from app.database import get_content
from app.sepay import pg_checkout_available

router = APIRouter(prefix="/admin")
templates = Jinja2Templates(directory=str(config.TEMPLATES_DIR))


@router.get("/login", response_class=HTMLResponse)
def login_page(request: Request):
    return templates.TemplateResponse(
        "admin/login.html",
        {"request": request, "error": None},
    )


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
    return templates.TemplateResponse(
        "admin/login.html",
        {"request": request, "error": "Sai tên đăng nhập hoặc mật khẩu."},
        status_code=401,
    )


@router.get("/logout")
def logout(request: Request):
    logout_session(request)
    return RedirectResponse("/admin/login", status_code=303)


@router.get("", response_class=HTMLResponse)
def dashboard(request: Request, db: Session = Depends(get_db), _user=Depends(require_admin)):
    content = get_content(db)
    return templates.TemplateResponse(
        "admin/dashboard.html",
        {
            "request": request,
            "content": content,
            "pg_available": pg_checkout_available(),
        },
    )


@router.get("/content", response_class=HTMLResponse)
def edit_content(request: Request, db: Session = Depends(get_db), _user=Depends(require_admin)):
    return templates.TemplateResponse(
        "admin/content.html",
        {"request": request, "content": get_content(db), "saved": False},
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
    support_email: str = Form(""),
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
    c.support_email = support_email.strip() or config.SUPPORT_EMAIL
    db.commit()
    return templates.TemplateResponse(
        "admin/content.html",
        {"request": request, "content": c, "saved": True},
    )
