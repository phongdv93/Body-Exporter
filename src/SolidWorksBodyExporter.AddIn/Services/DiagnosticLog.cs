using System;
using System.IO;
using System.Threading;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Append-only log next to <c>%APPDATA%\SolidWorksBodyExporter\settings.json</c> so support
    /// can ask for one folder. (Older builds used <c>%TEMP%\SolidWorksBodyExporter\</c>.)
    /// </summary>
    public static class DiagnosticLog
    {
        private static readonly string LogDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SolidWorksBodyExporter");

        private static readonly string LogFile = System.IO.Path.Combine(LogDirectory, "addin.log");

        private static readonly object Sync = new object();

        /// <summary>Optional prefix so one log file can tell Launcher vs SolidWorks add-in apart.</summary>
        private static string _sourcePrefix = string.Empty;

        public static string LogFilePath => LogFile;

        /// <summary>Call once per process, e.g. <c>[Launcher]</c> or <c>[SW]</c>, before other log calls.</summary>
        public static void SetSourcePrefix(string prefix)
        {
            _sourcePrefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.Trim() + " ";
        }

        public static void Info(string message) => Write("INFO", message, null);

        public static void Warn(string message) => Write("WARN", message, null);

        public static void Error(string message, Exception ex) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (Sync)
                {
                    if (!Directory.Exists(LogDirectory))
                    {
                        Directory.CreateDirectory(LogDirectory);
                    }

                    using (var writer = new StreamWriter(LogFile, append: true))
                    {
                        writer.WriteLine(
                            "{0:yyyy-MM-dd HH:mm:ss.fff} [tid:{1}] {2} {3}{4}",
                            DateTime.Now,
                            Thread.CurrentThread.ManagedThreadId,
                            level,
                            _sourcePrefix,
                            message);
                        if (ex != null)
                        {
                            writer.WriteLine(ex);
                        }
                    }
                }
            }
            catch
            {
                // Logging must never throw - it is a diagnostic aid, not part of correctness.
            }
        }
    }
}
