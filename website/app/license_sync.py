"""Sync license fields from Cloudflare Worker KV into Postgres (admin CRM)."""

from __future__ import annotations

import logging
from typing import Any

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.models import License
from app.worker_client import fetch_worker_license_records

log = logging.getLogger("uvicorn.error")


def sync_fingerprints_from_worker(db: Session) -> dict[str, Any]:
    """
  Pull boundMachineId from Worker GET /admin/license/list and update be_licenses rows by license_key.
  Worker is the source of truth after the user activates in the plugin.
  """
    try:
        worker_rows = fetch_worker_license_records()
    except Exception as ex:
        log.warning("Worker license list failed: %s", ex)
        return {
            "ok": False,
            "error": str(ex),
            "updated": 0,
            "with_fingerprint": 0,
            "matched": 0,
        }

    by_key: dict[str, dict[str, Any]] = {}
    for rec in worker_rows:
        key = (rec.get("key") or "").strip()
        if key:
            by_key[key] = rec

    licenses = db.scalars(select(License)).all()
    updated = 0
    matched = 0
    with_fp = 0

    for lic in licenses:
        w = by_key.get(lic.license_key)
        if not w:
            continue
        matched += 1
        fp = (w.get("boundMachineId") or "").strip()
        if not fp:
            continue
        with_fp += 1
        if (lic.machine_fingerprint or "").strip() != fp:
            lic.machine_fingerprint = fp
            updated += 1

    if updated:
        db.commit()

    return {
        "ok": True,
        "error": None,
        "updated": updated,
        "with_fingerprint": with_fp,
        "matched": matched,
        "worker_total": len(worker_rows),
    }
