"""Language resolution and translations for the public site."""

from __future__ import annotations

import re
from typing import Any
from urllib.parse import urlparse

from fastapi import Request

from app.locales import en as locale_en
from app.locales import vi as locale_vi
from app.models import SiteContent

SUPPORTED = frozenset({"vi", "en"})
DEFAULT_LANG = "vi"
LANG_COOKIE = "be_lang"
LANG_COOKIE_MAX_AGE = 60 * 60 * 24 * 400

_LOCALES = {"vi": locale_vi.MESSAGES, "en": locale_en.MESSAGES}


def normalize_lang(value: str | None) -> str:
    if value and value.lower() in SUPPORTED:
        return value.lower()
    return DEFAULT_LANG


def resolve_lang(request: Request) -> str:
    q = request.query_params.get("lang")
    if q:
        return normalize_lang(q)
    c = request.cookies.get(LANG_COOKIE)
    if c:
        return normalize_lang(c)
    accept = (request.headers.get("accept-language") or "").lower()
    if accept.startswith("en") or ",en" in accept:
        return "en"
    return DEFAULT_LANG


def translate(lang: str, key: str, **kwargs: Any) -> str:
    lang = normalize_lang(lang)
    table = _LOCALES.get(lang, _LOCALES[DEFAULT_LANG])
    text = table.get(key) or _LOCALES[DEFAULT_LANG].get(key) or key
    if kwargs:
        return text.format(**kwargs)
    return text


def safe_redirect_path(url: str | None) -> str:
    if not url:
        return "/"
    parsed = urlparse(url)
    if parsed.scheme or parsed.netloc:
        return "/"
    path = parsed.path or "/"
    if not path.startswith("/"):
        return "/"
    return path + (f"?{parsed.query}" if parsed.query else "")


def lang_url(path: str, lang: str) -> str:
    lang = normalize_lang(lang)
    base = path.split("?")[0] or "/"
    return f"/lang/{lang}?next={base}"


def _strip_html(html: str) -> str:
    return re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", html or "")).strip()


def cms_text(content: SiteContent, field: str, lang: str) -> str:
    """Marketing copy from be_site_content only (field / field_en). EN may fall back to VI column."""
    lang = normalize_lang(lang)
    if lang == "en":
        en_val = (getattr(content, f"{field}_en", None) or "").strip()
        if en_val:
            return en_val
    return (getattr(content, field, None) or "").strip()


def cms_html(content: SiteContent, field: str, lang: str) -> str:
    return cms_text(content, field, lang)


def cms_bullets(content: SiteContent, lang: str) -> list[str]:
    raw = cms_text(content, "hero_bullets", lang)
    return [line.strip() for line in raw.splitlines() if line.strip()]


def page_meta_from_cms(lang: str, content: SiteContent, page: str) -> tuple[str, str]:
    """Title from locale UI; description from CMS (Admin → Chỉnh nội dung)."""
    from app import config

    lang = normalize_lang(lang)
    page = (page or "home").strip().lower()
    if page == "buy":
        title = cms_text(content, "buy_title", lang) or translate(lang, "page.buy")
    elif page == "buy_success":
        title = cms_text(content, "buy_success_title", lang) or translate(lang, "page.buy_success")
    else:
        title = translate(lang, f"page.{page}")

    if page == "home":
        desc = cms_text(content, "hero_subtitle", lang)
    elif page == "buy":
        desc = _strip_html(cms_html(content, "buy_intro", lang)) or cms_text(content, "buy_title", lang)
        if not desc:
            desc = cms_text(content, "hero_subtitle", lang)
    elif page == "buy_success":
        desc = _strip_html(cms_html(content, "buy_success_html", lang)) or cms_text(content, "hero_subtitle", lang)
    elif page == "download":
        desc = _strip_html(cms_text(content, "download_intro", lang)) or cms_text(content, "hero_subtitle", lang)
    elif page in ("terms", "privacy", "refund"):
        desc = translate(lang, f"meta.{page}")
    else:
        desc = cms_text(content, "hero_subtitle", lang)

    if not desc:
        desc = (config.SEO_DESCRIPTION_EN if lang == "en" else config.SEO_DESCRIPTION) or ""
    return desc[:320], title


def schema_website_json(lang: str, meta_description: str) -> str:
    import json

    from app import config

    lang = normalize_lang(lang)
    loc = "en-US" if lang == "en" else "vi-VN"
    return json.dumps(
        {
            "@context": "https://schema.org",
            "@type": "WebSite",
            "name": "Body Exporter",
            "url": config.SITE_URL,
            "description": (meta_description or "")[:500],
            "inLanguage": loc,
        },
        ensure_ascii=False,
    )
