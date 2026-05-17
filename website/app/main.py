from pathlib import Path

from fastapi import FastAPI
from fastapi.responses import PlainTextResponse, Response
from fastapi.staticfiles import StaticFiles
from starlette.middleware.sessions import SessionMiddleware

from app import config
from app.database import init_db
from app.routers import admin_routes, public

app = FastAPI(title="Body Exporter", docs_url=None, redoc_url=None)

app.add_middleware(
    SessionMiddleware,
    secret_key=config.SECRET_KEY,
    session_cookie="be_session",
    max_age=60 * 60 * 24 * 14,
    same_site="lax",
    https_only=config.SITE_URL.startswith("https://"),
)

static_dir = Path(__file__).resolve().parents[1] / "static"
app.mount("/static", StaticFiles(directory=static_dir), name="static")

app.include_router(public.router)
app.include_router(admin_routes.router)


@app.on_event("startup")
def startup():
    init_db()


@app.get("/health")
def health():
    return {"ok": True}


@app.get("/robots.txt", response_class=PlainTextResponse)
def robots_txt():
    base = config.SITE_URL.rstrip("/")
    return f"""User-agent: *
Allow: /
Disallow: /admin

Sitemap: {base}/sitemap.xml
"""


@app.get("/sitemap.xml")
def sitemap_xml():
    base = config.SITE_URL.rstrip("/")
    urls = ["/", "/download", "/buy"]
    xml = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
    ]
    for path in urls:
        xml.append(f"  <url><loc>{base}{path}</loc><changefreq>weekly</changefreq></url>")
    xml.append("</urlset>")
    return Response("\n".join(xml), media_type="application/xml")
