"""Admin CMS for blog posts + image uploads."""

from __future__ import annotations

import re
import uuid
from datetime import datetime
from pathlib import Path

from fastapi import APIRouter, Depends, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy import select
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db, require_admin
from app.blog import slugify
from app.models import BlogPost
from app.template_response import html_response

router = APIRouter(prefix="/admin/blog")
templates = Jinja2Templates(directory=str(config.TEMPLATES_DIR))

_ALLOWED_EXT = {".png", ".jpg", ".jpeg", ".webp", ".gif"}


def _uploads_blog_dir() -> Path:
    d = config.UPLOAD_DIR / "blog"
    d.mkdir(parents=True, exist_ok=True)
    return d


def _save_upload(file: UploadFile) -> str:
    name = (file.filename or "image.png").strip()
    ext = Path(name).suffix.lower()
    if ext not in _ALLOWED_EXT:
        raise HTTPException(status_code=400, detail="Chỉ nhận PNG/JPG/WEBP/GIF")
    dest_name = f"{datetime.utcnow().strftime('%Y%m%d')}-{uuid.uuid4().hex[:10]}{ext}"
    dest = _uploads_blog_dir() / dest_name
    data = file.file.read()
    if len(data) > 8 * 1024 * 1024:
        raise HTTPException(status_code=400, detail="Ảnh tối đa 8MB")
    dest.write_bytes(data)
    return f"/uploads/blog/{dest_name}"


@router.get("")
def blog_list(
    request: Request,
    saved: int = 0,
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    posts = list(
        db.scalars(select(BlogPost).order_by(BlogPost.sort_order.asc(), BlogPost.id.desc())).all()
    )
    return html_response(
        templates,
        "admin/blog_list.html",
        {
            "request": request,
            "posts": posts,
            "saved": bool(saved),
            "site_url": config.SITE_URL.rstrip("/"),
        },
    )


@router.get("/new")
def blog_new(request: Request, _user=Depends(require_admin)):
    return html_response(
        templates,
        "admin/blog_edit.html",
        {
            "request": request,
            "post": None,
            "site_url": config.SITE_URL.rstrip("/"),
            "uploaded_url": "",
            "error": None,
        },
    )


@router.get("/{post_id}")
def blog_edit(
    post_id: int,
    request: Request,
    uploaded: str = "",
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    post = db.get(BlogPost, post_id)
    if not post:
        raise HTTPException(status_code=404)
    return html_response(
        templates,
        "admin/blog_edit.html",
        {
            "request": request,
            "post": post,
            "site_url": config.SITE_URL.rstrip("/"),
            "uploaded_url": uploaded,
            "error": None,
        },
    )


@router.post("/save")
def blog_save(
    request: Request,
    post_id: int = Form(0),
    slug: str = Form(""),
    title_vi: str = Form(""),
    title_en: str = Form(""),
    excerpt_vi: str = Form(""),
    excerpt_en: str = Form(""),
    body_html_vi: str = Form(""),
    body_html_en: str = Form(""),
    meta_description_vi: str = Form(""),
    meta_description_en: str = Form(""),
    meta_keywords_vi: str = Form(""),
    meta_keywords_en: str = Form(""),
    cover_image: str = Form(""),
    author_name: str = Form(""),
    sort_order: int = Form(100),
    published: str = Form(""),
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    s = slugify(slug or title_vi or title_en)
    if not s:
        raise HTTPException(status_code=400, detail="Slug required")

    clash = db.scalar(select(BlogPost).where(BlogPost.slug == s, BlogPost.id != post_id))
    if clash:
        raise HTTPException(status_code=400, detail="Slug đã tồn tại")

    is_pub = published in ("1", "on", "true", "True")
    if post_id:
        post = db.get(BlogPost, post_id)
        if not post:
            raise HTTPException(status_code=404)
    else:
        post = BlogPost()
        db.add(post)

    post.slug = s
    post.title_vi = title_vi.strip()
    post.title_en = title_en.strip()
    post.excerpt_vi = excerpt_vi.strip()
    post.excerpt_en = excerpt_en.strip()
    post.body_html_vi = body_html_vi
    post.body_html_en = body_html_en
    post.meta_description_vi = meta_description_vi.strip()
    post.meta_description_en = meta_description_en.strip()
    post.meta_keywords_vi = meta_keywords_vi.strip()
    post.meta_keywords_en = meta_keywords_en.strip()
    post.cover_image = cover_image.strip()
    post.author_name = (author_name or "Body Exporter").strip()
    post.sort_order = int(sort_order or 100)
    was_pub = post.published
    post.published = is_pub
    if is_pub and (not was_pub or not post.published_at):
        post.published_at = datetime.utcnow()
    post.updated_at = datetime.utcnow()
    db.commit()
    return RedirectResponse(f"/admin/blog/{post.id}?uploaded=", status_code=303)


@router.post("/{post_id}/upload")
async def blog_upload(
    post_id: int,
    request: Request,
    file: UploadFile = File(...),
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    post = db.get(BlogPost, post_id)
    if not post:
        raise HTTPException(status_code=404)
    url = _save_upload(file)
    # If no cover yet, set it automatically (admin can change).
    if not (post.cover_image or "").strip():
        post.cover_image = url
        post.updated_at = datetime.utcnow()
        db.commit()
    return RedirectResponse(f"/admin/blog/{post_id}?uploaded={url}", status_code=303)


@router.post("/{post_id}/delete")
def blog_delete(
    post_id: int,
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    post = db.get(BlogPost, post_id)
    if post:
        db.delete(post)
        db.commit()
    return RedirectResponse("/admin/blog?saved=1", status_code=303)
