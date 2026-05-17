using System;
using System.Linq;
using System.Text;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Repairs Vietnamese (and other UTF-8) text that was mis-decoded as Latin-1/Windows-1252
    /// when saved on the server or written to the client-config cache.
    /// </summary>
    internal static class TextEncodingHelper
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public static string NormalizeRemote(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value ?? string.Empty;
            }

            var trimmed = value.Trim();
            if (!trimmed.Any(c => c > 127))
            {
                return trimmed;
            }

            var repaired = TryRepairUtf8FromLatin1(trimmed);
            return string.IsNullOrEmpty(repaired) ? trimmed : repaired;
        }

        private static string TryRepairUtf8FromLatin1(string value)
        {
            try
            {
                var bytes = Latin1.GetBytes(value);
                var utf8 = Encoding.UTF8.GetString(bytes);
                if (utf8 == value)
                {
                    return value;
                }

                if (ScoreVietnameseText(utf8) > ScoreVietnameseText(value))
                {
                    return utf8;
                }
            }
            catch
            {
                // ignore
            }

            return value;
        }

        /// <summary>Higher score = more likely correct Vietnamese display text.</summary>
        private static int ScoreVietnameseText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var score = 0;
            foreach (var c in text)
            {
                if (c == '?' || c == '\uFFFD')
                {
                    score -= 3;
                }
                else if (c >= 0x00C0 && c <= 0x024F)
                {
                    score += 2;
                }
                else if (c > 127)
                {
                    score += 1;
                }
            }

            return score;
        }
    }
}
