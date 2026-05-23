# License email templates (Resend)

Upload **two published templates** in [Resend → Templates](https://resend.com/templates) with the same variables:

| Variable | Example |
|----------|---------|
| `name` | Phong |
| `license_key` | `6d590e94-…` |
| `plan` | personal |
| `expires` | 23/05/2027 |

- **Vietnamese:** `license-vi.html` (same as your `template.html`).
- **English:** `license-en.html` (mirror layout, English copy).

Then on Render:

```env
RESEND_LICENSE_TEMPLATE_ID_VI=<published-vi-template-id>
RESEND_LICENSE_TEMPLATE_ID_EN=<published-en-template-id>
RESEND_LICENSE_SUBJECT_VI=License key Body Exporter — SolidWorks
RESEND_LICENSE_SUBJECT_EN=Your Body Exporter license key
```

Legacy single template (both languages fallback):

```env
RESEND_LICENSE_TEMPLATE_ID=<vi-id>
```

**Routing:** VietQR / SePay webhook → Vietnamese template. Paddle webhook → English template.
