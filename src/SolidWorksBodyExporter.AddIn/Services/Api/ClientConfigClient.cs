using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SolidWorksBodyExporter.AddIn.Services;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>
    /// Fetches <see cref="ClientRemoteConfig"/> from the Worker (or any HTTPS host) and
    /// caches it under <c>%APPDATA%\SolidWorksBodyExporter\client-config-cache.json</c>.
    /// </summary>
    public static class ClientConfigClient
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

        public static string CachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SolidWorksBodyExporter",
            "client-config-cache.json");

        /// <summary>
        /// Returns cached config if fresh; otherwise tries network. On failure returns
        /// last good cache or built-in defaults (author "Gió", empty payment fields).
        /// </summary>
        public static ClientRemoteConfig Load(string apiBaseUrl, bool forceRefresh = false)
        {
            var defaults = new ClientRemoteConfig();
            if (string.IsNullOrWhiteSpace(apiBaseUrl)) return TryReadCacheOnly() ?? defaults;

            ClientRemoteConfig staleCache = null;
            TryReadCache(out staleCache, out var staleAge);

            if (!forceRefresh && staleCache != null && staleAge < CacheTtl)
            {
                return staleCache;
            }

            try
            {
                var json = HttpGetJson(apiBaseUrl.TrimEnd('/') + "/v1/client-config");
                if (string.IsNullOrWhiteSpace(json)) return staleCache ?? defaults;

                var cfg = ParseConfig(json);
                WriteCache(cfg);
                return cfg;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("ClientConfigClient: fetch failed - " + ex.Message);
                return TryReadCacheOnly() ?? defaults;
            }
        }

        private static ClientRemoteConfig TryReadCacheOnly()
        {
            if (TryReadCache(out var c, out _)) return c;
            return null;
        }

        private static bool TryReadCache(out ClientRemoteConfig cfg, out TimeSpan age)
        {
            cfg = null;
            age = TimeSpan.MaxValue;
            try
            {
                if (!File.Exists(CachePath)) return false;
                var text = File.ReadAllText(CachePath, Encoding.UTF8);
                var jo = JObject.Parse(text);
                var saved = jo.Value<DateTime?>("savedUtc");
                cfg = jo["config"]?.ToObject<ClientRemoteConfig>() ?? new ClientRemoteConfig();
                NormalizeConfigText(cfg);
                if (saved.HasValue) age = DateTime.UtcNow - saved.Value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteCache(ClientRemoteConfig cfg)
        {
            try
            {
                var dir = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var jo = new JObject
                {
                    ["savedUtc"] = DateTime.UtcNow,
                    ["config"] = JObject.FromObject(cfg)
                };
                File.WriteAllText(CachePath, jo.ToString(Formatting.Indented), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("ClientConfigClient: cache write failed - " + ex.Message);
            }
        }

        private static string HttpGetJson(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.UserAgent = "SolidWorksBodyExporter/" + typeof(ClientConfigClient).Assembly.GetName().Version;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            {
                var encoding = ResolveResponseEncoding(response);
                using (var reader = new StreamReader(stream ?? Stream.Null, encoding, detectEncodingFromByteOrderMarks: true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static Encoding ResolveResponseEncoding(HttpWebResponse response)
        {
            try
            {
                var charset = response?.CharacterSet;
                if (!string.IsNullOrWhiteSpace(charset))
                {
                    return Encoding.GetEncoding(charset);
                }
            }
            catch
            {
                // fall through
            }

            return Encoding.UTF8;
        }

        private static ClientRemoteConfig ParseConfig(string json)
        {
            var jo = JObject.Parse(json);
            // Allow either flat JSON or { "config": { ... } } wrapper from admin publish.
            var inner = jo["config"] as JObject ?? jo;
            var cfg = inner.ToObject<ClientRemoteConfig>() ?? new ClientRemoteConfig();
            NormalizeConfigText(cfg);
            return cfg;
        }

        private static void NormalizeConfigText(ClientRemoteConfig cfg)
        {
            if (cfg == null)
            {
                return;
            }

            cfg.AuthorName = TextEncodingHelper.NormalizeRemote(cfg.AuthorName);
            cfg.SupportEmail = TextEncodingHelper.NormalizeRemote(cfg.SupportEmail);
            cfg.SupportUrl = TextEncodingHelper.NormalizeRemote(cfg.SupportUrl);
            cfg.PaymentWebTitle = TextEncodingHelper.NormalizeRemote(cfg.PaymentWebTitle);
            cfg.PaymentWebBody = TextEncodingHelper.NormalizeRemote(cfg.PaymentWebBody);
            cfg.PaymentVnTitle = TextEncodingHelper.NormalizeRemote(cfg.PaymentVnTitle);
            cfg.PaymentVnBody = TextEncodingHelper.NormalizeRemote(cfg.PaymentVnBody);
            cfg.PaymentIntlTitle = TextEncodingHelper.NormalizeRemote(cfg.PaymentIntlTitle);
            cfg.PaymentIntlBody = TextEncodingHelper.NormalizeRemote(cfg.PaymentIntlBody);
        }
    }
}
