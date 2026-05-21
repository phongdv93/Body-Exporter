from datetime import datetime

from sqlalchemy import BigInteger, Boolean, DateTime, Integer, String, Text
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class SiteContent(Base):
    """Singleton row id=1: editable marketing + download + payment copy."""

    __tablename__ = "be_site_content"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, default=1)
    hero_title: Mapped[str] = mapped_column(String(200), default="SolidWorks Body Exporter")
    hero_subtitle: Mapped[str] = mapped_column(Text, default="")
    hero_subtitle_en: Mapped[str] = mapped_column(Text, default="")
    hero_bullets: Mapped[str] = mapped_column(Text, default="")  # one feature per line
    hero_bullets_en: Mapped[str] = mapped_column(Text, default="")
    about_html: Mapped[str] = mapped_column(Text, default="")
    about_html_en: Mapped[str] = mapped_column(Text, default="")
    download_version: Mapped[str] = mapped_column(String(40), default="0.8.0")
    download_url: Mapped[str] = mapped_column(String(500), default="")
    download_notes: Mapped[str] = mapped_column(Text, default="")
    buy_intro: Mapped[str] = mapped_column(Text, default="")
    buy_intro_en: Mapped[str] = mapped_column(Text, default="")
    buy_footer: Mapped[str] = mapped_column(Text, default="")
    buy_footer_en: Mapped[str] = mapped_column(Text, default="")
    sepay_qr_base_url: Mapped[str] = mapped_column(String(500), default="")
    license_price_vnd: Mapped[int] = mapped_column(Integer, default=1590000)
    support_email: Mapped[str] = mapped_column(String(120), default="hotro@bodyexporter.com")
    author_name: Mapped[str] = mapped_column(String(120), default="Gió")
    sepay_pg_merchant_id: Mapped[str] = mapped_column(String(120), default="")
    sepay_pg_secret_key: Mapped[str] = mapped_column(String(300), default="")
    sepay_pg_env: Mapped[str] = mapped_column(String(20), default="sandbox")
    sepay_webhook_secret: Mapped[str] = mapped_column(String(300), default="")
    sepay_webhook_api_key: Mapped[str] = mapped_column(String(300), default="")
    license_term_days: Mapped[int] = mapped_column(Integer, default=365)
    updated_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow, onupdate=datetime.utcnow)


class License(Base):
    """Issued licenses — CRM + sync with Cloudflare Worker KV when configured."""

    __tablename__ = "be_licenses"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    license_key: Mapped[str] = mapped_column(String(40), unique=True, index=True)
    buyer_email: Mapped[str] = mapped_column(String(200), index=True, default="")
    plan: Mapped[str] = mapped_column(String(32), default="personal")
    purchased_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow)
    expires_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    machine_fingerprint: Mapped[str | None] = mapped_column(String(256), nullable=True)
    sepay_transaction_id: Mapped[int | None] = mapped_column(BigInteger, nullable=True, unique=True)
    revoked: Mapped[bool] = mapped_column(Boolean, default=False)
    notes: Mapped[str] = mapped_column(Text, default="")


class ClientMachine(Base):
    """Plugin install / heartbeat — one row per machine fingerprint."""

    __tablename__ = "be_client_machines"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    machine_id: Mapped[str] = mapped_column(String(128), unique=True, index=True)
    hostname: Mapped[str] = mapped_column(String(128), default="")
    plugin_version: Mapped[str] = mapped_column(String(40), default="")
    sw_version: Mapped[str] = mapped_column(String(40), default="")
    first_seen_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow)
    last_seen_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow, index=True)
    last_ip: Mapped[str | None] = mapped_column(String(45), nullable=True)
    country_code: Mapped[str | None] = mapped_column(String(8), nullable=True)
    country_name: Mapped[str | None] = mapped_column(String(80), nullable=True)
    region: Mapped[str | None] = mapped_column(String(80), nullable=True)
    city: Mapped[str | None] = mapped_column(String(80), nullable=True)
    license_status: Mapped[str] = mapped_column(String(32), default="unknown")
    has_purchased_license: Mapped[bool] = mapped_column(Boolean, default=False)
    last_event: Mapped[str] = mapped_column(String(32), default="ping")


class AdminUser(Base):
    __tablename__ = "be_admin_users"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    username: Mapped[str] = mapped_column(String(80), unique=True)
    password_hash: Mapped[str] = mapped_column(String(200))
