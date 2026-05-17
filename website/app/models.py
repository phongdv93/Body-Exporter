from datetime import datetime

from sqlalchemy import DateTime, Integer, String, Text
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class SiteContent(Base):
    """Singleton row id=1: editable marketing + download + payment copy."""

    __tablename__ = "site_content"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, default=1)
    hero_title: Mapped[str] = mapped_column(String(200), default="SolidWorks Body Exporter")
    hero_subtitle: Mapped[str] = mapped_column(Text, default="")
    hero_bullets: Mapped[str] = mapped_column(Text, default="")  # one feature per line
    about_html: Mapped[str] = mapped_column(Text, default="")
    download_version: Mapped[str] = mapped_column(String(40), default="0.7.3")
    download_url: Mapped[str] = mapped_column(String(500), default="")
    download_notes: Mapped[str] = mapped_column(Text, default="")
    buy_intro: Mapped[str] = mapped_column(Text, default="")
    buy_footer: Mapped[str] = mapped_column(Text, default="")
    sepay_qr_base_url: Mapped[str] = mapped_column(String(500), default="")
    license_price_vnd: Mapped[int] = mapped_column(Integer, default=1590000)
    support_email: Mapped[str] = mapped_column(String(120), default="hotro@bodyexporter.com")
    updated_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow, onupdate=datetime.utcnow)


class AdminUser(Base):
    __tablename__ = "admin_users"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    username: Mapped[str] = mapped_column(String(80), unique=True)
    password_hash: Mapped[str] = mapped_column(String(200))
