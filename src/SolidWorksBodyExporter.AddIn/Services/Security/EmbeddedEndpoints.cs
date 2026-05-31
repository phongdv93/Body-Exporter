using System;
using System.Text;

namespace SolidWorksBodyExporter.AddIn.Services.Security
{
    /// <summary>Obfuscation-friendly storage for fixed HTTPS endpoints (not plain literals in IL).</summary>
    internal static class EmbeddedEndpoints
    {
        private const byte XorKey = 0xA7;

        private static readonly byte[] DefaultApiBaseUrlBytes =
        {
            207, 211, 211, 215, 212, 157, 136, 136, 197, 200, 195, 222, 194, 223, 215, 200, 213, 211, 194, 213,
            138, 198, 215, 206, 137, 197, 200, 195, 222, 194, 223, 215, 200, 213, 211, 194, 213, 137, 208, 200,
            213, 204, 194, 213, 212, 137, 195, 194, 209,
        };

        private static readonly byte[] DefaultApiHostBytes =
        {
            197, 200, 195, 222, 194, 223, 215, 200, 213, 211, 194, 213, 138, 198, 215, 206, 137, 197, 200, 195,
            222, 194, 223, 215, 200, 213, 211, 194, 213, 137, 208, 200, 213, 204, 194, 213, 212, 137, 195, 194,
            209,
        };

        internal static string DefaultApiBaseUrl => Decode(DefaultApiBaseUrlBytes);

        internal static string DefaultApiHost => Decode(DefaultApiHostBytes);

        private static string Decode(byte[] encoded)
        {
            if (encoded == null || encoded.Length == 0)
            {
                return string.Empty;
            }

            var buf = new byte[encoded.Length];
            for (var i = 0; i < encoded.Length; i++)
            {
                buf[i] = (byte)(encoded[i] ^ XorKey);
            }

            return Encoding.UTF8.GetString(buf);
        }
    }
}
