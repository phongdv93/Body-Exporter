using System;
using System.Collections.Generic;
using System.Reflection;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// In-memory ledger of every SolidWorks body name that has been observed alive by the
    /// scanner during the current SolidWorks process. The set is reset implicitly each time the
    /// add-in assembly is loaded fresh (i.e. when SolidWorks starts), and is intentionally NOT
    /// persisted to disk.
    /// <para>
    /// Motivation: the on-disk metadata custom property accumulates entries for every body that
    /// ever existed in the part. When the user later opens the part in a fresh SolidWorks
    /// session, the scanner finds those orphan names in the metadata but not in the current
    /// body list, and (previously) emitted them as "Deleted" rows. Users found this confusing
    /// because the orphans were typically from operations they had since undone or from much
    /// earlier sessions. By only treating an absent name as "Deleted" when we previously saw it
    /// ALIVE within the same SolidWorks process, we keep the deleted-row warning useful (it
    /// flags a body that was just removed while the WPF window is open or shortly afterward)
    /// without polluting the table with historical noise.
    /// </para>
    /// </summary>
    [Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public static class SessionBodyTracker
    {
        private static readonly object _lock = new object();
        private static readonly HashSet<string> _seenAlive =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Records that a body with the given SolidWorks name was alive during a scan
        /// in this process. Safe to call repeatedly; subsequent calls for the same name are no-ops.</summary>
        public static void MarkAlive(string solidWorksBodyName)
        {
            if (string.IsNullOrWhiteSpace(solidWorksBodyName))
            {
                return;
            }

            lock (_lock)
            {
                _seenAlive.Add(solidWorksBodyName);
            }
        }

        /// <summary>Returns true if <paramref name="solidWorksBodyName"/> has been marked alive
        /// at least once in this process. Returning false for an absent name means "we have no
        /// evidence the body ever existed this session" and the scanner should silently prune it
        /// instead of surfacing a Deleted row.</summary>
        public static bool WasEverAlive(string solidWorksBodyName)
        {
            if (string.IsNullOrWhiteSpace(solidWorksBodyName))
            {
                return false;
            }

            lock (_lock)
            {
                return _seenAlive.Contains(solidWorksBodyName);
            }
        }
    }
}
