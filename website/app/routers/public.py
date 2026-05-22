import json
import re

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse, JSONResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db
from app.database import get_content
from app.download_tracking import record_plugin_download
from app.geo_pay import (
    PAY_MODE_COOKIE,
    PAY_MODE_MAX_AGE,
    geo_default_pay_mode,
    pay_mode_cookie_value,
    resolve_pay_mode,
)
from app.legal_pages import legal_html, legal_page_title
from app.paddle_billing import (
    create_paddle_checkout_transaction,
    paddle_checkout_settings,
    paddle_configured,
)
from app.i18n import (
    LANG_COOKIE,
    LANG_COOKIE_MAX_AGE,
    cms_bullets,
    cms_html,
    cms_text,
    normalize_lang,
    page_meta_from_cms,
    resolve_lang,
    safe_redirect_path,
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
        meta_desc, default_title = page_meta_from_cms(lang, content, seo_page)
        title = page_title or default_title
    else:
        meta_desc, default_title = page_meta_from_cms(lang, content, "home")
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
        "install_notes_extra": _install_notes_extra(cms_html(content, "download_notes", lang)),
        "hero_subtitle": cms_text(content, "hero_subtitle", lang),
        "about_html": cms_html(content, "about_html", lang),
        "buy_title": cms_text(content, "buy_title", lang),
        "buy_intro": cms_html(content, "buy_intro", lang),
        "buy_form_note": cms_html(content, "buy_form_note", lang),
        "buy_payment_note": cms_html(content, "buy_payment_note", lang),
        "buy_footer": cms_html(content, "buy_footer", lang),
        "buy_success_title": cms_text(content, "buy_success_title", lang),
        "buy_success_html": cms_html(content, "buy_success_html", lang),
        "download_intro": cms_text(content, "download_intro", lang),
        "download_policy_html": cms_html(content, "download_policy_html", lang),
        "download_guides_html": cms_html(content, "download_guides_html", lang),
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
    bullets = cms_bullets(content, lang)
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
    record_plugin_download(request, db, content)
    return RedirectResponse(url, status_code=302)


@router.get("/buy")
def buy_get(request: Request, email: str = "", pay: str = "", db: Session = Depends(get_db)):
    return _buy_response(request, db, email=email.strip(), pay_query=pay)


def _vietqr_payload(content, email: str) -> dict | None:
    email = (email or "").strip()
    if not email or "@" not in email:
        return None
    base = (content.sepay_qr_base_url or config.SEPAY_QR_BASE_URL or "").strip()
    if not base:
        return None
    bank = parse_qr_base(base)
    amount = content.license_price_vnd or config.LICENSE_PRICE_VND
    if bank.get("amount_vnd"):
        amount = bank["amount_vnd"]
    return {
        "qr_url": build_qr_image_url(base, email),
        "memo": build_transfer_memo(email),
        "bank": bank,
        "amount_vnd": amount,
        "amount_fmt": "{:,}".format(amount).replace(",", "."),
    }


@router.get("/buy/api/vietqr")
def buy_vietqr_api(request: Request, email: str = "", db: Session = Depends(get_db)):
    content = get_content(db)
    payload = _vietqr_payload(content, email)
    if not payload:
        return JSONResponse({"ok": False, "error": "invalid"}, status_code=400)
    lang = resolve_lang(request)
    return JSONResponse(
        {
            "ok": True,
            **payload,
            "wait_hint_html": translate(
                lang, "buy.wait_hint", email=email.strip(), memo=payload["memo"]
            ),
            "labels": {
                "title": translate(lang, "buy.transfer_title", amount=payload["amount_fmt"]),
                "bank": translate(lang, "buy.bank"),
                "account": translate(lang, "buy.account"),
                "amount": translate(lang, "buy.amount"),
                "memo": translate(lang, "buy.memo"),
            },
        }
    )


@router.post("/buy/api/paddle-checkout")
async def buy_paddle_checkout_api(request: Request, db: Session = Depends(get_db)):
    try:
        body = await request.json()
    except Exception:
        body = {}
    email = (body.get("email") or "").strip()
    if not email or "@" not in email:
        return JSONResponse({"ok": False, "error": "invalid_email"}, status_code=400)
    if not paddle_configured():
        return JSONResponse({"ok": False, "error": "not_configured"}, status_code=503)
    result = create_paddle_checkout_transaction(email)
    txn_id = result.get("transaction_id")
    if txn_id:
        return JSONResponse(
            {
                "ok": True,
                "transaction_id": txn_id,
                "checkout_url": result.get("checkout_url"),
            }
        )
    err = result.get("error") or "unknown"
    err_code = result.get("error_code")
    status = 503 if err == "paddle_api_not_configured" else 400
    return JSONResponse(
        {
            "ok": False,
            "error": err,
            "error_code": err_code,
            "checkout_url": result.get("checkout_url"),
            "use_client_price": err_code != "transaction_default_checkout_url_not_set",
        },
        status_code=status,
    )


@router.post("/buy")
def buy_post(
    request: Request,
    email: str = Form(...),
    pay_method: str = Form("qr"),
    db: Session = Depends(get_db),
):
    email = email.strip()
    if pay_method == "card" and pg_checkout_available_for_content(get_content(db)) and not paddle_configured():
        return _redirect_pg_checkout(request, email, db)
    return _buy_response(request, db, email=email)


def _redirect_pg_checkout(request: Request, email: str, db: Session):
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
    lang = resolve_lang(request)
    wait_msg = translate(lang, "buy.redirect_sepay")
    page = f"""<!DOCTYPE html><html lang="{lang}"><head><meta charset="utf-8"><title>Redirect…</title>
    <link rel="stylesheet" href="/static/css/site.css"></head>
    <body class="redirect-page"><p>{html.escape(wait_msg)}</p>
    <form id="f" method="POST" action="{action}">{inputs}</form>
    <script>document.getElementById('f').submit();</script></body></html>"""
    return HTMLResponse(page)


def _buy_response(request: Request, db: Session, email: str = "", pay_query: str = ""):
    content = get_content(db)
    base = content.sepay_qr_base_url or config.SEPAY_QR_BASE_URL
    bank = parse_qr_base(base)
    amount = content.license_price_vnd or config.LICENSE_PRICE_VND
    if bank.get("amount_vnd"):
        amount = bank["amount_vnd"]
    qr_url = build_qr_image_url(base, email) if email else ""
    memo = build_transfer_memo(email) if email else ""
    term_days = max(1, int(content.license_term_days or config.SEPAY_LICENSE_DAYS))
    price_usd = config.license_price_usd_display(amount)
    geo_default = geo_default_pay_mode(request)
    vietqr_ok = bool((base or "").strip())
    paddle_ok = paddle_configured()
    pg_ok = pg_checkout_available_for_content(content)
    pay_mode = resolve_pay_mode(request, query_override=pay_query)
    if pay_mode == "intl" and not paddle_ok and not pg_ok:
        pay_mode = "vn"
    if pay_mode == "vn" and not vietqr_ok and (paddle_ok or pg_ok):
        pay_mode = "intl"

    resp = html_response(
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
            license_term_days=term_days,
            price_usd=price_usd,
            pay_mode=pay_mode,
            geo_default_pay_mode=geo_default,
            vietqr_available=vietqr_ok,
            paddle_available=paddle_ok,
            paddle_checkout_json=json.dumps(paddle_checkout_settings()) if paddle_ok else "",
            pg_available=pg_ok and not paddle_ok,
        ),
    )
    cookie_val = pay_mode_cookie_value(pay_query) or pay_mode_cookie_value(pay_mode)
    if cookie_val:
        resp.set_cookie(
            PAY_MODE_COOKIE,
            cookie_val,
            max_age=PAY_MODE_MAX_AGE,
            httponly=False,
            samesite="lax",
            secure=config.SITE_URL.startswith("https://"),
        )
    return resp


def _legal_response(request: Request, db: Session, page: str):
    lang = resolve_lang(request)
    title = legal_page_title(lang, page)
    body = legal_html(lang, page)
    meta_desc = translate(lang, f"meta.{page}")
    return html_response(
        templates,
        "legal/page.html",
        _ctx(
            request,
            db,
            seo_page=page,
            page_title=title,
            meta_description=meta_desc[:320],
            legal_title=title,
            legal_html=body,
        ),
    )


@router.get("/terms-and-conditions")
def terms_and_conditions(request: Request, db: Session = Depends(get_db)):
    return _legal_response(request, db, "terms")


@router.get("/privacy")
def privacy_policy(request: Request, db: Session = Depends(get_db)):
    return _legal_response(request, db, "privacy")


@router.get("/refund")
def refund_policy(request: Request, db: Session = Depends(get_db)):
    return _legal_response(request, db, "refund")


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
