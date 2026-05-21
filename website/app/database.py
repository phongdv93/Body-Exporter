import logging
import re

from sqlalchemy import create_engine, inspect, select, text
from sqlalchemy.orm import Session, sessionmaker

from app import config
from app.models import AdminUser, Base, ClientMachine, License, SiteContent

_BE_LICENSES_REQUIRED_COLUMNS = frozenset(
    {
        "id",
        "license_key",
        "buyer_email",
        "plan",
        "purchased_at",
        "expires_at",
        "machine_fingerprint",
        "sepay_transaction_id",
        "revoked",
        "notes",
    }
)

log = logging.getLogger("uvicorn.error")

_LEGACY_TABLE_RENAMES = (
    ("site_content", "be_site_content"),
    ("admin_users", "be_admin_users"),
    ("licenses", "be_licenses"),
)

_SITE_CONTENT_ALTER = [
    ("sepay_pg_merchant_id", "VARCHAR(120) DEFAULT ''"),
    ("sepay_pg_secret_key", "VARCHAR(300) DEFAULT ''"),
    ("sepay_pg_env", "VARCHAR(20) DEFAULT 'sandbox'"),
    ("sepay_webhook_secret", "VARCHAR(300) DEFAULT ''"),
    ("sepay_webhook_api_key", "VARCHAR(300) DEFAULT ''"),
    ("license_term_days", "INTEGER DEFAULT 365"),
    ("author_name", "VARCHAR(120) DEFAULT 'Gió'"),
    ("hero_subtitle_en", "TEXT DEFAULT ''"),
    ("hero_bullets_en", "TEXT DEFAULT ''"),
    ("about_html_en", "TEXT DEFAULT ''"),
    ("buy_intro_en", "TEXT DEFAULT ''"),
    ("buy_footer_en", "TEXT DEFAULT ''"),
]


def _table_columns(insp, name: str) -> set[str]:
    try:
        return {c["name"] for c in insp.get_columns(name)}
    except Exception:
        return set()


def _legacy_site_content_ours(insp, old: str) -> bool:
    c = _table_columns(insp, old)
    return "hero_title" in c and "download_version" in c


def _legacy_admin_users_ours(insp, old: str) -> bool:
    c = _table_columns(insp, old)
    return "password_hash" in c and "username" in c


def _legacy_licenses_ours(insp, old: str) -> bool:
    """Avoid renaming another app's ``licenses`` table when sharing one Postgres."""
    c = _table_columns(insp, old)
    return "license_key" in c and "buyer_email" in c


_LEGACY_RENAME_GUARDS = {
    "site_content": _legacy_site_content_ours,
    "admin_users": _legacy_admin_users_ours,
    "licenses": _legacy_licenses_ours,
}


def rename_legacy_tables() -> None:
    """One-time rename from unprefixed tables (older Body Exporter installs only)."""
    try:
        insp = inspect(engine)
        names = set(insp.get_table_names())
    except Exception as ex:
        log.warning("rename_legacy_tables: inspect failed: %s", ex)
        return

    for old, new in _LEGACY_TABLE_RENAMES:
        if old not in names or new in names:
            continue
        guard = _LEGACY_RENAME_GUARDS.get(old)
        if guard and not guard(insp, old):
            log.warning(
                "Skip rename %s -> %s: table is not Body Exporter schema (shared DB?) — using %s as new table",
                old,
                new,
                new,
            )
            continue
        stmt = f"ALTER TABLE {old} RENAME TO {new}"
        log.info("DB migrate: %s", stmt)
        with engine.begin() as conn:
            conn.execute(text(stmt))
        names.discard(old)
        names.add(new)


def ensure_be_client_machines_table() -> None:
    """Create be_client_machines if missing (model added after first deploy)."""
    try:
        insp = inspect(engine)
        if "be_client_machines" in insp.get_table_names():
            return
        log.info("Creating be_client_machines table")
        ClientMachine.__table__.create(engine, checkfirst=True)
    except Exception:
        log.exception("ensure_be_client_machines_table failed")


def ensure_be_licenses_table() -> None:
    """Recreate be_licenses if it exists with wrong schema (shared DB / bad rename)."""
    try:
        insp = inspect(engine)
        if "be_licenses" not in insp.get_table_names():
            return
        have = _table_columns(insp, "be_licenses")
        if _BE_LICENSES_REQUIRED_COLUMNS.issubset(have):
            return
        log.warning(
            "be_licenses schema mismatch (columns: %s) — dropping and recreating Body Exporter table",
            sorted(have),
        )
        with engine.begin() as conn:
            conn.execute(text("DROP TABLE IF EXISTS be_licenses"))
        License.__table__.create(engine, checkfirst=True)
        log.info("be_licenses recreated with correct schema")
    except Exception:
        log.exception("ensure_be_licenses_table failed")


def ensure_schema() -> None:
    insp = inspect(engine)
    names = insp.get_table_names()
    if "be_site_content" not in names:
        return
    have = {c["name"] for c in insp.get_columns("be_site_content")}
    for col, ddl in _SITE_CONTENT_ALTER:
        if col in have:
            continue
        stmt = f"ALTER TABLE be_site_content ADD COLUMN {col} {ddl}"
        log.info("DB migrate: %s", stmt)
        with engine.begin() as conn:
            conn.execute(text(stmt))

config.UPLOAD_DIR.mkdir(parents=True, exist_ok=True)


def _normalize_postgres_url(url: str) -> str:
    u = url.strip()
    if u.startswith("postgres://"):
        u = "postgresql+psycopg2://" + u[len("postgres://") :]
    elif u.startswith("postgresql://") and not re.match(r"^postgresql\+[^:]+://", u):
        u = "postgresql+psycopg2://" + u[len("postgresql://") :]
    return u


def _create_engine():
    if config.DATABASE_URL:
        url = _normalize_postgres_url(config.DATABASE_URL)
        log.info("Using PostgreSQL database")
        return create_engine(url, pool_pre_ping=True, pool_size=5, max_overflow=10)

    config.DATA_DIR.mkdir(parents=True, exist_ok=True)
    log.info("Using SQLite at %s", config.DB_PATH)
    return create_engine(
        f"sqlite:///{config.DB_PATH}",
        connect_args={"check_same_thread": False},
    )


engine = _create_engine()
SessionLocal = sessionmaker(bind=engine, autocommit=False, autoflush=False)


def init_db() -> None:
    rename_legacy_tables()
    Base.metadata.create_all(bind=engine)
    ensure_be_licenses_table()
    ensure_be_client_machines_table()
    ensure_schema()
    with SessionLocal() as db:
        if not db.get(SiteContent, 1):
            db.add(
                SiteContent(
                    id=1,
                    hero_subtitle=(
                        "Xuất thông tin body/part từ SolidWorks — Excel, template, workflow nhanh cho xưởng mộc."
                    ),
                    hero_bullets=(
                        "Kéo thả kích thước L×W×T\n"
                        "Export Excel & template {{placeholder}}\n"
                        "License online — trial 14 ngày"
                    ),
                    about_html=(
                        "<p>Body Exporter là add-in SolidWorks giúp bạn liệt kê và xuất body "
                        "theo format sẵn cho sản xuất.</p>"
                    ),
                    download_notes="",
                    buy_intro=(
                        "Nhập email để nhận license tự động sau khi thanh toán. "
                        "Chọn chuyển khoản QR hoặc thẻ (SePay) bên dưới."
                    ),
                    buy_footer=(
                        "Sau khi chuyển khoản đúng số tiền và nội dung CK, license gửi về email trong vài phút. "
                        "Cần hỗ trợ: hotro@bodyexporter.com"
                    ),
                    sepay_qr_base_url=config.SEPAY_QR_BASE_URL,
                    license_price_vnd=config.LICENSE_PRICE_VND,
                    support_email=config.SUPPORT_EMAIL,
                    author_name=config.AUTHOR_NAME,
                    sepay_pg_merchant_id=config.SEPAY_PG_MERCHANT_ID,
                    sepay_pg_secret_key=config.SEPAY_PG_SECRET_KEY,
                    sepay_pg_env=config.SEPAY_PG_ENV or "sandbox",
                    sepay_webhook_secret=config.SEPAY_WEBHOOK_SECRET,
                    sepay_webhook_api_key=config.SEPAY_WEBHOOK_API_KEY,
                    license_term_days=max(1, config.SEPAY_LICENSE_DAYS),
                )
            )
            db.commit()

        admin = db.scalar(select(AdminUser).where(AdminUser.username == config.ADMIN_USERNAME.strip()))
        if not admin:
            from passlib.context import CryptContext

            pwd = CryptContext(schemes=["bcrypt"], deprecated="auto")
            db.add(
                AdminUser(
                    username=config.ADMIN_USERNAME.strip(),
                    password_hash=pwd.hash(config.ADMIN_PASSWORD),
                )
            )
            db.commit()


def get_content(db: Session) -> SiteContent:
    row = db.get(SiteContent, 1)
    if not row:
        init_db()
        row = db.get(SiteContent, 1)
    return row
