using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SolidWorksBodyExporter.AddIn.Services.Api;

namespace SolidWorksBodyExporter.AddIn.Services.Security
{
    /// <summary>
    /// Holds the ERP connection on the one machine and Windows account that established it.
    ///
    /// <para>
    /// The link used to live as plain text in settings.json, so copying that single file to another
    /// PC carried a working ERP connection with it. Here the file is sealed with DPAPI under the
    /// current user, keyed to the machine fingerprint. Elsewhere it does not decrypt — the link is
    /// not withheld by a check that could be bypassed, Windows simply will not hand the bytes
    /// back. A machine without its own link therefore looks exactly like a fresh install.
    /// </para>
    /// </summary>
    internal static class ErpLinkStore
    {
        private static string LinkPath =>
            Path.Combine(
                Path.GetDirectoryName(AppSettings.GetPath())
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SolidWorksBodyExporter"),
                "erp.link");

        [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        public sealed class ErpLink
        {
            [JsonProperty("baseUrl")]
            public string BaseUrl { get; set; }

            [JsonProperty("apiKey")]
            public string ApiKey { get; set; }

            /// <summary>Fingerprint of the machine that established the link.</summary>
            [JsonProperty("machineFingerprint")]
            public string MachineFingerprint { get; set; }

            [JsonProperty("linkedUtc")]
            public DateTime LinkedUtc { get; set; }

            [JsonIgnore]
            public bool IsUsable =>
                !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
        }

        /// <summary>
        /// The link for this machine, or null when there is none. Also carries over a link made by
        /// an older build, which kept the key in settings.json.
        /// </summary>
        public static ErpLink Current()
        {
            var stored = TryRead();
            if (stored != null && stored.IsUsable)
            {
                return stored;
            }

            return TryAdoptLegacyLink();
        }

        public static bool IsLinked()
        {
            var link = Current();
            return link != null && link.IsUsable;
        }

        public static bool Save(string baseUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                return false;
            }

            var fingerprint = TryGetFingerprint();
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                DiagnosticLog.Warn("ErpLinkStore: no machine fingerprint, refusing to store the link");
                return false;
            }

            var link = new ErpLink
            {
                BaseUrl = ErpBomClient.NormalizeBaseUrl(baseUrl),
                ApiKey = apiKey.Trim(),
                MachineFingerprint = fingerprint,
                LinkedUtc = DateTime.UtcNow
            };

            try
            {
                var plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(link));
                var sealedBytes = ProtectedData.Protect(
                    plain, DeriveEntropy(fingerprint), DataProtectionScope.CurrentUser);

                var dir = Path.GetDirectoryName(LinkPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllBytes(LinkPath, sealedBytes);
                DiagnosticLog.Info("ErpLinkStore: ERP link stored for this machine");
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("ErpLinkStore.Save failed", ex);
                return false;
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(LinkPath))
                {
                    File.Delete(LinkPath);
                    DiagnosticLog.Info("ErpLinkStore: ERP link removed");
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("ErpLinkStore.Clear: " + ex.Message);
            }
        }

        private static ErpLink TryRead()
        {
            if (!File.Exists(LinkPath))
            {
                return null;
            }

            var fingerprint = TryGetFingerprint();
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                return null;
            }

            try
            {
                var sealedBytes = File.ReadAllBytes(LinkPath);
                var plain = ProtectedData.Unprotect(
                    sealedBytes, DeriveEntropy(fingerprint), DataProtectionScope.CurrentUser);
                var link = JsonConvert.DeserializeObject<ErpLink>(Encoding.UTF8.GetString(plain));
                if (link == null || !link.IsUsable)
                {
                    return null;
                }

                // The fingerprint inside the file is redundant against DPAPI, but a machine that
                // was re-imaged keeps its user profile while the fingerprint changes; re-linking is
                // the honest answer there rather than carrying a stale link forward.
                if (!string.Equals(link.MachineFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticLog.Info("ErpLinkStore: link belongs to another machine, ignored");
                    return null;
                }

                return link;
            }
            catch
            {
                // A link sealed on a different machine or account cannot be opened here. That is
                // the point, so it is not an error worth reporting to the user.
                return null;
            }
        }

        /// <summary>
        /// Moves a link made by an older build out of settings.json and into the sealed file.
        ///
        /// <para>
        /// The old key is only trusted when the settings seal verifies, which it can only do on
        /// the machine and account that wrote it. A copied settings.json arrives without a
        /// matching seal, so its key is discarded instead of being adopted — otherwise this
        /// migration would hand the copied machine exactly the link it should not have.
        /// </para>
        /// </summary>
        private static ErpLink TryAdoptLegacyLink()
        {
            AppSettings settings;
            try
            {
                settings = AppSettings.LoadOrCreate();
            }
            catch
            {
                return null;
            }

            var legacyKey = settings.ErpApiKey;
            if (string.IsNullOrWhiteSpace(legacyKey) || string.IsNullOrWhiteSpace(settings.ErpBaseUrl))
            {
                return null;
            }

            var fingerprint = TryGetFingerprint();
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                return null;
            }

            if (!SettingsIntegrity.HasSeal || !SettingsIntegrity.Verify(settings, fingerprint))
            {
                DiagnosticLog.Info(
                    "ErpLinkStore: settings.json carries an ERP key with no valid seal, discarded");
                DiscardLegacyKey(settings, fingerprint);
                return null;
            }

            if (!Save(settings.ErpBaseUrl, legacyKey))
            {
                return null;
            }

            DiscardLegacyKey(settings, fingerprint);
            DiagnosticLog.Info("ErpLinkStore: migrated the ERP key out of settings.json");
            return TryRead();
        }

        private static void DiscardLegacyKey(AppSettings settings, string fingerprint)
        {
            try
            {
                settings.ErpApiKey = null;
                SettingsIntegrity.Stamp(settings, fingerprint);
                settings.Save();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("ErpLinkStore: could not clear the legacy ERP key: " + ex.Message);
            }
        }

        private static string TryGetFingerprint()
        {
            try
            {
                return LicenseManager.Current.GetMachineFingerprint();
            }
            catch
            {
                return null;
            }
        }

        private static byte[] DeriveEntropy(string machineFingerprint)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes("erp.link|" + machineFingerprint));
            }
        }
    }
}
