using System;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>
    /// Public marketing + support + payment copy served from
    /// <c>GET /v1/client-config</c>. Stored in Worker KV under <c>__client_config__</c>
    /// so you can change wording without shipping a new DLL. Client caches locally with
    /// a short TTL to stay responsive when offline.
    /// </summary>
    public sealed class ClientRemoteConfig
    {
        public string AuthorName { get; set; } = "Gió";
        public string SupportEmail { get; set; }
        public string SupportUrl { get; set; }
        public string LatestVersion { get; set; }
        public string UpdateManifestUrl { get; set; }
        public string ReleaseNotesUrl { get; set; }
        public string DownloadPageUrl { get; set; }

        /// <summary>Remote kill switch / beta cap (no new DLL). See <see cref="EntitlementPolicyConfig"/>.</summary>
        public EntitlementPolicyConfig EntitlementPolicy { get; set; }

        /// <summary>Single payment landing page — user picks QR, card, etc. on the website.</summary>
        public string PaymentWebUrl { get; set; }
        public string PaymentWebTitle { get; set; }
        public string PaymentWebBody { get; set; }

        /// <summary>Vietnam bank transfer / Sepay instructions (plain text or short HTML).</summary>
        public string PaymentVnTitle { get; set; }
        public string PaymentVnBody { get; set; }
        public string PaymentVnSepayUrl { get; set; }

        /// <summary>International / Triple instructions (legacy; prefer <see cref="PaymentWebUrl"/>).</summary>
        public string PaymentIntlTitle { get; set; }
        public string PaymentIntlBody { get; set; }
        /// <summary>Legacy Triple / card URL (optional fallback).</summary>
        public string PaymentIntlTripleUrl { get; set; }
        /// <summary>Lemon Squeezy checkout or store URL for international customers.</summary>
        public string PaymentIntlLemonsqueezyUrl { get; set; }
    }

    /// <summary>
    /// Server-controlled entitlement. Modes: normal | cap_days | trial_only | blocked.
    /// Update via PUT /admin/client-config without shipping a new client build.
    /// </summary>
    public sealed class EntitlementPolicyConfig
    {
        /// <summary>normal | cap_days | trial_only | blocked</summary>
        public string Mode { get; set; } = "normal";

        /// <summary>Max days from today when mode is cap_days (default 14).</summary>
        public int? CapDays { get; set; }

        /// <summary>Shown in license message when policy limits access.</summary>
        public string Message { get; set; }
    }

    /// <summary>Optional manifest behind <see cref="ClientRemoteConfig.UpdateManifestUrl"/>.</summary>
    public sealed class UpdateManifest
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string Sha256 { get; set; }
        public string ReleaseNotes { get; set; }
    }
}
