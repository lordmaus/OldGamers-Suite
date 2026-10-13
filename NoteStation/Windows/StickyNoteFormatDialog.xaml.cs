using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NoteStation
{
    /// <summary>
    /// Per-note font family, font size, bold, italic, text color, background color.
    /// This is the primary Low Vision accommodation — each note is individually customised.
    /// </summary>
    public partial class StickyNoteFormatDialog : Window
    {
        public StickyNote Note { get; }
        public event EventHandler NoteAppearanceChanged;

        private bool _suppress;

        // Use canonical chip list — single source of truth, avoids drift with StickyNoteWindow
        private static readonly NoteColorChip[] _presets = StickyNote.PresetChips;

        public StickyNoteFormatDialog(StickyNote note)
        {
            InitializeComponent();
            WindowSizeMemory.Attach(this, "StickyNoteFormatDialog");
            Note = note;

            // Populate font families (sorted)
            var families = new System.Collections.Generic.List<string>();
            foreach (var ff in Fonts.SystemFontFamilies) families.Add(ff.Source);
            families.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var f in families) CboFontFamily.Items.Add(f);
            // Select saved font; fall back to Segoe UI if not found in list
            CboFontFamily.SelectedItem = Note.FontFamily;
            if (CboFontFamily.SelectedItem == null) CboFontFamily.SelectedItem = "Segoe UI";

            // Build preset color buttons — sourced from StickyNote.PresetChips (canonical)
            foreach (var chip in _presets)
            {
                var btn = new Button
                {
                    Width = 44, Height = 44, MinHeight = 44, MinWidth = 44,
                    Margin = new Thickness(2),
                    Tag = chip,
                    ToolTip = chip.DisplayName,
                    Content = "",
                };
                btn.Click += PresetColor_Click;
                AutomationProperties.SetName(btn, $"Set background: {chip.DisplayName}");

                // Solid colored box visual
                var factory = new FrameworkElementFactory(typeof(Border));
                factory.SetValue(Border.BackgroundProperty, BrushFromHex(chip.Background));
                factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
                factory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Colors.Gray));
                factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                var tpl = new ControlTemplate(typeof(Button));
                tpl.VisualTree = factory;
                btn.Template = tpl;

                BgColorPanel.Children.Add(btn);
            }

            _suppress = true;
            SliderFontSize.Value  = Note.FontSize;
            LblFontSize.Text      = $"{Note.FontSize:0}pt";
            SliderTitleSize.Value = Note.TitleFontSize > 0 ? Note.TitleFontSize : 14;
            LblTitleSize.Text     = $"{SliderTitleSize.Value:0}";
            ChkBold.IsChecked     = Note.FontWeight == "Bold";
            ChkItalic.IsChecked   = Note.FontStyle  == "Italic";
            TxtTextColor.Text     = Note.TextColor;
            TxtBgColor.Text       = Note.BackgroundColor;
            SliderOpacity.Value   = Note.NoteOpacity > 0 ? Note.NoteOpacity : 1.0;
            LblOpacity.Text       = $"{SliderOpacity.Value:P0}";
            _suppress = false;

            // F15: Select current texture
            string currentTexture = Note.NoteTexture ?? "";
            foreach (var rb in TexturePanel.Children.OfType<System.Windows.Controls.RadioButton>())
                if (rb.Tag?.ToString() == currentTexture) { rb.IsChecked = true; break; }

            UpdateSwatches();
            UpdatePreview();
        }

        // ── Font family ───────────────────────────────────────────────────────
        private void CboFontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress) return;
            Note.FontFamily = CboFontFamily.SelectedItem as string ?? "Segoe UI";
            UpdatePreview();
        }

        // ── Font size slider ──────────────────────────────────────────────────
        private void SliderFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppress || LblFontSize == null) return;
            Note.FontSize = Math.Round(SliderFontSize.Value);
            LblFontSize.Text = $"{Note.FontSize:0}pt";
            UpdatePreview();
            NoteAppearanceChanged?.Invoke(this, EventArgs.Empty); // live-update the note window
        }

        // ── Title size slider ─────────────────────────────────────────────────
        private void SliderTitleSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppress || LblTitleSize == null || Note == null) return;
            double v = Math.Round(e.NewValue);
            Note.TitleFontSize = v;
            LblTitleSize.Text = v.ToString("0");
            NoteAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Bold / Italic ─────────────────────────────────────────────────────
        private void ChkBold_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            Note.FontWeight = ChkBold.IsChecked == true ? "Bold" : "Normal";
            UpdatePreview();
        }

        private void ChkItalic_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            Note.FontStyle = ChkItalic.IsChecked == true ? "Italic" : "Normal";
            UpdatePreview();
        }

        // ── Text color ────────────────────────────────────────────────────────
        private void TxtTextColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppress || Note == null) return;
            if (IsValidHex(TxtTextColor.Text))
            {
                Note.TextColor = TxtTextColor.Text;
                UpdateSwatches();
                UpdatePreview();
            }
        }

        // ── Native color pickers ──────────────────────────────────────────────
        private void BtnPickTextColor_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color    = HexToFormColor(Note.TextColor),
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                _suppress = true;
                TxtTextColor.Text = hex;
                _suppress = false;
                Note.TextColor = hex;
                UpdateSwatches();
                UpdatePreview();
                NoteAppearanceChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnPickBgColor_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color    = HexToFormColor(Note.BackgroundColor),
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                _suppress = true;
                TxtBgColor.Text = hex;
                _suppress = false;
                Note.BackgroundColor = hex;
                UpdateSwatches();
                UpdatePreview();
                NoteAppearanceChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private static System.Drawing.Color HexToFormColor(string hex)
        {
            try
            {
                var c = (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex);
                return System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            }
            catch { return System.Drawing.Color.White; }
        }

        // ── Background color ──────────────────────────────────────────────────
        private void TxtBgColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppress || Note == null) return;
            if (IsValidHex(TxtBgColor.Text))
            {
                Note.BackgroundColor = TxtBgColor.Text;
                UpdateSwatches();
                UpdatePreview();
            }
        }

        private void PresetColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NoteColorChip chip)
            {
                _suppress = true;
                Note.BackgroundColor = chip.Background;
                Note.TextColor       = chip.TextColor;
                TxtBgColor.Text      = chip.Background;
                TxtTextColor.Text    = chip.TextColor;
                _suppress = false;
                UpdateSwatches();
                UpdatePreview();
            }
        }

        // ── Preview ───────────────────────────────────────────────────────────
        private void UpdatePreview()
        {
            try
            {
                var bg = BrushFromHex(Note.BackgroundColor);
                var fg = BrushFromHex(Note.TextColor);
                var ff = new FontFamily(Note.FontFamily ?? "Segoe UI");
                double fs = Note.FontSize > 4 ? Note.FontSize : 14;
                var fw = Note.FontWeight == "Bold" ? FontWeights.Bold : FontWeights.Normal;
                var fst = Note.FontStyle == "Italic" ? FontStyles.Italic : FontStyles.Normal;

                double previewFs = Math.Min(fs, 28); // cap preview at 28 to fit
                PreviewBorder.Background = bg;
                PreviewText.Foreground   = fg;
                PreviewText.FontFamily   = ff;
                PreviewText.FontSize     = previewFs;
                PreviewText.FontWeight   = fw;
                PreviewText.FontStyle    = fst;

                // Match the note's deterministic line rhythm so ruled textures align here too.
                double factor = ff.LineSpacing > 0 ? ff.LineSpacing : 1.3;
                double L = Math.Ceiling(previewFs * factor);
                double baseline = ff.Baseline > 0 ? ff.Baseline : 1.0;
                double ruleY = Math.Max(2, Math.Min(L - 0.5, baseline * previewFs + 1));
                PreviewText.LineHeight = L;
                PreviewText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

                // Texture overlay — same engine as the live note
                string tex = Note.NoteTexture ?? "";
                if (string.IsNullOrEmpty(tex))
                {
                    PreviewTexture.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var bgColor = (Color)ColorConverter.ConvertFromString(Note.BackgroundColor);
                    var stroke  = StickyNoteWindow.StrokeForBackground(bgColor, out double op);
                    PreviewTexture.Background = StickyNoteWindow.BuildTextureBrush(tex, stroke, L, ruleY);
                    PreviewTexture.Opacity    = op;
                    // Shift ruled textures by the preview text's top inset (8) to register rows.
                    bool ruled = tex == "Lined" || tex == "Grid";
                    PreviewTexture.Margin = new Thickness(0, ruled ? 8 : 0, 0, 0);
                    PreviewTexture.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private void UpdateSwatches()
        {
            try { TextColorSwatch.Background = BrushFromHex(Note.TextColor); } catch { }
            try { BgColorSwatch.Background   = BrushFromHex(Note.BackgroundColor); } catch { }
        }

        // ── Opacity ───────────────────────────────────────────────────────────
        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppress || LblOpacity == null) return;
            Note.NoteOpacity = SliderOpacity.Value;
            LblOpacity.Text  = $"{Note.NoteOpacity:P0}";
            NoteAppearanceChanged?.Invoke(this, EventArgs.Empty);
            UpdatePreview();
        }

        // ── F15: Texture ──────────────────────────────────────────────────────
        private void RbTexture_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Note == null) return;
            string texture = (sender as System.Windows.Controls.RadioButton)?.Tag?.ToString() ?? "";
            Note.NoteTexture = texture;
            UpdatePreview();                                  // reflect in dialog preview
            if (Owner is StickyNoteWindow snw) snw.RefreshTexture(); // live-update the note
            NoteAppearanceChanged?.Invoke(this, EventArgs.Empty);   // persist
        }

        // ── Apply / Close ─────────────────────────────────────────────────────
        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            NoteAppearanceChanged?.Invoke(this, EventArgs.Empty);
            // F15: Texture already applied directly in RbTexture_Checked via _note.NoteTexture
            // Signal the owner StickyNoteWindow to refresh texture display:
            if (Owner is StickyNoteWindow snw)
                snw.RefreshTexture();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            NoteAppearanceChanged?.Invoke(this, EventArgs.Empty); // apply on close
            // F15: Signal owner to refresh texture on close
            if (Owner is StickyNoteWindow snw)
                snw.RefreshTexture();
            // Persist last-used colors so next new note inherits them
            AppPrefs.Instance.LastNoteBackground = Note.BackgroundColor;
            AppPrefs.Instance.LastNoteTextColor  = Note.TextColor;
            AppPrefs.Instance.Save();
            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static SolidColorBrush BrushFromHex(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.Gray); }
        }

        private static bool IsValidHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            try { ColorConverter.ConvertFromString(s); return true; }
            catch { return false; }
        }
    }
}
