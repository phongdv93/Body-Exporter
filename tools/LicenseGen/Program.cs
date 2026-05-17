using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SolidWorksBodyExporter.LicenseGen
{
    /// <summary>
    /// Tiny CLI that issues a signed <c>license.lic</c> for the Body Exporter add-in. Reads the
    /// developer's private RSA key from <c>tools/license-keys/private.xml</c> (kept off the user's
    /// machine and ignored by git), bakes in the requested owner / plan / machine fingerprint /
    /// expiry, signs the canonical JSON payload with RSA-SHA256, and writes a file the add-in's
    /// embedded public key will accept.
    /// <para>
    /// Usage:
    /// <code>
    /// dotnet run --project tools/LicenseGen -- ^
    ///     --key tools/license-keys/private.xml ^
    ///     --owner "Pham Van Phong" ^
    ///     --plan Pro ^
    ///     --machine 1a2b3c... ^
    ///     --days 365 ^
    ///     --out C:\path\to\license.lic
    /// </code>
    /// Pass <c>--machine *</c> for a wildcard license that works on any PC (handy for internal
    /// dev/test, but should NOT be issued to paying customers because it's effectively non-revocable).
    /// </para>
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var opts = ParseArgs(args);
                if (opts == null)
                {
                    PrintUsage();
                    return 64; // EX_USAGE
                }

                var privateXml = File.ReadAllText(opts.KeyPath);

                var payload = new LicensePayload
                {
                    Version = 1,
                    Owner = opts.Owner,
                    Plan = opts.Plan,
                    MachineFingerprint = opts.Machine,
                    IssuedUtc = DateTime.UtcNow,
                    ExpiresUtc = DateTime.UtcNow.AddDays(opts.Days)
                };

                var canonical = JsonConvert.SerializeObject(payload, Formatting.None);
                byte[] signature;
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(privateXml);
                    signature = rsa.SignData(new UTF8Encoding(false).GetBytes(canonical), "SHA256");
                }

                var file = new LicenseFile
                {
                    Payload = payload,
                    Signature = Convert.ToBase64String(signature)
                };

                var fileJson = JsonConvert.SerializeObject(file, Formatting.Indented);
                File.WriteAllText(opts.OutPath, fileJson, new UTF8Encoding(false));

                Console.WriteLine("Wrote " + opts.OutPath);
                Console.WriteLine("  owner   : " + payload.Owner);
                Console.WriteLine("  plan    : " + payload.Plan);
                Console.WriteLine("  machine : " + payload.MachineFingerprint);
                Console.WriteLine("  expires : " + payload.ExpiresUtc.ToString("u", CultureInfo.InvariantCulture));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("License generation failed: " + ex.Message);
                return 1;
            }
        }

        private static Options ParseArgs(string[] args)
        {
            var opts = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--key":     opts.KeyPath = NextArg(args, ref i); break;
                    case "--owner":   opts.Owner   = NextArg(args, ref i); break;
                    case "--plan":    opts.Plan    = NextArg(args, ref i); break;
                    case "--machine": opts.Machine = NextArg(args, ref i); break;
                    case "--days":    opts.Days    = int.Parse(NextArg(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--out":     opts.OutPath = NextArg(args, ref i); break;
                    default:
                        Console.Error.WriteLine("Unknown argument: " + args[i]);
                        return null;
                }
            }

            if (string.IsNullOrEmpty(opts.KeyPath) ||
                string.IsNullOrEmpty(opts.Owner) ||
                string.IsNullOrEmpty(opts.Plan) ||
                string.IsNullOrEmpty(opts.Machine) ||
                string.IsNullOrEmpty(opts.OutPath) ||
                opts.Days <= 0)
            {
                return null;
            }
            return opts;
        }

        private static string NextArg(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException("Missing value after " + args[i]);
            }
            return args[++i];
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage: swbe-licensegen --key <private.xml> --owner <name> --plan <Pro|Trial|...> --machine <fingerprint|*> --days <N> --out <path.lic>");
        }

        private sealed class Options
        {
            public string KeyPath;
            public string Owner;
            public string Plan;
            public string Machine;
            public int Days;
            public string OutPath;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class LicensePayload
        {
            [JsonProperty("version", Order = 1)]
            public int Version { get; set; }

            [JsonProperty("owner", Order = 2)]
            public string Owner { get; set; }

            [JsonProperty("plan", Order = 3)]
            public string Plan { get; set; }

            [JsonProperty("machineFingerprint", Order = 4)]
            public string MachineFingerprint { get; set; }

            [JsonProperty("issuedUtc", Order = 5)]
            public DateTime IssuedUtc { get; set; }

            [JsonProperty("expiresUtc", Order = 6)]
            public DateTime ExpiresUtc { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class LicenseFile
        {
            [JsonProperty("payload")]
            public LicensePayload Payload { get; set; }

            [JsonProperty("signature")]
            public string Signature { get; set; }
        }
    }
}
