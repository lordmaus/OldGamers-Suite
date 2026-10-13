using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OldGamers.Wpf.Shared
{
    /// <summary>
    /// Second-launch activation (S-10, 2026-10-13). The first instance <see cref="Listen"/>s; a second
    /// launch calls <see cref="SignalExisting"/> and exits instead of showing "Already running".
    /// The running app restores its window from tray/minimised and takes focus.
    ///
    /// Pipe is scoped to Windows session + user SID (like the Local\ mutexes) and created with
    /// <see cref="PipeOptions.CurrentUserOnly"/>; payload capped at 64 bytes, 2 s read timeout,
    /// 1 s back-off after an error so a persistent failure can't spin.
    /// </summary>
    public static class InstanceActivation
    {
        private const string Message = "activate";
        private static CancellationTokenSource _cts;

        public static string PipeName(string appId) =>
            $"OldGamers.{appId}.Activate.{Process.GetCurrentProcess().SessionId}." +
            (WindowsIdentity.GetCurrent().User?.Value ?? "user");

        /// <summary>Second instance: ask the running one to come forward. True when it was reached.</summary>
        public static bool SignalExisting(string appId, int timeoutMs = 2000)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName(appId), PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(timeoutMs);
                var bytes = Encoding.ASCII.GetBytes(Message);
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>First instance: listen for activation requests; <paramref name="onActivate"/> runs on the UI thread.</summary>
        public static void Listen(string appId, Action onActivate)
        {
            if (_cts != null || onActivate == null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var dispatcher = Application.Current?.Dispatcher;
            string name = PipeName(appId);
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                                                                     PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        readCts.CancelAfter(TimeSpan.FromSeconds(2));
                        var buf = new byte[64];
                        int n = await server.ReadAsync(buf.AsMemory(0, buf.Length), readCts.Token).ConfigureAwait(false);
                        if (Encoding.ASCII.GetString(buf, 0, n) == Message)
                            dispatcher?.BeginInvoke(onActivate);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception)
                    {
                        try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { return; }
                    }
                }
            });
        }

        public static void Stop()
        {
            try { _cts?.Cancel(); } catch { }
        }

        /// <summary>Restore + focus a window (from tray, minimised or behind other windows).</summary>
        public static void BringToFront(Window window)
        {
            if (window == null) return;
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            bool top = window.Topmost;
            window.Topmost = true;
            window.Topmost = top;
            window.Focus();
        }
    }
}
