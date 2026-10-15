using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TimerStudio
{
    /// <summary>
    /// Fires HTTP POST webhooks when a timer alert or reminder triggers.
    /// Each timer card can have an optional Webhook URL configured.
    /// The JSON payload is compatible with Zapier / Make.com catch hooks.
    ///
    /// Payload shape:
    ///   { "event": "alert"|"reminder",
    ///     "timer": "...", "firedAt": "ISO-8601",
    ///     "notes": "...", "remaining": "HH:MM:SS" }
    ///
    /// Failures are logged to DiagLog and swallowed — webhook errors never
    /// block the UI or interrupt timer state.
    /// </summary>
    public static class WebhookService
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>POST a timer-fired alert payload. Fire-and-forget.</summary>
        public static void SendAlert(string webhookUrl, string timerName, DateTime firedAt, string notes)
        {
            if (!IsSendable(webhookUrl)) return;
            _ = PostAsync(webhookUrl.Trim(), BuildPayload("alert", timerName, firedAt, notes, null));
        }

        /// <summary>POST a reminder payload. Fire-and-forget.</summary>
        public static void SendReminder(string webhookUrl, string timerName, TimeSpan remaining, string notes)
        {
            if (!IsSendable(webhookUrl)) return;
            _ = PostAsync(webhookUrl.Trim(), BuildPayload("reminder", timerName, DateTime.UtcNow, notes, remaining));
        }

        private static bool IsSendable(string webhookUrl)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl)) return false;
            if (ValidateUrl(webhookUrl) is null) return true;
            AppLog.Info($"[WebhookService] skipped — URL not allowed: {TruncateUrl(webhookUrl)}");
            return false;
        }

        // ── Internals ─────────────────────────────────────────────────────────

        // TS-05: source-generated serialisation — every control character is escaped,
        // so the receiver always gets valid JSON. Shape unchanged from earlier versions.
        internal static string BuildPayload(string eventType, string timerName,
            DateTime firedAt, string notes, TimeSpan? remaining)
        {
            string remStr = remaining.HasValue
                ? string.Format("{0:D2}:{1:D2}:{2:D2}",
                    (int)remaining.Value.TotalHours,
                    remaining.Value.Minutes,
                    remaining.Value.Seconds)
                : "";
            var payload = new WebhookPayload(eventType, timerName ?? "",
                firedAt.ToUniversalTime().ToString("o"), notes ?? "", remStr);
            return JsonSerializer.Serialize(payload, WebhookJsonContext.Default.WebhookPayload);
        }

        /// <summary>
        /// TS-05: https anywhere; plain http only for local-network targets
        /// (loopback, private/link-local/CGNAT IPs, single-label or .local/.lan/
        /// .home.arpa/.internal hostnames) so LAN automation keeps working.
        /// Returns null when allowed, otherwise a user-facing reason.
        /// </summary>
        public static string ValidateUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u))
                return "Not a valid web address.";
            if (u.Scheme == Uri.UriSchemeHttps) return null;
            if (u.Scheme != Uri.UriSchemeHttp)
                return "Use an https:// address.";
            return IsLocalNetworkHost(u)
                ? null
                : "Use an https:// address. Plain http is only allowed for local-network addresses.";
        }

        internal static bool IsLocalNetworkHost(Uri u)
        {
            if (u.IsLoopback) return true;
            string host = u.IdnHost.TrimEnd('.');
            if (IPAddress.TryParse(host.Trim('[', ']'), out var ip))
            {
                if (IPAddress.IsLoopback(ip)) return true;
                if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    if (ip.IsIPv4MappedToIPv6) return IsPrivateV4(ip.MapToIPv4());
                    byte b0 = ip.GetAddressBytes()[0];
                    return ip.IsIPv6LinkLocal || (b0 & 0xFE) == 0xFC;   // fe80::/10, fc00::/7
                }
                return IsPrivateV4(ip);
            }
            if (!host.Contains('.')) return true;                       // e.g. http://homeassistant:8123
            return host.EndsWith(".local",     StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".lan",       StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".internal",  StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrivateV4(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10
                || b[0] == 127
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);            // CGNAT / Tailscale
        }

        private static async Task PostAsync(string url, string json)
        {
            // L-7: one retry after 2 s for 5xx server errors.
            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var content  = new StringContent(json, Encoding.UTF8, "application/json");
                    // M-7: dispose response to drain the connection and return it to the pool
                    using var response = await _http.PostAsync(url, content).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return;

                    int code = (int)response.StatusCode;
                    AppLog.Info($"[WebhookService] HTTP {code} from {TruncateUrl(url)} (attempt {attempt})");

                    // Retry only on 5xx; 4xx are client errors — retrying won't help
                    if (code < 500 || attempt == maxAttempts) return;
                    await System.Threading.Tasks.Task.Delay(2000).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"WebhookService.PostAsync {TruncateUrl(url)} attempt {attempt}", ex);
                    if (attempt == maxAttempts) return;
                    await System.Threading.Tasks.Task.Delay(2000).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Returns host+path only — strips query string to avoid logging tokens.</summary>
        private static string TruncateUrl(string url)
        {
            try { var u = new Uri(url); return u.Host + u.AbsolutePath; }
            catch { return "(invalid URL)"; }
        }
    }

    /// <summary>TS-05: webhook body. Property names serialise camelCase (event, timer, firedAt, notes, remaining).</summary>
    internal sealed record WebhookPayload(string Event, string Timer, string FiredAt, string Notes, string Remaining);

    [JsonSerializable(typeof(WebhookPayload))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal partial class WebhookJsonContext : JsonSerializerContext { }
}
