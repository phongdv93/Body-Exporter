from pathlib import Path
import logging
import traceback

from fastapi import FastAPI, Request
from fastapi.responses import FileResponse, PlainTextResponse, Response
from fastapi.staticfiles import StaticFiles
from starlette.exceptions import HTTPException as StarletteHTTPException
from starlette.middleware.sessions import SessionMiddleware

from app import config
from app.database import init_db
from app.error_pages import http_exception_handler, server_error_handler
from app.routers import admin_routes, client_api, licenses_admin, public, sepay_webhook

_log = logging.getLogger("uvicorn.error")

app = FastAPI(title="Body Exporter", docs_url=None, redoc_url=None)

app.add_exception_handler(StarletteHTTPException, http_exception_handler)
app.add_exception_handler(Exception, server_error_handler)

app.add_middleware(
    SessionMiddleware,
    secret_key=config.SECRET_KEY,
    session_cookie="be_session",
    max_age=60 * 60 * 24 * 14,
    same_site="lax",
    https_only=config.SITE_URL.startswith("https://"),
)


@app.middleware("http")
async def log_unhandled_exceptions(request: Request, call_next):
    """Render free tier has no shell; tracebacks go to Logs via stderr."""
    try:
        return await call_next(request)
    except StarletteHTTPException:
        raise
    except Exception:
        _log.error(
            "UNHANDLED %s %s\n%s",
            request.method,
            request.url.path,
            traceback.format_exc(),
        )
        raise


static_dir = Path(__file__).resolve().parents[1] / "static"
app.mount("/static", StaticFiles(directory=static_dir), name="static")

app.include_router(public.router)
app.include_router(client_api.router)
app.include_router(sepay_webhook.router)
app.include_router(admin_routes.router)
app.include_router(licenses_admin.router)


@app.on_event("startup")
def startup():
    init_db()


@app.get("/health")
def health():
    return {"ok": True}


@app.get("/favicon.ico")
def favicon():
    ico = static_dir / "favicon.ico"
    if ico.is_file():
        return FileResponse(ico, media_type="image/x-icon")
    return Response(status_code=404)


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
