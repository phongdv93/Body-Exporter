"""Language resolution and translations for the public site."""

from __future__ import annotations

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


def localized_text(content: SiteContent, field: str, lang: str, *, default_key: str | None = None) -> str:
    """CMS text for active language only — EN never falls back to Vietnamese copy."""
    lang = normalize_lang(lang)
    if lang == "en":
        en_val = (getattr(content, f"{field}_en", None) or "").strip()
        if en_val:
            return en_val
        return translate(lang, default_key) if default_key else ""

    vi_val = (getattr(content, field, None) or "").strip()
    if vi_val:
        return vi_val
    return translate(lang, default_key) if default_key else ""


def localized_html_field(
    content: SiteContent, field: str, lang: str, *, default_key: str | None = None
) -> str:
    """HTML block for active language; optional locale default when CMS field empty."""
    lang = normalize_lang(lang)
    if lang == "en":
        en_val = (getattr(content, f"{field}_en", None) or "").strip()
        if en_val:
            return en_val
        return translate(lang, default_key) if default_key else ""

    return (getattr(content, field, None) or "").strip() or (
        translate(lang, default_key) if default_key else ""
    )


def page_meta(lang: str, page: str) -> tuple[str, str]:
    """Per-route title + meta description from locale files."""
    lang = normalize_lang(lang)
    page = (page or "home").strip().lower()
    title = translate(lang, f"page.{page}")
    desc = translate(lang, f"meta.{page}")
    return desc[:320], title


def localized_bullets(content: SiteContent, lang: str) -> list[str]:
    lang = normalize_lang(lang)
    if lang == "en":
        raw = (content.hero_bullets_en or "").strip() or translate(lang, "home.bullets_default")
    else:
        raw = (content.hero_bullets or "").strip() or translate(lang, "home.bullets_default")
    return [line.strip() for line in raw.splitlines() if line.strip()]


def seo_meta(lang: str, content: SiteContent) -> tuple[str, str]:
    from app import config

    lang = normalize_lang(lang)
    title = (content.hero_title or "Body Exporter").strip()
    subtitle = localized_text(content, "hero_subtitle", lang, default_key="home.hero_subtitle_default")
    if subtitle:
        return subtitle[:320], title
    if lang == "en":
        return (config.SEO_DESCRIPTION_EN or config.SEO_DESCRIPTION)[:320], title
    return (config.SEO_DESCRIPTION or "")[:320], title


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
