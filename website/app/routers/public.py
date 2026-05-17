import json

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db
from app.database import get_content
from app.template_response import html_response
from app.sepay import (
    build_pg_checkout_fields,
    build_qr_image_url,
    build_transfer_memo,
    new_invoice_number,
    parse_qr_base,
    pg_checkout_available_for_content,
    pg_checkout_init_url_for_env,
    pg_credentials_for_content,
)

router = APIRouter()
templates = Jinja2Templates(directory=str(config.TEMPLATES_DIR))


def _ctx(request: Request, db: Session, page_title: str | None = None, **extra):
    content = get_content(db)
    title = page_title or f"{content.hero_title} — Body Exporter"
    subtitle = (content.hero_subtitle or "").strip()
    meta_desc = subtitle[:320] if subtitle else config.SEO_DESCRIPTION
    canonical = f"{config.SITE_URL}{request.url.path.split('?')[0]}"
    schema_web = json.dumps(
        {
            "@context": "https://schema.org",
            "@type": "WebSite",
            "name": "Body Exporter",
            "url": config.SITE_URL,
            "description": meta_desc[:500],
            "inLanguage": "vi-VN",
        },
        ensure_ascii=False,
    )
    schema_app = json.dumps(
        {
            "@context": "https://schema.org",
            "@type": "SoftwareApplication",
            "name": "Body Exporter",
            "applicationCategory": "DesignApplication",
            "operatingSystem": "Windows",
            "offers": {
                "@type": "Offer",
                "priceCurrency": "VND",
                "availability": "https://schema.org/InStock",
            },
        },
        ensure_ascii=False,
    )
    return {
        "request": request,
        "content": content,
        "support_email": content.support_email or config.SUPPORT_EMAIL,
        "site_url": config.SITE_URL,
        "page_title": title,
        "meta_description": meta_desc,
        "meta_keywords": config.SEO_KEYWORDS,
        "canonical_url": canonical,
        "seo_og_image": config.SEO_OG_IMAGE or None,
        "schema_website_json": schema_web,
        "schema_app_json": schema_app,
        **extra,
    }


@router.get("/")
def home(request: Request, db: Session = Depends(get_db)):
    content = get_content(db)
    bullets = [b.strip() for b in (content.hero_bullets or "").splitlines() if b.strip()]
    return html_response(
        templates,
        "home.html",
        _ctx(
            request,
            db,
            bullets=bullets,
            page_title=f"{content.hero_title} — Body Exporter",
        ),
    )


@router.get("/download")
def download_page(request: Request, db: Session = Depends(get_db)):
    return html_response(
        templates,
        "download.html",
        _ctx(request, db, page_title="Tải plugin Body Exporter — SolidWorks"),
    )


@router.get("/buy")
def buy_get(request: Request, email: str = "", db: Session = Depends(get_db)):
    return _render_buy(request, db, email=email.strip())


@router.post("/buy")
def buy_post(
    request: Request,
    email: str = Form(...),
    pay_method: str = Form("qr"),
    db: Session = Depends(get_db),
):
    email = email.strip()
    if pay_method == "card" and pg_checkout_available_for_content(get_content(db)):
        return _redirect_pg_checkout(email, db)
    return _render_buy(request, db, email=email)


def _redirect_pg_checkout(email: str, db: Session):
    import html
    from urllib.parse import quote

    content = get_content(db)
    amount = content.license_price_vnd or config.LICENSE_PRICE_VND
    invoice = new_invoice_number()
    email_q = quote(email, safe="")
    mid, sk, envn = pg_credentials_for_content(content)
    fields = build_pg_checkout_fields(
        merchant_id=mid,
        secret_key=sk,
        email=email,
        amount_vnd=amount,
        invoice=invoice,
        description=f"Body Exporter license — {email}",
        success_url=f"{config.SITE_URL}/buy/success?email={email_q}",
        error_url=f"{config.SITE_URL}/buy?email={email_q}",
        cancel_url=f"{config.SITE_URL}/buy?email={email_q}",
    )
    from fastapi.responses import HTMLResponse

    inputs = "".join(
        f'<input type="hidden" name="{html.escape(k)}" value="{html.escape(v)}">'
        for k, v in fields.items()
    )
    action = pg_checkout_init_url_for_env(envn)
    page = f"""<!DOCTYPE html><html><head><meta charset="utf-8"><title>Redirect…</title>
    <link rel="stylesheet" href="/static/css/site.css"></head>
    <body class="redirect-page"><p>Đang chuyển sang SePay…</p>
    <form id="f" method="POST" action="{action}">{inputs}</form>
    <script>document.getElementById('f').submit();</script></body></html>"""
    return HTMLResponse(page)


def _render_buy(request: Request, db: Session, email: str = ""):
    content = get_content(db)
    base = content.sepay_qr_base_url or config.SEPAY_QR_BASE_URL
    bank = parse_qr_base(base)
    amount = content.license_price_vnd or config.LICENSE_PRICE_VND
    if bank.get("amount_vnd"):
        amount = bank["amount_vnd"]
    qr_url = build_qr_image_url(base, email) if email else ""
    memo = build_transfer_memo(email) if email else ""
    return html_response(
        templates,
        "buy.html",
        _ctx(
            request,
            db,
            page_title="Mua license Body Exporter — SePay",
            email=email,
            qr_url=qr_url,
            memo=memo,
            bank=bank,
            amount_vnd=amount,
            pg_available=pg_checkout_available_for_content(content),
        ),
    )


@router.get("/buy/success")
def buy_success(request: Request, email: str = "", db: Session = Depends(get_db)):
    return html_response(
        templates,
        "buy_success.html",
        _ctx(
            request,
            db,
            page_title="Thanh toán thành công — Body Exporter",
            email=email.strip(),
        ),
    )
