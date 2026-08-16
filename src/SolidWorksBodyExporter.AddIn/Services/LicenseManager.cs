using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Newtonsoft.Json;
using SolidWorksBodyExporter.AddIn.Services.Api;
using SolidWorksBodyExporter.AddIn.Services.Security;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Offline license enforcement for the Body Exporter add-in. The licensing flow is:
    /// <list type="number">
    ///   <item>If a signed <c>license.lic</c> exists in the per-user data folder, verify its
    ///         signature against the embedded RSA-2048 public key. If the signature passes, the
    ///         machine fingerprint matches, and the expiry date hasn't passed, return
    ///         <see cref="LicenseSource.Licensed"/>.</item>
    ///   <item>If no license file is present, fall back to a 14-day trial measured from the
    ///         first run on this machine. The trial timestamp is HMAC'd to the machine fingerprint
    ///         so simply copying the file to another PC or editing it by hand invalidates the
    ///         trial.</item>
    ///   <item>If neither path qualifies, return an actionable status with a message the WPF
    ///         window surfaces to the user (so they can install a license or contact support).</item>
    /// </list>
    /// <para>
    /// All cryptographic verification happens locally: there's no phone-home. Licenses are signed
    /// once with the developer's private key (kept off the user's machine and out of source
    /// control) and verified anywhere with the public key embedded below. This deliberately keeps
    /// licensing usable in air-gapped engineering environments.
    /// </para>
    /// <para>
    /// Determined users can always tamper with offline licensing on their own PC; the goal here
    /// is to make casual sharing inconvenient, not to provide cryptographic DRM. The trial file
    /// is HMAC-bound to the machine, the license file is RSA-signed, and the verification logic
    /// is mirrored in two callsites so that disabling one bypass doesn't unlock the add-in.
    /// </para>
    /// </summary>
    internal sealed class LicenseActivationSummary
    {
        public int NewlyActivated { get; set; }
        public int SkippedAlreadyApplied { get; set; }
        public int RetiredPreviousKeys { get; set; }
        public bool RecalculatedStack { get; set; }
        public int KeysInStack { get; set; }
    }

    internal sealed class LicenseManager
    {
        // Embedded RSA-2048 public key. The matching private key lives in tools/license-keys/
        // (gitignored) and is the ONLY thing that can produce signatures this code will accept.
        // Replacing this string with an attacker-controlled key would require a recompile, which
        // raises the bar for casual unlocking attempts considerably.
        private const string PublicKeyXml =
            "<RSAKeyValue><Modulus>uQolgM6BDYWOMNx1UEj13uhKhIO5lIqpYfuWAu7ELgBgCB6iF4fdbH+xXkBkC7l7" +
            "I1FfayEQfhYhZqHqMoJNo9rsmzMYCJrxrosBZoZZlbTVmxPyLEfCgMil4vcAuE3P5Qv15By7TDCBJYMLpzOK" +
            "62f3moRfI7n1JG8QI9mm73fr0JICvAAtSwtZDou/Kr3qtf/vSSY/cY+TXUYkDTtZTJRdqOExXaqI/4r20LUs" +
            "+N+qG27gcWOLa/QQsytp6V4ioN/mlJfxbvWuyfbKFjnDHpyO+8B/ZwVm2e3UJQhli1wZrM9X5YDKQB9G7KIg" +
            "oqvnLBTbKYohXg2kjItWXTVOYQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private const int TrialDurationDays = 14;
        private const string LicenseFileName = "license.lic";
        private const string TrialFileName = "trial.dat";

        /// <summary>Used when <see cref="AppSettings.ApiBaseUrl"/> is empty during online activation.</summary>
        public static string DefaultApiBaseUrl => ApiUrlPolicy.DefaultApiBaseUrl;

        /// <summary>Re-validate online license once per SolidWorks session (ConnectToSW).</summary>
        private static bool _sessionStartupValidated;

        /// <summary>Every online check when reachable; short offline grace after last OK validation.</summary>
        private const int OnlineRecheckDays = 0;
        private const int OnlineOfflineGraceDays = 1;

        private static readonly object Gate = new object();
        private static LicenseManager _instance;

        private readonly string _dataDirectory;
        private string _cachedFingerprint;

        private LicenseManager()
        {
            // Same root as AppSettings (%APPDATA%\SolidWorksBodyExporter) so license.lic and settings.json live together.
            _dataDirectory = Path.GetDirectoryName(AppSettings.GetPath())
                               ?? Path.Combine(
                                   Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                   "SolidWorksBodyExporter");
        }

        public static LicenseManager Current
        {
            get
            {
                if (_instance == null)
                {
                    lock (Gate)
                    {
                        if (_instance == null)
                        {
                            _instance = new LicenseManager();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Path the WPF window opens when the user clicks "Install license file" - keeps the file
        /// dialog default location aligned with where <see cref="TryInstallLicense"/> writes.
        /// </summary>
        public string LicenseDirectory => _dataDirectory;

        /// <summary>
        /// Hex-encoded SHA-256 of the machine GUID + computer name. Stable across reboots and
        /// updates but different per machine. Surfaced to the user so they can paste it to support
        /// when requesting a new license.
        /// </summary>
        public string GetMachineFingerprint()
        {
            if (_cachedFingerprint == null)
            {
                _cachedFingerprint = ComputeMachineFingerprint();
            }
            return _cachedFingerprint;
        }

        /// <summary>
        /// Called from <see cref="AddInIntegration.ConnectToSW"/> once per SolidWorks process.
        /// Forces a server round-trip when a license key is installed (revoked keys fail fast).
        /// </summary>
        public void EnsureStartupOnlineValidation()
        {
            lock (Gate)
            {
                if (_sessionStartupValidated)
                {
                    return;
                }

                _sessionStartupValidated = true;
            }

            try
            {
                var settings = AppSettings.LoadOrCreate();
                if (string.IsNullOrWhiteSpace(settings.LicenseKey))
                {
                    return;
                }

                var fingerprint = GetMachineFingerprint();
                var api = ResolveApiBaseUrl(settings);
                if (!TryRefreshOnlineLicense(settings, fingerprint, api))
                {
                    DiagnosticLog.Warn("Startup license refresh failed (offline or invalid).");
                }
            }
            catch (LicenseApiException ex)
            {
                DiagnosticLog.Warn("Startup license refresh rejected: " + ex.Message);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("Startup license refresh error: " + ex.Message);
            }
        }

        public LicenseStatus GetStatus()
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                RepairSettingsIfTampered();

                var remote = LoadRemoteConfig();
                if (EntitlementPolicyEnforcer.IsBlocked(remote))
                {
                    return EntitlementPolicyEnforcer.Apply(
                        new LicenseStatus
                        {
                            IsAllowed = false,
                            Source = LicenseSource.Error,
                            Message = remote?.EntitlementPolicy?.Message,
                            MachineFingerprint = TryGetFingerprint()
                        },
                        remote);
                }

                LicenseStatus status;
                if (EntitlementPolicyEnforcer.IsTrialOnlyMode(remote))
                {
                    status = EvaluateTrial();
                }
                else
                {
                    var licPath = Path.Combine(_dataDirectory, LicenseFileName);
                    if (File.Exists(licPath))
                    {
                        status = EvaluateLicenseFile(licPath);
                    }
                    else
                    {
                        var online = EvaluateOnlineLicense();
                        status = online ?? EvaluateTrial();
                    }
                }

                return EntitlementPolicyEnforcer.Apply(status, remote);
            }
            catch (Exception ex)
            {
                return new LicenseStatus
                {
                    IsAllowed = false,
                    Source = LicenseSource.Error,
                    Message = "License check failed: " + ex.Message,
                    MachineFingerprint = TryGetFingerprint()
                };
            }
        }

        private static ClientRemoteConfig LoadRemoteConfig()
        {
            try
            {
                return ClientConfigClient.Load(DefaultApiBaseUrl, forceRefresh: false);
            }
            catch
            {
                return null;
            }
        }

        public bool TryInstallLicense(string sourcePath, out string error)
        {
            try
            {
                var raw = File.ReadAllText(sourcePath);
                return TryInstallLicenseContent(raw, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Installs license JSON pasted from support (same format as license.lic file body).
        /// </summary>
        public bool TryInstallLicenseContent(string raw, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    error = "License text is empty.";
                    return false;
                }

                raw = raw.Trim();
                if (raw.StartsWith("{", StringComparison.Ordinal))
                {
                    return TryInstallSignedLicenseJson(raw, out error);
                }

                var keys = ParseLicenseKeyLines(raw);
                if (keys.Count > 0)
                {
                    return TryActivateNewLicenseKeys(keys, out error, out _);
                }

                return TryInstallSignedLicenseJson(raw, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private LicenseStatus EvaluateLicenseFile(string path)
        {
            string raw;
            try
            {
                raw = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return Fail(LicenseSource.Error, "Cannot read license file: " + ex.Message);
            }

            return ValidateLicenseContent(raw);
        }

        private LicenseStatus ValidateLicenseContent(string raw)
        {
            LicenseFile file;
            try
            {
                file = JsonConvert.DeserializeObject<LicenseFile>(raw);
            }
            catch (Exception ex)
            {
                return Fail(LicenseSource.Tampered, "License is not valid JSON: " + ex.Message);
            }

            if (file == null || file.Payload == null || string.IsNullOrWhiteSpace(file.Signature))
            {
                return Fail(LicenseSource.Tampered, "License file is missing required fields.");
            }

            // Re-serialise the payload with the same property order (Order attributes on
            // LicensePayload) so the bytes we verify match what the signing tool produced.
            byte[] canonical;
            try
            {
                var json = JsonConvert.SerializeObject(file.Payload, Formatting.None);
                canonical = new UTF8Encoding(false).GetBytes(json);
            }
            catch (Exception ex)
            {
                return Fail(LicenseSource.Error, "Failed to canonicalise license payload: " + ex.Message);
            }

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(file.Signature);
            }
            catch (Exception ex)
            {
                return Fail(LicenseSource.Tampered, "License signature is not valid base64: " + ex.Message);
            }

            bool sigOk;
            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(PublicKeyXml);
                    sigOk = rsa.VerifyData(canonical, "SHA256", signature);
                }
            }
            catch (Exception ex)
            {
                return Fail(LicenseSource.Tampered, "License signature verification threw: " + ex.Message);
            }

            if (!sigOk)
            {
                return Fail(LicenseSource.Tampered, "License signature does not match the embedded public key.");
            }

            var fingerprint = GetMachineFingerprint();
            var wildcardMachine = string.IsNullOrEmpty(file.Payload.MachineFingerprint)
                                  || file.Payload.MachineFingerprint == "*";
            if (!wildcardMachine && !string.Equals(file.Payload.MachineFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return Fail(LicenseSource.WrongMachine,
                    "License is signed for a different machine. Your fingerprint: " + fingerprint);
            }

            var nowUtc = DateTime.UtcNow;
            if (file.Payload.ExpiresUtc <= nowUtc)
            {
                return new LicenseStatus
                {
                    IsAllowed = false,
                    Source = LicenseSource.Expired,
                    Owner = file.Payload.Owner,
                    PlanName = file.Payload.Plan,
                    ExpiresUtc = file.Payload.ExpiresUtc,
                    DaysRemaining = 0,
                    Message = "License expired on " + file.Payload.ExpiresUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                              + ". Contact support to renew.",
                    MachineFingerprint = fingerprint
                };
            }

            var remaining = (int)Math.Ceiling((file.Payload.ExpiresUtc - nowUtc).TotalDays);
            return new LicenseStatus
            {
                IsAllowed = true,
                Source = LicenseSource.Licensed,
                Owner = file.Payload.Owner,
                PlanName = string.IsNullOrEmpty(file.Payload.Plan) ? "Licensed" : file.Payload.Plan,
                ExpiresUtc = file.Payload.ExpiresUtc,
                DaysRemaining = remaining,
                Message = "Licensed to " + (file.Payload.Owner ?? "Unknown") + ". " + remaining + " day(s) remaining.",
                MachineFingerprint = fingerprint
            };
        }

        /// <summary>
        /// Lemon / server UUID in settings.json, validated via POST /v1/license/validate.
        /// </summary>
        private LicenseStatus EvaluateOnlineLicense()
        {
            var settings = AppSettings.LoadOrCreate();
            if (string.IsNullOrWhiteSpace(settings.LicenseKey))
            {
                return null;
            }

            var fingerprint = GetMachineFingerprint();
            var api = ResolveApiBaseUrl(settings);

            if (TryGetActiveOnlineStatus(settings, fingerprint, out var active))
            {
                if (!NeedsOnlineRefresh(settings))
                {
                    return active;
                }

                try
                {
                    if (TryRefreshOnlineLicense(settings, fingerprint, api))
                    {
                        return BuildOnlineLicensedStatus(AppSettings.LoadOrCreate(), fingerprint);
                    }
                }
                catch (LicenseApiException ex)
                {
                    // The local stacked end can outlive the Worker's expiresAt. When the server
                    // says the key is finished, drop the local end so the badge matches CRM.
                    if (IsServerExpiryRejection(ex.Message))
                    {
                        ClearExpiredOnlineEntitlement(settings, fingerprint);
                        return Fail(LicenseSource.Expired, ex.Message);
                    }

                    if (IsWithinOfflineGrace(settings) && HasValidCachedJwt(settings, fingerprint))
                    {
                        return active;
                    }

                    return Fail(LicenseSource.Error, ex.Message);
                }

                if (IsWithinOfflineGrace(settings) && HasValidCachedJwt(settings, fingerprint))
                {
                    return active;
                }

                return Fail(LicenseSource.Expired,
                    "License must be re-validated online. Connect to the internet and open License, or contact support.");
            }

            try
            {
                if (TryRefreshOnlineLicense(settings, fingerprint, api))
                {
                    return BuildOnlineLicensedStatus(AppSettings.LoadOrCreate(), fingerprint);
                }

                return Fail(LicenseSource.Error, "Could not validate license with the server.");
            }
            catch (LicenseApiException ex)
            {
                if (IsServerExpiryRejection(ex.Message))
                {
                    ClearExpiredOnlineEntitlement(settings, fingerprint);
                    return Fail(LicenseSource.Expired, ex.Message);
                }

                return Fail(LicenseSource.Error, ex.Message);
            }
        }

        private bool TryInstallSignedLicenseJson(string raw, out string error)
        {
            error = null;
            var fileStatus = ValidateLicenseContent(raw);
            if (!fileStatus.IsAllowed)
            {
                error = fileStatus.Message;
                return false;
            }

            Directory.CreateDirectory(_dataDirectory);
            var licPath = Path.Combine(_dataDirectory, LicenseFileName);
            File.WriteAllText(licPath, raw, new UTF8Encoding(false));
            error = null;
            return true;
        }

        public bool TryActivateNewLicenseKeys(IList<string> keys, out string error, out LicenseActivationSummary summary)
        {
            summary = new LicenseActivationSummary();
            error = null;

            if (keys == null || keys.Count == 0)
            {
                error = "Paste at least one license key UUID (one per line).";
                return false;
            }

            var ordered = keys
                .Select(k => k?.Trim())
                .Where(k => !string.IsNullOrWhiteSpace(k) && Guid.TryParse(k, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count == 0)
            {
                error = "No valid license UUID found. Each line must be a key like 00ed73b7-103a-4b09-87fe-e28b5f8dd366.";
                return false;
            }

            var settings = AppSettings.LoadOrCreate();
            settings.NormalizeAppliedLicenseKeys();

            foreach (var key in ordered)
            {
                if (settings.HasAppliedLicenseKey(key))
                {
                    summary.SkippedAlreadyApplied++;
                    continue;
                }

                var hadOtherActive = settings.AppliedLicenseKeys != null
                    && settings.AppliedLicenseKeys.Any(k =>
                        !string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

                if (!TryActivateOnlineLicenseCore(key, out error))
                {
                    return false;
                }

                summary.NewlyActivated++;
                if (hadOtherActive)
                {
                    summary.RetiredPreviousKeys++;
                }

                settings = AppSettings.LoadOrCreate();
            }

            if (summary.NewlyActivated == 0 && summary.SkippedAlreadyApplied > 0 && ordered.Count > 1)
            {
                if (!TryRecalculateStackedEntitlement(out error, out var stacked))
                {
                    return false;
                }

                summary.RecalculatedStack = true;
                summary.KeysInStack = stacked;
            }

            return true;
        }

        /// <summary>
        /// Re-read every UUID ever applied on this machine (<see cref="AppSettings.GetAllKnownLicenseKeys"/>)
        /// from the Worker and keep the one that runs longest. The expiry always comes from a server
        /// record, so the plugin badge and the CRM row can never disagree.
        /// </summary>
        public bool TryRecalculateStackedEntitlement(out string error, out int keysStacked)
        {
            error = null;
            keysStacked = 0;

            try
            {
                var settings = AppSettings.LoadOrCreate();
                var keys = settings.GetAllKnownLicenseKeys()
                    .Select(k => k?.Trim())
                    .Where(k => !string.IsNullOrWhiteSpace(k) && Guid.TryParse(k, out _))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (keys.Count == 0)
                {
                    error = "No license keys to stack.";
                    return false;
                }

                var fingerprint = GetMachineFingerprint();
                var api = ResolveApiBaseUrl(settings);
                var now = DateTime.UtcNow;
                DateTime? bestEnd = null;
                LicenseValidationResponse bestResponse = null;
                string bestKey = null;

                foreach (var key in keys)
                {
                    LicenseValidationResponse response;
                    try
                    {
                        response = new LicenseApiClient(api)
                            .ValidateAsync(key, fingerprint)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch (LicenseApiException ex)
                    {
                        DiagnosticLog.Warn("Recalculate stack: skip key " + key + " - " + ex.Message);
                        continue;
                    }

                    var serverEnd = NormalizeUtc(response.LicenseExpires);
                    if (serverEnd <= now.AddYears(-1))
                    {
                        serverEnd = NormalizeUtc(response.ExpiresUtc).AddDays(365);
                    }

                    if (serverEnd <= now)
                    {
                        continue;
                    }

                    keysStacked++;
                    if (bestEnd.HasValue && serverEnd <= bestEnd.Value)
                    {
                        continue;
                    }

                    bestEnd = serverEnd;
                    bestResponse = response;
                    bestKey = key;
                }

                if (!bestEnd.HasValue || string.IsNullOrEmpty(bestKey) || bestResponse == null)
                {
                    error = "No valid license key left (expired or rejected by the server).";
                    return false;
                }

                ApplyOnlineValidation(settings, bestKey, api, fingerprint, bestResponse);
                settings.SetActiveLicenseKey(bestKey);

                if (!SaveSettings(settings, fingerprint))
                {
                    error = "Could not save settings.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static IList<string> ParseLicenseKeyLines(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return list;
            }

            foreach (var line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (Guid.TryParse(t, out _))
                {
                    list.Add(t);
                }
            }

            return list;
        }

        public bool TryActivateOnlineLicense(string licenseKey, out string error)
        {
            return TryActivateOnlineLicenseCore(licenseKey, out error);
        }

        private bool TryActivateOnlineLicenseCore(string licenseKey, out string error)
        {
            error = null;
            try
            {
                var key = licenseKey?.Trim();
                if (string.IsNullOrWhiteSpace(key) || !Guid.TryParse(key, out _))
                {
                    error = "Enter the license key UUID from your purchase email (e.g. 00ed73b7-103a-4b09-87fe-e28b5f8dd366).";
                    return false;
                }

                var settings = AppSettings.LoadOrCreate();
                settings.NormalizeAppliedLicenseKeys();

                var fingerprint = GetMachineFingerprint();
                var api = ResolveApiBaseUrl(settings);

                var response = new LicenseApiClient(api)
                    .ValidateAsync(key, fingerprint)
                    .GetAwaiter()
                    .GetResult();

                ApplyOnlineValidation(settings, key, api, fingerprint, response);
                settings.SetActiveLicenseKey(key);

                if (!SaveSettings(settings, fingerprint))
                {
                    error = "Could not save license settings to " + AppSettings.GetPath();
                    return false;
                }

                // More than one key on this machine: keep the one whose server record runs longest.
                if (settings.GetAllKnownLicenseKeys().Count() > 1)
                {
                    string recalcError;
                    int recalcCount;
                    TryRecalculateStackedEntitlement(out recalcError, out recalcCount);
                }

                var verify = AppSettings.LoadOrCreate();
                if (!verify.HasAppliedLicenseKey(key))
                {
                    error = "License was not persisted. Check write access to " + AppSettings.GetPath();
                    return false;
                }

                var status = BuildOnlineLicensedStatus(verify, fingerprint);
                if (!status.IsAllowed)
                {
                    error = status.Message;
                    return false;
                }

                return true;
            }
            catch (LicenseApiException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool LooksLikeOnlineLicenseKey(string raw)
        {
            return !string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw.Trim(), out _);
        }

        private static bool TryGetActiveOnlineStatus(AppSettings settings, string fingerprint, out LicenseStatus status)
        {
            status = null;
            if (!IsOnlineSubscriptionActive(settings, fingerprint))
            {
                return false;
            }

            status = BuildOnlineLicensedStatus(settings, fingerprint);
            return status.IsAllowed;
        }

        /// <summary>Subscription valid from last successful online activation + integrity + machine binding.</summary>
        private static bool IsOnlineSubscriptionActive(AppSettings settings, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(settings.LicenseKey))
            {
                return false;
            }

            if (!SettingsIntegrity.Verify(settings, fingerprint))
            {
                return false;
            }

            if (!ApiUrlPolicy.IsAllowed(settings.ApiBaseUrl))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(settings.TokenBoundMachineHash)
                && !string.Equals(settings.TokenBoundMachineHash, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var now = DateTime.UtcNow;
            if (!settings.LicenseExpiresUtc.HasValue || settings.LicenseExpiresUtc.Value <= now)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(settings.CachedToken)
                && settings.CachedTokenExpiresUtc.HasValue
                && settings.CachedTokenExpiresUtc.Value > now
                && !HasValidCachedJwt(settings, fingerprint))
            {
                return false;
            }

            return true;
        }

        private static bool HasValidCachedJwt(AppSettings settings, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(settings.CachedToken))
            {
                return true;
            }

            return JwtValidator.TryValidate(settings.CachedToken, fingerprint, PublicKeyXml, out _);
        }

        private static bool NeedsOnlineRefresh(AppSettings settings)
        {
            if (!settings.LastOnlineValidationUtc.HasValue)
            {
                return true;
            }

            return (DateTime.UtcNow - settings.LastOnlineValidationUtc.Value).TotalDays >= OnlineRecheckDays;
        }

        private static bool IsWithinOfflineGrace(AppSettings settings)
        {
            if (!settings.LastOnlineValidationUtc.HasValue)
            {
                return false;
            }

            return (DateTime.UtcNow - settings.LastOnlineValidationUtc.Value).TotalDays <= OnlineOfflineGraceDays;
        }

        private static bool IsServerExpiryRejection(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.IndexOf("license_expired", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("license expired", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ClearExpiredOnlineEntitlement(AppSettings settings, string fingerprint)
        {
            try
            {
                settings.LicenseExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
                settings.CachedToken = null;
                settings.CachedTokenExpiresUtc = null;
                SaveSettings(settings, fingerprint);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("ClearExpiredOnlineEntitlement: " + ex.Message);
            }
        }

        private static bool TryRefreshOnlineLicense(AppSettings settings, string fingerprint, string api)
        {
            if (string.IsNullOrWhiteSpace(settings.LicenseKey))
            {
                return false;
            }

            var response = new LicenseApiClient(api)
                .ValidateAsync(settings.LicenseKey.Trim(), fingerprint)
                .GetAwaiter()
                .GetResult();
            ApplyOnlineValidation(settings, settings.LicenseKey.Trim(), api, fingerprint, response);
            return SaveSettings(settings, fingerprint);
        }

        private static string ResolveApiBaseUrl(AppSettings settings)
        {
            return ApiUrlPolicy.Normalize(settings.ApiBaseUrl, DefaultApiBaseUrl);
        }

        private void RepairSettingsIfTampered()
        {
            var settings = AppSettings.LoadOrCreate();
            var fingerprint = GetMachineFingerprint();
            if (SettingsIntegrity.Verify(settings, fingerprint))
            {
                return;
            }

            DiagnosticLog.Warn("AppSettings integrity check failed; clearing cached online license fields.");
            SettingsIntegrity.ClearSensitiveFields(settings, DefaultApiBaseUrl);
            SaveSettings(settings, fingerprint);
        }

        private static bool SaveSettings(AppSettings settings, string fingerprint)
        {
            settings.ApiBaseUrl = ResolveApiBaseUrl(settings);
            SettingsIntegrity.Stamp(settings, fingerprint);
            return settings.Save();
        }

        private static void ApplyOnlineValidation(
            AppSettings settings,
            string licenseKey,
            string api,
            string fingerprint,
            LicenseValidationResponse response)
        {
            settings.LicenseKey = licenseKey;
            settings.ApiBaseUrl = api;
            settings.CachedToken = response.Token;
            settings.CachedTokenExpiresUtc = NormalizeUtc(response.ExpiresUtc);
            settings.TokenBoundMachineHash = fingerprint;
            var licenseEnd = NormalizeUtc(response.LicenseExpires);
            if (licenseEnd <= DateTime.UtcNow.AddYears(-1))
            {
                licenseEnd = NormalizeUtc(response.ExpiresUtc).AddDays(365);
            }

            // The Worker record is the only source of truth for the entitlement end. Mirroring it on
            // every validation keeps the plugin badge equal to the CRM date; a local end that only
            // ever grows (the old stacking behaviour) drifted years past what the server had.
            settings.LicenseExpiresUtc = licenseEnd;

            settings.OnlineOwner = response.Owner;
            settings.OnlinePlan = response.Plan;
            settings.LastOnlineValidationUtc = DateTime.UtcNow;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }

            return value.ToUniversalTime();
        }

        private static LicenseStatus BuildOnlineLicensedStatus(AppSettings settings, string fingerprint)
        {
            var expires = settings.LicenseExpiresUtc ?? DateTime.UtcNow;
            var now = DateTime.UtcNow;
            if (expires <= now)
            {
                return Fail(LicenseSource.Expired,
                    "Online license expired on " + expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".");
            }

            var remaining = (int)Math.Ceiling((expires - now).TotalDays);
            var owner = string.IsNullOrWhiteSpace(settings.OnlineOwner) ? "Licensed user" : settings.OnlineOwner;
            var plan = string.IsNullOrWhiteSpace(settings.OnlinePlan) ? "Licensed" : settings.OnlinePlan;
            return new LicenseStatus
            {
                IsAllowed = true,
                Source = LicenseSource.Licensed,
                Owner = owner,
                PlanName = plan,
                ExpiresUtc = expires,
                DaysRemaining = remaining,
                Message = "Licensed to " + owner + " (online). " + remaining + " day(s) remaining.",
                MachineFingerprint = fingerprint
            };
        }

        private LicenseStatus EvaluateTrial()
        {
            var fingerprint = GetMachineFingerprint();
            var trialPath = Path.Combine(_dataDirectory, TrialFileName);

            var serverTrial = TryEvaluateTrialFromServer(fingerprint, trialPath);
            if (serverTrial != null)
            {
                return serverTrial;
            }

            return EvaluateTrialOffline(fingerprint, trialPath);
        }

        private LicenseStatus TryEvaluateTrialFromServer(string fingerprint, string trialPath)
        {
            try
            {
                var api = ResolveApiBaseUrl(AppSettings.LoadOrCreate());
                var version = typeof(LicenseManager).Assembly.GetName().Version?.ToString();
                var response = new TrialApiClient(api)
                    .StartOrGetAsync(fingerprint, version)
                    .GetAwaiter()
                    .GetResult();

                var started = NormalizeUtc(response.StartedUtc);
                var expires = NormalizeUtc(response.ExpiresUtc);
                WriteTrialFile(trialPath, started, fingerprint);

                var remaining = response.DaysRemaining > 0
                    ? response.DaysRemaining
                    : (int)Math.Ceiling((expires - DateTime.UtcNow).TotalDays);

                if (remaining <= 0)
                {
                    return Fail(LicenseSource.TrialExpired,
                        "Trial expired. Install a license to continue using Body Exporter.");
                }

                return new LicenseStatus
                {
                    IsAllowed = true,
                    Source = LicenseSource.Trial,
                    Owner = Environment.UserName,
                    PlanName = "Trial",
                    ExpiresUtc = expires,
                    DaysRemaining = remaining,
                    Message = "Trial: " + remaining + " day(s) remaining (of " + TrialDurationDays + ").",
                    MachineFingerprint = fingerprint
                };
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("Server trial check unavailable: " + ex.Message);
                return null;
            }
        }

        private LicenseStatus EvaluateTrialOffline(string fingerprint, string trialPath)
        {
            DateTime firstRunUtc;

            if (File.Exists(trialPath))
            {
                firstRunUtc = ReadTrialFile(trialPath, fingerprint);
                if (firstRunUtc == DateTime.MinValue)
                {
                    return Fail(LicenseSource.Tampered,
                        "Trial data is invalid or was copied from another machine. Connect to the internet once to register trial, or install a license.");
                }
            }
            else
            {
                return Fail(LicenseSource.TrialExpired,
                    "Trial requires an internet connection on first use. Connect and reopen Body Exporter, or install a license.");
            }

            var expires = firstRunUtc.AddDays(TrialDurationDays);
            var nowUtc = DateTime.UtcNow;
            var remaining = (int)Math.Ceiling((expires - nowUtc).TotalDays);

            if (remaining <= 0)
            {
                return new LicenseStatus
                {
                    IsAllowed = false,
                    Source = LicenseSource.TrialExpired,
                    PlanName = "Trial",
                    ExpiresUtc = expires,
                    DaysRemaining = 0,
                    Message = "Trial expired. Install a license to continue using Body Exporter.",
                    MachineFingerprint = fingerprint
                };
            }

            return new LicenseStatus
            {
                IsAllowed = true,
                Source = LicenseSource.Trial,
                Owner = Environment.UserName,
                PlanName = "Trial",
                ExpiresUtc = expires,
                DaysRemaining = remaining,
                Message = "Trial: " + remaining + " day(s) remaining (of " + TrialDurationDays + ") [offline].",
                MachineFingerprint = fingerprint
            };
        }

        private static DateTime ReadTrialFile(string path, string fingerprint)
        {
            try
            {
                var content = File.ReadAllText(path);
                var parts = content.Split('|');
                if (parts.Length != 2)
                {
                    return DateTime.MinValue;
                }

                var payload = parts[0];
                var providedMac = Convert.FromBase64String(parts[1]);

                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(fingerprint)))
                {
                    var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                    if (!ConstantTimeEquals(expected, providedMac))
                    {
                        return DateTime.MinValue;
                    }
                }

                if (DateTime.TryParseExact(payload, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var firstRun))
                {
                    return firstRun;
                }
                return DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static void WriteTrialFile(string path, DateTime firstRunUtc, string fingerprint)
        {
            var payload = firstRunUtc.ToString("O", CultureInfo.InvariantCulture);
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(fingerprint)))
            {
                var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                File.WriteAllText(path, payload + "|" + Convert.ToBase64String(mac), new UTF8Encoding(false));
            }
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            var diff = 0;
            for (var i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }

        private string TryGetFingerprint()
        {
            try
            {
                return GetMachineFingerprint();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static LicenseStatus Fail(LicenseSource source, string message)
        {
            return new LicenseStatus
            {
                IsAllowed = false,
                Source = source,
                Message = message
            };
        }

        private static string ComputeMachineFingerprint()
        {
            string machineGuid = null;
            try
            {
                // RegistryView.Registry64 because on 64-bit Windows the WoW6432Node hive sometimes
                // holds a different (or missing) MachineGuid, which would change the fingerprint
                // depending on whether this DLL is loaded into a 32 or 64-bit host.
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    machineGuid = key?.GetValue("MachineGuid") as string;
                }
            }
            catch
            {
                // Fall through to the deterministic fallback below.
            }

            if (string.IsNullOrEmpty(machineGuid))
            {
                machineGuid = Environment.MachineName + "::" + Environment.OSVersion.VersionString;
            }

            var input = machineGuid + "|" + Environment.MachineName;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Strongly-typed shape of <c>license.lic</c>. The property order set by
        /// <see cref="JsonPropertyAttribute.Order"/> matters: signatures are computed over
        /// JsonConvert.SerializeObject(Payload), and a different property order would produce a
        /// different byte sequence and break verification.
        /// </summary>
        [JsonObject(MemberSerialization.OptIn)]
        internal sealed class LicensePayload
        {
            [JsonProperty("version", Order = 1)]
            public int Version { get; set; } = 1;

            [JsonProperty("owner", Order = 2)]
            public string Owner { get; set; }

            [JsonProperty("plan", Order = 3)]
            public string Plan { get; set; }

            [JsonProperty("machineFingerprint", Order = 4)]
            public string MachineFingerprint { get; set; }

            [JsonProperty("issuedUtc", Order = 5)]
            public DateTime IssuedUtc { get; set; }

            [JsonProperty("expiresUtc", Order = 6)]
            public DateTime ExpiresUtc { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        internal sealed class LicenseFile
        {
            [JsonProperty("payload")]
            public LicensePayload Payload { get; set; }

            [JsonProperty("signature")]
            public string Signature { get; set; }
        }
    }
}
