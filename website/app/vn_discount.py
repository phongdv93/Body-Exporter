"""VietQR / SePay bank-transfer discount codes (not Paddle)."""

from __future__ import annotations

from dataclasses import dataclass

from app import config
from app.sepay import parse_qr_base


@dataclass(frozen=True)
class VnDiscount:
    code: str
    percent_off: int


def unit_price_vnd(content) -> int:
    base = (getattr(content, "sepay_qr_base_url", None) or "").strip() or config.SEPAY_QR_BASE_URL
    info = parse_qr_base(base.strip()) if base else {}
    n = info.get("amount_vnd")
    if isinstance(n, int) and n > 0:
        return n
    return int(getattr(content, "license_price_vnd", None) or config.LICENSE_PRICE_VND or 0)


def _parse_discount_blob(raw: str) -> list[VnDiscount]:
    out: list[VnDiscount] = []
    seen: set[str] = set()
    for chunk in (raw or "").replace(",", "\n").splitlines():
        line = chunk.strip()
        if not line or line.startswith("#"):
            continue
        if ":" in line:
            code_part, pct_part = line.split(":", 1)
        elif "=" in line:
            code_part, pct_part = line.split("=", 1)
        else:
            continue
        code = code_part.strip().upper()
        if not code or len(code) > 40:
            continue
        try:
            pct = int(pct_part.strip().rstrip("%"))
        except ValueError:
            continue
        pct = max(1, min(100, pct))
        if code in seen:
            continue
        seen.add(code)
        out.append(VnDiscount(code=code, percent_off=pct))
    return out


def list_discounts(content) -> list[VnDiscount]:
    merged: dict[str, VnDiscount] = {}
    for blob in (
        config.VN_DISCOUNT_CODES,
        getattr(content, "vn_discount_codes", None) or "",
    ):
        for d in _parse_discount_blob(blob):
            merged[d.code] = d
    return list(merged.values())


def lookup_discount(code: str, content) -> VnDiscount | None:
    key = (code or "").strip().upper()
    if not key:
        return None
    for d in list_discounts(content):
        if d.code == key:
            return d
    return None


def apply_percent_off(subtotal_vnd: int, percent_off: int) -> int:
    if subtotal_vnd <= 0:
        return 0
    pct = max(0, min(100, int(percent_off)))
    if pct <= 0:
        return subtotal_vnd
    if pct >= 100:
        return 0
    return int(round(subtotal_vnd * (100 - pct) / 100))


def checkout_amount_vnd(
    content,
    *,
    years: int,
    discount_code: str | None = None,
) -> tuple[int, int, VnDiscount | None]:
    """Return (amount_vnd, subtotal_vnd, discount or None)."""
    unit = unit_price_vnd(content)
    y = max(1, min(int(years or 1), config.MAX_LICENSE_YEARS))
    subtotal = unit * y
    disc = lookup_discount(discount_code or "", content) if discount_code else None
    if disc:
        return apply_percent_off(subtotal, disc.percent_off), subtotal, disc
    return subtotal, subtotal, None


def all_allowed_amounts_vnd(content) -> list[int]:
    amounts: set[int] = set()
    unit = unit_price_vnd(content)
    if unit <= 0:
        amounts.add(990000)
        return sorted(amounts)
    percents = {0}
    for d in list_discounts(content):
        percents.add(d.percent_off)
    for y in range(1, config.MAX_LICENSE_YEARS + 1):
        subtotal = unit * y
        for pct in percents:
            amounts.add(apply_percent_off(subtotal, pct))
    legacy = (config.SEPAY_LEGACY_AMOUNTS_VND or "").strip()
    if legacy:
        for part in legacy.split(","):
            part = part.strip()
            if part.isdigit():
                amounts.add(int(part))
    return sorted(a for a in amounts if a >= 0)


def years_from_transfer_amount(content, amount_vnd: float) -> int:
    """Match paid amount to license years (full or discounted)."""
    unit = unit_price_vnd(content)
    if unit <= 0:
        return 1
    amt = int(round(float(amount_vnd)))
    percents = {0}
    for d in list_discounts(content):
        percents.add(d.percent_off)
    for y in range(config.MAX_LICENSE_YEARS, 0, -1):
        subtotal = unit * y
        for pct in percents:
            if apply_percent_off(subtotal, pct) == amt:
                return y
        if subtotal == amt:
            return y
    fallback = int(round(amt / unit)) if unit else 1
    return max(1, min(fallback, config.MAX_LICENSE_YEARS))
