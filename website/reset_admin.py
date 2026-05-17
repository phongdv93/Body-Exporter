r"""
Reset admin password from .env (does not touch site content).

Usage (from website folder):
  python reset_admin.py
Or:
  .\reset-admin.ps1
"""
from pathlib import Path

# Ensure imports resolve
import sys

_root = Path(__file__).resolve().parent
if str(_root) not in sys.path:
    sys.path.insert(0, str(_root))

from dotenv import load_dotenv

load_dotenv(_root / ".env")

from passlib.context import CryptContext
from sqlalchemy import select

from app import config
from app.database import SessionLocal, init_db
from app.models import AdminUser

pwd = CryptContext(schemes=["bcrypt"], deprecated="auto")


def main() -> None:
    with SessionLocal() as db:
        user = db.scalar(
            select(AdminUser).where(AdminUser.username == config.ADMIN_USERNAME.strip())
        )
        if user:
            user.password_hash = pwd.hash(config.ADMIN_PASSWORD)
            db.commit()
            print(f"OK - updated password for: {config.ADMIN_USERNAME}")
            print("Log in again at /admin")
            return

        init_db()
        user2 = db.scalar(
            select(AdminUser).where(AdminUser.username == config.ADMIN_USERNAME.strip())
        )
        if user2:
            print(f"OK - created admin: {config.ADMIN_USERNAME}")
        else:
            print("Error - could not create admin. Check .env and data/site.db")


if __name__ == "__main__":
    main()
