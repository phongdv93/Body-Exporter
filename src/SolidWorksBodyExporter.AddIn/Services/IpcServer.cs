using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Per-user named pipe server hosted inside the SolidWorks process by the Body Exporter
    /// add-in. Exists so the standalone launcher executable can ask the in-process add-in to
    /// open its WPF window without going through the SolidWorks CommandManager ribbon (whose
    /// state machine greys our icon for reasons we have no control over) or through COM
    /// Running-Object-Table lookups (which SolidWorks 2024 silently refuses to honour unless
    /// it was started with the <c>-ole</c> automation switch).
    ///
    /// Protocol is intentionally trivial: one UTF-8 line of input from the client per
    /// connection, one UTF-8 line of response back. Currently only the <c>OPEN</c> command is
    /// supported; the server replies <c>OK</c> if the add-in marshalled the request to its WPF
    /// dispatcher successfully, or <c>ERR &lt;message&gt;</c> on failure.
    ///
    /// Security: the pipe ACL is set so only the current Windows user account can open it.
    /// Different sessions on the same machine (RDP, Fast User Switching) each have their own
    /// pipe name derived from the user SID so two users cannot accidentally cross-launch each
    /// other's add-in.
    /// </summary>
    public sealed class IpcServer : IDisposable
    {
        private readonly string _pipeName;
        private readonly Action _onOpenRequest;
        private CancellationTokenSource _cts;
        private Task _loopTask;
        private int _disposed;

        public IpcServer(string pipeName, Action onOpenRequest)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException("pipeName must be non-empty", nameof(pipeName));
            }
            _pipeName = pipeName;
            _onOpenRequest = onOpenRequest ?? throw new ArgumentNullException(nameof(onOpenRequest));
        }

        /// <summary>
        /// Resolves the pipe name shared between launcher and add-in. Embedding the current
        /// user's SID isolates pipes per Windows session, which matters on Terminal Server,
        /// Fast User Switching, and any shared workstation where two accounts might run
        /// SolidWorks simultaneously. The SID is a stable, OS-canonical identifier so the
        /// launcher and add-in always derive the same string without any extra IPC handshake.
        /// </summary>
        public static string GetPipeName()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var sid = identity?.User?.Value;
                    if (!string.IsNullOrEmpty(sid))
                    {
                        return "SolidWorksBodyExporter." + sid;
                    }
                }
            }
            catch
            {
                // Fall through to the well-known default below.
            }
            return "SolidWorksBodyExporter.default";
        }

        public void Start()
        {
            if (_loopTask != null)
            {
                return;
            }
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => ServeLoopAsync(_cts.Token));
            DiagnosticLog.Info("IpcServer: started, pipe=" + _pipeName);
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            try
            {
                _cts?.Cancel();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("IpcServer.Stop: cancel failed: " + ex.Message);
            }

            try
            {
                // Wake a server that is currently blocked inside WaitForConnectionAsync by
                // connecting to its own pipe as a client. Disposing the server stream alone
                // does NOT reliably unblock the pending wait on .NET Framework 4.8 - the
                // canonical workaround is a one-shot client connection that the loop catches
                // and then notices the cancellation token, falls through, and exits cleanly.
                using (var bump = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.None))
                {
                    bump.Connect(150);
                }
            }
            catch
            {
                // Best effort. If the server already exited or no pipe exists, nothing to nudge.
            }

            try
            {
                _loopTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("IpcServer.Stop: loop join failed: " + ex.Message);
            }

            _loopTask = null;
            _cts?.Dispose();
            _cts = null;
            DiagnosticLog.Info("IpcServer: stopped");
        }

        public void Dispose() => Stop();

        private async Task ServeLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = CreateServerStream();

                    // WaitForConnectionAsync on .NET Framework 4.8 does not honour the
                    // overload taking a CancellationToken on every patch level, so the
                    // canonical-and-portable cancellation pattern is to register a callback
                    // that disposes the stream. Disposal makes WaitForConnectionAsync throw
                    // ObjectDisposedException which we treat as a clean exit signal.
                    using (ct.Register(() => { try { server.Dispose(); } catch { /* best effort */ } }))
                    {
                        try
                        {
                            await server.WaitForConnectionAsync().ConfigureAwait(false);
                        }
                        catch (ObjectDisposedException)
                        {
                            return;
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (IOException ex)
                        {
                            DiagnosticLog.Warn("IpcServer: WaitForConnectionAsync failed: " + ex.Message);
                            await SafeDelayAsync(500, ct).ConfigureAwait(false);
                            continue;
                        }
                    }

                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    HandleClient(server);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    DiagnosticLog.Warn("IpcServer: loop iteration failed: " + ex.Message);
                    await SafeDelayAsync(500, ct).ConfigureAwait(false);
                }
                finally
                {
                    try { server?.Dispose(); } catch { /* best effort */ }
                }
            }
        }

        private static async Task SafeDelayAsync(int milliseconds, CancellationToken ct)
        {
            try { await Task.Delay(milliseconds, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected during shutdown */ }
        }

        private NamedPipeServerStream CreateServerStream()
        {
            // Restrict the pipe to the current user identity. SolidWorks always runs in the
            // user's interactive session at MEDIUM integrity; the launcher is forced to run
            // asInvoker via its manifest. Both therefore share the same SID, and any other
            // user (or any elevated process) is denied by Windows. This is defence-in-depth on
            // top of the per-SID pipe-name scheme already chosen by GetPipeName.
            var security = new PipeSecurity();
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (identity?.User != null)
                {
                    security.AddAccessRule(new PipeAccessRule(
                        identity.User,
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));
                }
            }

            return new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 1024,
                outBufferSize: 1024,
                pipeSecurity: security);
        }

        private void HandleClient(NamedPipeServerStream server)
        {
            try
            {
                using (var reader = new StreamReader(server, new UTF8Encoding(false), false, 256, leaveOpen: true))
                using (var writer = new StreamWriter(server, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true })
                {
                    var line = reader.ReadLine();
                    DiagnosticLog.Info("IpcServer: received command = " + (line ?? "<null>"));

                    if (line == null)
                    {
                        return;
                    }

                    var command = line.Trim();
                    if (command.Equals("OPEN", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            _onOpenRequest();
                            writer.WriteLine("OK");
                        }
                        catch (Exception ex)
                        {
                            var safe = (ex.Message ?? "unknown error").Replace('\r', ' ').Replace('\n', ' ');
                            writer.WriteLine("ERR " + safe);
                            DiagnosticLog.Error("IpcServer: OPEN handler threw", ex);
                        }
                    }
                    else if (command.Equals("PING", StringComparison.OrdinalIgnoreCase))
                    {
                        // Cheap liveness probe the launcher can use to confirm the add-in is
                        // loaded inside SolidWorks before showing UI of its own. Reply is the
                        // assembly version so the launcher can sanity-check version skew.
                        var version = typeof(IpcServer).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
                        writer.WriteLine("PONG " + version);
                    }
                    else
                    {
                        writer.WriteLine("ERR Unknown command: " + command);
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("IpcServer: HandleClient failed: " + ex.Message);
            }
            finally
            {
                try { if (server.IsConnected) server.Disconnect(); } catch { /* best effort */ }
            }
        }
    }
}
