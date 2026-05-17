"""Resend API — license key emails (same shape as Cloudflare Worker)."""

from __future__ import annotations

import html
import json

import httpx

from app import config


def send_license_key_email(*, to: str, license_key: str, order_id: str) -> dict:
    """Return {ok, id?, detail?, skipped?}."""
    api_key = (config.RESEND_API_KEY or "").strip()
    if not api_key:
        return {"ok": True, "skipped": True}

    from_addr = config.RESEND_FROM.strip() or "SolidWorks Body Exporter <onboarding@resend.dev>"
    subject = "Your SolidWorks Body Exporter license key"
    text = (
        "Thank you for your purchase.\n\n"
        f"License key: {license_key}\n\n"
        "Online activation: set LicenseKey and ApiBaseUrl in "
        "%APPDATA%\\SolidWorksBodyExporter\\settings.json (see product documentation).\n\n"
        f"Order reference: {order_id}"
    )
    esc = html.escape(license_key)
    esc_o = html.escape(order_id)
    body_html = (
        "<p>Thank you for your purchase.</p>"
        f"<p><strong>License key:</strong> <code>{esc}</code></p>"
        "<p>Online activation: set <code>LicenseKey</code> and <code>ApiBaseUrl</code> "
        "in your Body Exporter settings (see documentation).</p>"
        f'<p style="color:#666;font-size:12px">Order: {esc_o}</p>'
    )
    try:
        r = httpx.post(
            "https://api.resend.com/emails",
            headers={"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"},
            json={"from": from_addr, "to": [to], "subject": subject, "text": text, "html": body_html},
            timeout=30.0,
        )
        raw = r.text
        if not r.is_success:
            detail = raw if len(raw) <= 600 else raw[:600] + "…"
            return {"ok": False, "detail": detail}
        try:
            jid = json.loads(raw).get("id")
        except Exception:
            jid = None
        return {"ok": True, "id": jid}
    except Exception as ex:
        return {"ok": False, "detail": str(ex)}
