"""Geo + user preference for VietQR vs international (Paddle) checkout."""

from __future__ import annotations

from fastapi import Request

from app import config
from app.client_telemetry import _client_ip, lookup_geo

PAY_MODE_COOKIE = "be_pay_mode"
PAY_MODE_MAX_AGE = 60 * 60 * 24 * 90


def _country_from_headers(request: Request) -> str | None:
    for name in (
        "cf-ipcountry",
        "x-vercel-ip-country",
        "x-country-code",
        "cloudfront-viewer-country",
    ):
        raw = (request.headers.get(name) or "").strip().upper()
        if len(raw) == 2 and raw.isalpha():
            return raw
    return None


def is_vietnam_request(request: Request) -> bool:
    cc = _country_from_headers(request)
    if cc == "VN":
        return True
    if cc and cc != "XX":
        return False
    ip = _client_ip(request)
    geo = lookup_geo(ip)
    return (geo.get("country_code") or "").upper() == "VN"


def geo_default_pay_mode(request: Request) -> str:
    """vn = VietQR first; intl = Paddle/card first."""
    if not config.sepay_public_checkout_enabled():
        return "intl"
    if is_vietnam_request(request):
        return "vn"
    return "intl"


def resolve_pay_mode(request: Request, *, query_override: str | None = None) -> str:
    if not config.sepay_public_checkout_enabled():
        return "intl"
    q = (query_override or request.query_params.get("pay") or "").strip().lower()
    if q in ("vn", "intl", "vietqr", "international"):
        return "vn" if q in ("vn", "vietqr") else "intl"
    cookie = (request.cookies.get(PAY_MODE_COOKIE) or "").strip().lower()
    if cookie in ("vn", "intl"):
        return cookie
    return geo_default_pay_mode(request)


def pay_mode_cookie_value(mode: str) -> str | None:
    mode = (mode or "").strip().lower()
    return mode if mode in ("vn", "intl") else None
