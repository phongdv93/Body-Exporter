"""Resend API — license key emails (plain HTML or published Resend template)."""

from __future__ import annotations

import html
import json
from datetime import datetime

import httpx

from app import config


def _normalize_lang(lang: str | None) -> str:
    v = (lang or "vi").strip().lower()
    return "en" if v.startswith("en") else "vi"


def _display_name(email: str, lang: str) -> str:
    local = (email or "").split("@", 1)[0].strip()
    if not local:
        return "there" if lang == "en" else "bạn"
    return local.replace(".", " ").replace("_", " ").title()[:80]


def _format_expires(expires_at: datetime | None) -> str:
    if not expires_at:
        return "—"
    try:
        return expires_at.strftime("%d/%m/%Y")
    except Exception:
        return str(expires_at)[:10]


def _template_id_for_lang(lang: str) -> str:
    if lang == "en":
        return (config.RESEND_LICENSE_TEMPLATE_ID_EN or config.RESEND_LICENSE_TEMPLATE_ID or "").strip()
    return (config.RESEND_LICENSE_TEMPLATE_ID_VI or config.RESEND_LICENSE_TEMPLATE_ID or "").strip()


def _subject_for_lang(lang: str) -> str:
    if lang == "en":
        return config.RESEND_LICENSE_SUBJECT_EN or "Your Body Exporter license key — SolidWorks"
    return config.RESEND_LICENSE_SUBJECT_VI or "License key Body Exporter — SolidWorks"


def send_license_key_email(
    *,
    to: str,
    license_key: str,
    order_id: str,
    plan: str = "personal",
    expires_at: datetime | None = None,
    buyer_name: str | None = None,
    lang: str = "vi",
) -> dict:
    """Return {ok, id?, detail?, skipped?}. lang: vi (VietQR/SePay) or en (Paddle)."""
    api_key = (config.RESEND_API_KEY or "").strip()
    if not api_key:
        return {"ok": True, "skipped": True}

    lang = _normalize_lang(lang)
    to = (to or "").strip()
    from_addr = config.RESEND_FROM.strip() or "Body Exporter <onboarding@resend.dev>"
    subject = _subject_for_lang(lang)
    name = (buyer_name or "").strip() or _display_name(to, lang)
    plan_label = (plan or "personal").strip() or "personal"
    expires_label = _format_expires(expires_at)

    template_id = _template_id_for_lang(lang)
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
        if lang == "en":
            text = (
                "Thank you for your purchase.\n\n"
                f"License key: {license_key}\n\n"
                "In Body Exporter: License → Activate license → paste key → Apply.\n\n"
                f"Order: {order_id}"
            )
            body_html = (
                "<p>Thank you for your purchase.</p>"
                f"<p><strong>License key:</strong> <code>{html.escape(license_key)}</code></p>"
                "<p>In Body Exporter: <strong>License</strong> → <strong>Activate license</strong> "
                "→ paste key → <strong>Apply</strong>.</p>"
                f'<p style="color:#666;font-size:12px">Order: {html.escape(order_id)}</p>'
            )
        else:
            text = (
                "Cảm ơn bạn đã mua Body Exporter.\n\n"
                f"License key: {license_key}\n\n"
                "Trong plugin: License → Kích hoạt license → dán key → Apply.\n\n"
                f"Mã đơn: {order_id}"
            )
            body_html = (
                "<p>Cảm ơn bạn đã mua Body Exporter.</p>"
                f"<p><strong>License key:</strong> <code>{html.escape(license_key)}</code></p>"
                "<p>Trong plugin: <strong>License</strong> → <strong>Kích hoạt license</strong> "
                "→ dán key → <strong>Apply</strong>.</p>"
                f'<p style="color:#666;font-size:12px">Mã đơn: {html.escape(order_id)}</p>'
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


def send_license_renewed_email(
    *,
    to: str,
    license_key: str,
    order_id: str,
    plan: str = "personal",
    expires_at: datetime | None = None,
    days_added: int = 0,
    buyer_name: str | None = None,
    lang: str = "vi",
) -> dict:
    """Email after auto-renew (same key, new expiry). Uses plain text — no separate Resend template required."""
    api_key = (config.RESEND_API_KEY or "").strip()
    if not api_key:
        return {"ok": True, "skipped": True}

    lang = _normalize_lang(lang)
    to = (to or "").strip()
    from_addr = config.RESEND_FROM.strip() or "Body Exporter <onboarding@resend.dev>"
    name = (buyer_name or "").strip() or _display_name(to, lang)
    expires_label = _format_expires(expires_at)
    days_label = str(max(0, int(days_added)))

    if lang == "en":
        subject = "Body Exporter license renewed"
        text = (
            f"Hi {name},\n\n"
            f"Your license was renewed (+{days_label} days).\n"
            f"Same key — no need to paste again:\n{license_key}\n\n"
            f"New expiry: {expires_label}\n"
            "Open Body Exporter → License → Refresh days (or reopen SolidWorks) to sync.\n\n"
            f"Order: {order_id}"
        )
        body_html = (
            f"<p>Hi {html.escape(name)},</p>"
            f"<p>Your license was renewed (<strong>+{html.escape(days_label)} days</strong>).</p>"
            f"<p>Same key — no need to paste again:<br><code>{html.escape(license_key)}</code></p>"
            f"<p><strong>New expiry:</strong> {html.escape(expires_label)}</p>"
            "<p>Open Body Exporter → <strong>License</strong> → <strong>Refresh days</strong> "
            "(or reopen SolidWorks) to sync.</p>"
            f'<p style="color:#666;font-size:12px">Order: {html.escape(order_id)}</p>'
        )
    else:
        subject = "Body Exporter đã gia hạn license"
        text = (
            f"Xin chào {name},\n\n"
            f"License của bạn đã được gia hạn (+{days_label} ngày).\n"
            f"Cùng key — không cần dán lại:\n{license_key}\n\n"
            f"Hết hạn mới: {expires_label}\n"
            "Mở Body Exporter → License → Refresh days (hoặc mở lại SolidWorks) để đồng bộ.\n\n"
            f"Mã đơn: {order_id}"
        )
        body_html = (
            f"<p>Xin chào {html.escape(name)},</p>"
            f"<p>License của bạn đã được gia hạn (<strong>+{html.escape(days_label)} ngày</strong>).</p>"
            f"<p>Cùng key — không cần dán lại:<br><code>{html.escape(license_key)}</code></p>"
            f"<p><strong>Hết hạn mới:</strong> {html.escape(expires_label)}</p>"
            "<p>Mở Body Exporter → <strong>License</strong> → <strong>Refresh days</strong> "
            "(hoặc mở lại SolidWorks) để đồng bộ.</p>"
            f'<p style="color:#666;font-size:12px">Mã đơn: {html.escape(order_id)}</p>'
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
