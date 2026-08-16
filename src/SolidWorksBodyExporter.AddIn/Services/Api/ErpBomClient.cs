using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>
    /// HTTP client for a customer ERP that exposes Body Exporter CAD integration:
    /// <c>GET /api/integrations/v1/me</c> and <c>POST /api/integrations/v1/bom/lines</c>.
    /// Uses <see cref="HttpWebRequest"/> (same rationale as <see cref="LicenseApiClient"/>).
    /// </summary>
    internal sealed class ErpBomClient
    {
        private const string MePath = "/api/integrations/v1/me";
        private const string BomLinesPath = "/api/integrations/v1/bom/lines";
        private const int TimeoutMs = 20000;

        private readonly string _baseUrl;
        private readonly string _apiKey;

        public ErpBomClient(string baseUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("ERP base URL is required.", nameof(baseUrl));
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("ERP API key is required.", nameof(apiKey));
            }

            _baseUrl = NormalizeBaseUrl(baseUrl);
            _apiKey = apiKey.Trim();
        }

        public static string NormalizeBaseUrl(string baseUrl)
        {
            var url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (url.EndsWith("/api/integrations/v1", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - "/api/integrations/v1".Length).TrimEnd('/');
            }
            else if (url.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - 4).TrimEnd('/');
            }

            return url;
        }

        /// <summary>
        /// Whether this machine and Windows account hold their own ERP link. A link made on
        /// another PC is not visible here, so this is false until someone links this one.
        /// </summary>
        public static bool IsLinked()
        {
            return Security.ErpLinkStore.IsLinked();
        }

        /// <summary>The client for this machine's link, or null when it has none.</summary>
        public static ErpBomClient ForThisMachine()
        {
            var link = Security.ErpLinkStore.Current();
            return link != null && link.IsUsable
                ? new ErpBomClient(link.BaseUrl, link.ApiKey)
                : null;
        }

        public async Task<ErpMeResponse> TestConnectionAsync()
        {
            var body = await SendAsync(MePath, "GET", null).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return new ErpMeResponse { Ok = true };
            }

            try
            {
                return JsonConvert.DeserializeObject<ErpMeResponse>(body) ?? new ErpMeResponse { Ok = true };
            }
            catch
            {
                return new ErpMeResponse { Ok = true, Raw = body };
            }
        }

        public async Task<ErpBomPushResult> PushBomAsync(
            string productCode,
            IEnumerable<BodyExportRow> rows,
            string partFileName,
            PartOverallSize overallSize = null)
        {
            if (string.IsNullOrWhiteSpace(productCode))
            {
                throw new ArgumentException("productCode is required.", nameof(productCode));
            }

            var lines = (rows ?? Enumerable.Empty<BodyExportRow>())
                .Where(r => r != null && r.Status != BodyRowStatus.Deleted)
                .Select(r => MapLine(r, partFileName))
                .ToList();

            if (lines.Count == 0)
            {
                throw new InvalidOperationException("No BOM lines to send (all rows deleted or empty).");
            }

            var overall = overallSize ?? new PartOverallSize();
            var payload = JsonConvert.SerializeObject(new
            {
                productCode = productCode.Trim(),
                replaceSection = true,
                source = "body-exporter",
                productLengthMm = RoundMm(overall.LengthMm),
                productWidthMm = RoundMm(overall.WidthMm),
                productHeightMm = RoundMm(overall.HeightMm),
                lines
            });

            var body = await SendAsync(BomLinesPath, "POST", payload).ConfigureAwait(false);
            return new ErpBomPushResult
            {
                LineCount = lines.Count,
                ResponseBody = body
            };
        }

        internal static object MapLine(BodyExportRow row, string partFileName)
        {
            var material = string.IsNullOrWhiteSpace(row.MaterialName) ? "Default" : row.MaterialName.Trim();
            var appearance = (row.AppearanceDisplay ?? string.Empty).Trim();
            var remarkParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(appearance) && !appearance.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                remarkParts.Add(appearance);
            }

            if (!string.IsNullOrWhiteSpace(partFileName))
            {
                remarkParts.Add(partFileName.Trim());
            }

            return new
            {
                partCode = string.IsNullOrWhiteSpace(row.PluginBodyId)
                    ? (row.DisplayName ?? row.SolidWorksBodyName ?? Guid.NewGuid().ToString("N"))
                    : row.PluginBodyId.Trim(),
                partName = string.IsNullOrWhiteSpace(row.DisplayName)
                    ? (row.SolidWorksBodyName ?? "Body")
                    : row.DisplayName.Trim(),
                section = BomCategoryInfo.ToErpSection(row.TypeId),
                material,
                qty = row.Quantity <= 0 ? 1 : row.Quantity,
                lengthMm = RoundMm(row.Length),
                widthMm = RoundMm(row.Width),
                thicknessMm = RoundMm(row.Thickness),
                remark = string.Join(" | ", remarkParts)
            };
        }

        private static double RoundMm(double value)
        {
            return Math.Round(value, 3, MidpointRounding.AwayFromZero);
        }

        private async Task<string> SendAsync(string path, string method, string jsonBody)
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            var request = (HttpWebRequest)WebRequest.Create(_baseUrl + path);
            request.Method = method;
            request.UserAgent = "SolidWorksBodyExporter/" + typeof(ErpBomClient).Assembly.GetName().Version;
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Accept = "application/json";
            ApplyAuth(request);

            if (jsonBody != null)
            {
                request.ContentType = "application/json; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(jsonBody);
                using (var stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
            }

            try
            {
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse httpResponse)
            {
                string body;
                using (var stream = httpResponse.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                var code = (int)httpResponse.StatusCode;
                var detail = ExtractErrorDetail(body);
                var hint = code == 401 || code == 403
                    ? " Check the API key from ERP → BOM setup → CAD connection."
                    : code == 404
                        ? " Check the product code exists in ERP."
                        : string.Empty;

                throw new ErpBomClientException(
                    "ERP rejected the request (HTTP " + code + "): "
                    + (string.IsNullOrWhiteSpace(detail) ? body : detail)
                    + hint,
                    code,
                    ex);
            }
            catch (WebException ex)
            {
                throw new ErpBomClientException(
                    "Could not reach ERP at " + _baseUrl + ": " + ex.Message,
                    0,
                    ex);
            }
        }

        private void ApplyAuth(HttpWebRequest request)
        {
            request.Headers["X-API-Key"] = _apiKey;
            if (!_apiKey.StartsWith("hp_", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + _apiKey;
            }
        }

        private static string ExtractErrorDetail(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            try
            {
                var jo = Newtonsoft.Json.Linq.JObject.Parse(body);
                foreach (var key in new[] { "message", "error", "detail", "title" })
                {
                    var token = jo[key];
                    if (token != null && token.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        var s = token.ToString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            return s;
                        }
                    }
                }
            }
            catch
            {
                /* not JSON */
            }

            return body.Length > 400 ? body.Substring(0, 400) + "…" : body;
        }
    }

    internal sealed class ErpMeResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("company")]
        public string Company { get; set; }

        [JsonIgnore]
        public string Raw { get; set; }

        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Company))
                {
                    return Name + " (" + Company + ")";
                }

                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name;
                }

                if (!string.IsNullOrWhiteSpace(Company))
                {
                    return Company;
                }

                return "Connected";
            }
        }
    }

    internal sealed class ErpBomPushResult
    {
        public int LineCount { get; set; }

        public string ResponseBody { get; set; }
    }

    internal sealed class ErpBomClientException : Exception
    {
        public int StatusCode { get; }

        public ErpBomClientException(string message, int statusCode, Exception inner)
            : base(message, inner)
        {
            StatusCode = statusCode;
        }
    }
}
