"""Public blog routes: /blog and /blog/{slug}."""

from __future__ import annotations

import json

from fastapi import APIRouter, Depends, HTTPException, Request
from fastapi.templating import Jinja2Templates
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db
from app.blog import get_by_slug, list_published, localized
from app.routers.public import _ctx
from app.template_response import html_response

router = APIRouter()
templates = Jinja2Templates(directory=str(config.TEMPLATES_DIR))


@router.get("/blog")
def blog_index(request: Request, db: Session = Depends(get_db)):
    posts = list_published(db)
    ctx = _ctx(request, db)
    lang = ctx["lang"]
    cards = []
    for p in posts:
        loc = localized(p, lang)
        cards.append(
            {
                "slug": p.slug,
                "title": loc["title"],
                "excerpt": loc["excerpt"],
                "cover_image": (p.cover_image or "").strip(),
                "published_at": p.published_at,
            }
        )
    if lang == "vi":
        title = "Bài viết — Body Exporter"
        meta = (
            "Hướng dẫn xuất BOM, xuất body SolidWorks sang Excel, danh sách chi tiết xưởng mộc, "
            "tích hợp ERP — Body Exporter."
        )
    else:
        title = "Blog — Body Exporter"
        meta = (
            "Guides on SolidWorks BOM export, body-to-Excel workflows, wood-shop cutting lists, "
            "and ERP integration — Body Exporter."
        )
    ctx.update(
        {
            "page_title": title,
            "meta_description": meta,
            "meta_keywords": config.SEO_KEYWORDS_EN if lang == "en" else config.SEO_KEYWORDS,
            "canonical_url": f"{config.SITE_URL.rstrip('/')}/blog",
            "blog_posts": cards,
        }
    )
    return html_response(templates, "blog/index.html", ctx)


@router.get("/blog/{slug}")
def blog_post(slug: str, request: Request, db: Session = Depends(get_db)):
    post = get_by_slug(db, slug, published_only=True)
    if not post:
        raise HTTPException(status_code=404, detail="Article not found")

    ctx = _ctx(request, db)
    lang = ctx["lang"]
    loc = localized(post, lang)
    og = (post.cover_image or "").strip()
    if og.startswith("/"):
        og = config.SITE_URL.rstrip("/") + og
    elif not og:
        og = ctx["seo_og_image"]

    schema_article = json.dumps(
        {
            "@context": "https://schema.org",
            "@type": "BlogPosting",
            "headline": loc["title"],
            "description": loc["meta_description"],
            "datePublished": (post.published_at or post.created_at).isoformat() + "Z"
            if post.published_at or post.created_at
            else None,
            "dateModified": post.updated_at.isoformat() + "Z" if post.updated_at else None,
            "author": {"@type": "Organization", "name": post.author_name or "Body Exporter"},
            "publisher": {"@type": "Organization", "name": "Body Exporter"},
            "mainEntityOfPage": f"{config.SITE_URL.rstrip('/')}/blog/{post.slug}",
            "image": og,
        },
        ensure_ascii=False,
    )

    ctx.update(
        {
            "page_title": f"{loc['title']} — Body Exporter",
            "meta_description": loc["meta_description"],
            "meta_keywords": loc["meta_keywords"] or ctx["meta_keywords"],
            "canonical_url": f"{config.SITE_URL.rstrip('/')}/blog/{post.slug}",
            "seo_og_image": og,
            "post": post,
            "loc": loc,
            "schema_article_json": schema_article,
        }
    )
    return html_response(templates, "blog/post.html", ctx)
