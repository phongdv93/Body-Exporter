from fastapi import Depends, HTTPException, Request
from passlib.context import CryptContext
from sqlalchemy import select
from sqlalchemy.orm import Session

from app import config
from app.database import SessionLocal
from app.models import AdminUser

pwd_context = CryptContext(schemes=["bcrypt"], deprecated="auto")
SESSION_KEY = "admin_user"


def get_db():
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()


def verify_admin(username: str, password: str, db: Session) -> bool:
    uname = (username or "").strip()
    user = db.scalar(select(AdminUser).where(AdminUser.username == uname))
    if not user:
        return False
    return pwd_context.verify(password, user.password_hash)


def login_session(request: Request, username: str) -> None:
    request.session[SESSION_KEY] = username


def logout_session(request: Request) -> None:
    request.session.pop(SESSION_KEY, None)


def require_admin(request: Request, db: Session = Depends(get_db)) -> AdminUser:
    username = request.session.get(SESSION_KEY)
    if not username:
        raise HTTPException(status_code=302, headers={"Location": "/admin/login"})
    user = db.scalar(select(AdminUser).where(AdminUser.username == username))
    if not user:
        raise HTTPException(status_code=302, headers={"Location": "/admin/login"})
    return user
