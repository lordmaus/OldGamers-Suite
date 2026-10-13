using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TimerStudio
{
    /// <summary>
    /// Lightweight rotating file logger.
    /// Path:     %APPDATA%\TimerStudio\diagnostics.log
    /// Rotation: when the file exceeds 512 KB it is renamed to diagnostics.old
    ///           and a fresh log is started.
    /// Thread-safety: all writes serialised via a static lock.
    /// </summary>
    public static class DiagLog
    {
        private static readonly object _lock   = new object();
        private const           long   MaxBytes = 512 * 1024;   // 512 KB

        // ── Log path ──────────────────────────────────────────────────────────

        private static string LogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TimerStudio",
                "diagnostics.log");

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Writes a plain timestamped message.</summary>
        public static void Write(string message)
        {
            string line = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message;
            Debug.WriteLine("[TimerStudio] " + message);
            try
            {
                lock (_lock)
                {
                    string path = LogPath;
                    EnsureDir(path);
                    Rotate(path);
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* never let logging crash the app */ }
        }

        // S-1 (2026-09-14 review): SMTP/MimeKit/Outlook-COM/MSAL exceptions routinely
        // embed the actual rejected recipient address in ex.Message (e.g. "550 5.1.1
        // user@domain.com: recipient rejected") — the literal to= field at each email
        // call site was already redacted, but the raw exception text was a second,
        // unredacted channel for the same PII. This regex catches that shape wherever
        // it appears in the message chain.
        private static readonly Regex EmailPattern =
            new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

        private static string RedactEmails(string text) =>
            string.IsNullOrEmpty(text) ? text : EmailPattern.Replace(text, "<redacted>");

        /// <summary>
        /// Writes an exception with a context label, full message, inner exception (if any),
        /// and the first stack-trace frame for quick location of the problem.
        /// </summary>
        /// <param name="redactEmails">
        /// S-1: pass true from any call site whose exception may originate from an
        /// email send (SMTP/MailKit/Outlook-COM/MSAL) — scrubs email-shaped substrings
        /// from the exception message chain before writing to disk.
        /// </param>
        public static void WriteException(string context, Exception ex, bool redactEmails = false)
        {
            if (ex == null)   { Write(context + "  (null exception)"); return; }
            var sb = new StringBuilder();
            string msg = redactEmails ? RedactEmails(ex.Message) : ex.Message;
            sb.Append(context).Append("  ")
              .Append(ex.GetType().Name).Append(": ").Append(msg);

            // Inner exception chain (one level)
            Exception inner = ex.InnerException;
            while (inner != null)
            {
                string innerMsg = redactEmails ? RedactEmails(inner.Message) : inner.Message;
                sb.Append("  →  ").Append(inner.GetType().Name).Append(": ").Append(innerMsg);
                inner = inner.InnerException;
            }

            // First stack frame for file/line context
            string stack = ex.StackTrace ?? "";
            int nl = stack.IndexOf('\n');
            string frame = (nl > 0 ? stack.Substring(0, nl) : stack).Trim();
            if (!string.IsNullOrEmpty(frame))
                sb.Append("  @ ").Append(frame);

            Write(sb.ToString());
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void EnsureDir(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static void Rotate(string path)
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length < MaxBytes) return;
            string old = Path.ChangeExtension(path, ".old");
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            try { File.Move(path, old); }                   catch { }
        }
    }
}
