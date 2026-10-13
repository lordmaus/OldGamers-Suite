using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReticleOverlay;
using Xunit;

namespace ReticleOverlay.Tests
{
    public class ImagePathValidatorTests   // RO-01
    {
        [Theory]
        [InlineData(@"C:\Images\reticle.png")]
        [InlineData(@"C:/Images/reticle.png")]
        [InlineData(@"c:\a b\x.png")]
        public void LocalDrivePaths_Allowed(string path) =>
            Assert.True(ImagePathValidator.IsSafeLocalPath(path));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(@"\\attacker\share\x.png")]
        [InlineData(@"//attacker/share/x.png")]
        [InlineData(@"\\?\UNC\attacker\share\x.png")]
        [InlineData(@"\??\UNC\attacker\share\x.png")]
        [InlineData(@"\\.\pipe\x")]
        [InlineData(@"\\?\C:\x.png")]
        [InlineData("file://attacker/share/x.png")]
        [InlineData("http://attacker/x.png")]
        [InlineData(@"x.png")]
        [InlineData(@"..\x.png")]
        [InlineData(@"C:x.png")]
        [InlineData(@"C:\a.png:stream")]
        [InlineData("C:\\a\0.png")]
        [InlineData(@"1:\x.png")]
        public void NonLocalOrAmbiguous_Rejected(string path) =>
            Assert.False(ImagePathValidator.IsSafeLocalPath(path));
    }

    public class ProfileStoreTests   // RO-05
    {
        private static string TempFile() =>
            Path.Combine(Path.GetTempPath(), "ro_test_" + Guid.NewGuid().ToString("N"), "profiles.json");

        [Fact]
        public void RoundTrip()
        {
            string f = TempFile();
            try
            {
                var src = new List<ReticleProfile>
                {
                    new() { Name = "CS \"main\"", ProcessName = "cs2.exe", ReticleSize = 24.5, ReticleColor = "#FF00FF", Layer2Enabled = true },
                };
                ProfileStore.Save(src, f);
                var back = ProfileStore.Load(f);
                Assert.Single(back);
                Assert.Equal("CS \"main\"", back[0].Name);
                Assert.Equal("cs2.exe", back[0].ProcessName);
                Assert.Equal(24.5, back[0].ReticleSize);
                Assert.True(back[0].Layer2Enabled);
            }
            finally { Directory.Delete(Path.GetDirectoryName(f), true); }
        }

        [Fact]
        public void CorruptFile_IsKept_NotOverwritten()
        {
            string f = TempFile();
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            const string garbage = "[{\"Name\":\"Broken\", ";
            File.WriteAllText(f, garbage);
            try
            {
                var list = ProfileStore.Load(f);
                Assert.Empty(list);
                Assert.False(File.Exists(f));
                var kept = Directory.GetFiles(Path.GetDirectoryName(f), "profiles.corrupt-*.json");
                Assert.Single(kept);
                Assert.Equal(garbage, File.ReadAllText(kept[0]));
                Assert.Contains(kept[0], CorruptFile.SetAsideThisSession);

                ProfileStore.Save(new List<ReticleProfile> { new() }, f);   // later save can't touch the kept copy
                Assert.Equal(garbage, File.ReadAllText(kept[0]));
            }
            finally { Directory.Delete(Path.GetDirectoryName(f), true); }
        }

        [Fact]
        public void MissingFile_ReturnsEmpty() => Assert.Empty(ProfileStore.Load(TempFile()));
    }

    public class HotkeyParseTests
    {
        [Theory]
        [InlineData("F8", NativeMethods.MOD_NONE, 0x77u)]
        [InlineData("Ctrl+F8", NativeMethods.MOD_CTRL, 0x77u)]
        [InlineData("Alt+Shift+F9", NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, 0x78u)]
        [InlineData("control+Left", NativeMethods.MOD_CTRL, 0x25u)]
        public void Parses(string text, uint mods, uint vk)
        {
            Assert.True(HotkeyManager.TryParse(text, out uint m, out uint v));
            Assert.Equal(mods, m);
            Assert.Equal(vk, v);
        }

        [Theory]
        [InlineData("Ctrl+")]
        [InlineData("NotAKey")]
        public void RejectsGarbage(string text) => Assert.False(HotkeyManager.TryParse(text, out _, out _));

        [Fact]
        public void ResetDefaults_MatchFreshInstall()   // RO-08
        {
            var fresh = new AppPrefsDefaults();
            Assert.Equal(fresh.Toggle, HotkeysDialog.Defaults["Toggle"]);
            Assert.Equal(fresh.Next,   HotkeysDialog.Defaults["Next"]);
            Assert.Equal(fresh.Prev,   HotkeysDialog.Defaults["Prev"]);
        }

        private sealed class AppPrefsDefaults
        {
            public string Toggle { get; } = new AppPrefsData().HotkeyToggle;
            public string Next   { get; } = new AppPrefsData().HotkeyProfileNext;
            public string Prev   { get; } = new AppPrefsData().HotkeyProfilePrev;
        }
    }
}
