import logging
import re

from sqlalchemy import create_engine, select
from sqlalchemy.orm import Session, sessionmaker

from app import config
from app.models import AdminUser, Base, SiteContent

log = logging.getLogger("uvicorn.error")

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
    Base.metadata.create_all(bind=engine)
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
                    download_notes=(
                        "1. Tải file ZIP.\n"
                        "2. Giải nén và chạy <strong>Install-BodyExporter.cmd</strong> (Run as administrator).\n"
                        "3. Mở SolidWorks → Tools → Add-Ins → bật <em>SolidWorks Body Exporter</em>.\n"
                        "4. Dùng shortcut Desktop hoặc menu add-in."
                    ),
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
