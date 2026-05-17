namespace SolidWorksBodyExporter.AddIn.Services.Ui
{
    /// <summary>English UI strings for the license window.</summary>
    internal static class LicenseUiText
    {
        public const string EmptyField = "-";
        public const string OpenBuyOnWeb = "Buy license on web";
        public const string ActivateLicense = "Activate license";
        public const string ApplyKeys = "Apply keys";
        public const string ChooseLicFile = "Choose .lic file…";
        public const string RecalculateDays = "Recalculate days";
        public const string ActivateSectionHint =
            "Paste UUID license keys from email (one per line). New keys stack days onto your current term.";
        public const string LicenseKeysToolTip =
            "One UUID per line. Add new lines, then click Apply keys.";
        public const string RecalculateToolTip = "Recalculate expiry from all keys in the box.";
        public const string FooterNote =
            "Buy a license at bodyexporter.com/buy — you will receive keys by email; paste them above.";
        public const string KeysAlreadyApplied =
            "These keys were already applied. Your license term did not change.";
        public const string PasteKeysPrompt =
            "Paste new keys below (one UUID per line), or choose a .lic file.";
        public const string BankLabel = "Bank: ";
        public const string AccountLabel = "Account: ";
        public const string AmountLabel = "Amount: ";
        public const string TransferMemoLabel = "Transfer memo (required): ";
        public const string TransferMemoHint =
            "Transfer the exact amount with the memo above to receive your license by email automatically.";
    }
}
