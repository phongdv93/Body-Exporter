"""Paddle Billing webhooks — issue license after successful checkout."""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, Request
from fastapi.responses import JSONResponse
from sqlalchemy.orm import Session

from app import config
from app.auth import get_db
from app.paddle_billing import handle_paddle_webhook, parse_webhook_json, verify_paddle_signature

log = logging.getLogger("uvicorn.error")

router = APIRouter()


@router.post("/webhook/paddle")
async def paddle_webhook(request: Request, db: Session = Depends(get_db)):
    secret = config.PADDLE_WEBHOOK_SECRET
    if not secret:
        log.warning("Paddle webhook: PADDLE_WEBHOOK_SECRET not set")
        return JSONResponse({"error": "not_configured"}, status_code=503)

    raw = await request.body()
    sig = request.headers.get("Paddle-Signature") or request.headers.get("paddle-signature") or ""
    if not verify_paddle_signature(raw, sig, secret):
        log.warning("Paddle webhook: invalid signature")
        return JSONResponse({"error": "unauthorized"}, status_code=401)

    try:
        payload = parse_webhook_json(raw)
    except Exception:
        return JSONResponse({"error": "invalid_json"}, status_code=400)

    result = handle_paddle_webhook(db, payload)
    if result.get("status") == "error":
        return JSONResponse(result, status_code=500)
    return JSONResponse(result)
