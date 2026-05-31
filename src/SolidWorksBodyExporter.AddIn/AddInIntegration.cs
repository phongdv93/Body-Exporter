using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SolidWorksBodyExporter.AddIn.Services;
using SolidWorksBodyExporter.AddIn.Services.Api;
using SolidWorksBodyExporter.AddIn.Ui;

namespace SolidWorksBodyExporter.AddIn
{
    // SolidWorks looks this type up by ProgId at runtime; if any obfuscator renamed the class
    // name, the public method names, or the COM attributes, SolidWorks would silently refuse to
    // load the add-in. Exclude=true on the type AND ApplyToMembers=true on the assembly is the
    // belt-and-braces guard that keeps Obfuscar / ConfuserEx hands off everything inside.
    [ComVisible(true)]
    [Guid(AddInGuid)]
    [ProgId("SolidWorksBodyExporter.AddIn")]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class AddInIntegration : ISwAddin
    {
        public const string AddInGuid = "D61E8EAA-B7F1-4EE3-8B8A-9D6C673A7E1F";

        /// <summary>
        /// Every UserID that has EVER shipped with this add-in. SolidWorks keeps cached toolbar XML
        /// and callback bindings for any UserID it has seen, which surfaces as orphan floating
        /// toolbars (broken <c>%1 has been moved to %2</c> tooltip), zombie ribbon buttons that
        /// look enabled but silently swallow clicks, etc. We force-remove every legacy id on each
        /// connect so the stale UI never reappears, regardless of which intermediate build first
        /// caused the rogue cache entry.
        /// </summary>
        private static readonly int[] LegacyCommandGroupUserIds =
        {
            1001,                       // Original hard-coded ID from the MVP scaffold.
            unchecked((int)0xD61E8EAA), // First GUID-derived ID that started caching bad bindings.
            unchecked((int)0xD61E8EAC), // v0.5.x CommandGroup ID. Now legacy because v0.6+ does
                                        // not register a ribbon entry at all (the launcher is the
                                        // sole entry point), but the ID still has cached toolbar
                                        // bindings in the registry of any user who installed v0.5.x.
        };

        // Hold a strong reference to the WPF Application we create so it is not collected between
        // invocations. Without this, garbage collection of the Application can dispose its Dispatcher
        // and any subsequent Show from the same callback path will fail, which SolidWorks
        // interprets as an add-in fault and disables the button.
        private static Application _wpfApplication;

        /// <summary>
        /// Reference to the currently visible Body Exporter window, if any. We show the window
        /// modelessly so the SolidWorks UI thread is never blocked, which avoids two related
        /// problems: (a) SolidWorks 2024 greys out the ribbon button after the first modal dialog
        /// returns, and (b) the previous RecreateCommandGroup workaround for that greying leaked
        /// internal SolidWorks state and crashed the process (<c>sldappu</c> fault module) after
        /// a few open/close cycles. The trade-off is that the user can re-trigger the command
        /// while the window is already up; we handle that by bringing the existing window to the
        /// foreground rather than spawning a duplicate.
        /// </summary>
        private static BodyExportWindow _currentWindow;

        private SldWorks _solidWorks;
        private int _addInCookie;

        /// <summary>
        /// Captured at <see cref="ConnectToSW"/> time so the IPC server thread can marshal the
        /// "OPEN" command back to the WPF / SolidWorks UI thread. The IPC pipe is serviced on a
        /// ThreadPool worker, but constructing the <see cref="BodyExportWindow"/> must happen on
        /// the same thread that owns the WPF <see cref="Application"/> dispatcher, otherwise WPF
        /// throws <c>InvalidOperationException("The calling thread cannot access this object
        /// because a different thread owns it")</c>.
        /// </summary>
        private static System.Windows.Threading.Dispatcher _swMainDispatcher;

        /// <summary>
        /// Named pipe server hosted in-process for the standalone launcher executable. Lives for
        /// the full lifetime of the SolidWorks process: started in <see cref="ConnectToSW"/>,
        /// stopped in <see cref="DisconnectFromSW"/>. Replaces the v0.5.x grey-icon mitigations
        /// (heartbeat timer, multi-stage ribbon kicks, InvalidateRect/RedrawWindow P/Invoke
        /// chain) that telemetry proved to be ineffective on SolidWorks 2024: the callback was
        /// being polled and was returning enabled, yet the ribbon kept rendering greyed out. The
        /// launcher path sidesteps the broken ribbon entirely.
        /// </summary>
        private static IpcServer _ipcServer;


        public bool ConnectToSW(object thisSw, int cookie)
        {
            _solidWorks = (SldWorks)thisSw;
            _addInCookie = cookie;
            _solidWorks.SetAddinCallbackInfo2(0, this, _addInCookie);

            // Publish ourselves to the static field that the IPC worker thread reads when it
            // marshals an OPEN command back onto the SW main dispatcher. Must happen before
            // StartIpcServer below, otherwise a launcher that connects within the first few
            // milliseconds of the SolidWorks session would get a "still initialising" reply.
            _instance = this;

            DiagnosticLog.SetSourcePrefix("[SW]");
            var asmPath = GetType().Assembly.Location;
            string fileVer = null;
            try
            {
                fileVer = FileVersionInfo.GetVersionInfo(asmPath).FileVersion;
            }
            catch
            {
                // ignore — log path + cookie still useful
            }

            DiagnosticLog.Info(
                "ConnectToSW: cookie=" + cookie + ", asm=" + asmPath + ", fileVer=" + (fileVer ?? "(n/a)"));
            try
            {
                CleanupLegacyCommandManagerState();
            }
            catch (Exception ex)
            {
                // Cleanup failure is not fatal - the IPC pipe path does not depend on a clean
                // CommandManager. Worst case the user still sees a stale grey BE icon left over
                // from v0.5.x, which is exactly the problem the launcher was built to bypass.
                DiagnosticLog.Error("CleanupLegacyCommandManagerState threw (non-fatal)", ex);
            }

            // Telemetry-only event subscriptions. Earlier versions tried to re-attach the
            // CommandTab from these to un-grey the ribbon, but the approach was both ineffective
            // (SW still greyed our icon despite the callback returning 1) and harmful (corrupted
            // command state crashed SW after a few cycles). We keep the subscriptions so the
            // log file still captures document-lifecycle activity for debugging.
            SubscribeToSwEvents();

            // Ensure the WPF Application is alive on this thread BEFORE the IPC server starts.
            // EnsureWpfApplication captures the SolidWorks main-thread dispatcher into
            // _swMainDispatcher; the IPC server's worker thread will use it to marshal the OPEN
            // command back to the UI thread that owns BodyExportWindow.
            EnsureWpfApplication();
            StartIpcServer();

            LicenseManager.Current.EnsureStartupOnlineValidation();

            var installRoot = Path.GetDirectoryName(asmPath);
            TelemetryReporter.TrySendConnectPing(_solidWorks, installRoot);

            return true;
        }

        /// <summary>
        /// Starts the named pipe server that the standalone Body Exporter launcher uses to open
        /// the WPF window from outside the SolidWorks ribbon. Idempotent. Failures are logged
        /// but never thrown - the add-in must still load even on machines where the pipe ACL
        /// cannot be established (extremely locked-down corporate environments).
        /// </summary>
        private static void StartIpcServer()
        {
            try
            {
                if (_ipcServer != null)
                {
                    return;
                }
                var pipeName = IpcServer.GetPipeName();
                _ipcServer = new IpcServer(pipeName, HandleIpcOpenRequest);
                _ipcServer.Start();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("StartIpcServer: launcher pipe could not be started", ex);
                _ipcServer = null;
            }
        }

        private static void StopIpcServer()
        {
            try
            {
                _ipcServer?.Stop();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("StopIpcServer failed: " + ex.Message);
            }
            finally
            {
                _ipcServer = null;
            }
        }

        /// <summary>
        /// Called on the IPC server's background thread when the launcher sends <c>OPEN</c>.
        /// Marshals onto the SolidWorks main-thread dispatcher and triggers the same
        /// <see cref="ShowBodyExporter"/> path the ribbon callback uses, so the launcher and
        /// ribbon share one license / window / scanner pipeline. <see cref="Dispatcher.Invoke"/>
        /// blocks the IPC thread until the call returns, which is what we want here: the
        /// launcher protocol promises one round-trip per OPEN request and the dispatcher returns
        /// quickly because ShowBodyExporter itself defers the heavy work via BeginInvoke.
        /// </summary>
        private static void HandleIpcOpenRequest()
        {
            DiagnosticLog.Info("Ipc: OPEN command received, marshalling to SolidWorks main dispatcher");
            var dispatcher = _swMainDispatcher;
            if (dispatcher == null)
            {
                throw new InvalidOperationException(
                    "Body Exporter is still initialising; please try again in a few seconds.");
            }

            Exception captured = null;
            try
            {
                dispatcher.Invoke(new Action(() =>
                {
                    try
                    {
                        ResolveSingletonInstance()?.ShowBodyExporter();
                    }
                    catch (Exception ex)
                    {
                        captured = ex;
                    }
                }));
            }
            catch (Exception ex)
            {
                captured = ex;
            }

            if (captured != null)
            {
                throw captured;
            }
        }

        /// <summary>
        /// Last-known live <see cref="AddInIntegration"/> instance. SolidWorks always keeps a
        /// single instance per process (the COM activation goes through our registered ProgID),
        /// so caching the most recent <c>this</c> in a static field gives the IPC server a way
        /// to call <see cref="ShowBodyExporter"/> without requiring the IPC worker to also be
        /// an instance method. The reference is cleared in <see cref="DisconnectFromSW"/>.
        /// </summary>
        private static AddInIntegration _instance;
        private static bool _updatePromptedThisSession;

        private static AddInIntegration ResolveSingletonInstance() => _instance;

        public bool DisconnectFromSW()
        {
            DiagnosticLog.Info("DisconnectFromSW: invoked - SolidWorks is unloading the add-in");

            // Stop the launcher IPC server first. A new OPEN command racing in after the rest
            // of the teardown would marshal onto a half-disposed window and crash SolidWorks.
            StopIpcServer();
            UnsubscribeFromSwEvents();

            // Make sure the modeless review window is closed before we tear down the add-in,
            // otherwise SolidWorks unloads our assembly while a WPF window is still alive, which
            // crashes the SolidWorks process the next time the dispatcher tries to pump a message
            // (this matches the "sldappu" fault module the user reported after multiple cycles).
            try
            {
                if (_currentWindow != null)
                {
                    var window = _currentWindow;
                    _currentWindow = null;
                    window.Closed -= OnWindowClosed;
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("DisconnectFromSW: closing window failed: " + ex.Message);
                _currentWindow = null;
            }

            try
            {
                // v0.6.x no longer creates a CommandGroup, but we still run the legacy-ID
                // sweep on disconnect so the next SolidWorks session boots from a clean
                // CommandManager state regardless of whether the previous run was v0.5.x.
                var commandManager = _solidWorks?.GetCommandManager(_addInCookie);
                if (commandManager != null)
                {
                    foreach (var legacyId in LegacyCommandGroupUserIds)
                    {
                        try { commandManager.RemoveCommandGroup(legacyId); }
                        catch { /* SW throws for unknown ids; that's expected */ }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("DisconnectFromSW cleanup failed", ex);
            }

            // Final pass on the way out: nuke ALL Tab# entries that belong to our GUID, with no
            // preserved tab. This is the only safe time to delete the live tab too because
            // SolidWorks is in the middle of unloading us anyway. Without this, Tab# entries
            // accumulate forever (Tab20, Tab21, Tab22, ...) and the first connect of every
            // subsequent SW session starts from a polluted state that triggers the broken
            // "%1 has been moved to %2" tooltip and faded label symptoms.
            PurgeOrphanCommandManagerTabs(preserveTabNames: null);

            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }

            _solidWorks = null;
            return true;
        }

        /// <summary>
        /// Subscribes to <see cref="SldWorks"/> notifications. Currently the handlers only emit
        /// diagnostic log lines - earlier versions tried to re-attach the CommandTab from these
        /// events to "un-grey" the ribbon button, but that approach (a) did not actually fix the
        /// greying and (b) ended up corrupting the CommandGroup state after WPF window close,
        /// producing phantom ribbon entries (CommandID[0]=-1) and crashing SolidWorks when the
        /// user later toggled the add-in entry. Keeping the subscriptions purely for telemetry
        /// makes it possible to verify in the log that SolidWorks is actually firing the events
        /// we expect while leaving the CommandGroup untouched.
        /// </summary>
        private void SubscribeToSwEvents()
        {
            try
            {
                _solidWorks.FileOpenPostNotify         -= OnFileOpenPostNotify;
                _solidWorks.FileOpenPostNotify         += OnFileOpenPostNotify;
                _solidWorks.ActiveModelDocChangeNotify -= OnActiveModelDocChangeNotify;
                _solidWorks.ActiveModelDocChangeNotify += OnActiveModelDocChangeNotify;
                DiagnosticLog.Info("SubscribeToSwEvents: FileOpenPost + ActiveModelDocChange wired (telemetry only)");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("SubscribeToSwEvents failed: " + ex.Message);
            }
        }

        private void UnsubscribeFromSwEvents()
        {
            try
            {
                if (_solidWorks != null)
                {
                    _solidWorks.FileOpenPostNotify         -= OnFileOpenPostNotify;
                    _solidWorks.ActiveModelDocChangeNotify -= OnActiveModelDocChangeNotify;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("UnsubscribeFromSwEvents failed: " + ex.Message);
            }
        }

        private int OnFileOpenPostNotify(string fileName)
        {
            DiagnosticLog.Info("Event FileOpenPostNotify: " + fileName);
            ScheduleDocumentListChangedNotification();
            return 0;
        }

        private int OnActiveModelDocChangeNotify()
        {
            DiagnosticLog.Info("Event ActiveModelDocChangeNotify");
            ScheduleDocumentListChangedNotification();
            return 0;
        }

        /// <summary>
        /// Marshals to the SolidWorks / WPF UI thread so <see cref="BodyExportWindow"/> can
        /// refresh its part list when the user opens a part from outside the add-in (File Open,
        /// recent files, etc.).
        /// </summary>
        private static void ScheduleDocumentListChangedNotification()
        {
            try
            {
                var d = _swMainDispatcher;
                if (d == null || d.HasShutdownStarted)
                {
                    SolidWorksDocumentEvents.RaiseDocumentsMayHaveChanged();
                    return;
                }

                d.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(SolidWorksDocumentEvents.RaiseDocumentsMayHaveChanged));
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("ScheduleDocumentListChangedNotification: " + ex.Message);
            }
        }

        /// <summary>
        /// SolidWorks ribbon callback. Validates pre-conditions, then defers the heavy work
        /// (constructing the WPF window, scanning bodies, generating thumbnails) to the next
        /// dispatcher pump via <see cref="System.Windows.Threading.Dispatcher.BeginInvoke"/> so
        /// the callback itself returns in microseconds. SolidWorks 2024 disables an add-in
        /// command button whenever the addin's code holds the SolidWorks main thread for "too
        /// long", which previously caused the button to grey out after the first click; deferring
        /// makes SolidWorks see the callback as instantaneous and keeps the button hot.
        /// </summary>
        public int ShowBodyExporter()
        {
            DiagnosticLog.Info("ShowBodyExporter: enter");
            try
            {
                if (_currentWindow != null)
                {
                    try
                    {
                        _currentWindow.Activate();
                        _currentWindow.Focus();
                        DiagnosticLog.Info("ShowBodyExporter: activated existing window");
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Warn("Could not re-activate existing window: " + ex.Message);
                        _currentWindow = null;
                    }
                }

                // First gate: licensing. We allow the window to surface a license dialog itself for
                // trial / expired states (which is friendlier than a modal MessageBox during the
                // SolidWorks command callback), but truly tampered/wrong-machine files are blocked
                // up here so the heavyweight WPF scan never runs in an unlicensed state.
                var license = LicenseManager.Current.GetStatus();
                DiagnosticLog.Info("License gate: source=" + license.Source + ", allowed=" + license.IsAllowed
                                   + ", remaining=" + (license.DaysRemaining?.ToString() ?? "-"));
                if (!license.IsAllowed &&
                    (license.Source == LicenseSource.Tampered || license.Source == LicenseSource.WrongMachine))
                {
                    MessageBox.Show(
                        license.Message + System.Environment.NewLine + System.Environment.NewLine +
                        "Machine fingerprint: " + (license.MachineFingerprint ?? "(unknown)"),
                        "Body Exporter - License blocked",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return 0;
                }

                var activeModel = TryResolveInitialPartDocument();
                if (activeModel == null)
                {
                    DiagnosticLog.Info("ShowBodyExporter: no part open yet; opening Body Exporter empty so user can pick a part");
                }

                EnsureWpfApplication();

                // Defer window construction to the next dispatcher pump. Returning fast keeps
                // SolidWorks 2024 from greying our ribbon button after the modal dialog closes.
                var dispatcher = _wpfApplication?.Dispatcher
                                 ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
                dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => OpenWindowDeferred(activeModel)));
                DiagnosticLog.Info("ShowBodyExporter: deferred Show via dispatcher");
            }
            catch (Exception ex)
            {
                _currentWindow = null;
                DiagnosticLog.Error("ShowBodyExporter top-level", ex);
                try
                {
                    MessageBox.Show(ex.ToString(), "Body Exporter error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception messageEx)
                {
                    DiagnosticLog.Error("Failed to surface MessageBox", messageEx);
                }
            }

            return 0;
        }

        /// <summary>
        /// Prefers the active document when it is a Part; otherwise returns the first open Part in
        /// the session so the window still opens with data when a drawing or assembly has focus.
        /// </summary>
        private ModelDoc2 TryResolveInitialPartDocument()
        {
            try
            {
                var active = _solidWorks?.ActiveDoc as ModelDoc2;
                if (active != null && active.GetType() == (int)swDocumentTypes_e.swDocPART)
                {
                    return active;
                }

                var docs = _solidWorks?.GetDocuments() as object[] ?? Array.Empty<object>();
                foreach (var o in docs)
                {
                    if (o is ModelDoc2 m && m.GetType() == (int)swDocumentTypes_e.swDocPART)
                    {
                        return m;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("TryResolveInitialPartDocument: " + ex.Message);
            }

            return null;
        }

        private void OpenWindowDeferred(ModelDoc2 activeModel)
        {
            try
            {
                // Forward _solidWorks so the window can enumerate every open Part document for
                // the active-part dropdown. The window keeps the reference for the duration of
                // its lifetime; SolidWorks owns the lifecycle of the COM object so we do not
                // need to release it ourselves.
                var window = new BodyExportWindow(_solidWorks, activeModel);
                // Do NOT set WindowInteropHelper.Owner to the SolidWorks HWND. An owned WPF window
                // stacks in the same activation chain as its owner, which many users read as
                // "SolidWorks is locked" while Body Exporter is up. We still want the exporter to
                // float independently so both apps accept input at the same time.
                _currentWindow = window;
                window.Closed += OnWindowClosed;

                DiagnosticLog.Info("OpenWindowDeferred: showing modeless window");
                window.Show();
                try
                {
                    window.WindowState = WindowState.Normal;
                    window.ShowInTaskbar = true;
                    window.Activate();
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Warn("OpenWindowDeferred: Activate/WindowState: " + ex.Message);
                }

                MaybePromptForUpdateOnce(window);
            }
            catch (Exception ex)
            {
                _currentWindow = null;
                DiagnosticLog.Error("OpenWindowDeferred", ex);
                try
                {
                    MessageBox.Show(ex.ToString(), "Body Exporter error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    // Best effort; nothing more we can do here.
                }
            }
        }

        private void MaybePromptForUpdateOnce(Window owner)
        {
            if (_updatePromptedThisSession || owner == null)
            {
                return;
            }

            _updatePromptedThisSession = true;
            try
            {
                var cfg = ClientConfigClient.Load(LicenseManager.DefaultApiBaseUrl, forceRefresh: false);
                UpdateChecker.PromptIfNewerAvailable(owner, cfg);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("MaybePromptForUpdateOnce: " + ex.Message);
            }
        }

        private static void OnWindowClosed(object sender, EventArgs e)
        {
            DiagnosticLog.Info("BodyExportWindow closed");
            if (_currentWindow != null)
            {
                _currentWindow.Closed -= OnWindowClosed;
                _currentWindow = null;
            }

            // v0.5.x used to schedule InvalidateRect / RedrawWindow Win32 calls here to nudge
            // SolidWorks into re-polling IsBodyExporterEnabled, in the belief that the ribbon
            // greyed out because SW stopped polling. Telemetry from v0.5.10 disproved that
            // theory: SW kept polling the callback (~50 Hz) AND the callback kept returning 1
            // AND the ribbon still greyed out. The repaint chain was therefore both ineffective
            // and noisy, so we deleted it. The launcher exe is now the dependable entry point
            // for the WPF window; the ribbon icon is best-effort and outside our control.
        }

        /// <summary>
        /// SolidWorks hosts our WPF code without owning a WPF <see cref="Application"/>. The first
        /// implicit <see cref="Window.ShowDialog"/> spins up a transient dispatcher that disposes when
        /// the dialog closes, which can wedge a subsequent invocation and make SolidWorks mark the
        /// add-in as failed. Creating an explicit application with manual shutdown keeps the
        /// dispatcher alive for the lifetime of the SolidWorks process.
        /// </summary>
        private static void EnsureWpfApplication()
        {
            if (Application.Current == null)
            {
                _wpfApplication = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
            }
            else
            {
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _wpfApplication = Application.Current;
            }

            // Cache the dispatcher of the thread that currently owns the WPF Application. The
            // IPC worker thread reads this field when forwarding launcher OPEN commands. It is
            // safe to overwrite an existing value here: the same SolidWorks main thread owns
            // the dispatcher for the full lifetime of the SW process.
            _swMainDispatcher = _wpfApplication.Dispatcher;

            // SolidWorks disables an add-in whose callback bubbles up an exception. WPF dispatcher
            // exceptions (e.g. from a DataGrid template binding, drag/drop handler, or animation)
            // bypass the try/catch in ShowBodyExporter because they fire asynchronously on the
            // dispatcher pump after ShowDialog has already started. Marking them Handled keeps the
            // dispatcher alive AND prevents SolidWorks from greying out the toolbar.
            _wpfApplication.DispatcherUnhandledException -= OnDispatcherUnhandledException;
            _wpfApplication.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private static void OnDispatcherUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            DiagnosticLog.Error("WPF dispatcher unhandled exception", e.Exception);
            e.Handled = true;
        }

        /// <summary>
        /// Enable callback bound to the Body Exporter CommandItem. ALWAYS returns
        /// <c>swCommandItem_EnabledAndNotChecked</c> (value 1 in the SolidWorks enable bitmask)
        /// so the ribbon button stays active regardless of the active document. Document-type
        /// validation runs at click time inside <see cref="ShowBodyExporter"/> with a friendly
        /// MessageBox - that is a far better UX than a greyed-out button the user can't reason
        /// about.
        /// <para>
        /// Every invocation is logged (no de-duplication) because the "icon greys after the WPF
        /// window closes the Nth time" bug has zero visibility otherwise: the icon flips between
        /// greyed/active and we need to know whether SolidWorks is actually polling this callback
        /// or just using a cached state.
        /// </para>
        /// </summary>
        // The v0.5.x IsBodyExporterEnabled callback was removed in v0.6.x. With no CommandGroup
        // registered, SolidWorks never asks us for the command's enable state, so the method had
        // no callers left. The method shell stayed visible in earlier 0.6.x previews as a
        // forwards-compatibility no-op, but with COM activation going through ProgID-bound late
        // binding there is no benefit to keeping a method nobody can route to.

        [ComRegisterFunction]
        public static void RegisterFunction(Type type)
        {
            using (var addInKey = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\SolidWorks\Addins\{{{AddInGuid}}}"))
            {
                addInKey?.SetValue(null, 1);
                addInKey?.SetValue("Title", "SolidWorks Body Exporter");
                addInKey?.SetValue("Description", "Export and manage SolidWorks part bodies.");
            }

            using (var startupKey = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup"))
            {
                startupKey?.SetValue("{" + AddInGuid + "}", 1, RegistryValueKind.DWord);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type type)
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\SolidWorks\Addins\{{{AddInGuid}}}", false);

            using (var startupKey = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup"))
            {
                startupKey?.DeleteValue("{" + AddInGuid + "}", false);
            }
        }

        /// <summary>
        /// v0.6.x replacement for the old <c>AddCommandManager</c>. Does NOT register a
        /// CommandGroup, an AddCommandItem2 callback binding, or a CommandTab. The launcher
        /// executable is the sole entry point for the WPF window from v0.6+ onward, so any
        /// ribbon UI we registered here would only serve to confuse users with a permanently
        /// greyed-out icon (the SolidWorks 2024 ribbon paint bug we documented in v0.5.x).
        ///
        /// What this method DOES is remove every CommandGroup and Tab# registry entry that any
        /// previous installed version of the add-in left behind. Without that sweep, a user
        /// upgrading from v0.5.x to v0.6.0 would still see the dead grey BE icon in their
        /// CommandManager toolbar dropdown until they manually cleared it.
        /// </summary>
        private void CleanupLegacyCommandManagerState()
        {
            var commandManager = _solidWorks?.GetCommandManager(_addInCookie);
            if (commandManager == null)
            {
                DiagnosticLog.Warn("CleanupLegacyCommandManagerState: GetCommandManager returned null");
                return;
            }

            // Remove every UserID we have ever shipped (current id is now in the legacy array
            // because v0.6+ does not create a fresh CommandGroup). RemoveCommandGroup no-ops for
            // ids SW never knew about, so over-calling is harmless.
            foreach (var legacyId in LegacyCommandGroupUserIds)
            {
                try { commandManager.RemoveCommandGroup(legacyId); }
                catch { /* SW throws for unknown ids; expected */ }
            }

            // Sweep a small UserID window around our historical ID range to catch any
            // intermediate dev-build IDs that escaped our explicit enumeration.
            SweepLegacyCommandGroups(commandManager, unchecked((int)0xD61E8EA0), unchecked((int)0xD61E8EAF));

            // Delete every Tab# subkey in the SolidWorks CommandManager registry that claims
            // our add-in GUID as its ModuleName. This evicts the "Body Exporter" entry from the
            // ribbon's right-click Tabs menu and from the orphan dropdown toolbar slot that
            // v0.5.x users see at the top-left of the SolidWorks window.
            PurgeOrphanCommandManagerTabs(preserveTabNames: null);

            DiagnosticLog.Info("CleanupLegacyCommandManagerState: legacy CommandGroup + Tab entries swept");
        }

        /// <summary>
        /// Calls <see cref="ICommandManager.RemoveCommandGroup"/> for every UserID in the inclusive
        /// range [<paramref name="firstUserId"/>, <paramref name="lastUserId"/>]. SolidWorks happily
        /// no-ops for ids it never registered, so this is safe to over-call. The narrow 16-id
        /// window around our production UserID is the cheapest reliable way to evict orphan
        /// CommandGroups from intermediate dev builds that we forgot to enumerate explicitly.
        /// </summary>
        private static void SweepLegacyCommandGroups(ICommandManager commandManager, int firstUserId, int lastUserId)
        {
            if (commandManager == null)
            {
                return;
            }

            var removed = 0;
            for (var id = firstUserId; id <= lastUserId; id++)
            {
                try
                {
                    commandManager.RemoveCommandGroup(id);
                    removed++;
                }
                catch
                {
                    // SolidWorks throws for ids that were never used by this addin. We don't even
                    // log because the loop hits 16 ids on every connect; only the aggregate count
                    // is useful telemetry.
                }
            }
            DiagnosticLog.Info("SweepLegacyCommandGroups: range=0x" + firstUserId.ToString("X") +
                               "-0x" + lastUserId.ToString("X") + ", removed=" + removed);
        }

        /// <summary>
        /// SolidWorks CommandManager contexts that can host our ribbon tab. SolidWorks creates a
        /// separate Tab# subkey under each context the user has visited, so cleanup MUST walk all
        /// six contexts or the orphan slot persists in whichever context we skipped.
        /// </summary>
        private static readonly string[] CommandManagerContexts =
        {
            "PartContext",
            "AssemblyContext",
            "DrawingContext",
            "PartDoc",
            "AssemblyDoc",
            "DrawingDoc",
        };

        private const string AddInGuidBracketed = "{" + AddInGuid + "}";

        /// <summary>
        /// Deletes <c>Tab#</c> registry entries SolidWorks created for previous instances of our
        /// add-in. The orphan top-left "%1 has been moved to %2" tooltip traces back to these:
        /// every time we rebuild and SolidWorks calls <c>AddCommandTab</c> for "Body Exporter",
        /// SolidWorks allocates a fresh Tab# (Tab20, Tab21, Tab22, ...) entry, BUT
        /// <c>RemoveCommandTab</c> only severs the live link - it does NOT delete the underlying
        /// Tab# subkey. Over many dev builds the registry collects N orphan Tab# entries that all
        /// claim our GUID as <c>ModuleName</c>, and SolidWorks renders the standalone CommandGroup
        /// icon bound to whichever one still has a "Tab Props" record - typically with the broken
        /// callback that produces the moved/replaced tooltip. We only touch Tab# subkeys whose
        /// ModuleName equals our addin GUID, which guarantees we never break a built-in or third-
        /// party tab. The single LIVE Tab# for our current connect cycle is preserved via the
        /// optional <paramref name="preserveTabName"/> parameter.
        /// </summary>
        private static void PurgeOrphanCommandManagerTabs(
            System.Collections.Generic.ICollection<string> preserveTabNames = null)
        {
            try
            {
                var preserve = preserveTabNames == null
                    ? new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new System.Collections.Generic.HashSet<string>(preserveTabNames, StringComparer.OrdinalIgnoreCase);

                using (var swRoot = Registry.CurrentUser.OpenSubKey(@"Software\SolidWorks", writable: false))
                {
                    if (swRoot == null) return;

                    var deleted = 0;
                    foreach (var yearName in swRoot.GetSubKeyNames())
                    {
                        if (yearName == null ||
                            yearName.IndexOf("SOLIDWORKS", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        var cmdMgrPath = yearName + @"\User Interface\CommandManager";
                        using (var cmdMgrKey = swRoot.OpenSubKey(cmdMgrPath, writable: false))
                        {
                            if (cmdMgrKey == null) continue;

                            foreach (var contextName in CommandManagerContexts)
                            {
                                using (var ctxKey = cmdMgrKey.OpenSubKey(contextName, writable: true))
                                {
                                    if (ctxKey == null) continue;
                                    deleted += PurgeOrphanTabsInContext(ctxKey, preserve);
                                }
                            }
                        }
                    }

                    if (deleted > 0)
                    {
                        DiagnosticLog.Info("PurgeOrphanCommandManagerTabs: deleted=" + deleted +
                                           ", preserved=" + (preserve.Count == 0 ? "(none)" : string.Join(",", preserve)));
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("PurgeOrphanCommandManagerTabs failed: " + ex.Message);
            }
        }

        private static int PurgeOrphanTabsInContext(
            RegistryKey ctxKey,
            System.Collections.Generic.HashSet<string> preserveTabNames)
        {
            var toDelete = new System.Collections.Generic.List<string>();
            foreach (var subName in ctxKey.GetSubKeyNames())
            {
                if (subName == null || !subName.StartsWith("Tab", StringComparison.Ordinal)) continue;

                if (preserveTabNames.Contains(subName)) continue;

                using (var tabKey = ctxKey.OpenSubKey(subName, writable: false))
                {
                    if (tabKey == null) continue;
                    var module = tabKey.GetValue("ModuleName") as string;
                    if (!string.Equals(module, AddInGuidBracketed, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                toDelete.Add(subName);
            }

            var deleted = 0;
            foreach (var subName in toDelete)
            {
                try
                {
                    ctxKey.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                    deleted++;
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Warn("PurgeOrphanTab: could not delete " + subName + " - " + ex.Message);
                }
            }
            return deleted;
        }

    }
}
