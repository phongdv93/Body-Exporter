using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SolidWorksBodyExporter.AddIn.Services.Security
{
    internal sealed class JwtClaims
    {
        public string Subject { get; set; }
        public string MachineId { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime? LicenseExpiresUtc { get; set; }
    }

    /// <summary>RS256 JWT verification using the same RSA public key as offline license.lic files.</summary>
    internal static class JwtValidator
    {
        public static bool TryValidate(string jwt, string expectedMachineId, string publicKeyXml, out JwtClaims claims)
        {
            claims = null;
            if (string.IsNullOrWhiteSpace(jwt) || string.IsNullOrWhiteSpace(expectedMachineId))
            {
                return false;
            }

            var parts = jwt.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            var signingInput = parts[0] + "." + parts[1];
            byte[] signature;
            try
            {
                signature = Base64UrlDecode(parts[2]);
            }
            catch
            {
                return false;
            }

            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(publicKeyXml);
                    if (!rsa.VerifyData(Encoding.UTF8.GetBytes(signingInput), "SHA256", signature))
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }

            JObject payload;
            try
            {
                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                payload = JObject.Parse(payloadJson);
            }
            catch
            {
                return false;
            }

            var expSeconds = payload["exp"]?.Value<long>();
            if (expSeconds.HasValue)
            {
                var expUtc = DateTimeOffset.FromUnixTimeSeconds(expSeconds.Value).UtcDateTime;
                if (expUtc <= DateTime.UtcNow)
                {
                    return false;
                }

                claims = new JwtClaims { ExpiresUtc = expUtc };
            }
            else
            {
                claims = new JwtClaims { ExpiresUtc = DateTime.UtcNow.AddHours(1) };
            }

            var machineId = payload["machineId"]?.ToString();
            if (string.IsNullOrWhiteSpace(machineId)
                || !string.Equals(machineId, expectedMachineId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            claims.MachineId = machineId;
            claims.Subject = payload["sub"]?.ToString();

            var licenseExpires = payload["licenseExpires"]?.ToString();
            if (!string.IsNullOrWhiteSpace(licenseExpires)
                && DateTime.TryParse(licenseExpires, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var licExp))
            {
                claims.LicenseExpiresUtc = licExp;
            }

            return true;
        }

        private static byte[] Base64UrlDecode(string input)
        {
            var s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }

            return Convert.FromBase64String(s);
        }
    }
}
