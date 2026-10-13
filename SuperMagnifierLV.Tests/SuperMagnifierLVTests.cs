using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using SuperMagnifier;
using Xunit;

namespace SuperMagnifierLV.Tests
{
    internal sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _c, _u;
        public CultureScope(string name)
        {
            _c = CultureInfo.CurrentCulture; _u = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
        }
        public void Dispose() { CultureInfo.CurrentCulture = _c; CultureInfo.CurrentUICulture = _u; }
    }

    public class PresetPersistenceTests
    {
        public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "fr-FR", "tr-TR" };

        [Theory, MemberData(nameof(Cultures))]
        public void Presets_RoundTrip_AnyCulture(string culture)
        {
            using var _ = new CultureScope(culture);
            string path = Path.Combine(Path.GetTempPath(), $"sml_presets_{Guid.NewGuid():N}.json");
            try
            {
                var src = new List<LensPreset>
                {
                    new() { Name = "Reading \"big\"\nline2 \\ İı", Zoom = 2.5, LensW = 640, LensH = 300,
                            LensOpacity = 0.85, ZoomMin = 1.75, ZoomMax = 12.5, FollowMode = "caret", SnapToWord = true },
                };
                Assert.True(LensPreset.SaveAll(src, path));
                Assert.DoesNotContain("2,5", File.ReadAllText(path));

                var back = LensPreset.LoadAll(path);
                Assert.Equal(5, back.Count);
                Assert.Equal(src[0].Name, back[0].Name);
                Assert.Equal(2.5, back[0].Zoom);
                Assert.Equal(0.85, back[0].LensOpacity);
                Assert.Equal(1.75, back[0].ZoomMin);
                Assert.Equal(12.5, back[0].ZoomMax);
                Assert.Equal("caret", back[0].FollowMode);
                Assert.True(back[0].SnapToWord);
                Assert.True(back[1].IsEmpty);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void LegacyCommaDecimal_IsRepaired()
        {
            const string legacy = "[{\"Name\":\"Old\",\"Zoom\":2,50,\"LensW\":520,\"LensH\":360,\"ColorMode\":\"normal\"," +
                                  "\"LensOpacity\":0,9,\"ZoomMin\":1,25,\"ZoomMax\":20,\"FollowMode\":\"mouse\"," +
                                  "\"SnapToWord\":false,\"ReticleEnabled\":true}]";
            var list = LensPreset.ParseLegacy(legacy);
            Assert.Single(list);
            Assert.Equal("Old", list[0].Name);
            Assert.Equal(2.5, list[0].Zoom);
            Assert.Equal(0.9, list[0].LensOpacity);
            Assert.Equal(1.25, list[0].ZoomMin);
            Assert.Equal(520, list[0].LensW);
            Assert.True(list[0].ReticleEnabled);
        }

        [Fact]
        public void LoadAll_LegacyImportFile_IsNotRewritten()
        {
            string path = Path.Combine(Path.GetTempPath(), $"sml_legacy_{Guid.NewGuid():N}.json");
            const string legacy = "[{\"Name\":\"X\",\"Zoom\":3,0}]";
            try
            {
                File.WriteAllText(path, legacy);
                var list = LensPreset.LoadAll(path);
                Assert.Equal(3.0, list[0].Zoom);
                Assert.Equal(legacy, File.ReadAllText(path));
            }
            finally { File.Delete(path); }
        }
    }

    public class MapPersistenceTests
    {
        [Theory, InlineData("de-DE"), InlineData("tr-TR")]
        public void AppPositions_RoundTrip(string culture)
        {
            using var _ = new CultureScope(culture);
            var json = System.Text.Json.JsonSerializer.Serialize(
                new List<AppPositionMap.AppPositionEntry> { new() { ExeName = "chrome", MonName = "\\\\.\\DISPLAY1", RelX = 0.125, RelY = 0.5 } },
                MapsJsonContext.Default.ListAppPositionEntry);
            Assert.Contains("\"Exe\"", json);
            var back = AppPositionMap.Deserialise(json);
            Assert.Equal(0.125, back[0].RelX);
            Assert.Equal("\\\\.\\DISPLAY1", back[0].MonName);
        }

        [Fact]
        public void AppPositions_LegacyComma_IsRepaired()
        {
            var back = AppPositionMap.Deserialise("[{\"Exe\":\"code\",\"Mon\":\"M1\",\"RelX\":0,0500,\"RelY\":0,25}]");
            Assert.Single(back);
            Assert.Equal("code", back[0].ExeName);
            Assert.Equal(0.05, back[0].RelX, 6);
            Assert.Equal(0.25, back[0].RelY, 6);
        }

        [Fact]
        public void AppPresetRules_RoundTrip_QuotedName()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                new List<AppPresetMap.AppPresetRule> { new() { ExeName = "my \"app\"", PresetSlot = 3 } },
                MapsJsonContext.Default.ListAppPresetRule);
            var back = AppPresetMap.Deserialise(json);
            Assert.Equal("my \"app\"", back[0].ExeName);
            Assert.Equal(3, back[0].PresetSlot);
        }
    }

    public class HotkeyTests
    {
        [Fact]
        public void NoDefault_IsBareCtrlChord()
        {
            var bare = HotkeyBinding.Defaults().Where(kv => kv.Value.VirtualKey != 0 && kv.Value.IsBareCtrlChord).Select(kv => kv.Key).ToList();
            Assert.Empty(bare);
        }

        [Fact]
        public void Defaults_HaveNoDuplicates()
        {
            var bound = HotkeyBinding.Defaults().Values.Where(b => b.VirtualKey != 0).Select(b => (b.Modifiers, b.VirtualKey)).ToList();
            Assert.Equal(bound.Count, bound.Distinct().Count());
        }

        [Fact]
        public void Migration_MovesUntouchedV1Defaults_KeepsCustom()
        {
            var v1 = HotkeyBinding.DefaultsV1();
            var mods = new Dictionary<string, int>(); var vks = new Dictionary<string, int>();
            foreach (var kv in v1) { mods[kv.Key] = (int)kv.Value.Modifiers; vks[kv.Key] = (int)kv.Value.VirtualKey; }
            // custom: Freeze → Ctrl+Shift+F9
            mods["Freeze"] = (int)(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_SHIFT); vks["Freeze"] = 0x78;
            // unbound: Pulse
            vks["Pulse"] = 0;

            var result = AppSettings.MigrateHotkeys(mods, vks, 0, out bool migrated);
            var defaults = HotkeyBinding.Defaults();

            Assert.True(migrated);
            Assert.True(result["Toggle"].SameAs(defaults["Toggle"]));
            Assert.Equal(0x78u, result["Freeze"].VirtualKey);
            Assert.Equal(0u, result["Pulse"].VirtualKey);
        }

        [Fact]
        public void Migration_NotRepeated_WhenSchemeCurrent()
        {
            var v1 = HotkeyBinding.DefaultsV1();
            var mods = v1.ToDictionary(k => k.Key, k => (int)k.Value.Modifiers);
            var vks  = v1.ToDictionary(k => k.Key, k => (int)k.Value.VirtualKey);
            var result = AppSettings.MigrateHotkeys(mods, vks, HotkeyBinding.SchemeVersion, out bool migrated);
            Assert.False(migrated);
            Assert.True(result["Toggle"].SameAs(v1["Toggle"]));
        }

        [Fact]
        public void Migration_EmptySettings_GetsDefaults()
        {
            var result = AppSettings.MigrateHotkeys(new Dictionary<string, int>(), new Dictionary<string, int>(), 0, out bool migrated);
            Assert.False(migrated);
            Assert.Equal(HotkeyBinding.Defaults().Count, result.Count);
        }
    }

    public class ColourPickerTests
    {
        [Theory, InlineData("#FF0000", true), InlineData("#80FFFFFF", true), InlineData("Red", true),
                 InlineData("#GG0000", false), InlineData("", false), InlineData(null, false)]
        public void TryParseColor(string hex, bool ok)
        {
            Assert.Equal(ok, ColourPicker.TryParseColor(hex, out _));
        }

        [Fact]
        public void ParseColorOrDefault_FallsBack()
        {
            Assert.Equal(Colors.Blue, ColourPicker.ParseColorOrDefault("nope", Colors.Blue));
        }
    }
}
