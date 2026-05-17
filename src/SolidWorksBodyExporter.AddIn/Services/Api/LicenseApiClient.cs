using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>
    /// HTTP client that talks to the licensing server. Single method: exchange a
    /// license key + machine fingerprint for a short-lived JWT. The token is RSA-signed
    /// by the server using the same public key that's embedded in the addin DLL, so
    /// the addin can validate it offline once issued.
    /// <para>
    /// The class is intentionally small and uses <c>HttpWebRequest</c> (built into
    /// .NET Framework 4.8) instead of <c>HttpClient</c> to keep the dependency surface
    /// tight: pulling System.Net.Http into a SolidWorks-loaded DLL has historically
    /// caused assembly-binding redirect headaches when SolidWorks itself loads a
    /// conflicting version into its AppDomain.
    /// </para>
    /// </summary>
    public sealed class LicenseApiClient
    {
        private readonly string _baseUrl;

        public LicenseApiClient(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("baseUrl required", nameof(baseUrl));
            _baseUrl = baseUrl.TrimEnd('/');
        }

        /// <summary>
        /// POST <c>{baseUrl}/v1/license/validate</c> with <c>{key, machineId}</c>.
        /// Server returns <c>{token, expiresUtc, owner, plan, expires}</c> or an HTTP
        /// 4xx with a problem-detail JSON body describing the rejection reason.
        /// </summary>
        public async Task<LicenseValidationResponse> ValidateAsync(string licenseKey, string machineFingerprint)
        {
            if (string.IsNullOrWhiteSpace(licenseKey)) throw new ArgumentException("licenseKey required");
            if (string.IsNullOrWhiteSpace(machineFingerprint)) throw new ArgumentException("machineFingerprint required");

            var payload = JsonConvert.SerializeObject(new
            {
                key = licenseKey,
                machineId = machineFingerprint,
                productVersion = typeof(LicenseApiClient).Assembly.GetName().Version?.ToString()
            });

            var request = (HttpWebRequest)WebRequest.Create(_baseUrl + "/v1/license/validate");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.UserAgent = "SolidWorksBodyExporter/" + typeof(LicenseApiClient).Assembly.GetName().Version;
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            // Force TLS 1.2 minimum. Some older SolidWorks installs run on a .NET 4.8 in-process
            // host that defaults to SSL3 + TLS 1.0, which Cloudflare and most modern hosts now reject.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            var bytes = Encoding.UTF8.GetBytes(payload);
            using (var stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }

            try
            {
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<LicenseValidationResponse>(body)
                           ?? throw new InvalidOperationException("Server returned empty body.");
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse httpResponse)
            {
                // Surface the server's problem detail to the user so they can see exactly
                // why the license was rejected: expired plan, mismatched machineId, etc.
                string body = string.Empty;
                using (var stream = httpResponse.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
                throw new LicenseApiException(
                    "License server rejected validation (HTTP " + (int)httpResponse.StatusCode + "): " + body, ex);
            }
        }
    }

    /// <summary>Payload mirror of the Cloudflare Worker's response.</summary>
    public sealed class LicenseValidationResponse
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("expiresUtc")]
        public DateTime ExpiresUtc { get; set; }

        [JsonProperty("owner")]
        public string Owner { get; set; }

        [JsonProperty("plan")]
        public string Plan { get; set; }

        [JsonProperty("licenseExpires")]
        public DateTime LicenseExpires { get; set; }
    }

    public sealed class LicenseApiException : Exception
    {
        public LicenseApiException(string message, Exception inner) : base(message, inner) { }
    }
}
