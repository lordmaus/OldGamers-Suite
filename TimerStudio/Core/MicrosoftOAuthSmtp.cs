using Microsoft.Identity.Client;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace TimerStudio
{
    /// <summary>
    /// OAuth2 / Modern Authentication helper for sending email via smtp-mail.outlook.com
    /// using MailKit XOAUTH2. Works for personal @hotmail / @outlook / @live accounts
    /// without SMTP basic auth (which Microsoft disabled in 2024).
    ///
    /// ── ONE-TIME AZURE APP SETUP ────────────────────────────────────────────────
    ///  1. Go to https://portal.azure.com → "App registrations" → New registration
    ///  2. Name: "TimerStudio Email"
    ///     Supported account types: "Personal Microsoft accounts only"
    ///  3. Redirect URI: Mobile and desktop applications → http://localhost
    ///  4. After creation, go to "API permissions" → Add a permission →
    ///     "APIs my organization uses" → search "Office 365 Exchange Online" →
    ///     Delegated permissions → SMTP.Send → Add
    ///  5. Go to "Authentication" → scroll to "Advanced settings" →
    ///     "Allow public client flows" → Yes → Save
    ///  6. Copy the "Application (client) ID" (GUID) → paste into the
    ///     Client ID field in Timer Studio's Calendar & Email Settings.
    ///
    /// Users click "Sign in with Microsoft", authenticate once in their browser,
    /// and Timer Studio caches the refresh token for silent renewal.
    /// </summary>
    public static class MicrosoftOAuthSmtp
    {
        // Azure App Registration (client) ID — personal Microsoft accounts (consumers authority).
        // Registered once by the developer; users just click Sign in with Microsoft.
        private const string EmbeddedClientId = "357cdadc-1502-47b0-b8c9-6945e10e2bd8";

        // SMTP AUTH scope for personal Outlook/Hotmail accounts on smtp-mail.outlook.com
        private static readonly string[] Scopes =
            { "https://outlook.office.com/SMTP.Send", "offline_access" };

        private static IPublicClientApplication _app;

        private static IPublicClientApplication GetApp()
        {
            if (_app == null)
            {
                _app = PublicClientApplicationBuilder
                    .Create(EmbeddedClientId)
                    // consumers = personal Microsoft accounts only
                    .WithAuthority("https://login.microsoftonline.com/consumers")
                    .WithRedirectUri("http://localhost")
                    .Build();

                _app.UserTokenCache.SetBeforeAccess(BeforeCacheAccess);
                _app.UserTokenCache.SetAfterAccess(AfterCacheAccess);
            }
            return _app;
        }

        // ── Token cache persistence (DPAPI-encrypted, CurrentUser scope) ────────
        //
        // Storage format: base64( DPAPI-protected( MSAL-v3-bytes ) )
        // Migration: existing installs stored raw base64 MSAL bytes (pre-DPAPI).
        // On first read, ProtectedData.Unprotect will throw CryptographicException
        // for the old format; we fall back to treating the stored value as raw MSAL
        // bytes so the user stays signed-in. AfterCacheAccess re-writes in the new
        // encrypted format immediately afterward.

        private static void BeforeCacheAccess(TokenCacheNotificationArgs args)
        {
            string stored = AppPrefs.Instance.MsOAuthTokenCache;
            if (string.IsNullOrEmpty(stored)) return;
            try
            {
                byte[] raw = Convert.FromBase64String(stored);
                // Attempt DPAPI decrypt (new format)
                try
                {
                    byte[] msalBytes = ProtectedData.Unprotect(raw, null,
                        DataProtectionScope.CurrentUser);
                    args.TokenCache.DeserializeMsalV3(msalBytes);
                }
                catch (CryptographicException)
                {
                    // Migration path: stored value is raw MSAL bytes (plaintext base64, old format).
                    // Deserialize directly; AfterCacheAccess will re-encrypt on next write.
                    args.TokenCache.DeserializeMsalV3(raw);
                }
            }
            catch { /* corrupt/unreadable — start fresh */ }
        }

        private static void AfterCacheAccess(TokenCacheNotificationArgs args)
        {
            if (!args.HasStateChanged) return;
            try
            {
                byte[] msalBytes    = args.TokenCache.SerializeMsalV3();
                byte[] protected_   = ProtectedData.Protect(msalBytes, null,
                                          DataProtectionScope.CurrentUser);
                AppPrefs.Instance.MsOAuthTokenCache = Convert.ToBase64String(protected_);
            }
            catch (Exception ex)
            {
                // M-? (2026-09-14 review): previously fell back to storing the MSAL token
                // cache as plaintext base64 when DPAPI encryption failed (e.g. no user
                // profile, Windows Server Core) — that could persist a refresh token to
                // disk unencrypted with no retry. Fail closed instead: log and leave the
                // on-disk cache untouched (prior encrypted value, or none). The in-memory
                // token cache for this session is unaffected; worst case the user signs
                // in again next launch, which is strictly safer than a plaintext refresh
                // token on disk.
                AppLog.Error("MicrosoftOAuthSmtp.AfterCacheAccess DPAPI", ex);
                return;
            }
            AppPrefs.Instance.Save();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the system browser for interactive sign-in.
        /// Returns (accessToken, accountEmail). Call from UI thread or any thread.
        /// </summary>
        public static async Task<(string token, string email)> SignInAsync()
        {
            var app    = GetApp();
            var result = await app.AcquireTokenInteractive(Scopes)
                .WithPrompt(Prompt.SelectAccount)
                .WithUseEmbeddedWebView(false)   // system browser — works across elevation
                .ExecuteAsync();
            return (result.AccessToken, result.Account.Username);
        }

        /// <summary>
        /// Returns a valid access token from the cache (renewing silently if needed).
        /// Does NOT open a browser — safe to call from background threads.
        /// Throws if not signed in or token cannot be renewed without UI.
        /// </summary>
        public static async Task<(string token, string email)> GetTokenSilentAsync()
        {
            var app      = GetApp();
            var accounts = (await app.GetAccountsAsync()).ToList();
            if (accounts.Count == 0)
                throw new InvalidOperationException(
                    "Not signed in to a Microsoft account.\n" +
                    "Open Calendar & Email Settings and click 'Sign in with Microsoft'.");

            // AcquireTokenSilent uses the refresh token — no browser, no UI.
            var result = await app
                .AcquireTokenSilent(Scopes, accounts[0])
                .ExecuteAsync();
            return (result.AccessToken, result.Account.Username);
        }

        /// <summary>Returns the cached account email, or null if not signed in.</summary>
        public static async Task<string> GetSignedInAccountAsync()
        {
            try
            {
                var accounts = await GetApp().GetAccountsAsync();
                return accounts.FirstOrDefault()?.Username;
            }
            catch { return null; }
        }

        /// <summary>Signs out and clears the token cache.</summary>
        public static async Task SignOutAsync()
        {
            try
            {
                var app = GetApp();
                foreach (var acc in await app.GetAccountsAsync())
                    await app.RemoveAsync(acc);
            }
            catch { }
            AppPrefs.Instance.MsOAuthTokenCache = "";
            AppPrefs.Instance.Save();
        }
    }
}
