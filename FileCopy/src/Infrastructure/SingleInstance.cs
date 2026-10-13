using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    public sealed record InstanceArgs(string? SourcePath, string? DestPath, bool AutoScan, string? JobPath = null);

    [JsonSerializable(typeof(InstanceArgs))]
    internal partial class InstanceArgsContext : JsonSerializerContext { }

    /// <summary>
    /// Ensures only one running FileCopy. Subsequent launches forward their CLI
    /// args to the running instance via a named pipe and exit immediately.
    /// </summary>
    public static class SingleInstance
    {
        private static readonly Microsoft.Extensions.Logging.ILogger _log =
            AppLogger.ForCategory("SingleInstance");
        // Pin per-machine; Local\\ scope = per-session (one per RDP user etc.)
        private const string MutexName = "Local\\FileCopyStudio.Singleton.{B7E1-4C3A}";
        // FC-04: pipe names are machine-global — scope to session + user like the Local\ mutex,
        // so a second RDP / fast-user-switch session gets its own pipe instead of failing forever.
        private static readonly string PipeName =
            $"FileCopyStudio.Singleton.{{B7E1-4C3A}}.{System.Diagnostics.Process.GetCurrentProcess().SessionId}." +
            (WindowsIdentity.GetCurrent().User?.Value ?? "user");
        private const int MaxPayloadBytes = 64 * 1024;

        private static Mutex _mutex;
        private static CancellationTokenSource _serverCts;

        public static event Action<InstanceArgs>? SecondaryLaunched;

        /// <summary>
        /// Returns true if this is the first instance. False = args were forwarded
        /// and the caller should shut down without showing a window.
        /// </summary>
        public static bool TryAcquire(InstanceArgs args)
        {
            // Portable mode: each portable instance runs independently — no mutex needed
            if (PortableMode.Active) return true;

            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (createdNew)
            {
                StartServer();
                return true;
            }

            try { ForwardSync(args); }
            catch (Exception ex) { _log.LogWarning(ex, "Forward failed"); }
            return false;
        }

        public static void Release()
        {
            try { _serverCts?.Cancel(); } catch { }
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
        }

        private static void ForwardSync(InstanceArgs args)
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(5000); // 5 s — accommodates cold-start on slow machines
            string json = JsonSerializer.Serialize(args, InstanceArgsContext.Default.InstanceArgs);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
        }

        private static void StartServer()
        {
            _serverCts = new CancellationTokenSource();
            _ = Task.Run(() => ServerLoop(_serverCts.Token));
        }

        private static async Task ServerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = CreatePipeServer();

                    await server.WaitForConnectionAsync(ct);

                    // Bounded read: 64 KB cap, 5 s timeout — one idle/oversized client can't block forwarding.
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readCts.CancelAfter(TimeSpan.FromSeconds(5));
                    var buf = new byte[MaxPayloadBytes + 1];
                    int total = 0, n;
                    while ((n = await server.ReadAsync(buf.AsMemory(total), readCts.Token)) > 0)
                    {
                        total += n;
                        if (total > MaxPayloadBytes) break;
                    }
                    if (total > MaxPayloadBytes) { _log.LogWarning("Forwarded args rejected: over {Max} bytes", MaxPayloadBytes); continue; }
                    string json = Encoding.UTF8.GetString(buf, 0, total);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    var args = JsonSerializer.Deserialize(json, InstanceArgsContext.Default.InstanceArgs);
                    if (args == null) continue;

                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        SecondaryLaunched?.Invoke(args);
                    });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Server loop error");
                    // keep listening — back off so a persistent failure can't spin the CPU / flood the log
                    try { await Task.Delay(1000, ct); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        /// <summary>
        /// Creates a NamedPipeServerStream with an ACL that only grants
        /// access to the current user SID (and SYSTEM). This blocks other
        /// users in the same session — and processes running under a
        /// different identity in the same session — from forwarding args.
        /// </summary>
        private static NamedPipeServerStream CreatePipeServer()
        {
            var owner = WindowsIdentity.GetCurrent().User!;
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                owner,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));
            // SYSTEM access is conventional for service interop; harmless here.
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0, outBufferSize: 0,
                pipeSecurity: security);
        }
    }
}
