using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NoteStation.Core;
using Xunit;

namespace NoteStation.Tests
{
    public class PinHasherTests
    {
        [Fact]
        public void V2_round_trip()
        {
            string h = PinHasher.CreateHash("4711");
            Assert.StartsWith("v2:210000:", h);
            Assert.True(PinHasher.Verify("4711", h, out bool rehash));
            Assert.False(rehash);
            Assert.False(PinHasher.Verify("4712", h, out _));
        }

        [Fact]
        public void Legacy_sha256_matches_and_requests_rehash()
        {
            string legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("1234"))).ToLowerInvariant();
            Assert.True(PinHasher.Verify("1234", legacy, out bool rehash));
            Assert.True(rehash);
        }

        [Theory]
        [InlineData("1")]            // downgrade to 1 iteration
        [InlineData("99999")]
        [InlineData("2147483647")]   // UI freeze
        [InlineData("-5")]
        [InlineData("abc")]
        public void Tampered_iteration_count_is_rejected(string iterations)
        {
            var parts = PinHasher.CreateHash("4711").Split(':');
            string tampered = $"v2:{iterations}:{parts[2]}:{parts[3]}";
            Assert.False(PinHasher.Verify("4711", tampered, out _));
        }

        [Theory]
        [InlineData("v2:210000:!!!:AAAA")]
        [InlineData("v2:210000")]
        [InlineData("not-a-hash")]
        [InlineData("")]
        public void Malformed_hash_is_rejected(string stored)
            => Assert.False(PinHasher.Verify("4711", stored, out _));
    }

    public class ClipboardPrivacyTests
    {
        [Fact]
        public void Plain_text_is_captured()
        {
            var d = new System.Windows.DataObject();
            d.SetData(System.Windows.DataFormats.UnicodeText, "hello");
            Assert.False(ClipboardPrivacy.IsExcluded(d));
        }

        [Theory]
        [InlineData(ClipboardPrivacy.ExcludeFromMonitor)]
        [InlineData(ClipboardPrivacy.ClipboardViewerIgnore)]
        public void Marker_formats_exclude(string format)
        {
            var d = new System.Windows.DataObject();
            d.SetData(System.Windows.DataFormats.UnicodeText, "secret");
            d.SetData(format, new MemoryStream(new byte[] { 0, 0, 0, 0 }));
            Assert.True(ClipboardPrivacy.IsExcluded(d));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        public void CanIncludeInClipboardHistory_dword(int value, bool excluded)
        {
            var d = new System.Windows.DataObject();
            d.SetData(System.Windows.DataFormats.UnicodeText, "secret");
            d.SetData(ClipboardPrivacy.CanIncludeInHistory, new MemoryStream(BitConverter.GetBytes(value)));
            Assert.Equal(excluded, ClipboardPrivacy.IsExcluded(d));
        }
    }

    public class ClipVaultTests
    {
        [Fact]
        public void Protect_round_trip_and_ciphertext_differs()
        {
            byte[] plain = Encoding.UTF8.GetBytes("{\"Text\":\"hunter2\"}");
            byte[] cipher = ClipVault.Protect(plain);
            Assert.False(cipher.AsSpan().IndexOf("hunter2"u8) >= 0);
            Assert.Equal(plain, ClipVault.Unprotect(cipher));
        }

        [Fact]
        public void ConvertImage_encrypts_then_restores_bytes()
        {
            string dir = Directory.CreateTempSubdirectory("ns-vault-").FullName;
            try
            {
                string png = Path.Combine(dir, "7.png");
                byte[] bytes = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
                File.WriteAllBytes(png, bytes);

                string enc = ClipVault.ConvertImage(png, encrypt: true);
                Assert.EndsWith(".png.enc", enc);
                Assert.False(File.Exists(png));
                Assert.Equal(bytes, ClipVault.ReadImageBytes(enc));

                string back = ClipVault.ConvertImage(enc, encrypt: false);
                Assert.Equal(png, back);
                Assert.Equal(bytes, File.ReadAllBytes(back));
            }
            finally { Directory.Delete(dir, true); }
        }
    }

    public class AppPolicyTests
    {
        [Fact]
        public void Clipboard_com_errors_are_recoverable()
            => Assert.True(App.IsRecoverable(new System.Runtime.InteropServices.COMException("CLIPBRD_E_CANT_OPEN")));

        [Fact]
        public void Wrapped_xaml_parse_is_recoverable()
            => Assert.True(App.IsRecoverable(new InvalidOperationException("outer", new System.Windows.Markup.XamlParseException("bad"))));

        [Fact]
        public void Null_reference_in_app_code_is_fatal()
            => Assert.False(App.IsRecoverable(new NullReferenceException()));
    }

    public class TimerStudioIpcContractTests
    {
        private static string Root()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "OldGamers.Suite.slnx")))
                    return dir.FullName;
            throw new DirectoryNotFoundException("Suite root (OldGamers.Suite.slnx) not found above " + AppContext.BaseDirectory);
        }

        [Fact]
        public void Both_apps_use_the_same_local_mutex_name()
        {
            Assert.StartsWith(@"Local\", TimerStudioIpc.TimersJsonMutexName);
            string ts = File.ReadAllText(Path.Combine(Root(), "TimerStudio", "Core", "TimerStudioIpc.cs"));
            Assert.Contains($"@\"{TimerStudioIpc.TimersJsonMutexName}\"", ts);
            Assert.Contains($"PushContractVersion = {TimerStudioIpc.PushContractVersion};", ts);
        }

        [Fact]
        public void Ipc_files_differ_only_in_namespace_and_peer_name()
        {
            string ns = File.ReadAllText(Path.Combine(Root(), "NoteStation", "Core", "TimerStudioIpc.cs"));
            string ts = File.ReadAllText(Path.Combine(Root(), "TimerStudio", "Core", "TimerStudioIpc.cs"));
            string Norm(string s) => s.Replace("namespace NoteStation", "namespace X").Replace("namespace TimerStudio", "namespace X")
                                      .Replace("NoteStation's own", "PEER's own").Replace("TimerStudio's own", "PEER's own");
            Assert.Equal(Norm(ns), Norm(ts));
        }
    }
}
