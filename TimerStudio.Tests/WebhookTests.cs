using System;
using System.Text.Json;
using TimerStudio;
using Xunit;

namespace TimerStudio.Tests
{
    public class WebhookTests
    {
        [Theory]
        [InlineData("plain")]
        [InlineData("quote \" back\\slash")]
        [InlineData("ctrl \b \f \u0001 \u001f end")]
        [InlineData("line\r\nbreak\ttab")]
        [InlineData("émoji 🎯 <script>")]
        public void BuildPayload_is_always_valid_json(string name)
        {
            string json = WebhookService.BuildPayload("alert", name, new DateTime(2026, 10, 12, 14, 0, 0, DateTimeKind.Utc),
                                                      name, TimeSpan.FromSeconds(3725));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("alert", root.GetProperty("event").GetString());
            Assert.Equal(name,    root.GetProperty("timer").GetString());
            Assert.Equal(name,    root.GetProperty("notes").GetString());
            Assert.Equal("01:02:05", root.GetProperty("remaining").GetString());
            Assert.StartsWith("2026-10-12T14:00:00", root.GetProperty("firedAt").GetString());
        }

        [Fact]
        public void BuildPayload_without_remaining_writes_empty_string()
        {
            string json = WebhookService.BuildPayload("reminder", "t", DateTime.UtcNow, null, null);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("", doc.RootElement.GetProperty("remaining").GetString());
            Assert.Equal("", doc.RootElement.GetProperty("notes").GetString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("https://hooks.slack.com/services/x")]
        [InlineData("http://localhost:8080/hook")]
        [InlineData("http://127.0.0.1/hook")]
        [InlineData("http://192.168.1.20:8123/api/webhook/x")]
        [InlineData("http://10.0.0.5/x")]
        [InlineData("http://172.20.1.1/x")]
        [InlineData("http://100.101.102.103/x")]
        [InlineData("http://homeassistant:8123/api/webhook/x")]
        [InlineData("http://nas.local/x")]
        [InlineData("http://router.lan/x")]
        [InlineData("http://box.home.arpa/x")]
        [InlineData("http://[fd00::1]/x")]
        [InlineData("http://[::1]/x")]
        public void ValidateUrl_allows(string url) => Assert.Null(WebhookService.ValidateUrl(url));

        [Theory]
        [InlineData("http://hooks.example.net/timer")]
        [InlineData("http://8.8.8.8/x")]
        [InlineData("http://172.32.0.1/x")]
        [InlineData("ftp://192.168.1.1/x")]
        [InlineData("file:///C:/x")]
        [InlineData("not a url")]
        public void ValidateUrl_rejects(string url) => Assert.NotNull(WebhookService.ValidateUrl(url));
    }
}
