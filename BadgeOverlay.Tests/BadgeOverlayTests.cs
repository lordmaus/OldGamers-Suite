using System;
using System.Text.Json;
using System.Windows.Media;
using BadgeOverlay;
using Xunit;

namespace BadgeOverlay.Tests
{
    public class ContrastHelperTests
    {
        [Fact]
        public void Black_on_white_is_21()
            => Assert.Equal(21.0, ContrastHelper.Ratio(Colors.Black, Colors.White), 2);

        [Fact]
        public void Grey_777_on_white_is_just_below_AA()
            => Assert.InRange(ContrastHelper.Ratio(Color.FromRgb(0x77, 0x77, 0x77), Colors.White), 4.47, 4.49);

        [Fact]
        public void Ratio_is_symmetric()
            => Assert.Equal(ContrastHelper.Ratio(Colors.Navy, Colors.Pink), ContrastHelper.Ratio(Colors.Pink, Colors.Navy), 6);
    }

    public class OverlayPaletteTests
    {
        public static TheoryData<string> Styles => new() { "Dark", "Light" };

        private static OverlayPalette.PaletteColors Get(string s) => s == "Dark" ? OverlayPalette.Dark : OverlayPalette.Light;
        private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);

        [Theory, MemberData(nameof(Styles))]
        public void Text_and_muted_are_AAA_on_bg_and_bar(string style)
        {
            var p = Get(style);
            foreach (var bg in new[] { Opaque(p.Bg), p.Bar })
            {
                Assert.True(ContrastHelper.Ratio(p.Text,  bg) >= 7.0, $"{style} text");
                Assert.True(ContrastHelper.Ratio(p.Muted, bg) >= 7.0, $"{style} muted");
                Assert.True(ContrastHelper.Ratio(p.Warn,  bg) >= 7.0, $"{style} warn");
            }
        }

        [Theory, MemberData(nameof(Styles))]
        public void Focus_and_border_are_at_least_3_to_1(string style)
        {
            var p = Get(style);
            Assert.True(ContrastHelper.Ratio(p.Focus,  Opaque(p.Bg)) >= 3.0);
            Assert.True(ContrastHelper.Ratio(p.Border, Opaque(p.Bg)) >= 3.0);
        }

        [Theory]
        [InlineData("HighContrast", true)]
        [InlineData("HighContrastLight", true)]
        [InlineData("HCAmber", true)]
        [InlineData("Dark", false)]
        [InlineData("PaperWhite", false)]
        [InlineData(null, false)]
        public void Hc_theme_keys(string key, bool hc) => Assert.Equal(hc, OverlayPalette.IsHcThemeKey(key));
    }

    public class RestartArgsTests
    {
        [Theory]
        [InlineData(new string[0], 0)]
        [InlineData(new[] { "--restart", "1234" }, 1234)]
        [InlineData(new[] { "--RESTART", "42" }, 42)]
        [InlineData(new[] { "--restart" }, -1)]
        [InlineData(new[] { "--restart", "abc" }, -1)]
        [InlineData(new[] { "--restart", "-5" }, -1)]
        public void ParseRestartPid(string[] args, int expected) => Assert.Equal(expected, App.ParseRestartPid(args));
    }

    public class SettingsTests
    {
        [Theory]
        [InlineData(BadgeStyle.Dark)]
        [InlineData(BadgeStyle.Light)]
        [InlineData(BadgeStyle.MatchTheme)]
        public void BadgeStyle_round_trips_as_string(BadgeStyle style)
        {
            var s = new AppSettings { BadgeStyle = style };
            string json = JsonSerializer.Serialize(s, AppSettingsJsonContext.Default.AppSettings);
            Assert.Contains($"\"{style}\"", json);
            var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            Assert.Equal(style, back.BadgeStyle);
        }

        [Fact]
        public void Default_badge_style_is_dark() => Assert.Equal(BadgeStyle.Dark, new AppSettings().BadgeStyle);
    }

    public class MsaaTests
    {
        [Fact]
        public void Access_denied_hresult() => Assert.Equal(unchecked((int)0x80070005), MsaaDetector.E_ACCESSDENIED);
    }
}
