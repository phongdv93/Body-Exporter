using System;
using SolidWorksBodyExporter.AddIn.Services;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>
    /// Remote kill switch / cap from <c>GET /v1/client-config</c> (no new DLL required).
    /// You control trial-only or max-days from the server after beta or if installs were tampered.
    /// </summary>
    internal static class EntitlementPolicyEnforcer
    {
        public static LicenseStatus Apply(LicenseStatus status, ClientRemoteConfig config)
        {
            if (status == null || config?.EntitlementPolicy == null)
            {
                return status;
            }

            var policy = config.EntitlementPolicy;
            var mode = (policy.Mode ?? "normal").Trim().ToLowerInvariant();

            if (mode == "blocked")
            {
                return new LicenseStatus
                {
                    IsAllowed = false,
                    Source = LicenseSource.Error,
                    Message = string.IsNullOrWhiteSpace(policy.Message)
                        ? "This build is temporarily disabled. Contact support."
                        : policy.Message,
                    MachineFingerprint = status.MachineFingerprint
                };
            }

            var capDays = policy.CapDays ?? 14;
            if (mode == "cap_days" || mode == "trial_only")
            {
                if (capDays > 0 && status.IsAllowed)
                {
                    var now = DateTime.UtcNow;
                    if (!status.DaysRemaining.HasValue || status.DaysRemaining.Value > capDays)
                    {
                        status.ExpiresUtc = now.AddDays(capDays);
                        status.DaysRemaining = capDays;
                        if (mode == "trial_only")
                        {
                            status.Source = LicenseSource.Trial;
                            status.PlanName = "Trial (server policy)";
                        }

                        var suffix = string.IsNullOrWhiteSpace(policy.Message)
                            ? " (server policy: max " + capDays + " days)"
                            : " — " + policy.Message;
                        status.Message = (status.Message ?? string.Empty) + suffix;
                    }
                }

                return status;
            }

            return status;
        }

        public static bool IsTrialOnlyMode(ClientRemoteConfig config)
        {
            var mode = config?.EntitlementPolicy?.Mode;
            return string.Equals(mode, "trial_only", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBlocked(ClientRemoteConfig config)
        {
            return string.Equals(config?.EntitlementPolicy?.Mode, "blocked", StringComparison.OrdinalIgnoreCase);
        }
    }
}
