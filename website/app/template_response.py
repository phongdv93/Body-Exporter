"""HTML responses with no-store so admin edits show on public pages immediately."""

from fastapi.templating import Jinja2Templates
from starlette.responses import Response


def html_response(
    templates: Jinja2Templates,
    name: str,
    context: dict,
    status_code: int = 200,
) -> Response:
    resp = templates.TemplateResponse(name, context, status_code=status_code)
    resp.headers["Cache-Control"] = "no-store, no-cache, must-revalidate"
    resp.headers["Pragma"] = "no-cache"
    return resp
