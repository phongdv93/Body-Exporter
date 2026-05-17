using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>
    /// Persisted user settings used by the API layer. Lives at
    /// <c>%APPDATA%\SolidWorksBodyExporter\settings.json</c>. The file is editable by
    /// hand for power users; the addin also writes to it whenever a fresh license JWT
    /// is fetched so subsequent launches can skip the network call until the cached
    /// token expires.
    /// <para>
    /// IMPORTANT: this file stores a bearer JWT issued by the licensing server. Treat
    /// it as a secret - do not commit it to source control, do not include it in
    /// support bundles. The server expires tokens after <c>TokenLifetimeMinutes</c>
    /// (default 24 h) so a leaked token has limited blast radius.
    /// </para>
    /// </summary>
    public sealed class AppSettings
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>
        /// Optional base URL for the licensing/API server (e.g.
        /// <c>https://api.bodyexporter.io</c>). When null/empty the addin runs in
        /// "local-only" mode and uses the offline RSA license file under
        /// <c>%APPDATA%\SolidWorksBodyExporter\license.json</c>.
        /// </summary>
        public string ApiBaseUrl { get; set; }

        /// <summary>
        /// User-typed license key. The first call to the server exchanges this key
        /// for a JWT that's cached in <see cref="CachedToken"/>; subsequent launches
        /// only re-hit the server when the cached JWT is near expiry.
        /// </summary>
        public string LicenseKey { get; set; }

        /// <summary>Current active license UUID on this machine (after renew, older keys are retired).</summary>
        public List<string> AppliedLicenseKeys { get; set; }

        /// <summary>Keys replaced by a renewal stack (support audit only).</summary>
        public List<string> RetiredLicenseKeys { get; set; }

        /// <summary>
        /// Most recently issued JWT from the licensing server. Validated locally on
        /// each launch (signature check against the embedded RSA public key) so an
        /// offline machine can keep working until the token expires.
        /// </summary>
        public string CachedToken { get; set; }

        /// <summary>UTC expiry of <see cref="CachedToken"/>.</summary>
        public DateTime? CachedTokenExpiresUtc { get; set; }

        /// <summary>Subscription/license end date from the server (not the 24h JWT expiry).</summary>
        public DateTime? LicenseExpiresUtc { get; set; }

        /// <summary>Owner email from last successful online validation.</summary>
        public string OnlineOwner { get; set; }

        /// <summary>Plan name from last successful online validation.</summary>
        public string OnlinePlan { get; set; }

        /// <summary>
        /// Hash of the machine fingerprint at the time the token was issued. If the
        /// fingerprint changes (new hardware) we force a re-validation against the
        /// server even if the JWT is still time-valid. Prevents license-file sharing
        /// across machines after the initial activation.
        /// </summary>
        public string TokenBoundMachineHash { get; set; }

        /// <summary>
        /// Last-selected Excel template (.xlsx) for <see cref="Ui.BodyExportWindow"/> "Fill
        /// template" export. When set and the file still exists, the export flow skips the
        /// template picker and only asks for the output save path.
        /// </summary>
        public string ExcelTemplatePath { get; set; }

        /// <summary>Last workbook produced by "Fill Excel template…" (quick reopen in Excel).</summary>
        public string ExcelTemplateLastOutputPath { get; set; }

        /// <summary>Email used for VN bank transfer memo (Sepay). Prefilled from license owner when possible.</summary>
        public string PaymentEmail { get; set; }

        /// <summary>Last successful online license validation (UTC). Used for periodic re-check.</summary>
        public DateTime? LastOnlineValidationUtc { get; set; }

        /// <summary>HMAC over sensitive fields; tampering clears online license cache.</summary>
        public string SettingsHmac { get; set; }

        public static AppSettings LoadOrCreate()
        {
            try
            {
                var path = GetPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    AppSettings parsed;
                    try
                    {
                        var jo = JObject.Parse(
                            json,
                            new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Ignore });
                        parsed = jo.ToObject<AppSettings>(JsonSerializer.Create(JsonSettings));
                    }
                    catch
                    {
                        parsed = JsonConvert.DeserializeObject<AppSettings>(json, JsonSettings);
                    }

                    if (parsed != null)
                    {
                        parsed.NormalizeAppliedLicenseKeys();
                        parsed.ApiBaseUrl = Security.ApiUrlPolicy.Normalize(
                            parsed.ApiBaseUrl,
                            Security.ApiUrlPolicy.DefaultApiBaseUrl);
                        return parsed;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("AppSettings: load failed, using defaults - " + ex.Message);
            }
            return new AppSettings();
        }

        /// <returns>false if the file could not be written.</returns>
        public bool Save()
        {
            try
            {
                var path = GetPath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(this, JsonSettings);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("AppSettings: save failed - " + ex.Message);
                return false;
            }
        }

        public static string GetPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SolidWorksBodyExporter",
                "settings.json");
        }

        public void NormalizeAppliedLicenseKeys()
        {
            if (AppliedLicenseKeys == null)
            {
                AppliedLicenseKeys = new List<string>();
            }

            if (!string.IsNullOrWhiteSpace(LicenseKey))
            {
                var k = LicenseKey.Trim();
                if (!AppliedLicenseKeys.Any(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase)))
                {
                    AppliedLicenseKeys.Add(k);
                }
            }
        }

        public bool HasAppliedLicenseKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            NormalizeAppliedLicenseKeys();
            return AppliedLicenseKeys.Any(x => string.Equals(x, key.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public void RegisterAppliedLicenseKey(string key)
        {
            SetActiveLicenseKey(key);
        }

        /// <summary>
        /// One active key for validation; previous keys move to <see cref="RetiredLicenseKeys"/>.
        /// Stacked time lives in <see cref="LicenseExpiresUtc"/>.
        /// </summary>
        public void SetActiveLicenseKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            NormalizeAppliedLicenseKeys();
            key = key.Trim();

            if (RetiredLicenseKeys == null)
            {
                RetiredLicenseKeys = new List<string>();
            }

            foreach (var existing in AppliedLicenseKeys.ToList())
            {
                if (!string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)
                    && !RetiredLicenseKeys.Any(x => string.Equals(x, existing, StringComparison.OrdinalIgnoreCase)))
                {
                    RetiredLicenseKeys.Add(existing);
                }
            }

            AppliedLicenseKeys = new List<string> { key };
            LicenseKey = key;
        }
    }
}
