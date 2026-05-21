import json
import re

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db
from app.database import get_content
from app.i18n import (
    LANG_COOKIE,
    LANG_COOKIE_MAX_AGE,
    localized_bullets,
    localized_html_field,
    localized_text,
    normalize_lang,
    resolve_lang,
    safe_redirect_path,
    page_meta,
    schema_website_json,
    translate,
)
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


def _dedupe_title(title: str) -> str:
    """Collapse duplicated or redundant suffixes in titles (browser tab + og:title)."""
    t = (title or "").strip()
    if not t or " — " not in t:
        return t
    left, _, right = t.partition(" — ")
    l, r = left.strip(), right.strip()
    if l.lower() == r.lower():
        return l
    if r.lower() == "body exporter" and "body exporter" in l.lower():
        return l
    if r.lower() == "solidworks body exporter" and "solidworks" in l.lower():
        return l
    return t


def _og_image_url() -> str:
    if config.SEO_OG_IMAGE:
        return config.SEO_OG_IMAGE
    base = config.SITE_URL.rstrip("/")
    return f"{base}/static/og.png?v=4"


def _install_notes_extra(raw: str | None) -> str | None:
    """Optional admin HTML below install steps — hide legacy duplicate of built-in guide."""
    notes = (raw or "").strip()
    if not notes:
        return None
    plain = re.sub(r"<[^>]+>", " ", notes.lower())
    plain = re.sub(r"\s+", " ", plain).strip()
    if "install-bodyexporter" in plain and ("add-ins" in plain or "add ins" in plain):
        return None
    return notes


def _ctx(
    request: Request,
    db: Session,
    page_title: str | None = None,
    *,
    seo_page: str | None = None,
    **extra,
):
    content = get_content(db)
    lang = resolve_lang(request)
    if seo_page:
        meta_desc, default_title = page_meta(lang, seo_page)
        title = page_title or default_title
    else:
        meta_desc, default_title = page_meta(lang, "home")
        title = page_title or _dedupe_title(content.hero_title) or default_title
    keywords = config.SEO_KEYWORDS_EN if lang == "en" else config.SEO_KEYWORDS
    canonical = f"{config.SITE_URL}{request.url.path.split('?')[0]}"
    schema_web = schema_website_json(lang, meta_desc)
    og_locale = "en_US" if lang == "en" else "vi_VN"
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
        "lang": lang,
        "t": lambda key, **kw: translate(lang, key, **kw),
        "page_title": title,
        "meta_description": meta_desc,
        "meta_keywords": keywords,
        "canonical_url": canonical,
        "og_locale": og_locale,
        "seo_og_image": _og_image_url(),
        "schema_website_json": schema_web,
        "schema_app_json": schema_app,
        "install_notes_extra": _install_notes_extra(content.download_notes),
        "hero_subtitle": localized_text(content, "hero_subtitle", lang, default_key="home.hero_subtitle_default"),
        "about_html": localized_html_field(content, "about_html", lang),
        "buy_intro": localized_html_field(content, "buy_intro", lang, default_key="buy.intro_default"),
        "buy_footer": localized_html_field(content, "buy_footer", lang, default_key="buy.footer_default"),
        **extra,
    }


@router.get("/lang/{lang_code}")
def set_language(lang_code: str, request: Request, next: str = "/"):
    lang = normalize_lang(lang_code)
    dest = safe_redirect_path(next or request.headers.get("referer"))
    resp = RedirectResponse(dest, status_code=303)
    resp.set_cookie(
        LANG_COOKIE,
        lang,
        max_age=LANG_COOKIE_MAX_AGE,
        httponly=False,
        samesite="lax",
        secure=config.SITE_URL.startswith("https://"),
    )
    return resp


@router.get("/")
def home(request: Request, db: Session = Depends(get_db)):
    content = get_content(db)
    lang = resolve_lang(request)
    bullets = localized_bullets(content, lang)
    return html_response(
        templates,
        "home.html",
        _ctx(
            request,
            db,
            seo_page="home",
            bullets=bullets,
            page_title=_dedupe_title(content.hero_title) or translate(lang, "page.home"),
        ),
    )


def _download_consent_ok(request: Request) -> bool:
    return request.cookies.get(config.DOWNLOAD_CONSENT_COOKIE) == config.DOWNLOAD_CONSENT_VALUE


@router.get("/download")
def download_page(request: Request, db: Session = Depends(get_db)):
    return html_response(
        templates,
        "download.html",
        _ctx(
            request,
            db,
            seo_page="download",
            download_consent=_download_consent_ok(request),
        ),
    )


@router.post("/download/accept")
def download_accept(
    request: Request,
    agree_policy: str = Form(""),
    db: Session = Depends(get_db),
):
    if agree_policy != "1":
        return html_response(
            templates,
            "download.html",
            _ctx(
                request,
                db,
                seo_page="download",
                download_consent=False,
                policy_error=translate(resolve_lang(request), "download.policy_error"),
            ),
            status_code=400,
        )
    if not get_content(db).download_url:
        lang = resolve_lang(request)
        email = get_content(db).support_email or config.SUPPORT_EMAIL
        return html_response(
            templates,
            "download.html",
            _ctx(
                request,
                db,
                seo_page="download",
                download_consent=False,
                policy_error=translate(lang, "download.policy_error_unavailable", email=email),
            ),
            status_code=503,
        )
    resp = RedirectResponse("/download/go", status_code=303)
    resp.set_cookie(
        config.DOWNLOAD_CONSENT_COOKIE,
        config.DOWNLOAD_CONSENT_VALUE,
        max_age=config.DOWNLOAD_CONSENT_MAX_AGE,
        httponly=True,
        samesite="lax",
        secure=config.SITE_URL.startswith("https://"),
    )
    return resp


@router.get("/download/go")
def download_go(request: Request, db: Session = Depends(get_db)):
    content = get_content(db)
    url = (content.download_url or "").strip()
    if not url:
        return RedirectResponse("/download", status_code=303)
    if not _download_consent_ok(request):
        return RedirectResponse("/download", status_code=303)
    return RedirectResponse(url, status_code=302)


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
            seo_page="buy",
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
            seo_page="buy_success",
            email=email.strip(),
        ),
    )
