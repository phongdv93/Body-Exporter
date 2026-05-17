using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace SolidWorksBodyExporter.Launcher
{
    /// <summary>
    /// Locates and starts SolidWorks when the launcher fires before the user has opened SW.
    /// </summary>
    internal static class SolidWorksStarter
    {
        /// <summary>Returns path to SLDWORKS.exe or null if not found.</summary>
        public static string TryFindSolidWorksExe()
        {
            // 1) Typical install layout: %ProgramFiles%\SOLIDWORKS Corp\<Product>\SLDWORKS.exe
            try
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var corp = Path.Combine(programFiles, "SOLIDWORKS Corp");
                if (Directory.Exists(corp))
                {
                    foreach (var dir in Directory.EnumerateDirectories(corp))
                    {
                        var exe = Path.Combine(dir, "SLDWORKS.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 2) Registry fallback (year-version keys).
            try
            {
                using (var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SolidWorks"))
                {
                    if (root == null) return null;
                    foreach (var yearKey in root.GetSubKeyNames().OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!yearKey.StartsWith("SOLIDWORKS ", StringComparison.OrdinalIgnoreCase)) continue;
                        using (var setup = root.OpenSubKey(yearKey + @"\Setup"))
                        {
                            var folder = setup?.GetValue("SolidWorks Folder") as string;
                            if (string.IsNullOrWhiteSpace(folder)) continue;
                            var exe = Path.Combine(folder.TrimEnd('\\'), "SLDWORKS.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        /// <summary>Starts SolidWorks if an executable path was resolved.</summary>
        public static bool TryStartSolidWorks(out string error)
        {
            error = null;
            var exe = TryFindSolidWorksExe();
            if (string.IsNullOrEmpty(exe))
            {
                error = "Could not find SolidWorks (SLDWORKS.exe). Install SolidWorks or repair the installation.";
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Waits until <paramref name="predicate"/> returns true or timeout elapses.</summary>
        public static void WaitWhile(Func<bool> predicate, int timeoutMs, int sliceMs)
        {
            var deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                if (!predicate()) return;
                Thread.Sleep(sliceMs);
            }
        }
    }
}
