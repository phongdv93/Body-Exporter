using System;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Raised on the SolidWorks main-thread dispatcher when a document is opened or the active
    /// document changes, so <see cref="Ui.BodyExportWindow"/> can refresh its part picker and
    /// re-bind bodies without requiring the user to click Refresh.
    /// </summary>
    public static class SolidWorksDocumentEvents
    {
        public static event Action DocumentsMayHaveChanged;

        internal static void RaiseDocumentsMayHaveChanged()
        {
            DocumentsMayHaveChanged?.Invoke();
        }
    }
}
