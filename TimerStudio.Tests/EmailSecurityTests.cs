using System;
using System.Security.Cryptography;
using MailKit.Security;
using TimerStudio;
using Xunit;

namespace TimerStudio.Tests
{
    public class EmailSecurityTests
    {
        [Theory]
        [InlineData(465, SmtpSecurity.Auto,     false, SecureSocketOptions.SslOnConnect)]
        [InlineData(587, SmtpSecurity.Auto,     true,  SecureSocketOptions.StartTls)]
        [InlineData(25,  SmtpSecurity.Auto,     true,  SecureSocketOptions.StartTls)]
        [InlineData(587, SmtpSecurity.SslTls,   true,  SecureSocketOptions.SslOnConnect)]
        [InlineData(465, SmtpSecurity.StartTls, true,  SecureSocketOptions.StartTls)]
        [InlineData(25,  SmtpSecurity.None,     false, SecureSocketOptions.None)]
        public void ResolveSecureOptions_maps_port_and_mode(int port, SmtpSecurity mode, bool auth, SecureSocketOptions expected)
            => Assert.Equal(expected, EmailAlertService.ResolveSecureOptions(port, mode, auth));

        [Fact]
        public void ResolveSecureOptions_refuses_plaintext_login()
            => Assert.Throws<InvalidOperationException>(
                () => EmailAlertService.ResolveSecureOptions(25, SmtpSecurity.None, authenticating: true));

        [Theory]
        [InlineData(null,  "",     SmtpSecurity.Auto)]   // pre-migration, TLS default on
        [InlineData(true,  "u@x.com", SmtpSecurity.Auto)]
        [InlineData(false, "u@x.com", SmtpSecurity.Auto)] // TLS-off + login → never plaintext
        [InlineData(false, "",     SmtpSecurity.None)]   // anonymous relay keeps working
        public void Legacy_UseSmtpTls_migrates(bool? useTls, string user, SmtpSecurity expected)
        {
            var d = new PrefsDto { UseSmtpTls = useTls, SmtpUser = user, SmtpSecurity = null };
            Assert.Equal(expected, DtoMapper.ResolveSmtpSecurity(d));
        }

        [Fact]
        public void Stored_SmtpSecurity_wins_over_legacy_flag()
        {
            var d = new PrefsDto { UseSmtpTls = false, SmtpUser = "", SmtpSecurity = SmtpSecurity.SslTls };
            Assert.Equal(SmtpSecurity.SslTls, DtoMapper.ResolveSmtpSecurity(d));
        }

        [Theory]
        [InlineData("not base64 !!")]
        [InlineData("AAAAAAAAAAAAAAAA")]
        public void DecryptPassword_unreadable_throws(string stored)
        {
            var ex = Assert.Throws<CryptographicException>(() => EmailAlertService.DecryptPassword(stored));
            Assert.Equal(EmailAlertService.PasswordUnreadableMessage, ex.Message);
            Assert.False(EmailAlertService.CanDecryptPassword(stored));
        }

        [Fact]
        public void Password_roundtrips()
        {
            string enc = EmailAlertService.EncryptPassword("s3cret!");
            Assert.Equal("s3cret!", EmailAlertService.DecryptPassword(enc));
            Assert.True(EmailAlertService.CanDecryptPassword(enc));
        }

        [Theory]
        [InlineData("me@example.com", new string[0])]
        [InlineData("me@example.com, you@example.org", new string[0])]
        [InlineData("me@example.com, bob@", new[] { "bob@" })]
        [InlineData("bob; alice@host", new[] { "bob", "alice@host" })]
        [InlineData("", new string[0])]
        public void InvalidRecipients_lists_bad_entries(string to, string[] expected)
            => Assert.Equal(expected, EmailAlertService.InvalidRecipients(to));

        [Fact]
        public void EmailMode_values_match_legacy_ints()
        {
            Assert.Equal(0, (int)EmailMode.Disabled);
            Assert.Equal(1, (int)EmailMode.MailKit);
            Assert.Equal(2, (int)EmailMode.Outlook);
            Assert.Equal(3, (int)EmailMode.MicrosoftOAuth);
        }
    }
}
