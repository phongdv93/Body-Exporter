import os
from pathlib import Path

from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[1]  # website/
TEMPLATES_DIR = ROOT / "templates"
STATIC_DIR = ROOT / "static"
load_dotenv(ROOT / ".env")

# Database: set DATABASE_URL for production Postgres (Railway, Neon, Supabase, Fly, ...).
# If unset, falls back to SQLite at data/site.db (local dev).
DATABASE_URL = os.getenv("DATABASE_URL", "").strip()
DATA_DIR = ROOT / "data"
UPLOAD_DIR = ROOT / "uploads"
DB_PATH = DATA_DIR / "site.db"

SECRET_KEY = os.getenv("SECRET_KEY", "dev-only-change-in-production")
ADMIN_USERNAME = os.getenv("ADMIN_USERNAME", "admin").strip()
ADMIN_PASSWORD = os.getenv("ADMIN_PASSWORD", "admin")

SITE_URL = os.getenv("SITE_URL", "http://127.0.0.1:8080").rstrip("/")
SUPPORT_EMAIL = os.getenv("SUPPORT_EMAIL", "hotro@bodyexporter.com")
AUTHOR_NAME = os.getenv("AUTHOR_NAME", "Gió").strip() or "Gió"

# SEO (fallback when hero subtitle empty)
SEO_DESCRIPTION = (
    os.getenv(
        "SEO_DESCRIPTION",
        "Body Exporter — add-in SolidWorks xuat thong tin body/part, Excel & template, "
        "license online. Ho tro xuong go, nesting.",
    )
    .strip()
)
SEO_KEYWORDS = (
    os.getenv(
        "SEO_KEYWORDS",
        "SolidWorks, add-in, Body Exporter, body export, BOM export, part export, "
        "xuat body, xuat BOM, xuat BOM tu SolidWorks, xuat danh sach chi tiet tu SolidWorks, "
        "xuất BOM từ SolidWorks, xuất danh sách chi tiết từ SolidWorks, xuất body từ SolidWorks, "
        "Excel, license, plugin CAD, go, nesting, Viet Nam, SePay",
    )
    .strip()
)
SEO_DESCRIPTION_EN = (
    os.getenv(
        "SEO_DESCRIPTION_EN",
        "Body Exporter — SolidWorks add-in to export body/part data to Excel and templates. "
        "Online licensing, 14-day trial, woodworking and nesting workflows.",
    )
    .strip()
)
SEO_KEYWORDS_EN = (
    os.getenv(
        "SEO_KEYWORDS_EN",
        "SolidWorks, add-in, Body Exporter, body export, BOM export, export body, export BOM, "
        "export bodies, export part list, export detail list, export BOM from SolidWorks, "
        "export body from SolidWorks, Excel, license, CAD plugin, woodworking, nesting, "
        "bill of materials",
    )
    .strip()
)
SEO_OG_IMAGE = os.getenv("SEO_OG_IMAGE", "").strip()

SEPAY_QR_BASE_URL = os.getenv(
    "SEPAY_QR_BASE_URL",
    "https://qr.sepay.vn/img?bank=ACB&acc=4518527&amount=1590000&des=Body%20Export%20License",
)
LICENSE_PRICE_VND = int(os.getenv("LICENSE_PRICE_VND", "1590000"))
# Max license years per checkout (VietQR amount multiple + Paddle quantity).
MAX_LICENSE_YEARS = max(1, min(10, int(os.getenv("MAX_LICENSE_YEARS", "5"))))
# Shown on /buy for international checkout (Paddle, etc.). Override or leave empty to derive from VND.
_license_usd = os.getenv("LICENSE_PRICE_USD", "").strip()
USD_VND_RATE = float(os.getenv("USD_VND_RATE", "25000") or "25000")


def license_price_usd_display(price_vnd: int | None = None) -> str:
    vnd = int(price_vnd or LICENSE_PRICE_VND)
    if _license_usd:
        try:
            return f"{float(_license_usd):.2f}".rstrip("0").rstrip(".")
        except ValueError:
            pass
    if USD_VND_RATE <= 0:
        return ""
    return f"{vnd / USD_VND_RATE:.2f}".rstrip("0").rstrip(".")

SEPAY_PG_MERCHANT_ID = os.getenv("SEPAY_PG_MERCHANT_ID", "").strip()
SEPAY_PG_SECRET_KEY = os.getenv("SEPAY_PG_SECRET_KEY", "").strip()
SEPAY_PG_ENV = os.getenv("SEPAY_PG_ENV", "sandbox").strip().lower()

def _normalize_http_base_url(url: str) -> str:
    u = (url or "").strip().rstrip("/")
    if not u:
        return ""
    if not u.lower().startswith(("http://", "https://")):
        u = "https://" + u
    return u


# Cloudflare Worker — mint licenses for online validation (POST /admin/license/issue)
WORKER_API_BASE_URL = _normalize_http_base_url(os.getenv("WORKER_API_BASE_URL", ""))
WORKER_ADMIN_TOKEN = os.getenv("WORKER_ADMIN_TOKEN", "").strip()
SEPAY_LICENSE_DAYS = int(os.getenv("SEPAY_LICENSE_DAYS", "365"))

SEPAY_WEBHOOK_SECRET = os.getenv("SEPAY_WEBHOOK_SECRET", "").strip()
SEPAY_WEBHOOK_API_KEY = os.getenv("SEPAY_WEBHOOK_API_KEY", "").strip()
# Comma-separated VND amounts still accepted after a price change (e.g. 990000)
SEPAY_LEGACY_AMOUNTS_VND = os.getenv("SEPAY_LEGACY_AMOUNTS_VND", "").strip()
# VietQR discount codes: CODE:percent per line or comma (e.g. TESTRUN:99). Admin DB field overrides/extends.
VN_DISCOUNT_CODES = os.getenv("VN_DISCOUNT_CODES", "").strip()

RESEND_API_KEY = os.getenv("RESEND_API_KEY", "").strip()
RESEND_FROM = os.getenv("RESEND_FROM", "Body Exporter <noreply@bodyexporter.com>").strip()
# Published Resend templates — variables: name, license_key, plan, expires
RESEND_LICENSE_TEMPLATE_ID = os.getenv("RESEND_LICENSE_TEMPLATE_ID", "").strip()
RESEND_LICENSE_TEMPLATE_ID_VI = os.getenv(
    "RESEND_LICENSE_TEMPLATE_ID_VI", RESEND_LICENSE_TEMPLATE_ID
).strip()
RESEND_LICENSE_TEMPLATE_ID_EN = os.getenv("RESEND_LICENSE_TEMPLATE_ID_EN", "").strip()
RESEND_LICENSE_SUBJECT = os.getenv(
    "RESEND_LICENSE_SUBJECT", "License key Body Exporter — SolidWorks"
).strip()
RESEND_LICENSE_SUBJECT_VI = os.getenv(
    "RESEND_LICENSE_SUBJECT_VI", RESEND_LICENSE_SUBJECT
).strip()
RESEND_LICENSE_SUBJECT_EN = os.getenv(
    "RESEND_LICENSE_SUBJECT_EN", "Your Body Exporter license key — SolidWorks"
).strip()

# Admin dashboard: machine "still in use" vs "likely removed" (days since last ping)
TELEMETRY_ACTIVE_DAYS = max(1, int(os.getenv("TELEMETRY_ACTIVE_DAYS", "14")))
TELEMETRY_INACTIVE_DAYS = max(TELEMETRY_ACTIVE_DAYS + 1, int(os.getenv("TELEMETRY_INACTIVE_DAYS", "90")))

# Cookie set after user accepts data policy on /download (required before ZIP)
DOWNLOAD_CONSENT_COOKIE = "be_dl_consent"
DOWNLOAD_CONSENT_VALUE = "v1"
DOWNLOAD_CONSENT_MAX_AGE = 60 * 60 * 24 * 730  # ~2 years

# Paddle Billing (international checkout on /buy)
PADDLE_CLIENT_TOKEN = os.getenv("PADDLE_CLIENT_TOKEN", "").strip()
PADDLE_API_KEY = os.getenv("PADDLE_API_KEY", "").strip()
PADDLE_WEBHOOK_SECRET = os.getenv("PADDLE_WEBHOOK_SECRET", "").strip()
PADDLE_PRICE_ID = os.getenv("PADDLE_PRICE_ID", "").strip()
_paddle_env_raw = os.getenv("PADDLE_ENV", "sandbox").strip().lower()
PADDLE_ENV = _paddle_env_raw if _paddle_env_raw in ("sandbox", "production") else "sandbox"

# Public /buy: VietQR bank transfer + SePay PG card. Set true to re-enable Vietnam checkout.
SEPAY_PUBLIC_ENABLED = os.getenv("SEPAY_PUBLIC_ENABLED", "false").strip().lower() in (
    "1",
    "true",
    "yes",
    "on",
)


def sepay_public_checkout_enabled() -> bool:
    return SEPAY_PUBLIC_ENABLED
