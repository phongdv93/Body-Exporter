using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SolidWorksBodyExporter.AddIn.Services.Api
{
    /// <summary>POST /v1/trial/start — server-authoritative 14-day trial per machine.</summary>
    public sealed class TrialApiClient
    {
        private readonly string _baseUrl;

        public TrialApiClient(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("baseUrl required", nameof(baseUrl));
            }

            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<TrialStartResponse> StartOrGetAsync(string machineFingerprint, string productVersion)
        {
            if (string.IsNullOrWhiteSpace(machineFingerprint))
            {
                throw new ArgumentException("machineFingerprint required");
            }

            var payload = JsonConvert.SerializeObject(new
            {
                machineId = machineFingerprint,
                productVersion,
            });

            var request = (HttpWebRequest)WebRequest.Create(_baseUrl + "/v1/trial/start");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.UserAgent = "SolidWorksBodyExporter/" + typeof(TrialApiClient).Assembly.GetName().Version;
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

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
                    return JsonConvert.DeserializeObject<TrialStartResponse>(body)
                           ?? throw new InvalidOperationException("Server returned empty body.");
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse httpResponse)
            {
                string body = string.Empty;
                using (var stream = httpResponse.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                throw new LicenseApiException(
                    "Trial server rejected request (HTTP " + (int)httpResponse.StatusCode + "): " + body, ex);
            }
        }
    }

    public sealed class TrialStartResponse
    {
        [JsonProperty("startedUtc")]
        public DateTime StartedUtc { get; set; }

        [JsonProperty("expiresUtc")]
        public DateTime ExpiresUtc { get; set; }

        [JsonProperty("daysRemaining")]
        public int DaysRemaining { get; set; }
    }
}
