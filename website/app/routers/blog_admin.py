"""Admin CMS for blog posts — visual editor, no HTML required for staff."""

from __future__ import annotations

import uuid
from datetime import datetime
from pathlib import Path
from urllib.parse import unquote

from fastapi import APIRouter, Depends, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy import select
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db, require_admin
from app.blog import (
    clear_slot_image,
    merge_image_slots,
    replace_slot_image,
    slugify,
)
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


def _safe_upload_path(url: str) -> Path | None:
    raw = unquote((url or "").strip())
    if not raw.startswith("/uploads/blog/"):
        return None
    name = Path(raw).name
    if not name or name in (".", "..") or "/" in name or "\\" in name:
        return None
    path = _uploads_blog_dir() / name
    try:
        path.resolve().relative_to(_uploads_blog_dir().resolve())
    except Exception:
        return None
    return path if path.is_file() else None


def _list_library() -> list[dict]:
    items = []
    root = _uploads_blog_dir()
    for p in sorted(root.glob("*"), key=lambda x: x.stat().st_mtime, reverse=True):
        if p.suffix.lower() in _ALLOWED_EXT and p.is_file():
            items.append(
                {
                    "url": f"/uploads/blog/{p.name}",
                    "name": p.name,
                    "size_kb": max(1, p.stat().st_size // 1024),
                }
            )
    return items[:60]


def _edit_context(request: Request, post: BlogPost | None, *, flash: str = "", error: str = ""):
    slots = []
    if post:
        slots = merge_image_slots(post.body_html_vi or "", post.body_html_en or "")
    return {
        "request": request,
        "post": post,
        "site_url": config.SITE_URL.rstrip("/"),
        "slots": slots,
        "library": _list_library(),
        "flash": flash,
        "error": error,
    }


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
    return html_response(templates, "admin/blog_edit.html", _edit_context(request, None))


@router.get("/{post_id}")
def blog_edit(
    post_id: int,
    request: Request,
    ok: str = "",
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    post = db.get(BlogPost, post_id)
    if not post:
        raise HTTPException(status_code=404)
    flash = {
        "saved": "Đã lưu bài viết.",
        "slot": "Đã cập nhật ảnh trong bài.",
        "cleared": "Đã gỡ ảnh — chỗ chừa trống lại.",
        "deleted": "Đã xóa file ảnh trên máy chủ.",
        "cover": "Đã đặt ảnh bìa.",
    }.get(ok, "")
    return html_response(templates, "admin/blog_edit.html", _edit_context(request, post, flash=flash))


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

    def _clean_editor_html(html: str) -> str:
        # Strip TinyMCE helper classes so public pages stay clean.
        h = html or ""
        h = h.replace(" mceNonEditable", "").replace("mceNonEditable ", "").replace("mceNonEditable", "")
        return h

    post.slug = s
    post.title_vi = title_vi.strip()
    post.title_en = title_en.strip()
    post.excerpt_vi = excerpt_vi.strip()
    post.excerpt_en = excerpt_en.strip()
    post.body_html_vi = _clean_editor_html(body_html_vi)
    post.body_html_en = _clean_editor_html(body_html_en)
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
    return RedirectResponse(f"/admin/blog/{post.id}?ok=saved", status_code=303)


@router.post("/{post_id}/slot/{slot_name}/upload")
async def blog_slot_upload(
    post_id: int,
    slot_name: str,
    file: UploadFile = File(...),
    caption: str = Form(""),
    set_cover: str = Form(""),
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    post = db.get(BlogPost, post_id)
    if not post:
        raise HTTPException(status_code=404)
    url = _save_upload(file)
    post.body_html_vi = replace_slot_image(post.body_html_vi or "", slot_name, url, caption, lang="vi")
    post.body_html_en = replace_slot_image(post.body_html_en or "", slot_name, url, caption, lang="en")
    if set_cover in ("1", "on", "true") or not (post.cover_image or "").strip():
        post.cover_image = url
    post.updated_at = datetime.utcnow()
    db.commit()
    return RedirectResponse(f"/admin/blog/{post_id}?ok=slot", status_code=303)


@router.post("/{post_id}/slot/{slot_name}/assign")
def blog_slot_assign(
    post_id: int,
    slot_name: str,
    url: str = Form(""),
    set_cover: str = Form(""),
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    """Reuse an already-uploaded /uploads/blog/… image into a slot."""
    post = db.get(BlogPost, post_id)
    if not post:
        raise HTTPException(status_code=404)
    if not _safe_upload_path(url):
        raise HTTPException(status_code=400, detail="Ảnh thư viện không hợp lệ")
    clean = unquote((url or "").strip())
    post.body_html_vi = replace_slot_image(post.body_html_vi or "", slot_name, clean, lang="vi")
    post.body_html_en = replace_slot_image(post.body_html_en or "", slot_name, clean, lang="en")
    if set_cover in ("1", "on", "true") or not (post.cover_image or "").strip():
        post.cover_image = clean
    post.updated_at = datetime.utcnow()
    db.commit()
    return RedirectResponse(f"/admin/blog/{post_id}?ok=slot", status_code=303)


@router.post("/{post_id}/slot/{slot_name}/clear")
def blog_slot_clear(
    post_id: int,
    slot_name: str,
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    post = db.get(BlogPost, post_id)
    if not post:
        raise HTTPException(status_code=404)
    post.body_html_vi = clear_slot_image(post.body_html_vi or "", slot_name, lang="vi")
    post.body_html_en = clear_slot_image(post.body_html_en or "", slot_name, lang="en")
    post.updated_at = datetime.utcnow()
    db.commit()
    return RedirectResponse(f"/admin/blog/{post_id}?ok=cleared", status_code=303)


@router.post("/{post_id}/cover")
async def blog_cover_upload(
    post_id: int,
    file: UploadFile = File(...),
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    post = db.get(BlogPost, post_id)
    if not post:
        raise HTTPException(status_code=404)
    url = _save_upload(file)
    post.cover_image = url
    post.updated_at = datetime.utcnow()
    db.commit()
    return RedirectResponse(f"/admin/blog/{post_id}?ok=cover", status_code=303)


@router.post("/{post_id}/cover/assign")
def blog_cover_assign(
    post_id: int,
    url: str = Form(""),
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    post = db.get(BlogPost, post_id)
    if not post:
        raise HTTPException(status_code=404)
    if not _safe_upload_path(url):
        raise HTTPException(status_code=400, detail="Ảnh thư viện không hợp lệ")
    post.cover_image = unquote((url or "").strip())
    post.updated_at = datetime.utcnow()
    db.commit()
    return RedirectResponse(f"/admin/blog/{post_id}?ok=cover", status_code=303)


@router.post("/{post_id}/cover/clear")
def blog_cover_clear(
    post_id: int,
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    post = db.get(BlogPost, post_id)
    if not post:
        raise HTTPException(status_code=404)
    post.cover_image = ""
    post.updated_at = datetime.utcnow()
    db.commit()
    return RedirectResponse(f"/admin/blog/{post_id}?ok=cleared", status_code=303)


@router.post("/delete-file")
def blog_delete_file(
    request: Request,
    url: str = Form(""),
    post_id: int = Form(0),
    db: Session = Depends(get_db),
    _user=Depends(require_admin),
):
    path = _safe_upload_path(url)
    if not path:
        raise HTTPException(status_code=400, detail="File không hợp lệ")

    # Remove references from this post (and optionally all posts).
    posts = list(db.scalars(select(BlogPost)).all())
    for p in posts:
        changed = False
        for slot in merge_image_slots(p.body_html_vi or "", p.body_html_en or ""):
            if slot.get("image_url") == url:
                p.body_html_vi = clear_slot_image(p.body_html_vi or "", slot["slot"], lang="vi")
                p.body_html_en = clear_slot_image(p.body_html_en or "", slot["slot"], lang="en")
                changed = True
        if (p.cover_image or "").strip() == url:
            p.cover_image = ""
            changed = True
        # Also strip bare <img src="url"> if any
        if url in (p.body_html_vi or "") or url in (p.body_html_en or ""):
            p.body_html_vi = (p.body_html_vi or "").replace(url, "")
            p.body_html_en = (p.body_html_en or "").replace(url, "")
            changed = True
        if changed:
            p.updated_at = datetime.utcnow()
    try:
        path.unlink(missing_ok=True)
    except TypeError:
        if path.exists():
            path.unlink()
    db.commit()
    dest = f"/admin/blog/{post_id}?ok=deleted" if post_id else "/admin/blog?saved=1"
    return RedirectResponse(dest, status_code=303)


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
