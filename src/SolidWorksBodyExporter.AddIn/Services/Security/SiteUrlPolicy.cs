using System;

namespace SolidWorksBodyExporter.AddIn.Services.Security
{
    /// <summary>HTTPS site hosts allowed for telemetry POST (marketing site, not Worker API).</summary>
    internal static class SiteUrlPolicy
    {
        public const string DefaultSiteUrl = "https://bodyexporter.com";

        private static readonly string[] AllowedHosts =
        {
            "bodyexporter.com",
            "www.bodyexporter.com",
            "127.0.0.1",
            "localhost",
        };

        public static string ResolveSiteBaseUrl(string downloadPageUrl)
        {
            if (!string.IsNullOrWhiteSpace(downloadPageUrl)
                && Uri.TryCreate(downloadPageUrl.Trim(), UriKind.Absolute, out var dl)
                && string.Equals(dl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && IsHostAllowed(dl.Host))
            {
                return dl.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            }

            return DefaultSiteUrl;
        }

        public static bool IsHostAllowed(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            foreach (var allowed in AllowedHosts)
            {
                if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
