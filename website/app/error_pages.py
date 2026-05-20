"""Minimal HTML error pages; JSON for API routes."""

from fastapi import Request
from fastapi.responses import JSONResponse
from fastapi.templating import Jinja2Templates
from starlette.exceptions import HTTPException as StarletteHTTPException

from app import config
from app.template_response import html_response

templates = Jinja2Templates(directory=str(config.TEMPLATES_DIR))


def _ctx(request: Request) -> dict:
    return {
        "request": request,
        "site_url": config.SITE_URL,
        "support_email": config.SUPPORT_EMAIL,
    }


def wants_html_error(request: Request) -> bool:
    if request.url.path.startswith("/api/"):
        return False
    accept = (request.headers.get("accept") or "").lower()
    if "application/json" in accept and "text/html" not in accept:
        return False
    return True


async def not_found_handler(request: Request, _exc: StarletteHTTPException):
    if not wants_html_error(request):
        return JSONResponse({"detail": "Not Found"}, status_code=404)
    return html_response(templates, "404.html", _ctx(request), status_code=404)


async def server_error_handler(request: Request, _exc: Exception):
    if not wants_html_error(request):
        return JSONResponse({"detail": "Internal Server Error"}, status_code=500)
    return html_response(templates, "500.html", _ctx(request), status_code=500)


async def http_exception_handler(request: Request, exc: StarletteHTTPException):
    if exc.status_code == 404:
        return await not_found_handler(request, exc)
    if exc.status_code >= 500:
        return await server_error_handler(request, exc)
    headers = dict(exc.headers) if exc.headers else None
    if exc.status_code in (301, 302, 303, 307, 308) and headers and headers.get("location"):
        from fastapi.responses import RedirectResponse

        return RedirectResponse(url=headers["location"], status_code=exc.status_code)
    return JSONResponse(
        {"detail": exc.detail},
        status_code=exc.status_code,
        headers=headers,
    )
