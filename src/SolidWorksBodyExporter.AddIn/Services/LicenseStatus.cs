using System;
using System.Reflection;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Source / state classification of the current licensing decision returned by
    /// <see cref="LicenseManager.GetStatus"/>. Surface this in UI to explain why the user is being
    /// allowed (or blocked) instead of bare yes/no, so support can triage quickly from a screenshot.
    /// </summary>
    public enum LicenseSource
    {
        /// <summary>No license file installed and the trial window has never been used.</summary>
        FreshTrial,

        /// <summary>No license file installed; built-in trial window is still active.</summary>
        Trial,

        /// <summary>Valid signed license file installed; full access granted.</summary>
        Licensed,

        /// <summary>License file present but its expiry date has passed.</summary>
        Expired,

        /// <summary>License file present but its signature does not verify against the embedded public key.</summary>
        Tampered,

        /// <summary>License file present but its machine fingerprint does not match this PC.</summary>
        WrongMachine,

        /// <summary>Built-in trial window has been exhausted and no valid license is installed.</summary>
        TrialExpired,

        /// <summary>Something failed while reading licensing state (IO error, malformed JSON, etc.).</summary>
        Error
    }

    /// <summary>
    /// Snapshot describing the user's current entitlement to use the Body Exporter window. Returned
    /// from <see cref="LicenseManager.GetStatus"/> and consumed by the WPF window header and the
    /// pre-show gate in <c>AddInIntegration.ShowBodyExporter</c>.
    /// </summary>
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class LicenseStatus
    {
        public bool IsAllowed { get; set; }

        public LicenseSource Source { get; set; }

        public string Owner { get; set; }

        public string PlanName { get; set; }

        public DateTime? ExpiresUtc { get; set; }

        public int? DaysRemaining { get; set; }

        public string Message { get; set; }

        public string MachineFingerprint { get; set; }
    }
}
