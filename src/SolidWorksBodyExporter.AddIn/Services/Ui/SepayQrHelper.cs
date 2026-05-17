using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SolidWorksBodyExporter.AddIn.Services.Ui
{
    /// <summary>
    /// Parses Sepay QR image URLs (qr.sepay.vn/img?bank=...&amp;acc=...&amp;amount=...&amp;des=...).
    /// </summary>
    public static class SepayQrHelper
    {
        public sealed class SepayQrInfo
        {
            public string QrImageUrl { get; set; }
            public string Bank { get; set; }
            public string Account { get; set; }
            public long? AmountVnd { get; set; }
            public string Description { get; set; }
        }

        public static bool TryParse(string url, out SepayQrInfo info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            var q = uri.Query;
            if (string.IsNullOrEmpty(q))
            {
                return false;
            }

            var bank = GetQuery(q, "bank");
            var acc = GetQuery(q, "acc");
            if (string.IsNullOrEmpty(bank) && string.IsNullOrEmpty(acc))
            {
                return false;
            }

            long? amount = null;
            var amountRaw = GetQuery(q, "amount");
            if (!string.IsNullOrEmpty(amountRaw) && long.TryParse(amountRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amt))
            {
                amount = amt;
            }

            info = new SepayQrInfo
            {
                QrImageUrl = url.Trim(),
                Bank = bank ?? string.Empty,
                Account = acc ?? string.Empty,
                AmountVnd = amount,
                Description = GetQuery(q, "des") ?? string.Empty,
            };
            return true;
        }

        public static string BuildTransferMemo(string email)
        {
            var trimmed = email?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return "Body Export License";
            }

            return "BE " + trimmed;
        }

        public static string BuildQrImageUrl(string baseUrl, string email)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return string.Empty;
            }

            var memo = BuildTransferMemo(email);
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return baseUrl.Trim();
            }

            var builder = new UriBuilder(uri);
            var query = ParseQuery(builder.Query);
            query["des"] = memo;
            builder.Query = BuildQuery(query);
            return builder.Uri.ToString();
        }

        public static string FormatTransferDetails(SepayQrInfo info, string email)
        {
            if (info == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(info.Bank))
            {
                sb.AppendLine(LicenseUiText.BankLabel + info.Bank);
            }

            if (!string.IsNullOrEmpty(info.Account))
            {
                sb.AppendLine(LicenseUiText.AccountLabel + info.Account);
            }

            if (info.AmountVnd.HasValue)
            {
                sb.AppendLine(
                    LicenseUiText.AmountLabel
                    + info.AmountVnd.Value.ToString("N0", CultureInfo.InvariantCulture)
                    + " VND");
            }

            var memo = BuildTransferMemo(email);
            sb.AppendLine(LicenseUiText.TransferMemoLabel + memo);
            sb.AppendLine();
            sb.Append(LicenseUiText.TransferMemoHint);
            return sb.ToString().TrimEnd();
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query))
            {
                return result;
            }

            var start = query.StartsWith("?", StringComparison.Ordinal) ? 1 : 0;
            foreach (var pair in query.Substring(start).Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                {
                    continue;
                }

                var eq = pair.IndexOf('=');
                var name = eq >= 0 ? pair.Substring(0, eq) : pair;
                var value = eq >= 0 ? pair.Substring(eq + 1) : string.Empty;
                result[name] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }

            return result;
        }

        private static string BuildQuery(Dictionary<string, string> query)
        {
            if (query == null || query.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var kv in query)
            {
                parts.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value ?? string.Empty));
            }

            return string.Join("&", parts);
        }

        private static string GetQuery(string query, string key)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            var start = query.StartsWith("?", StringComparison.Ordinal) ? 1 : 0;
            var pairs = query.Substring(start).Split('&');
            foreach (var pair in pairs)
            {
                var eq = pair.IndexOf('=');
                var name = eq >= 0 ? pair.Substring(0, eq) : pair;
                if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = eq >= 0 ? pair.Substring(eq + 1) : string.Empty;
                return Uri.UnescapeDataString(value.Replace('+', ' '));
            }

            return null;
        }
    }
}
