using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace TimerStudio
{
    /// <summary>
    /// TS-10: single source of truth for the Email settings UI shared by
    /// SettingsDialog and CalendarSettingsDialog (via Controls/EmailSettingsPanel).
    /// Every setter persists to <see cref="AppPrefs"/> immediately, matching the
    /// previous LostFocus-save behaviour.
    /// </summary>
    public sealed class EmailSettingsViewModel : INotifyPropertyChanged
    {
        public const string PasswordPlaceholder = "••••••••";

        private static AppPrefs P => AppPrefs.Instance;

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void NotifyMany(params string[] names)
        {
            foreach (var n in names) Notify(n);
        }

        // ── Enable + mode ─────────────────────────────────────────────────────

        public bool EmailEnabled
        {
            get => P.EmailAlertEnabled;
            set { P.EmailAlertEnabled = value; P.Save(); Notify(); }
        }

        /// <summary>Combo index: 0 = MailKit/SMTP, 1 = Outlook, 2 = Microsoft account (OAuth).</summary>
        public int ModeIndex
        {
            get => P.EmailAlertMode switch
            {
                EmailMode.Outlook        => 1,
                EmailMode.MicrosoftOAuth => 2,
                _                                    => 0,
            };
            set
            {
                P.EmailAlertMode = value switch
                {
                    1 => EmailMode.Outlook,
                    2 => EmailMode.MicrosoftOAuth,
                    _ => EmailMode.MailKit,
                };
                P.Save();
                NotifyMany(nameof(ModeIndex), nameof(IsSmtp), nameof(IsOutlook), nameof(IsOAuth), nameof(CanSendTest));
                if (IsOAuth) _ = RefreshOAuthAsync();
            }
        }

        public bool IsSmtp    => ModeIndex == 0;
        public bool IsOutlook => ModeIndex == 1;
        public bool IsOAuth   => ModeIndex == 2;

        // ── Recipient (TS-14) ─────────────────────────────────────────────────

        public string To
        {
            get => P.EmailAlertTo ?? "";
            set
            {
                P.EmailAlertTo = (value ?? "").Trim();
                P.Save();
                NotifyMany(nameof(To), nameof(ToError), nameof(HasToError), nameof(CanSendTest));
            }
        }

        public string ToError
        {
            get
            {
                var bad = EmailAlertService.InvalidRecipients(To);
                return bad.Count == 0 ? null : "Not a valid address: " + string.Join(", ", bad);
            }
        }

        public bool HasToError => ToError != null;

        // ── SMTP fields ───────────────────────────────────────────────────────

        public string From
        {
            get => P.EmailAlertFrom ?? "";
            set { P.EmailAlertFrom = (value ?? "").Trim(); P.Save(); Notify(); }
        }

        public string Host
        {
            get => P.SmtpHost ?? "";
            set { P.SmtpHost = (value ?? "").Trim(); P.Save(); Notify(); }
        }

        /// <summary>Invalid input reverts to the stored port.</summary>
        public string PortText
        {
            get => P.SmtpPort.ToString();
            set
            {
                if (int.TryParse(value, out int port) && port > 0 && port <= 65535)
                {
                    P.SmtpPort = port;
                    P.Save();
                }
                NotifyMany(nameof(PortText), nameof(SecurityHint));
            }
        }

        public string User
        {
            get => P.SmtpUser ?? "";
            set
            {
                P.SmtpUser = (value ?? "").Trim();
                // TS-03: never keep "None" once credentials will be sent.
                if (!NoneAllowed && P.SmtpSecurity == SmtpSecurity.None)
                    P.SmtpSecurity = SmtpSecurity.Auto;
                P.Save();
                NotifyMany(nameof(User), nameof(NoneAllowed), nameof(SecurityIndex), nameof(SecurityHint));
            }
        }

        // ── Security (TS-03) ──────────────────────────────────────────────────

        /// <summary>Combo index == (int)SmtpSecurity: 0 Auto, 1 SSL/TLS, 2 STARTTLS, 3 None.</summary>
        public int SecurityIndex
        {
            get => (int)P.SmtpSecurity;
            set
            {
                var s = Enum.IsDefined(typeof(SmtpSecurity), value) ? (SmtpSecurity)value : SmtpSecurity.Auto;
                if (s == SmtpSecurity.None && !NoneAllowed) s = SmtpSecurity.Auto;
                P.SmtpSecurity = s;
                P.Save();
                NotifyMany(nameof(SecurityIndex), nameof(SecurityHint));
            }
        }

        /// <summary>"None" is only offered for servers that need no sign-in.</summary>
        public bool NoneAllowed => string.IsNullOrEmpty(P.SmtpUser);

        public string SecurityHint
        {
            get
            {
                var opt = EmailAlertService.ResolveSecureOptions(P.SmtpPort, P.SmtpSecurity, authenticating: false);
                return opt switch
                {
                    MailKit.Security.SecureSocketOptions.SslOnConnect => $"Will use SSL/TLS on port {P.SmtpPort}.",
                    MailKit.Security.SecureSocketOptions.StartTls     => $"Will use STARTTLS on port {P.SmtpPort}.",
                    _ => "No encryption — only for servers that don't need sign-in.",
                };
            }
        }

        // ── Password (TS-04) ──────────────────────────────────────────────────

        private bool _passwordUnreadable;

        /// <summary>True when a stored password exists but DPAPI can't decrypt it on this PC/profile.</summary>
        public bool PasswordUnreadable
        {
            get => _passwordUnreadable;
            private set
            {
                if (_passwordUnreadable == value) return;
                _passwordUnreadable = value;
                NotifyMany(nameof(PasswordUnreadable), nameof(CanSendTest));
            }
        }

        public bool HasStoredPassword => !string.IsNullOrEmpty(P.SmtpPasswordEncrypted);

        /// <summary>Encrypts and stores a newly typed password. Placeholder/blank text is ignored.</summary>
        public void SetPassword(string raw)
        {
            if (raw == null || raw == PasswordPlaceholder) return;
            P.SmtpPasswordEncrypted = raw.Length == 0 ? "" : EmailAlertService.EncryptPassword(raw);
            P.Save();
            PasswordUnreadable = false;
        }

        // ── Status / test ─────────────────────────────────────────────────────

        private string _status = "";
        public string Status
        {
            get => _status;
            private set { _status = value ?? ""; Notify(); }
        }

        private bool _busy;
        public bool IsBusy
        {
            get => _busy;
            private set { _busy = value; NotifyMany(nameof(IsBusy), nameof(CanSendTest)); }
        }

        public bool CanSendTest =>
            !IsBusy && !HasToError && !(IsSmtp && PasswordUnreadable);

        public async Task SendTestAsync()
        {
            if (!CanSendTest) return;
            IsBusy = true;
            Status = "Sending…";
            try
            {
                string error = await EmailAlertService.SendTestAsync(P);
                Status = error == null ? "Test email sent." : "Failed: " + error;
                if (error != null && IsSmtp) RefreshPasswordState();
            }
            catch (Exception ex)
            {
                Status = "Failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ── Microsoft OAuth ───────────────────────────────────────────────────

        private string _oauthAccount = "Not signed in";
        public string OAuthAccountText
        {
            get => _oauthAccount;
            private set { _oauthAccount = value; Notify(); }
        }

        private bool _signedIn;
        public bool IsSignedIn
        {
            get => _signedIn;
            private set { _signedIn = value; Notify(); }
        }

        private bool _signInBusy;
        public bool CanSignIn
        {
            get => !_signInBusy;
        }

        public async Task SignInAsync()
        {
            _signInBusy = true; Notify(nameof(CanSignIn));
            OAuthAccountText = "Opening browser…";
            try
            {
                var (_, email) = await MicrosoftOAuthSmtp.SignInAsync();
                OAuthAccountText = "Signed in as: " + email;
                IsSignedIn = true;
            }
            catch (Exception ex)
            {
                OAuthAccountText = "Sign-in failed: " + ex.Message;
            }
            finally
            {
                _signInBusy = false; Notify(nameof(CanSignIn));
            }
        }

        public async Task SignOutAsync()
        {
            try
            {
                await MicrosoftOAuthSmtp.SignOutAsync();
                OAuthAccountText = "Not signed in";
                IsSignedIn = false;
            }
            catch (Exception ex)
            {
                OAuthAccountText = "Sign-out failed: " + ex.Message;
            }
        }

        public async Task RefreshOAuthAsync()
        {
            try
            {
                string account = await MicrosoftOAuthSmtp.GetSignedInAccountAsync();
                IsSignedIn = account != null;
                OAuthAccountText = account != null ? "Signed in as: " + account : "Not signed in";
            }
            catch (Exception ex)
            {
                DiagLog.WriteException("EmailSettingsViewModel.RefreshOAuthAsync", ex, redactEmails: true);
            }
        }

        // ── Load ──────────────────────────────────────────────────────────────

        /// <summary>Re-reads derived state (password readability, OAuth account). Call once when shown.</summary>
        public void Initialize()
        {
            RefreshPasswordState();
            NotifyMany(nameof(EmailEnabled), nameof(ModeIndex), nameof(IsSmtp), nameof(IsOutlook), nameof(IsOAuth),
                       nameof(To), nameof(ToError), nameof(HasToError), nameof(From), nameof(Host), nameof(PortText),
                       nameof(User), nameof(SecurityIndex), nameof(NoneAllowed), nameof(SecurityHint),
                       nameof(HasStoredPassword), nameof(CanSendTest));
            if (IsOAuth) _ = RefreshOAuthAsync();
        }

        private void RefreshPasswordState() =>
            PasswordUnreadable = HasStoredPassword && !EmailAlertService.CanDecryptPassword(P.SmtpPasswordEncrypted);
    }
}
