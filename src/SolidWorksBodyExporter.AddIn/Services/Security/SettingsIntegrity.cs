using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SolidWorksBodyExporter.AddIn.Services.Api;

namespace SolidWorksBodyExporter.AddIn.Services.Security
{
    /// <summary>
    /// Seals sensitive <see cref="AppSettings"/> fields with Windows DPAPI (CurrentUser).
    /// The seal cannot be recomputed on another PC; entropy is derived from machine fingerprint
    /// (not a fixed string in the DLL).
    /// </summary>
    internal static class SettingsIntegrity
    {
        private static string SealPath =>
            Path.Combine(
                Path.GetDirectoryName(AppSettings.GetPath())
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SolidWorksBodyExporter"),
                "settings.seal");

        public static void Stamp(AppSettings settings, string machineFingerprint)
        {
            if (settings == null || string.IsNullOrWhiteSpace(machineFingerprint))
            {
                return;
            }

            var payload = BuildPayload(settings);
            var plain = Encoding.UTF8.GetBytes(payload);
            var entropy = DeriveEntropy(machineFingerprint);
            var protectedBytes = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);

            var dir = Path.GetDirectoryName(SealPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(SealPath, protectedBytes);
            settings.SettingsHmac = null;
        }

        public static bool Verify(AppSettings settings, string machineFingerprint)
        {
            if (settings == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(machineFingerprint))
            {
                return false;
            }

            if (File.Exists(SealPath))
            {
                return VerifySeal(settings, machineFingerprint);
            }

            if (!string.IsNullOrWhiteSpace(settings.SettingsHmac))
            {
                return VerifyLegacyHmac(settings, machineFingerprint);
            }

            return true;
        }

        public static void ClearSensitiveFields(AppSettings settings, string defaultApiUrl)
        {
            if (settings == null)
            {
                return;
            }

            settings.ApiBaseUrl = defaultApiUrl;
            settings.CachedToken = null;
            settings.CachedTokenExpiresUtc = null;
            settings.TokenBoundMachineHash = null;
            settings.LicenseExpiresUtc = null;
            settings.LastOnlineValidationUtc = null;
            settings.SettingsHmac = null;

            try
            {
                if (File.Exists(SealPath))
                {
                    File.Delete(SealPath);
                }
            }
            catch
            {
                /* best effort */
            }
        }

        private static bool VerifySeal(AppSettings settings, string machineFingerprint)
        {
            try
            {
                var protectedBytes = File.ReadAllBytes(SealPath);
                var entropy = DeriveEntropy(machineFingerprint);
                var plain = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
                var sealedPayload = Encoding.UTF8.GetString(plain);
                var current = BuildPayload(settings);
                return ConstantTimeEquals(sealedPayload, current);
            }
            catch
            {
                return false;
            }
        }

        private static bool VerifyLegacyHmac(AppSettings settings, string machineFingerprint)
        {
            try
            {
                var expected = ComputeLegacyHmac(settings, machineFingerprint);
                return ConstantTimeEquals(settings.SettingsHmac, expected);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildPayload(AppSettings settings)
        {
            var api = ApiUrlPolicy.Normalize(settings.ApiBaseUrl, ApiUrlPolicy.DefaultApiBaseUrl);
            return string.Join("|", new[]
            {
                api ?? string.Empty,
                settings.LicenseKey?.Trim() ?? string.Empty,
                settings.TokenBoundMachineHash?.Trim() ?? string.Empty,
                settings.LicenseExpiresUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                settings.CachedTokenExpiresUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                settings.LastOnlineValidationUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            });
        }

        private static byte[] DeriveEntropy(string machineFingerprint)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(machineFingerprint));
            }
        }

        private static string ComputeLegacyHmac(AppSettings settings, string machineFingerprint)
        {
            const string legacySalt = "BodyExporter.Settings.v1";
            var payload = BuildPayload(settings);
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(machineFingerprint + "|" + legacySalt)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return Convert.ToBase64String(hash);
            }
        }

        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            var ba = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            if (ba.Length != bb.Length)
            {
                return false;
            }

            var diff = 0;
            for (var i = 0; i < ba.Length; i++)
            {
                diff |= ba[i] ^ bb[i];
            }

            return diff == 0;
        }
    }
}
