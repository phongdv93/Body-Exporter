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
        "SolidWorks, add-in, Body Exporter, xuat body, Excel, license, plugin CAD, "
        "go, nesting, Viet Nam, SePay",
    )
    .strip()
)
SEO_OG_IMAGE = os.getenv("SEO_OG_IMAGE", "").strip()

SEPAY_QR_BASE_URL = os.getenv(
    "SEPAY_QR_BASE_URL",
    "https://qr.sepay.vn/img?bank=ACB&acc=4518527&amount=1590000&des=Body%20Export%20License",
)
LICENSE_PRICE_VND = int(os.getenv("LICENSE_PRICE_VND", "1590000"))

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

RESEND_API_KEY = os.getenv("RESEND_API_KEY", "").strip()
RESEND_FROM = os.getenv("RESEND_FROM", "Body Exporter <noreply@bodyexporter.com>").strip()
