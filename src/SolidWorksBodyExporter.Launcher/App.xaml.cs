using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;
using SolidWorksBodyExporter.AddIn.Services;

namespace SolidWorksBodyExporter.Launcher
{
    /// <summary>
    /// Standalone Body Exporter launcher. Connects to the in-process add-in through a named
    /// pipe. If SolidWorks is not running, offers to start it and waits for the add-in pipe
    /// instead of surfacing a hard error.
    /// </summary>
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "Local\\SolidWorksBodyExporter.Launcher.{D61E8EAA-B7F1-4EE3-8B8A-9D6C673A7E1F}";

        private const int PipeConnectTimeoutMs = 2000;
        private const int ResponseReadTimeoutMs = 10000;
        private const int WaitForSwAfterStartMs = 45000;

        private Mutex _singleInstanceMutex;
        private bool _ownsMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DiagnosticLog.SetSourcePrefix("[Launcher]");

            try
            {
                _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out _ownsMutex);
                if (!_ownsMutex)
                {
                    DiagnosticLog.Info("Launcher: another instance already running, exiting silently");
                    Shutdown(0);
                    return;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("Launcher: mutex acquisition failed (proceeding anyway): " + ex.Message);
            }

            DispatcherUnhandledException += (s, args) =>
            {
                DiagnosticLog.Error("Launcher: dispatcher unhandled exception", args.Exception);
                MessageBox.Show(args.Exception.ToString(), "Body Exporter - unexpected error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            DiagnosticLog.Info("Launcher: starting, version=" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

            try
            {
                SendOpenRequestWithOptionalSwStart();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("Launcher: fatal error during open request", ex);
                MessageBox.Show("Body Exporter could not start:" + Environment.NewLine + Environment.NewLine + ex.Message,
                    "Body Exporter", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Shutdown(0);
            }
        }

        /// <summary>
        /// Tries pipe OPEN once; on failure prompts to start SolidWorks and polls the pipe until
        /// the add-in comes online or a generous timeout expires.
        /// </summary>
        private static void SendOpenRequestWithOptionalSwStart()
        {
            if (TrySendOpenOnce(out var responseDetail))
            {
                return;
            }

            var offer = MessageBox.Show(
                "SolidWorks is not running yet, or the Body Exporter add-in has not finished loading.\n\n" +
                "Would you like to start SolidWorks now and keep waiting for the add-in?\n\n" +
                "Choose No to cancel.",
                "Body Exporter",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (offer != MessageBoxResult.Yes)
            {
                return;
            }

            if (!SolidWorksStarter.TryStartSolidWorks(out var startErr))
            {
                MessageBox.Show(startErr ?? "Could not start SolidWorks.",
                    "Body Exporter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // SolidWorks + add-in registration can take 15–40 s on a cold machine.
            var deadline = Environment.TickCount + WaitForSwAfterStartMs;
            while (Environment.TickCount < deadline)
            {
                if (TrySendOpenOnce(out _))
                {
                    return;
                }
                Thread.Sleep(400);
            }

            MessageBox.Show(
                "Still could not reach the Body Exporter add-in.\n\n" +
                "Please open SolidWorks, enable Tools → Add-Ins → SolidWorks Body Exporter, wait until it loads, then run this launcher again.\n\n" +
                (string.IsNullOrEmpty(responseDetail) ? string.Empty : "Last detail: " + responseDetail),
                "Body Exporter",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>Returns true when the OPEN handshake completed with OK.</summary>
        private static bool TrySendOpenOnce(out string detail)
        {
            detail = null;
            var pipeName = IpcServer.GetPipeName();
            DiagnosticLog.Info("Launcher: connecting to pipe '" + pipeName + "'");

            try
            {
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    try
                    {
                        client.Connect(PipeConnectTimeoutMs);
                    }
                    catch (TimeoutException)
                    {
                        detail = "Pipe connect timed out.";
                        return false;
                    }
                    catch (Exception ex)
                    {
                        detail = ex.Message;
                        return false;
                    }

                    using (var ct = new CancellationTokenSource(ResponseReadTimeoutMs))
                    using (ct.Token.Register(() => { try { client.Dispose(); } catch { /* best effort */ } }))
                    using (var writer = new StreamWriter(client, new UTF8Encoding(false), 256, leaveOpen: true) { NewLine = "\n", AutoFlush = true })
                    using (var reader = new StreamReader(client, new UTF8Encoding(false), false, 256, leaveOpen: true))
                    {
                        writer.WriteLine("OPEN");
                        string response;
                        try
                        {
                            response = reader.ReadLine();
                        }
                        catch (Exception ex)
                        {
                            detail = ex.Message;
                            return false;
                        }

                        DiagnosticLog.Info("Launcher: response = " + (response ?? "<null>"));

                        if (string.IsNullOrEmpty(response))
                        {
                            detail = "Empty response from add-in.";
                            return false;
                        }

                        if (response.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (response.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
                        {
                            detail = response.Length > 4 ? response.Substring(4).Trim() : "(no detail)";
                            MessageBox.Show(
                                "Body Exporter could not open the window:\n\n" + detail,
                                "Body Exporter",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return true; // Pipe reached add-in; stop retry loop
                        }

                        detail = response;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_ownsMutex && _singleInstanceMutex != null)
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                _singleInstanceMutex?.Dispose();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("Launcher: mutex release failed: " + ex.Message);
            }
            base.OnExit(e);
        }
    }
}
