/**
 * SolidWorks Body Exporter — Licensing API
 * =========================================
 *
 * Runs on Cloudflare Workers (free tier covers ~100k requests/day, which is plenty
 * even with daily JWT refresh from every active install). Stores license records
 * in Cloudflare KV (key-value storage) and signs short-lived JWTs with an RSA-2048
 * private key held in a Worker secret.
 *
 * Endpoints
 * ---------
 * - POST /v1/license/validate  -> body {key, machineId, productVersion}
 *      Validates the license key against the KV store, binds it to the supplied
 *      machineId on first activation, refuses subsequent calls from other
 *      machines (unless the license is plan=floating), and returns a 24h JWT.
 *
 * - POST /admin/license/issue   -> Bearer ADMIN_TOKEN, body {owner, plan, days}
 *      Admin-only endpoint you call from your billing webhook (Stripe, Lemon
 *      Squeezy, etc.) to mint a new license key when a customer pays. Returns
 *      {key, expires}. Hand the key to the customer in their receipt email.
 *
 * - GET  /admin/license/list    -> Bearer ADMIN_TOKEN
 *      Returns a JSON array of all issued license keys + their bound machineIds.
 *      Useful for support: "what's my license bound to?".
 *
 * - DELETE /admin/license/:key  -> Bearer ADMIN_TOKEN
 *      Revokes a license. Subsequent /v1/license/validate calls will fail with
 *      403 "Revoked". Useful for chargebacks or customer-requested re-binding.
 *
 * - GET  /v1/client-config
 *      Public JSON the desktop app shows in the License window: author name, support email,
 *      payment instructions (Vietnam Sepay + international Triple links), update manifest URL.
 *      Stored in KV under __client_config__ as raw JSON (edit via PUT /admin/client-config).
 *
 * - PUT  /admin/client-config -> Bearer ADMIN_TOKEN, body = raw JSON
 *      Replaces the published client config blob (no merge - full body is authoritative).
 *
 * - GET  /v1/update-manifest -> X-Machine-Id of a registered machine, or Bearer ADMIN_TOKEN
 *      Version, download URL and release notes. Clients must be a fingerprint the server knows
 *      from a license binding or a trial start. Site admin tools may read with the admin token.
 *
 * KV namespace layout
 * -------------------
 * - LICENSE_DB        : key=license-uuid, value=LicenseRecord JSON
 * - LICENSE_BY_MACHINE: key=machineId,    value=license-uuid  (reverse lookup)
 *
 * Secrets (set via `wrangler secret put`)
 * ---------------------------------------
 * - ADMIN_TOKEN       : bearer token for /admin endpoints
 * - JWT_PRIVATE_KEY   : RSA-2048 PEM (sign JWTs)
 * - JWT_PUBLIC_KEY    : RSA-2048 PEM (embed in client DLL for offline validation)
 * - LEMON_SQUEEZY_SIGNING_SECRET : HMAC secret from Lemon webhook settings
 * - RESEND_API_KEY    : optional — send license email after Lemon order (https://resend.com)
 * - RESEND_FROM       : optional — verified sender, e.g. "Body Exporter <orders@yourdomain.com>"
 *                       (defaults to Resend onboarding address for dev only)
 * - SEPAY_WEBHOOK_API_KEY : optional — Apikey auth from SePay webhook settings (recommended)
 * - SEPAY_WEBHOOK_SECRET  : optional — HMAC secret if you use HMAC-SHA256 instead of API Key
 * - SEPAY_LICENSE_AMOUNT_VND : optional fallback if client-config URL has no amount=
 * - SEPAY_LEGACY_AMOUNTS_VND : optional comma list (e.g. 990000) for older transfers after a price change
 * - SEPAY_LICENSE_DAYS       : optional — license duration after VN transfer (default 365)
 *
 * IMPORTANT: this file is intentionally framework-free (no Hono, no itty-router)
 * so it can be deployed via `wrangler deploy` with zero npm install required on
 * the user's machine beyond `wrangler` itself. Keep it that way unless you need
 * features that justify pulling a router in.
 */

export interface Env {
    LICENSE_DB: KVNamespace;
    LICENSE_BY_MACHINE: KVNamespace;
    ADMIN_TOKEN: string;
    JWT_PRIVATE_KEY: string;
    /** Same secret you typed in Lemon Squeezy when creating the webhook (HMAC SHA-256 of raw body → X-Signature). */
    LEMON_SQUEEZY_SIGNING_SECRET?: string;
    /** https://resend.com — after a paid Lemon order, email the buyer their license UUID. */
    RESEND_API_KEY?: string;
    /** Sender shown to the buyer; must be a domain you verified in Resend (or Resend onboarding for tests). */
    RESEND_FROM?: string;
    /** SePay → Authorization: Apikey YOUR_KEY */
    SEPAY_WEBHOOK_API_KEY?: string;
    /** SePay HMAC-SHA256 secret (alternative to API Key). */
    SEPAY_WEBHOOK_SECRET?: string;
    /** Fallback VND amount if client-config paymentVnSepayUrl has no amount= (prefer updating the URL). */
    SEPAY_LICENSE_AMOUNT_VND?: string;
    /** Comma-separated legacy amounts still accepted (e.g. 990000 after raising price to 1590000). */
    SEPAY_LEGACY_AMOUNTS_VND?: string;
    /** Days granted per successful SePay transfer (default 365). */
    SEPAY_LICENSE_DAYS?: string;
}

interface LicenseRecord {
    key: string;
    owner: string;
    plan: "personal" | "team" | "site" | "floating";
    issuedAt: string;
    expiresAt: string;
    boundMachineId: string | null;
    revoked: boolean;
}

const corsHeaders: Record<string, string> = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET, POST, DELETE, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type, Authorization, X-Machine-Id",
};

export default {
    async fetch(request: Request, env: Env): Promise<Response> {
        if (request.method === "OPTIONS") {
            return new Response(null, { status: 204, headers: corsHeaders });
        }

        const url = new URL(request.url);

        try {
            if (url.pathname === "/v1/license/validate" && request.method === "POST") {
                return await handleValidate(request, env);
            }
            if (url.pathname === "/v1/trial/start" && request.method === "POST") {
                return await handleTrialStart(request, env);
            }
            if (url.pathname === "/admin/license/issue" && request.method === "POST") {
                return await requireAdmin(request, env, () => handleIssue(request, env));
            }
            if (url.pathname === "/admin/license/list" && request.method === "GET") {
                return await requireAdmin(request, env, () => handleList(env));
            }
            if (url.pathname === "/admin/license/send-email" && request.method === "POST") {
                return await requireAdmin(request, env, () => handleSendLicenseEmail(request, env));
            }
            if (url.pathname === "/admin/sepay/resend-email" && request.method === "POST") {
                return await requireAdmin(request, env, () => handleSepayResendEmailAdmin(request, env));
            }
            if (url.pathname === "/admin/sepay/reprocess" && request.method === "POST") {
                return await requireAdmin(request, env, () => handleSepayReprocessAdmin(request, env));
            }
            if (url.pathname.startsWith("/admin/license/") && request.method === "DELETE") {
                const key = url.pathname.split("/").pop()!;
                return await requireAdmin(request, env, () => handleRevoke(key, env));
            }
            if (url.pathname === "/v1/client-config" && request.method === "GET") {
                return await handleClientConfigGet(env);
            }
            if (url.pathname === "/v1/update-manifest" && request.method === "GET") {
                return await handleUpdateManifestGet(request, env);
            }
            if (url.pathname === "/admin/update-manifest" && request.method === "PUT") {
                return await requireAdmin(request, env, () => handleUpdateManifestPut(request, env));
            }
            if (url.pathname === "/admin/client-config" && request.method === "PUT") {
                return await requireAdmin(request, env, () => handleClientConfigPut(request, env));
            }
            if (url.pathname === "/health") {
                return jsonResponse({ status: "ok", version: "1.0.1" });
            }
            if (url.pathname === "/webhook/lemon-squeezy" && request.method === "POST") {
                return await handleLemonWebhook(request, env);
            }
            if (url.pathname === "/webhook/sepay" && request.method === "POST") {
                return await handleSepayWebhook(request, env);
            }
            return jsonResponse({ error: "not_found" }, 404);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            return jsonResponse({ error: "internal_error", detail: msg }, 500);
        }
    },
} satisfies ExportedHandler<Env>;

/* ──────────────────────────────────────────────────────────────────────
   LICENSE VALIDATION (client-facing, hot path)
   Flow:
     1. Look up the license record by key. 404 if not found.
     2. Reject if revoked or past expiresAt.
     3. First activation: bind boundMachineId to the supplied machineId.
        Subsequent activations: enforce the binding for non-floating plans.
     4. Mint a 24h JWT signed with JWT_PRIVATE_KEY. Client validates the
        signature locally with the embedded public key, so the JWT survives
        offline use until expiry.
   ──────────────────────────────────────────────────────────────────── */
async function handleValidate(request: Request, env: Env): Promise<Response> {
    const body = await request.json<{ key: string; machineId: string; productVersion?: string }>();
    if (!body.key || !body.machineId) {
        return jsonResponse({ error: "missing_fields" }, 400);
    }

    const record = await env.LICENSE_DB.get<LicenseRecord>(body.key, "json");
    if (!record) return jsonResponse({ error: "license_not_found" }, 404);
    if (record.revoked) return jsonResponse({ error: "license_revoked" }, 403);

    const now = Date.now();
    const expiresAt = Date.parse(record.expiresAt);
    if (Number.isFinite(expiresAt) && expiresAt < now) {
        return jsonResponse({ error: "license_expired", expiresAt: record.expiresAt }, 403);
    }

    if (record.plan !== "floating") {
        if (!record.boundMachineId) {
            // First activation - bind to this machine.
            record.boundMachineId = body.machineId;
            await env.LICENSE_DB.put(body.key, JSON.stringify(record));
            await env.LICENSE_BY_MACHINE.put(body.machineId, body.key);
        } else if (record.boundMachineId !== body.machineId) {
            return jsonResponse(
                {
                    error: "machine_mismatch",
                    detail: "This license is already bound to a different machine. Contact support to rebind.",
                },
                403,
            );
        }
    }

    const tokenExpiresAt = new Date(now + 24 * 60 * 60 * 1000).toISOString();
    const token = await signJwt(
        {
            sub: record.key,
            owner: record.owner,
            plan: record.plan,
            machineId: body.machineId,
            licenseExpires: record.expiresAt,
            iat: Math.floor(now / 1000),
            exp: Math.floor(Date.parse(tokenExpiresAt) / 1000),
        },
        env.JWT_PRIVATE_KEY,
    );

    return jsonResponse({
        token,
        expiresUtc: tokenExpiresAt,
        owner: record.owner,
        plan: record.plan,
        licenseExpires: record.expiresAt,
    });
}

const TRIAL_DAYS = 14;
const TRIAL_KV_PREFIX = "trial:";

interface TrialRecord {
    machineId: string;
    startedAt: string;
    expiresAt: string;
}

/** One 14-day trial per machine fingerprint (stored in LICENSE_DB). */
async function handleTrialStart(request: Request, env: Env): Promise<Response> {
    const body = await request.json<{ machineId: string; productVersion?: string }>();
    if (!body.machineId || body.machineId.length < 16) {
        return jsonResponse({ error: "missing_machine_id" }, 400);
    }

    const key = TRIAL_KV_PREFIX + body.machineId;
    const now = Date.now();
    const existing = await env.LICENSE_DB.get<TrialRecord>(key, "json");

    if (existing) {
        const expiresAt = Date.parse(existing.expiresAt);
        if (Number.isFinite(expiresAt) && expiresAt < now) {
            return jsonResponse({ error: "trial_expired", expiresUtc: existing.expiresAt }, 403);
        }

        const daysRemaining = Math.max(0, Math.ceil((expiresAt - now) / (24 * 60 * 60 * 1000)));
        return jsonResponse({
            startedUtc: existing.startedAt,
            expiresUtc: existing.expiresAt,
            daysRemaining,
        });
    }

    const startedAt = new Date(now).toISOString();
    const expiresAt = new Date(now + TRIAL_DAYS * 24 * 60 * 60 * 1000).toISOString();
    const record: TrialRecord = {
        machineId: body.machineId,
        startedAt,
        expiresAt,
    };
    await env.LICENSE_DB.put(key, JSON.stringify(record));

    return jsonResponse({
        startedUtc: startedAt,
        expiresUtc: expiresAt,
        daysRemaining: TRIAL_DAYS,
    });
}

/* ──────────────────────────────────────────────────────────────────────
   ADMIN ENDPOINTS  (issue / list / revoke)
   ──────────────────────────────────────────────────────────────────── */
async function mintLicense(
    env: Env,
    owner: string,
    plan: LicenseRecord["plan"],
    days: number,
): Promise<{ key: string; owner: string; plan: string; expiresAt: string }> {
    const key = crypto.randomUUID();
    const issuedAt = new Date().toISOString();
    const expiresAt = new Date(Date.now() + days * 24 * 60 * 60 * 1000).toISOString();
    const record: LicenseRecord = {
        key,
        owner,
        plan,
        issuedAt,
        expiresAt,
        boundMachineId: null,
        revoked: false,
    };
    await env.LICENSE_DB.put(key, JSON.stringify(record));
    return { key, owner, plan, expiresAt };
}

async function handleIssue(request: Request, env: Env): Promise<Response> {
    const body = await request.json<{ owner: string; plan: LicenseRecord["plan"]; days: number }>();
    if (!body.owner || !body.plan || !body.days) {
        return jsonResponse({ error: "missing_fields" }, 400);
    }
    const out = await mintLicense(env, body.owner, body.plan, body.days);
    return jsonResponse(out);
}

/** Lemon Squeezy → mint KV license (same key format as POST /admin/license/issue). */
async function handleLemonWebhook(request: Request, env: Env): Promise<Response> {
    const rawBody = await request.text();
    const secret = env.LEMON_SQUEEZY_SIGNING_SECRET ?? "";
    if (!secret) {
        return jsonResponse({ error: "lemon_signing_secret_not_configured" }, 503);
    }

    const headerSig = request.headers.get("X-Signature") ?? "";
    const okSig = await verifyLemonHmacHex(secret, rawBody, headerSig);
    if (!okSig) {
        return jsonResponse({ error: "invalid_signature" }, 401);
    }

    let parsed: unknown;
    try {
        parsed = JSON.parse(rawBody);
    } catch {
        return jsonResponse({ error: "invalid_json" }, 400);
    }

    const doc = parsed as {
        data?: { type?: string; id?: string; attributes?: { user_email?: string; status?: string } };
    };

    const eventName =
        request.headers.get("X-Event-Name") ??
        (typeof parsed === "object" && parsed !== null && "meta" in parsed
            ? String((parsed as { meta?: { event_name?: string } }).meta?.event_name ?? "")
            : "");

    const isOrderEvent =
        (eventName === "order_created" || eventName === "order_paid") && doc.data?.type === "orders" && doc.data.id;

    if (isOrderEvent) {
        const orderId = doc.data!.id!;
        const issueKey = `lemon-order-issue:${orderId}`;
        const buyerEmail = doc.data!.attributes?.user_email?.trim() ?? "";

        const already = await env.LICENSE_DB.get(issueKey);
        if (already) {
            let previousKey: string | undefined;
            try {
                previousKey = (JSON.parse(already) as { key?: string }).key;
            } catch {
                /* ignore */
            }
            const resendOut =
                env.RESEND_API_KEY && buyerEmail && previousKey
                    ? await sendLicenseEmailResend(env, {
                          to: buyerEmail,
                          licenseKey: previousKey,
                          orderId,
                      })
                    : { ok: true as const, skipped: true as const };
            return jsonResponse({
                ok: true,
                duplicate: true,
                key: previousKey,
                resendEmail: resendOut,
                ...(resendOut.ok ? {} : { emailWarning: "License exists but Resend failed. Fix RESEND_FROM / domain on Resend, then POST /admin/license/send-email." }),
            });
        }

        const status = (doc.data!.attributes?.status ?? "").toLowerCase();
        if (status !== "paid" && status !== "completed") {
            return jsonResponse({ ok: true, ignored: "order_not_paid", status });
        }

        if (!buyerEmail) {
            return jsonResponse({ error: "missing_user_email" }, 400);
        }

        const days = 365;
        const plan: LicenseRecord["plan"] = "personal";
        const out = await mintLicense(env, buyerEmail, plan, days);
        await env.LICENSE_DB.put(issueKey, JSON.stringify({ at: new Date().toISOString(), key: out.key }), {
            expirationTtl: 86400 * 400,
        });

        const resendOut = await sendLicenseEmailResend(env, {
            to: buyerEmail,
            licenseKey: out.key,
            orderId,
        });
        return jsonResponse({
            ok: true,
            issued: true,
            key: out.key,
            owner: out.owner,
            plan: out.plan,
            expiresAt: out.expiresAt,
            resendEmail: resendOut,
            ...(resendOut.ok ? {} : { emailWarning: "License created in KV but Resend failed. Check resendEmail.detail; fix RESEND_FROM then POST /admin/license/send-email." }),
            hint: "Customer: set ApiBaseUrl in %APPDATA%\\SolidWorksBodyExporter\\settings.json and LicenseKey to this UUID if your build uses online validation; otherwise issue a signed .lic via tools/LicenseGen.",
        });
    }

    const idemKey = `lemon-webhook:${eventName}:${hash32(rawBody)}`;
    const seen = await env.LICENSE_DB.get(idemKey);
    if (!seen) {
        await env.LICENSE_DB.put(idemKey, "1", { expirationTtl: 86400 * 7 });
    }
    return jsonResponse({ ok: true, ignored: true, eventName });
}

/** SePay bank transfer → mint license + Resend email. Must respond {"success": true} on HTTP 200. */
async function handleSepayWebhook(request: Request, env: Env): Promise<Response> {
    const rawBody = await request.text();
    const authOk = await verifySepayWebhookAuth(request, env, rawBody);
    if (!authOk) {
        return new Response("Unauthorized", { status: 401, headers: corsHeaders });
    }

    let payload: SepayWebhookPayload;
    try {
        payload = JSON.parse(rawBody) as SepayWebhookPayload;
    } catch {
        return new Response("Bad Request", { status: 400, headers: corsHeaders });
    }

    await fulfillSepayTransfer(env, payload);
    return sepaySuccessResponse();
}

type SepayFulfillResult = {
    ok: boolean;
    action: "skipped" | "replayed_email" | "minted" | "ignored";
    transactionId?: number;
    email?: string;
    licenseKey?: string;
    reason?: string;
    resendEmail?: { ok: boolean; skipped?: boolean; id?: string; detail?: string };
};

/** Core mint + email logic (webhook and admin reprocess). */
async function fulfillSepayTransfer(env: Env, payload: SepayWebhookPayload): Promise<SepayFulfillResult> {
    if ((payload.transferType ?? "").toLowerCase() !== "in") {
        return { ok: true, action: "skipped", reason: "not_incoming_transfer" };
    }

    const txId = payload.id;
    if (txId == null || !Number.isFinite(Number(txId))) {
        return { ok: true, action: "skipped", reason: "invalid_transaction_id" };
    }

    const idemKey = `sepay-tx:${txId}`;
    const ignoredKey = `sepay-ignored:${txId}`;
    const already = await env.LICENSE_DB.get(idemKey);
    if (already) {
        await handleSepayIdempotentReplay(env, already, payload, txId);
        let licenseKey: string | undefined;
        let email: string | undefined;
        try {
            const parsed = JSON.parse(already) as { key?: string; email?: string };
            licenseKey = parsed.key;
            email = parsed.email;
        } catch {
            /* ignore */
        }
        return {
            ok: true,
            action: "replayed_email",
            transactionId: txId,
            licenseKey,
            email,
        };
    }

    await tryClearSepayIgnoredForRetry(env, ignoredKey, payload);

    const allowedAmounts = await getSepayAllowedAmounts(env);
    const amount = Number(payload.transferAmount);
    if (!isSepayAmountAllowed(amount, allowedAmounts)) {
        await env.LICENSE_DB.put(
            `sepay-ignored:${txId}`,
            JSON.stringify({
                reason: "amount_mismatch",
                amount,
                allowedAmounts,
                at: new Date().toISOString(),
            }),
            { expirationTtl: 86400 * 30 },
        );
        return {
            ok: true,
            action: "ignored",
            transactionId: txId,
            reason: "amount_mismatch",
        };
    }

    const buyerEmail = extractEmailFromTransferText(payload.content, payload.description, payload.code);
    if (!buyerEmail) {
        await env.LICENSE_DB.put(
            `sepay-ignored:${txId}`,
            JSON.stringify({ reason: "no_email_in_memo", content: payload.content, at: new Date().toISOString() }),
            { expirationTtl: 86400 * 30 },
        );
        return { ok: true, action: "ignored", transactionId: txId, reason: "no_email_in_memo" };
    }

    const days = Math.max(1, parseInt(env.SEPAY_LICENSE_DAYS ?? "365", 10) || 365);
    const plan: LicenseRecord["plan"] = "personal";
    const out = await mintLicense(env, buyerEmail, plan, days);
    await env.LICENSE_DB.put(idemKey, JSON.stringify({ at: new Date().toISOString(), key: out.key, email: buyerEmail }), {
        expirationTtl: 86400 * 400,
    });

    const resendOut = await sendLicenseEmailResend(env, {
        to: buyerEmail,
        licenseKey: out.key,
        orderId: `sepay-${txId}`,
    });
    logSepayResendResult("mint", txId, buyerEmail, resendOut);

    return {
        ok: true,
        action: "minted",
        transactionId: txId,
        email: buyerEmail,
        licenseKey: out.key,
        resendEmail: resendOut,
    };
}

/** Earlier 200 responses may have stored sepay-ignored (no @ in memo). Allow SePay replay to mint + email. */
async function tryClearSepayIgnoredForRetry(
    env: Env,
    ignoredKey: string,
    payload: SepayWebhookPayload,
): Promise<void> {
    const raw = await env.LICENSE_DB.get(ignoredKey);
    if (!raw) return;
    try {
        const ign = JSON.parse(raw) as { reason?: string };
        const email = extractEmailFromTransferText(payload.content, payload.description, payload.code);
        const amount = Number(payload.transferAmount);
        if (ign.reason === "no_email_in_memo" && email) {
            await env.LICENSE_DB.delete(ignoredKey);
            console.log(`Sepay: cleared ${ignoredKey} — email now parsed as ${email}`);
            return;
        }
        if (ign.reason === "amount_mismatch") {
            const allowed = await getSepayAllowedAmounts(env);
            const priorAmount = (ign as { amount?: number }).amount;
            if (
                isSepayAmountAllowed(amount, allowed) ||
                (Number.isFinite(priorAmount) && amount === priorAmount)
            ) {
                await env.LICENSE_DB.delete(ignoredKey);
                console.log(`Sepay: cleared ${ignoredKey} — amount ${amount} now accepted`);
            }
        }
    } catch {
        /* ignore */
    }
}

async function handleSepayIdempotentReplay(
    env: Env,
    already: string,
    payload: SepayWebhookPayload,
    txId: number,
): Promise<void> {
    let previousKey: string | undefined;
    let previousEmail: string | undefined;
    try {
        const parsed = JSON.parse(already) as { key?: string; email?: string };
        previousKey = parsed.key;
        previousEmail = parsed.email;
    } catch {
        /* ignore */
    }
    const buyerEmail =
        extractEmailFromTransferText(payload.content, payload.description, payload.code) ?? previousEmail;
    if (!previousKey) {
        console.warn(`Sepay replay tx=${txId}: no license key in KV — replay after deploy with fixed email parser`);
        return;
    }
    if (!buyerEmail) {
        console.warn(`Sepay replay tx=${txId}: could not parse buyer email from memo`);
        return;
    }
    const resendOut = await sendLicenseEmailResend(env, {
        to: buyerEmail,
        licenseKey: previousKey,
        orderId: `sepay-${txId}`,
    });
    logSepayResendResult("replay", txId, buyerEmail, resendOut);
}

function logSepayResendResult(
    phase: string,
    txId: number,
    to: string,
    resendOut: { ok: boolean; skipped?: boolean; id?: string; detail?: string },
): void {
    if (resendOut.skipped) {
        console.warn(
            `Sepay ${phase} tx=${txId}: license ok but RESEND_API_KEY not set — email not sent to ${to}`,
        );
        return;
    }
    if (!resendOut.ok) {
        console.error(`Sepay ${phase} tx=${txId}: Resend failed for ${to}: ${resendOut.detail ?? "unknown"}`);
        return;
    }
    console.log(`Sepay ${phase} tx=${txId}: email sent to ${to}, resendId=${resendOut.id ?? "n/a"}`);
}

interface SepayWebhookPayload {
    id?: number;
    gateway?: string;
    transactionDate?: string;
    accountNumber?: string;
    code?: string | null;
    content?: string;
    transferType?: string;
    description?: string;
    transferAmount?: number;
    referenceCode?: string;
}

function sepaySuccessResponse(): Response {
    return new Response(JSON.stringify({ success: true }), {
        status: 200,
        headers: { "Content-Type": "application/json", ...corsHeaders },
    });
}

function extractEmailFromTransferText(...parts: (string | null | undefined)[]): string | null {
    const combined = parts.filter((p) => p && String(p).trim().length > 0).join(" ");
    if (!combined) return null;

    const withAt = combined.match(/[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}/);
    if (withAt) return withAt[0].toLowerCase();

    // Banks often strip "@" from transfer memo (e.g. BE 2024hoaphonggmailcom).
    const beGmail = combined.match(/\bBE\s+([a-zA-Z0-9._+-]+)gmailcom\b/i);
    if (beGmail) return `${beGmail[1]}@gmail.com`.toLowerCase();

    const mangled = combined.match(/\b([a-zA-Z0-9._+-]{2,40})(gmail|yahoo|hotmail|outlook)com\b/i);
    if (mangled) return `${mangled[1]}@${mangled[2]}.com`.toLowerCase();

    return null;
}

/** Amounts that mint a license — primary source: paymentVnSepayUrl?amount= in client-config KV. */
async function getSepayAllowedAmounts(env: Env): Promise<number[]> {
    const amounts = new Set<number>();

    const fromConfigUrl = await getSepayAmountFromClientConfig(env);
    if (fromConfigUrl != null) {
        amounts.add(fromConfigUrl);
    }

    const fromSecret = env.SEPAY_LICENSE_AMOUNT_VND?.trim();
    if (fromSecret) {
        const n = parseInt(fromSecret, 10);
        if (Number.isFinite(n) && n > 0) {
            amounts.add(n);
        }
    }

    const legacy = env.SEPAY_LEGACY_AMOUNTS_VND?.trim();
    if (legacy) {
        for (const part of legacy.split(",")) {
            const n = parseInt(part.trim(), 10);
            if (Number.isFinite(n) && n > 0) {
                amounts.add(n);
            }
        }
    }

    if (amounts.size === 0) {
        amounts.add(990000);
    }

    return [...amounts];
}

async function getSepayAmountFromClientConfig(env: Env): Promise<number | null> {
    const raw = await env.LICENSE_DB.get(CLIENT_CONFIG_KV_KEY, "text");
    if (!raw?.trim()) {
        return null;
    }
    try {
        const cfg = JSON.parse(raw) as { paymentVnSepayUrl?: string };
        const url = cfg.paymentVnSepayUrl ?? "";
        const match = url.match(/[?&]amount=(\d+)/i);
        if (!match) {
            return null;
        }
        const n = parseInt(match[1], 10);
        return Number.isFinite(n) && n > 0 ? n : null;
    } catch {
        return null;
    }
}

function isSepayAmountAllowed(amount: number, allowed: number[]): boolean {
    return Number.isFinite(amount) && allowed.some((a) => a === amount);
}

async function verifySepayWebhookAuth(request: Request, env: Env, rawBody: string): Promise<boolean> {
    const apiKey = env.SEPAY_WEBHOOK_API_KEY?.trim();
    const hmacSecret = env.SEPAY_WEBHOOK_SECRET?.trim();

    if (!apiKey && !hmacSecret) {
        console.error("Sepay webhook: set SEPAY_WEBHOOK_SECRET (HMAC) or SEPAY_WEBHOOK_API_KEY, then wrangler deploy");
        return false;
    }

    const sigHeader =
        request.headers.get("X-SePay-Signature") ??
        request.headers.get("X-Sepay-Signature") ??
        "";
    const timestamp =
        request.headers.get("X-SePay-Timestamp") ??
        request.headers.get("X-Sepay-Timestamp") ??
        "";
    const hasHmacHeaders = Boolean(sigHeader && timestamp);

    // SePay HMAC mode sends X-SePay-Signature — verify that first (do not require Apikey header).
    if (hasHmacHeaders) {
        if (!hmacSecret) {
            console.error(
                "Sepay webhook: X-SePay-Signature present but SEPAY_WEBHOOK_SECRET is not set. " +
                    "Run: wrangler secret put SEPAY_WEBHOOK_SECRET",
            );
            return false;
        }
        if (await verifySepayHmac(hmacSecret, rawBody, timestamp, sigHeader)) {
            return true;
        }
        console.error(
            "Sepay webhook: HMAC mismatch — SEPAY_WEBHOOK_SECRET must match Secret Key in my.sepay.vn (HMAC-SHA256). " +
                "Use server/tools/verify-sepay-hmac.mjs to test locally.",
        );
        return false;
    }

    if (apiKey && verifySepayApiKeyHeader(request.headers.get("Authorization") ?? "", apiKey)) {
        return true;
    }

    console.error(
        "Sepay webhook: unauthorized — use HMAC-SHA256 on SePay + SEPAY_WEBHOOK_SECRET, or API Key + Authorization: Apikey …",
    );
    return false;
}

function verifySepayApiKeyHeader(authorization: string, apiKey: string): boolean {
    const auth = authorization.trim();
    if (!auth) return false;
    const prefix = "apikey ";
    if (!auth.toLowerCase().startsWith(prefix)) return false;
    const got = auth.slice(prefix.length).trim();
    return timingSafeEqualUtf8(got, apiKey);
}

async function verifySepayHmac(
    secret: string,
    rawBody: string,
    timestamp: string,
    signatureHeader: string,
): Promise<boolean> {
    if (!timestamp || !signatureHeader) return false;

    const ts = parseInt(timestamp, 10);
    if (!Number.isFinite(ts)) return false;
    const skewSec = Math.abs(Math.floor(Date.now() / 1000) - ts);
    if (skewSec > 300) {
        // SePay "Phát lại" may resend the original timestamp; verify signature only.
        console.warn(`Sepay webhook: timestamp skew ${skewSec}s (signature check continues)`);
    }

    const prefix = "sha256=";
    const sigHex = signatureHeader.toLowerCase().startsWith(prefix)
        ? signatureHeader.slice(prefix.length).trim()
        : signatureHeader.trim();
    const message = `${timestamp}.${rawBody}`;
    const enc = new TextEncoder();
    const key = await crypto.subtle.importKey(
        "raw",
        enc.encode(secret),
        { name: "HMAC", hash: "SHA-256" },
        false,
        ["sign"],
    );
    const mac = new Uint8Array(await crypto.subtle.sign("HMAC", key, enc.encode(message)));
    const hex = [...mac].map((b) => b.toString(16).padStart(2, "0")).join("");
    const expectedHeader = `sha256=${hex}`;
    return (
        timingSafeEqualUtf8(hex.toLowerCase(), sigHex.toLowerCase()) ||
        timingSafeEqualUtf8(expectedHeader.toLowerCase(), signatureHeader.trim().toLowerCase())
    );
}

function timingSafeEqualUtf8(a: string, b: string): boolean {
    const enc = new TextEncoder();
    const ba = enc.encode(a);
    const bb = enc.encode(b);
    if (ba.length !== bb.length) return false;
    let diff = 0;
    for (let i = 0; i < ba.length; i++) diff |= ba[i]! ^ bb[i]!;
    return diff === 0;
}

function hash32(text: string): string {
    let h = 2166136261;
    for (let i = 0; i < text.length; i++) {
        h ^= text.charCodeAt(i);
        h = Math.imul(h, 16777619);
    }
    return (h >>> 0).toString(16);
}

/** Resend REST API — no SDK dependency. Idempotency-Key dedupes Lemon retries. */
async function sendLicenseEmailResend(
    env: Env,
    opts: { to: string; licenseKey: string; orderId: string },
): Promise<{ ok: boolean; skipped?: boolean; id?: string; detail?: string }> {
    const apiKey = env.RESEND_API_KEY?.trim();
    if (!apiKey) {
        return { ok: true, skipped: true };
    }

    const from =
        env.RESEND_FROM?.trim() || "SolidWorks Body Exporter <onboarding@resend.dev>";
    const subject = "Your SolidWorks Body Exporter license key";
    const text = [
        "Thank you for your purchase.",
        "",
        `License key: ${opts.licenseKey}`,
        "",
        "Online activation: set LicenseKey and ApiBaseUrl in %APPDATA%\\SolidWorksBodyExporter\\settings.json (see product documentation).",
        "",
        `Order reference: ${opts.orderId}`,
    ].join("\n");
    const html = `<p>Thank you for your purchase.</p><p><strong>License key:</strong> <code>${escapeHtml(
        opts.licenseKey,
    )}</code></p><p>Online activation: set <code>LicenseKey</code> and <code>ApiBaseUrl</code> in your Body Exporter settings (see documentation).</p><p style="color:#666;font-size:12px">Order: ${escapeHtml(
        opts.orderId,
    )}</p>`;

    const res = await fetch("https://api.resend.com/emails", {
        method: "POST",
        headers: {
            Authorization: `Bearer ${apiKey}`,
            "Content-Type": "application/json",
        },
        body: JSON.stringify({ from, to: [opts.to], subject, text, html }),
    });
    const raw = await res.text();
    if (!res.ok) {
        return { ok: false, detail: raw.length > 600 ? `${raw.slice(0, 600)}…` : raw };
    }
    let id: string | undefined;
    try {
        id = (JSON.parse(raw) as { id?: string }).id;
    } catch {
        /* ignore */
    }
    return { ok: true, id };
}

function escapeHtml(s: string): string {
    return s
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");
}

async function verifyLemonHmacHex(secret: string, rawBody: string, headerSig: string): Promise<boolean> {
    const enc = new TextEncoder();
    const key = await crypto.subtle.importKey(
        "raw",
        enc.encode(secret),
        { name: "HMAC", hash: "SHA-256" },
        false,
        ["sign"],
    );
    const mac = new Uint8Array(await crypto.subtle.sign("HMAC", key, enc.encode(rawBody)));
    const hex = [...mac].map((b) => b.toString(16).padStart(2, "0")).join("");
    const a = enc.encode(hex.toLowerCase());
    const b = enc.encode(headerSig.trim().toLowerCase());
    if (a.length !== b.length) return false;
    let diff = 0;
    for (let i = 0; i < a.length; i++) diff |= a[i]! ^ b[i]!;
    return diff === 0;
}

async function handleList(env: Env): Promise<Response> {
    const list = await env.LICENSE_DB.list();
    const records: LicenseRecord[] = [];
    for (const k of list.keys) {
        if (k.name.startsWith("__")) continue;
        const rec = await env.LICENSE_DB.get<LicenseRecord>(k.name, "json");
        if (rec) records.push(rec);
    }
    return jsonResponse({ records });
}

/**
 * Admin: mint + email for a SePay tx that only has sepay-ignored (e.g. memo without @ on first webhook).
 * Body: { transactionId, transferAmount?, content?, description? }
 */
async function handleSepayReprocessAdmin(request: Request, env: Env): Promise<Response> {
    const body = await request.json<{
        transactionId?: number;
        transferAmount?: number;
        content?: string;
        description?: string;
    }>();
    const txId = body.transactionId;
    if (txId == null || !Number.isFinite(Number(txId))) {
        return jsonResponse({ error: "missing_transactionId" }, 400);
    }

    await env.LICENSE_DB.delete(`sepay-ignored:${txId}`);

    const payload: SepayWebhookPayload = {
        id: txId,
        transferType: "in",
        transferAmount:
            body.transferAmount ??
            (await getSepayAmountFromClientConfig(env)) ??
            parseInt(env.SEPAY_LICENSE_AMOUNT_VND ?? "990000", 10),
        content:
            body.content ??
            "BE 2024hoaphonggmailcom FT26136806966362 GD 6136IBT1kCC3A3SD 160526-13:19:02",
        description: body.description ?? body.content,
    };

    const result = await fulfillSepayTransfer(env, payload);
    return jsonResponse(result);
}

/** Resend license email for a SePay transaction already stored under sepay-tx:{id}. */
async function handleSepayResendEmailAdmin(request: Request, env: Env): Promise<Response> {
    const body = await request.json<{ transactionId?: number; to?: string }>();
    const txId = body.transactionId;
    if (txId == null || !Number.isFinite(Number(txId))) {
        return jsonResponse({ error: "missing_transactionId" }, 400);
    }
    const raw = await env.LICENSE_DB.get(`sepay-tx:${txId}`);
    if (!raw) {
        return jsonResponse(
            {
                error: "transaction_not_found",
                hint: "Replay webhook on SePay after wrangler deploy, or transaction was only sepay-ignored",
            },
            404,
        );
    }
    let parsed: { key?: string; email?: string };
    try {
        parsed = JSON.parse(raw) as { key?: string; email?: string };
    } catch {
        return jsonResponse({ error: "invalid_kv_record" }, 500);
    }
    if (!parsed.key) {
        return jsonResponse({ error: "no_license_key_in_record" }, 500);
    }
    const to = body.to?.trim() || parsed.email;
    if (!to) {
        return jsonResponse({ error: "missing_recipient", hint: "Pass to in body or fix memo and replay webhook" }, 400);
    }
    const resendOut = await sendLicenseEmailResend(env, {
        to,
        licenseKey: parsed.key,
        orderId: `sepay-${txId}`,
    });
    return jsonResponse({ ok: resendOut.ok, transactionId: txId, to, licenseKey: parsed.key, resendEmail: resendOut });
}

async function handleSendLicenseEmail(request: Request, env: Env): Promise<Response> {
    const body = await request.json<{ key?: string; to?: string }>();
    if (!body.key?.trim()) {
        return jsonResponse({ error: "missing_key" }, 400);
    }
    const record = await env.LICENSE_DB.get<LicenseRecord>(body.key.trim(), "json");
    if (!record) {
        return jsonResponse({ error: "license_not_found" }, 404);
    }
    const to = body.to?.trim() || record.owner;
    if (!to) {
        return jsonResponse({ error: "missing_recipient" }, 400);
    }
    const resendOut = await sendLicenseEmailResend(env, {
        to,
        licenseKey: record.key,
        orderId: `manual-${record.key}`,
    });
    return jsonResponse({ ok: resendOut.ok, key: record.key, to, resendEmail: resendOut });
}

async function handleRevoke(key: string, env: Env): Promise<Response> {
    const record = await env.LICENSE_DB.get<LicenseRecord>(key, "json");
    if (!record) return jsonResponse({ error: "license_not_found" }, 404);
    record.revoked = true;
    await env.LICENSE_DB.put(key, JSON.stringify(record));
    return jsonResponse({ ok: true, key });
}

const CLIENT_CONFIG_KV_KEY = "__client_config__";

const UPDATE_MANIFEST_KV_KEY = "__update_manifest__";

function defaultClientConfigJson(): string {
    return JSON.stringify({
        authorName: "Gió",
        supportEmail: "",
        supportUrl: "",
        latestVersion: "0.7.3",
        updateManifestUrl: "https://bodyexporter-api.bodyexporter.workers.dev/v1/update-manifest",
        releaseNotesUrl: "",
        downloadPageUrl: "",
        entitlementPolicy: { mode: "normal", capDays: 14, message: "" },
        paymentWebUrl: "",
        paymentWebTitle: "Thanh toán online",
        paymentWebBody:
            "Mở trang web, chọn QR chuyển khoản hoặc thẻ. Nhập email để nhận license tự động.",
        paymentVnTitle: "Thanh toán Việt Nam (chuyển khoản / Sepay)",
        paymentVnBody:
            "Quét mã QR Sepay hoặc chuyển khoản theo thông tin support gửi sau khi đặt hàng. Không lưu số tài khoản cứng trong app — mọi nội dung được tải từ server để bạn cập nhật bất cứ lúc nào.",
        paymentVnSepayUrl: "",
        paymentIntlTitle: "International payment (Triple / card)",
        paymentIntlBody:
            "Use the Triple link below for card or local payment methods outside Vietnam. License keys are emailed after payment is confirmed.",
        paymentIntlTripleUrl: "",
        paymentIntlLemonsqueezyUrl: "",
    });
}

async function handleClientConfigGet(env: Env): Promise<Response> {
    const raw = await env.LICENSE_DB.get(CLIENT_CONFIG_KV_KEY, "text");
    const body = raw && raw.trim().length > 0 ? raw : defaultClientConfigJson();
    return new Response(body, {
        status: 200,
        headers: { "Content-Type": "application/json; charset=utf-8", ...corsHeaders },
    });
}

async function handleClientConfigPut(request: Request, env: Env): Promise<Response> {
    const text = await request.text();
    if (!text || text.trim().length === 0) {
        return jsonResponse({ error: "empty_body" }, 400);
    }
    try {
        JSON.parse(text);
    } catch {
        return jsonResponse({ error: "invalid_json" }, 400);
    }
    await env.LICENSE_DB.put(CLIENT_CONFIG_KV_KEY, text);
    return jsonResponse({ ok: true });
}

function defaultUpdateManifestJson(): string {
    return JSON.stringify({
        version: "0.7.3",
        downloadUrl: "",
        sha256: "",
        releaseNotes: "Tai file zip moi va chay Install-BodyExporter.cmd (Admin).",
    });
}

/**
 * Serves the manifest only to a machine this server already has on record: one holding a license
 * bound to its fingerprint, or one that has started its trial. A machine nobody registered is not
 * a machine we owe an update to.
 *
 * The site admin and publish scripts may also read it with Bearer ADMIN_TOKEN. Builds older than
 * 1.2.1 send no fingerprint and are refused here, but they still learn about a new release from
 * `latestVersion` in /v1/client-config, so nobody is left stranded on an old build.
 */
async function handleUpdateManifestGet(request: Request, env: Env): Promise<Response> {
    const asAdmin = isAdminBearer(request, env);
    const machineId = (request.headers.get("X-Machine-Id") ?? "").trim();
    if (!asAdmin && !(await isKnownMachine(machineId, env))) {
        return jsonResponse(
            {
                error: "machine_not_registered",
                detail: "Activate a license or a trial on this machine before fetching updates.",
            },
            403,
        );
    }

    const raw = await env.LICENSE_DB.get(UPDATE_MANIFEST_KV_KEY, "text");
    const body = raw && raw.trim().length > 0 ? raw : defaultUpdateManifestJson();
    return new Response(body, {
        status: 200,
        headers: { "Content-Type": "application/json; charset=utf-8", ...corsHeaders },
    });
}

function isAdminBearer(request: Request, env: Env): boolean {
    const auth = request.headers.get("Authorization") ?? "";
    return auth.startsWith("Bearer ") && auth.slice("Bearer ".length) === env.ADMIN_TOKEN;
}

/** A machine the server has seen before, through a license binding or a trial start. */
async function isKnownMachine(machineId: string, env: Env): Promise<boolean> {
    if (machineId.length < 16) {
        return false;
    }

    const licensed = await env.LICENSE_BY_MACHINE.get(machineId);
    if (licensed) {
        return true;
    }

    const trial = await env.LICENSE_DB.get<TrialRecord>(TRIAL_KV_PREFIX + machineId, "json");
    return trial != null;
}

async function handleUpdateManifestPut(request: Request, env: Env): Promise<Response> {
    const text = await request.text();
    if (!text || text.trim().length === 0) {
        return jsonResponse({ error: "empty_body" }, 400);
    }
    try {
        JSON.parse(text);
    } catch {
        return jsonResponse({ error: "invalid_json" }, 400);
    }
    await env.LICENSE_DB.put(UPDATE_MANIFEST_KV_KEY, text);
    return jsonResponse({ ok: true });
}

/* ──────────────────────────────────────────────────────────────────────
   JWT (RS256) — minimal implementation using WebCrypto
   We don't pull a JWT library because Workers don't allow large npm deps
   and the spec is small. RS256 = SHA-256 over the base64url payload, signed
   with the RSA private key.
   ──────────────────────────────────────────────────────────────────── */
async function signJwt(payload: Record<string, unknown>, privateKeyPem: string): Promise<string> {
    const header = { alg: "RS256", typ: "JWT" };
    const encoder = new TextEncoder();
    const headerB64 = base64UrlEncode(encoder.encode(JSON.stringify(header)));
    const payloadB64 = base64UrlEncode(encoder.encode(JSON.stringify(payload)));
    const signingInput = `${headerB64}.${payloadB64}`;

    const keyData = pemToArrayBuffer(privateKeyPem);
    const cryptoKey = await crypto.subtle.importKey(
        "pkcs8",
        keyData,
        { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
        false,
        ["sign"],
    );
    const signature = await crypto.subtle.sign("RSASSA-PKCS1-v1_5", cryptoKey, encoder.encode(signingInput));
    const sigB64 = base64UrlEncode(new Uint8Array(signature));
    return `${signingInput}.${sigB64}`;
}

function pemToArrayBuffer(pem: string): ArrayBuffer {
    const cleaned = pem
        .replace(/-----BEGIN [A-Z ]+-----/g, "")
        .replace(/-----END [A-Z ]+-----/g, "")
        .replace(/\s+/g, "");
    const binary = atob(cleaned);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes.buffer;
}

function base64UrlEncode(bytes: Uint8Array): string {
    let bin = "";
    for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
    return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/* ──────────────────────────────────────────────────────────────────────
   HELPERS
   ──────────────────────────────────────────────────────────────────── */
async function requireAdmin(request: Request, env: Env, run: () => Promise<Response>): Promise<Response> {
    const auth = request.headers.get("Authorization") ?? "";
    if (!auth.startsWith("Bearer ") || auth.slice("Bearer ".length) !== env.ADMIN_TOKEN) {
        return jsonResponse({ error: "unauthorized" }, 401);
    }
    return await run();
}

function jsonResponse(body: unknown, status = 200): Response {
    return new Response(JSON.stringify(body), {
        status,
        headers: { "Content-Type": "application/json", ...corsHeaders },
    });
}
