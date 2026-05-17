"""Public API called by the SolidWorks plugin (telemetry)."""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from sqlalchemy.orm import Session

from app.auth import get_db
from app.client_telemetry import record_client_ping

log = logging.getLogger("uvicorn.error")

router = APIRouter(prefix="/api/v1/client", tags=["client"])


class ClientPingBody(BaseModel):
    machineId: str = Field(..., min_length=8, max_length=128)
    hostname: str = Field("", max_length=128)
    pluginVersion: str = Field("", max_length=40)
    swVersion: str = Field("", max_length=40)
    licenseStatus: str = Field("unknown", max_length=32)
    event: str = Field("ping", max_length=32)


@router.post("/ping")
def client_ping(request: Request, body: ClientPingBody, db: Session = Depends(get_db)):
    from app.client_telemetry import _client_ip

    try:
        row = record_client_ping(
            db,
            machine_id=body.machineId.strip(),
            hostname=body.hostname.strip(),
            plugin_version=body.pluginVersion.strip(),
            sw_version=body.swVersion.strip(),
            license_status=body.licenseStatus.strip(),
            event=body.event.strip(),
            client_ip=_client_ip(request),
        )
        return {"ok": True, "firstSeen": row.first_seen_at.isoformat() + "Z"}
    except ValueError as ex:
        return JSONResponse({"ok": False, "error": str(ex)}, status_code=400)
    except Exception as ex:
        log.exception("client ping failed")
        return JSONResponse({"ok": False, "error": "internal_error"}, status_code=500)
