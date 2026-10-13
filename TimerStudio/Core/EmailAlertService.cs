using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TimerStudio
{
    /// <summary>
    /// Sends email alerts when a timer fires.
    /// Supports two modes: MailKit/SMTP (any email provider) or Outlook COM (no config needed).
    /// Passwords stored via DPAPI (ProtectedData.CurrentUser scope).
    /// GDPR: only logs exception type, never message content or addresses.
    /// </summary>
    public static class EmailAlertService
    {
        // ── Password encryption — DPAPI (H-4) ─────────────────────────────────
        // ProtectedData.CurrentUser ties the secret to the OS user account;
        // no derived key that any process running as the same user can reproduce.

        public static string EncryptPassword(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return "";
            try
            {
                byte[] data      = Encoding.UTF8.GetBytes(plaintext);
                byte[] encrypted = ProtectedData.Protect(data, null,
                                       DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[EmailAlertService] Encrypt error: " + ex.GetType().Name);
                return "";
            }
        }

        public const string PasswordUnreadableMessage =
            "Saved SMTP password can't be read on this PC. Re-enter it in Settings → Email.";

        /// <summary>
        /// TS-04: throws <see cref="CryptographicException"/> (user-facing message) when the
        /// stored secret can't be decrypted — e.g. Windows profile migrated or file copied
        /// from another PC — instead of silently authenticating with an empty password.
        /// </summary>
        public static string DecryptPassword(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return "";
            try
            {
                byte[] data      = Convert.FromBase64String(encrypted);
                byte[] plaintext = ProtectedData.Unprotect(data, null,
                                       DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                DiagLog.Write("EmailAlertService.DecryptPassword failed: " + ex.GetType().Name);
                throw new CryptographicException(PasswordUnreadableMessage, ex);
            }
        }

        public static bool CanDecryptPassword(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return true;
            try { DecryptPassword(encrypted); return true; }
            catch (CryptographicException) { return false; }
        }

        private static readonly char[] RecipientSeparators = { ',', ' ', ';' };

        /// <summary>TS-14: entries in a To: list that aren't usable addresses (empty list = all valid).</summary>
        public static System.Collections.Generic.IReadOnlyList<string> InvalidRecipients(string to)
        {
            var bad = new System.Collections.Generic.List<string>();
            if (string.IsNullOrWhiteSpace(to)) return bad;
            foreach (var raw in to.Split(RecipientSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                string addr = raw.Trim();
                if (!TryParseRecipient(addr, out _)) bad.Add(addr);
            }
            return bad;
        }

        private static bool TryParseRecipient(string addr, out MimeKit.MailboxAddress mailbox)
        {
            mailbox = null;
            if (!MimeKit.MailboxAddress.TryParse(addr, out var mb)) return false;
            if (string.IsNullOrEmpty(mb.Domain) || mb.Domain.IndexOf('.') <= 0 || mb.Domain.EndsWith('.')) return false;
            mailbox = mb;
            return true;
        }

        private static void AddRecipients(MimeKit.MimeMessage message, string to)
        {
            int skipped = 0;
            foreach (var raw in to.Split(RecipientSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseRecipient(raw.Trim(), out var mb)) message.To.Add(mb);
                else skipped++;
            }
            if (skipped > 0)
                DiagLog.Write($"EmailAlertService: skipped {skipped} invalid recipient(s)");
            if (message.To.Count == 0)
                throw new InvalidOperationException("No valid recipient address. Check \"Send to\" in Settings → Email.");
        }

        // ── Send alert ─────────────────────────────────────────────────────────

        /// <summary>
        /// Sends an email alert for a fired timer. Fire-and-forget — exceptions are logged only.
        /// </summary>
        /// <param name="emailTo">
        /// Override recipient(s), comma-separated. Pass null or empty to use
        /// <see cref="AppPrefs.EmailAlertTo"/> from global settings.
        /// </param>
        public static void SendAlertAsync(string timerName, DateTime firedAt, string emailTo = "")
        {
            var p = AppPrefs.Instance;
            if (!p.EmailAlertEnabled || p.EmailAlertMode == EmailMode.Disabled) return;

            string to = string.IsNullOrWhiteSpace(emailTo) ? p.EmailAlertTo : emailTo.Trim();
            string subject = "⏱ Timer Alert: " + timerName;
            string body    = "Your timer \"" + timerName + "\" fired at "
                           + firedAt.ToString("ddd, MMM d yyyy  h:mm:ss tt") + ".\n\n"
                           + "— Timer Studio";

            Dispatch(p, subject, body, to, "SendAlert");
        }

        // TS-07: every fire-and-forget path gets a fault continuation so failures land
        // in diagnostics.log instead of surfacing as UnobservedTaskException.
        // C-5: RunOnStaThread calls t.Join() — must not run on the UI thread.
        private static void Dispatch(AppPrefs p, string subject, string body, string to, string context)
        {
            Task send = p.EmailAlertMode switch
            {
                EmailMode.MailKit        => Task.Run(() => SendViaMailKit(subject, body, p, to)),
                EmailMode.Outlook        => Task.Run(() => RunOnStaThread(() => SendViaOutlook(subject, body, p, to))),
                EmailMode.MicrosoftOAuth => Task.Run(() => SendViaMicrosoftOAuthAsync(subject, body, p, to)),
                _                  => null,
            };
            send?.ContinueWith(t => DiagLog.WriteException("EmailAlertService." + context,
                    t.Exception!.GetBaseException(), redactEmails: true),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Sends a reminder email for a repeat-reminder that just fired.
        /// Fire-and-forget — exceptions logged only.
        /// </summary>
        /// <param name="emailTo">
        /// Override recipient(s), comma-separated. Pass null or empty to use
        /// <see cref="AppPrefs.EmailAlertTo"/> from global settings.
        /// </param>
        public static void SendReminderAlertAsync(string timerName, TimeSpan remaining,
            string notes = "", string emailTo = "")
        {
            var p = AppPrefs.Instance;
            if (!p.EmailAlertEnabled || p.EmailAlertMode == EmailMode.Disabled) return;

            string to       = string.IsNullOrWhiteSpace(emailTo) ? p.EmailAlertTo : emailTo.Trim();
            string timeLeft = FormatRemaining(remaining);
            string subject  = "⏰ Reminder: " + timerName + " — " + timeLeft + " remaining";
            string body     = "Reminder for timer \"" + timerName + "\".\n\n"
                            + "Time remaining: " + timeLeft + "\n\n"
                            + (string.IsNullOrWhiteSpace(notes) ? "" : "Notes:\n" + notes.Trim() + "\n\n")
                            + "— Timer Studio";

            Dispatch(p, subject, body, to, "SendReminderAlert");
        }

        private static string FormatRemaining(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            int h = (int)ts.TotalHours;
            if (h >= 24)
                return $"{(int)ts.TotalDays}d {ts.Hours:D2}h {ts.Minutes:D2}m";
            if (h > 0)
                return $"{h}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
            return ts.Minutes > 0
                ? $"{ts.Minutes}m {ts.Seconds:D2}s"
                : $"{ts.Seconds}s";
        }

        /// <summary>
        /// Sends a test email — awaitable, returns null on success or an error string.
        /// </summary>
        public static async Task<string> SendTestAsync(AppPrefs p)
        {
            const string subject = "Timer Studio — Test Email";
            const string body    = "This is a test email from Timer Studio.\n\nIf you received this, your email alert settings are working correctly.";
            try
            {
                if (p.EmailAlertMode == EmailMode.MailKit)
                    await Task.Run(() => SendViaMailKit(subject, body, p));
                else if (p.EmailAlertMode == EmailMode.Outlook)
                    await RunOnStaThreadAsync(() => SendViaOutlook(subject, body, p));
                else if (p.EmailAlertMode == EmailMode.MicrosoftOAuth)
                    await SendViaMicrosoftOAuthAsync(subject, body, p);
                else
                    return "Email alerts are disabled.";
                return null; // success
            }
            catch (Exception ex)
            {
                return ex.Message; // safe to show to user — it's their own settings error
            }
        }

        // ── MailKit / SMTP ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the username/host looks like a personal Microsoft account
        /// (hotmail.com, outlook.com, live.com, msn.com).
        /// </summary>
        private static bool IsPersonalMicrosoftAccount(AppPrefs p)
        {
            string addr = (p.SmtpUser ?? p.EmailAlertFrom ?? "").ToLowerInvariant();
            return addr.EndsWith("@hotmail.com")  || addr.EndsWith("@outlook.com") ||
                   addr.EndsWith("@live.com")     || addr.EndsWith("@msn.com")     ||
                   (p.SmtpHost ?? "").Equals("smtp.office365.com",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// TS-03: Auto picks implicit TLS for 465 and STARTTLS for everything else.
        /// Plaintext is refused whenever credentials would be sent.
        /// </summary>
        internal static MailKit.Security.SecureSocketOptions ResolveSecureOptions(
            int port, SmtpSecurity mode, bool authenticating) => mode switch
        {
            SmtpSecurity.SslTls   => MailKit.Security.SecureSocketOptions.SslOnConnect,
            SmtpSecurity.StartTls => MailKit.Security.SecureSocketOptions.StartTls,
            SmtpSecurity.None when authenticating => throw new InvalidOperationException(
                "Encryption is required when signing in. Set Security to Automatic, SSL/TLS or STARTTLS."),
            SmtpSecurity.None     => MailKit.Security.SecureSocketOptions.None,
            _ => port == 465
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTls,
        };

        private static void SendViaMailKit(string subject, string body, AppPrefs p, string to = "")
        {
            if (string.IsNullOrWhiteSpace(to)) to = p.EmailAlertTo;
            try
            {
                var message = new MimeKit.MimeMessage();
                message.From.Add(new MimeKit.MailboxAddress("Timer Studio",
                    string.IsNullOrEmpty(p.EmailAlertFrom) ? p.SmtpUser : p.EmailAlertFrom));
                AddRecipients(message, to);
                message.Subject = subject;
                message.Body    = new MimeKit.TextPart("plain") { Text = body };

                bool authenticating = !string.IsNullOrEmpty(p.SmtpUser);
                var secureOpt = ResolveSecureOptions(p.SmtpPort, p.SmtpSecurity, authenticating);

                using var client = new MailKit.Net.Smtp.SmtpClient();
                client.Connect(p.SmtpHost, p.SmtpPort, secureOpt);

                if (authenticating)
                    client.Authenticate(p.SmtpUser, DecryptPassword(p.SmtpPasswordEncrypted));

                client.Send(message);
                client.Disconnect(true);
                DiagLog.Write("EmailAlertService.MailKit  send OK  to=<redacted>");
            }
            catch (Exception ex)
            {
                DiagLog.WriteException("EmailAlertService.MailKit  to=<redacted>", ex, redactEmails: true);
                string msg = ex.Message ?? "";

                // Microsoft basic-auth block — distinguish business vs personal account
                // so the user knows which server to use and whether App Passwords help.
                if (msg.Contains("5.7.139") || msg.Contains("basic authentication is disabled",
                    StringComparison.OrdinalIgnoreCase))
                {
                    bool personal = IsPersonalMicrosoftAccount(p);
                    string hint = personal
                        ? "For personal @hotmail / @outlook / @live accounts:\n" +
                          "  • SMTP host must be smtp-mail.outlook.com (not smtp.office365.com)\n" +
                          "  • App Password requires 2-step verification enabled at account.live.com/proofs\n" +
                          "  • Easiest: switch to Outlook mode — no SMTP config needed"
                        : "For Office 365 business accounts, the tenant admin has disabled basic auth.\n" +
                          "  • App Passwords do NOT bypass this tenant policy\n" +
                          "  • Easiest: switch to Outlook mode — no SMTP config needed\n" +
                          "  • Alternative: ask your admin to enable SMTP AUTH for your mailbox";
                    throw new InvalidOperationException(
                        "Microsoft rejected authentication — basic SMTP auth is disabled.\n" + hint);
                }
                throw;
            }
        }

        // ── STA thread helpers ─────────────────────────────────────────────────
        // Outlook's COM server is STA. Task.Run uses MTA thread-pool threads, which
        // causes CO_E_SERVER_EXEC_FAILURE (0x80080005) on CreateInstance/GetActiveObject.
        // These helpers spin a dedicated STA thread for all Outlook COM calls.

        private static void RunOnStaThread(Action action)
        {
            Exception caught = null;
            var t = new System.Threading.Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { caught = ex; }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            if (caught != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
        }

        private static Task RunOnStaThreadAsync(Action action)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var t = new System.Threading.Thread(() =>
            {
                try { action(); tcs.SetResult(true); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            return tcs.Task;
        }

        // ── Outlook COM ────────────────────────────────────────────────────────
        // All methods below MUST run on an STA thread (use RunOnStaThread/Async above).

        [DllImport("oleaut32.dll")]
        private static extern int GetActiveObject(
            ref Guid rclsid, IntPtr pvReserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        private static object TryGetRunningOutlook()
        {
            try
            {
                var type = Type.GetTypeFromProgID("Outlook.Application");
                if (type == null) return null;
                Guid clsid = type.GUID;
                int hr = GetActiveObject(ref clsid, IntPtr.Zero, out object punk);
                return hr == 0 ? punk : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Launches Outlook via the Windows shell (UseShellExecute=true) then polls
        /// GetActiveObject until it appears. Shell-launched processes run at normal
        /// user IL even when the calling process is elevated, bypassing the
        /// CO_E_SERVER_EXEC_FAILURE that Activator.CreateInstance hits across elevation
        /// boundaries. Polls up to <paramref name="timeoutMs"/> ms.
        /// </summary>
        private static object LaunchAndWaitForOutlook(int timeoutMs = 12000)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "outlook.exe",
                    UseShellExecute = true   // runs at normal user IL, not elevated
                });
            }
            catch { }

            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                System.Threading.Thread.Sleep(500);
                elapsed += 500;
                object o = TryGetRunningOutlook();
                if (o != null) return o;
            }
            return null;
        }

        private static void SendViaOutlook(string subject, string body, AppPrefs p, string to = "")
        {
            // Called on STA thread via RunOnStaThread/Async — COM activation works here.
            var type = Type.GetTypeFromProgID("Outlook.Application");
            if (type == null) throw new InvalidOperationException("Outlook is not installed.");

            object running   = TryGetRunningOutlook();
            bool   weStarted = false;
            object app       = running;
            if (app == null)
            {
                // Activator.CreateInstance fails with CO_E_SERVER_EXEC_FAILURE when there
                // is a process-elevation mismatch (e.g. app elevated, Outlook not).
                // Use Process.Start (shell-launch at normal IL) + GetActiveObject poll instead.
                app       = LaunchAndWaitForOutlook();
                weStarted = app != null;
            }
            if (app == null)
                throw new InvalidOperationException(
                    "Could not connect to Outlook. Please open Outlook and try again.");
            if (string.IsNullOrWhiteSpace(to)) to = p.EmailAlertTo;
            object mail      = null;
            try
            {
                mail = ((dynamic)app).CreateItem(0); // olMailItem = 0
                dynamic dynMail = (dynamic)mail;
                dynMail.Subject = subject;
                dynMail.Body    = body;
                if (!string.IsNullOrEmpty(to))
                    dynMail.To = to;
                dynMail.Send();
                DiagLog.Write("EmailAlertService.Outlook  send OK  to=<redacted>");
            }
            catch (Exception ex)
            {
                DiagLog.WriteException("EmailAlertService.Outlook  to=<redacted>", ex, redactEmails: true);
                throw;
            }
            finally
            {
                if (mail != null) try { Marshal.ReleaseComObject(mail); } catch { }
                if (app  != null)
                {
                    if (weStarted) try { ((dynamic)app).Quit(); } catch { }
                    try { Marshal.ReleaseComObject(app); } catch { }
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
        }

        // ── Microsoft OAuth2 / XOAUTH2 SMTP ───────────────────────────────────

        /// <summary>
        /// Sends via smtp-mail.outlook.com using XOAUTH2 (Modern Authentication).
        /// Works for personal @hotmail / @outlook / @live accounts.
        /// Requires the user to have signed in via MicrosoftOAuthSmtp.SignInAsync() at least once.
        /// </summary>
        private static async Task SendViaMicrosoftOAuthAsync(string subject, string body, AppPrefs p, string to = "")
        {
            if (string.IsNullOrWhiteSpace(to)) to = p.EmailAlertTo;
            try
            {
                var (token, email) = await MicrosoftOAuthSmtp.GetTokenSilentAsync();

                var message = new MimeKit.MimeMessage();
                message.From.Add(new MimeKit.MailboxAddress("Timer Studio", email));
                AddRecipients(message, to);
                message.Subject = subject;
                message.Body    = new MimeKit.TextPart("plain") { Text = body };

                using var client = new MailKit.Net.Smtp.SmtpClient();
                await client.ConnectAsync("smtp-mail.outlook.com", 587,
                    MailKit.Security.SecureSocketOptions.StartTls);

                // XOAUTH2 — Modern Auth — no basic auth involved
                var oauth2 = new MailKit.Security.SaslMechanismOAuth2(email, token);
                await client.AuthenticateAsync(oauth2);

                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                DiagLog.Write("EmailAlertService.OAuth2  send OK  to=<redacted>");
            }
            catch (Exception ex)
            {
                DiagLog.WriteException("EmailAlertService.OAuth2  to=<redacted>", ex, redactEmails: true);
                throw;
            }
        }
    }
}
