"""Resend API — license key emails (plain HTML or published Resend template)."""

from __future__ import annotations

import html
import json
from datetime import datetime

import httpx

from app import config


def _display_name(email: str) -> str:
    local = (email or "").split("@", 1)[0].strip()
    if not local:
        return "bạn"
    return local.replace(".", " ").replace("_", " ").title()[:80]


def _format_expires(expires_at: datetime | None) -> str:
    if not expires_at:
        return "—"
    try:
        return expires_at.strftime("%d/%m/%Y")
    except Exception:
        return str(expires_at)[:10]


def send_license_key_email(
    *,
    to: str,
    license_key: str,
    order_id: str,
    plan: str = "personal",
    expires_at: datetime | None = None,
    buyer_name: str | None = None,
) -> dict:
    """Return {ok, id?, detail?, skipped?}."""
    api_key = (config.RESEND_API_KEY or "").strip()
    if not api_key:
        return {"ok": True, "skipped": True}

    to = (to or "").strip()
    from_addr = config.RESEND_FROM.strip() or "Body Exporter <onboarding@resend.dev>"
    subject = config.RESEND_LICENSE_SUBJECT or "Your SolidWorks Body Exporter license key"
    name = (buyer_name or "").strip() or _display_name(to)
    plan_label = (plan or "personal").strip() or "personal"
    expires_label = _format_expires(expires_at)

    template_id = (config.RESEND_LICENSE_TEMPLATE_ID or "").strip()
    if template_id:
        payload: dict = {
            "from": from_addr,
            "to": [to],
            "subject": subject,
            "template": {
                "id": template_id,
                "variables": {
                    "name": name,
                    "license_key": license_key,
                    "plan": plan_label,
                    "expires": expires_label,
                },
            },
        }
    else:
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
        payload = {
            "from": from_addr,
            "to": [to],
            "subject": subject,
            "text": text,
            "html": body_html,
        }

    try:
        r = httpx.post(
            "https://api.resend.com/emails",
            headers={"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"},
            json=payload,
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
