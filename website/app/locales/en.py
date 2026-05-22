"""English UI strings (public site). CMS HTML may override via *_en columns."""

MESSAGES = {
    "nav.home": "Overview",
    "nav.download": "Download",
    "nav.buy": "Buy license",
    "lang.vi": "Tiếng Việt",
    "lang.en": "English",
    "footer.rights": "All rights reserved",
    "footer.terms": "Terms of Service",
    "footer.privacy": "Privacy Policy",
    "footer.refund": "Refund Policy",
    "meta.terms": "Body Exporter Terms of Service — SolidWorks add-in license.",
    "meta.privacy": "Body Exporter Privacy Policy — website, plugin, and payments.",
    "meta.refund": "Body Exporter Refund Policy — digital software license.",
    "home.eyebrow": "SolidWorks add-in",
    "home.hero_title_default": "SolidWorks Body Exporter",
    "home.hero_subtitle_default": (
        "Export body/part data from SolidWorks — Excel, templates, and a fast workflow for wood shops."
    ),
    "home.bullets_default": (
        "Drag-and-drop L×W×T dimensions\n"
        "Excel export & {{placeholder}} templates\n"
        "Online license — 14-day trial"
    ),
    "home.cta_download": "Download plugin",
    "home.cta_buy": "Buy license",
    "download.title": "Download plugin",
    "download.version": "Version",
    "download.policy_title": "Data policy",
    "download.policy_hint": "Click to expand",
    "download.policy_lead": (
        "Body Exporter collects minimal technical data so the plugin stays reliable — "
        "license activation, support when something fails, and product improvements over time."
    ),
    "download.policy_p2": (
        "We may record: a machine identifier (for license binding), plugin and SolidWorks versions, "
        "install and last-use timestamps, IP address for coarse region (city/country), and current license status."
    ),
    "download.policy_safe": (
        "We do <strong>not read, store, or transmit</strong> any content from your SolidWorks files — "
        "including 3D models, part names, or export files."
    ),
    "download.policy_foot": (
        "Data is used internally only, not for ads, and not shared with third parties. Contact:"
    ),
    "download.consent": "I have read and agree to the <strong>Data policy</strong>.",
    "download.btn": "Download version {version}",
    "download.unavailable": (
        "The file was removed for troubleshooting. Please contact "
        '<a href="mailto:{email}">{email}</a> for help.'
    ),
    "download.policy_error": "You must agree to the data policy to download the plugin.",
    "download.policy_error_unavailable": (
        "The file was removed for troubleshooting. Please contact {email} for help."
    ),
    "download.guide_install": "Installation guide",
    "download.guide_use": "How to use",
    "download.guide_excel": "Excel export & placeholders",
    "download.guide_notes": "Additional notes",
    "download.install_1": "<strong>Close SolidWorks</strong> (avoids locked DLLs during install).",
    "download.install_2": (
        "Unzip the package → run <code>Install-BodyExporter.cmd</code> "
        "<strong>Run as administrator</strong>."
    ),
    "download.install_3": (
        "Open SolidWorks → <strong>Tools → Add-Ins</strong> → enable "
        "<em>SolidWorks Body Exporter</em>."
    ),
    "download.install_4": "Use the <strong>Body Exporter</strong> desktop shortcut to open the export window.",
    "download.use_1": "Open a <strong>.SLDPRT</strong> file in SolidWorks.",
    "download.use_2": "Open <strong>Body Exporter</strong> (desktop shortcut or install folder launcher).",
    "download.use_3": (
        "The plugin scans bodies — edit <strong>display names</strong> and "
        "<strong>Length / Width / Thickness</strong> axes."
    ),
    "download.use_4": (
        "<strong>Copy All</strong> into Excel, or <strong>Export</strong> menu → save a workbook."
    ),
    "download.use_5": (
        "First run: <strong>License</strong> → 14-day trial (internet required) or a key from <a href=\"/buy\">/buy</a>."
    ),
    "download.use_foot": "Click Save in the export window to store metadata in the Part file.",
    "download.excel_lead": (
        "Create your company <code>.xlsx</code> template with <strong>placeholders</strong> in cells — "
        "the plugin fills body data on export."
    ),
    "download.excel_1": (
        "<strong>Export ▾ → Excel template panel…</strong> — <em>Add new…</em> / <em>Change…</em> "
        "pick a template row with placeholders."
    ),
    "download.excel_2": "<strong>Export ▾ → Fill Excel template…</strong> — choose the output file.",
    "download.excel_3": "First body overwrites the placeholder row; more bodies fill rows below.",
    "download.excel_table_title": "Supported placeholders (case-insensitive):",
    "download.excel_th_token": "Type in Excel",
    "download.excel_th_data": "Filled data",
    "download.excel_row_name": "Body / part name",
    "download.excel_row_len": "Length (mm)",
    "download.excel_row_wid": "Width (mm)",
    "download.excel_row_thk": "Thickness (mm)",
    "download.excel_row_qty": "Quantity",
    "download.excel_row_app": "Color / material",
    "download.excel_row_prev": "Thumbnail (PNG in cell)",
    "download.excel_foot": "Unknown placeholders are reported after export.",
    "download.trial_foot": "14-day trial after install — internet required on first activation.",
    "download.trial_buy": "Buy license",
    "meta.home": (
        "Body Exporter — SolidWorks add-in to export body/part data to Excel and company templates. "
        "Online license, 14-day trial."
    ),
    "meta.download": (
        "Download Body Exporter for SolidWorks — Excel export, template placeholders, installer guide."
    ),
    "meta.buy": (
        "Buy a Body Exporter license — bank transfer QR or card. License emailed after payment."
    ),
    "meta.buy_success": "Thank you for purchasing Body Exporter. Your license will be emailed shortly.",
    "buy.title": "Buy license",
    "buy.pricing_title": "Pricing",
    "buy.pricing_product": "Body Exporter — Personal license (one machine)",
    "buy.pricing_amount_label": "Price",
    "buy.pricing_vn_sub": "International checkout: about ${usd} USD",
    "buy.pricing_intl_sub": "Reference: {vnd} VND for bank transfer in Vietnam",
    "buy.pricing_intl_pending": "Contact us for international pricing",
    "buy.pricing_term": "License term: {days} days from activation",
    "buy.pricing_f1": "Export SolidWorks bodies/parts to Excel & templates",
    "buy.pricing_f2": "License key emailed automatically after payment",
    "buy.pricing_f3": "14-day trial on first install (internet required once)",
    "buy.pricing_f4": "Email support after purchase",
    "buy.pricing_legal": (
        'See our <a href="/terms-and-conditions">Terms</a>, '
        '<a href="/privacy">Privacy</a>, and <a href="/refund">Refund</a> policies.'
    ),
    "buy.checkout_title": "Checkout",
    "buy.step_label_email": "1. Email",
    "buy.step_label_pay": "2. Pay",
    "buy.btn_continue": "Continue",
    "buy.paddle_opening": "Opening Paddle checkout…",
    "buy.btn_edit_email": "Edit email",
    "buy.step2_email_sent": "License will be sent to",
    "buy.step2_vn_lead": "Scan the QR or transfer the exact amount and memo below.",
    "buy.step2_intl_lead": "Step 2: pay <strong>${usd} USD</strong> by card / PayPal.",
    "buy.paddle_page_title": "International payment",
    "buy.paddle_page_lead": "Secure checkout via Paddle (card / PayPal).",
    "buy.paddle_back": "← Back to buy page",
    "buy.paddle_create_fail": "Could not start Paddle checkout ({error}). Check server env.",
    "buy.paddle_default_link": (
        "Paddle default payment link is not set. In Paddle → Checkout → Checkout settings, "
        "set https://bodyexporter.com/buy/paddle (not /webhook/paddle)."
    ),
    "buy.paddle_network": (
        "<strong>Paddle checkout could not load</strong> (<code>checkout-service.paddle.com</code>). "
        "If mobile 4G still fails, it is often ISP/network or your Paddle account — not the website. "
        "Customers in Vietnam: use <strong>VietQR</strong> below. "
        "Paddle default payment link: <code>https://bodyexporter.com/buy/paddle</code>."
    ),
    "buy.paddle_use_vn": "Pay with VietQR (recommended in Vietnam)",
    "buy.paddle_retry": "Retry Paddle",
    "buy.paddle_sepay_fallback": "Pay by card via SePay (VND)",
    "buy.paddle_brave": (
        "<strong>VeePN / VPN with AdBlock:</strong> turn <strong>AdBlock OFF</strong> inside the VeePN extension "
        "(green toggle), or disconnect VPN while testing Paddle. VPN ad-blockers often block "
        "<code>checkout-service.paddle.com</code>. Edge: 🔒 → Tracking prevention → <strong>Off</strong>, then refresh."
    ),
    "buy.paddle_vn_fallback": "Or pay with VietQR (VND) on the buy page",
    "buy.paddle_support": (
        "Still stuck? Email <a href=\"mailto:{email}?subject=Paddle%20{txn}\">{email}</a> "
        "with <code>{txn}</code>."
    ),
    "buy.pay_mode_label": "Payment method",
    "buy.mode_vn": "VietQR · bank transfer",
    "buy.mode_intl": "International · card",
    "buy.enter_email_hint": "Enter your email to see payment options.",
    "buy.qr_loading": "Loading QR…",
    "buy.qr_fail": "Could not generate QR. Check your email.",
    "buy.net_fail": "Network error. Try again.",
    "buy.paddle_loading": "Checkout is loading. Wait a few seconds and try again.",
    "buy.paddle_fail": "Could not open checkout. Contact {email}",
    "buy.intl_lead": "You will pay <strong>${usd} USD</strong> (card / PayPal). License is emailed after payment.",
    "buy.paddle_hint": "Secure international checkout.",
    "buy.btn_paddle": "Pay ${usd} USD",
    "buy.intl_unavailable": "International checkout is not enabled yet. Contact <a href=\"mailto:{email}\">{email}</a>.",
    "buy.vietqr_unavailable": "Vietnamese bank transfer is not configured. Contact <a href=\"mailto:{email}\">{email}</a>.",
    "buy.checkout_unavailable": "Checkout is not configured. Email <a href=\"mailto:{email}\">{email}</a>.",
    "buy.intro_default": (
        "Enter your email to receive a license automatically after payment. "
        "Choose bank transfer (QR) or card below."
    ),
    "buy.footer_default": (
        "<p>After a successful transfer with the correct amount and memo, your license is sent to your email "
        "within a few minutes. Check spam if needed. Support: "
        '<a href="mailto:hotro@bodyexporter.com">hotro@bodyexporter.com</a>.</p>'
    ),
    "buy.email_label": "Email for license delivery",
    "buy.email_hint": "Use this exact email in the transfer memo: <code>BE {email}</code>",
    "buy.btn_qr": "Show bank QR",
    "buy.btn_card": "Pay by card",
    "buy.redirect_sepay": "Redirecting to payment…",
    "buy.transfer_title": "Bank transfer — {amount} ₫",
    "buy.bank": "Bank",
    "buy.account": "Account number",
    "buy.amount": "Amount",
    "buy.memo": "Transfer memo",
    "buy.wait_hint": (
        "<strong>License email is not instant.</strong> Key is sent to <strong>{email}</strong> "
        "after the bank confirms payment (usually 1–10 minutes). "
        "Memo must include <code>{memo}</code>. Check spam."
    ),
    "buy.success_title": "Thank you",
    "buy.success_with_email": (
        "Your license will be emailed to <strong>{email}</strong> after payment is confirmed, "
        "usually within a few minutes."
    ),
    "buy.success_no_email": "License will be sent to the email you used when payment is confirmed.",
    "buy.success_spam": "No email yet? Wait a bit, check spam, and ensure transfer memo is <code>BE {email}</code>.",
    "buy.success_download": "Download & install plugin",
    "page.home": "Body Exporter — SolidWorks Excel export add-in",
    "page.download": "Download Body Exporter plugin",
    "page.buy": "Buy Body Exporter license",
    "page.buy_success": "Payment received — Body Exporter",
    "error.404.title": "Page not found",
    "error.404.lead": "This URL does not exist or has moved.",
    "error.404.home": "Home",
    "error.404.download": "Download plugin",
    "error.500.title": "Something went wrong",
    "error.500.lead": "Try reloading. If it persists, contact support.",
    "error.500.home": "Home",
    "error.500.contact": "Contact",
}
