using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    internal static class Win32Native
    {
        public const int SwRestore = 9;

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void TryFocusSolidWorks(ISldWorks swApp)
        {
            if (swApp == null)
            {
                return;
            }

            try
            {
                var frame = swApp.Frame() as Frame;
                if (frame == null)
                {
                    return;
                }

                var hwnd = new IntPtr(frame.GetHWnd());
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                ShowWindow(hwnd, SwRestore);
                SetForegroundWindow(hwnd);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("TryFocusSolidWorks: " + ex.Message);
            }
        }
    }
}
