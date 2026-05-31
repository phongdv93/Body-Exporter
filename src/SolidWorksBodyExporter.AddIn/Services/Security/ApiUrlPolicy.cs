using System;

namespace SolidWorksBodyExporter.AddIn.Services.Security
{
    /// <summary>Only these API hosts are accepted — blocks pointing settings.json at a fake license server.</summary>
    internal static class ApiUrlPolicy
    {
        public static string DefaultApiBaseUrl => EmbeddedEndpoints.DefaultApiBaseUrl;

        private static readonly string[] AllowedHosts =
        {
            EmbeddedEndpoints.DefaultApiHost,
        };

        public static string Normalize(string apiBaseUrl, string defaultUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                return defaultUrl;
            }

            var trimmed = apiBaseUrl.Trim().TrimEnd('/');
            return IsAllowed(trimmed) ? trimmed : defaultUrl;
        }

        public static bool IsAllowed(string apiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                return true;
            }

            if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var host in AllowedHosts)
            {
                if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
