namespace SolidWorksBodyExporter.AddIn.Services.Ui
{
    /// <summary>English UI strings for the license window.</summary>
    internal static class LicenseUiText
    {
        public const string EmptyField = "-";
        public const string OpenBuyOnWeb = "Buy / renew on web";
        public const string ActivateLicense = "Activate license";
        public const string ApplyKeys = "Apply keys";
        public const string ChooseLicFile = "Choose .lic file…";
        public const string RecalculateDays = "Refresh days";
        public const string ActivateSectionHint =
            "Paste UUID license keys from email (one per line). Paying again with the same email extends your current key automatically — then click Refresh days.";
        public const string LicenseKeysToolTip =
            "One UUID per line. Add new lines, then click Apply keys.";
        public const string RecalculateToolTip = "Re-read the expiry date from the server for every key in the box.";
        public const string FooterNote =
            "Buy or renew at bodyexporter.com/buy — payment extends your key automatically; new buyers receive a key by email.";
        public const string KeysAlreadyApplied =
            "These keys were already applied. Click Refresh days after a renewal payment.";
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
