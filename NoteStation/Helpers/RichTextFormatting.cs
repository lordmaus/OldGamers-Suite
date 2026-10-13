using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace NoteStation
{
    /// <summary>
    /// Shared rich-text formatting support used by the Clip editor, StickyNoteWindow,
    /// and the Task Notes editor. Storage convention matches the one already
    /// established by StickyNote.Content: plain text is stored as-is; once a user
    /// applies any formatting, the field is re-saved as a XAML FlowDocument string
    /// (detected on load by a leading '&lt;'). No DTO/schema change is required —
    /// ClipboardItem.Text / TaskItem.Notes round-trip through this exactly like
    /// StickyNote.Content already does.
    /// </summary>
    public static class RichTextFormatting
    {
        public static readonly (string Name, Color Color)[] TextColors =
        {
            ("White",  Colors.White),
            ("Gold",   (Color)ColorConverter.ConvertFromString("#FFD700")),
            ("Cyan",   (Color)ColorConverter.ConvertFromString("#00E5FF")),
            ("Red",    (Color)ColorConverter.ConvertFromString("#F38BA8")),
            ("Green",  (Color)ColorConverter.ConvertFromString("#A6E3A1")),
        };

        public static readonly (string Name, Color? Color)[] HighlightColors =
        {
            ("None",    null),
            ("Yellow",  (Color)ColorConverter.ConvertFromString("#FFF59D")),
            ("Cyan",    (Color)ColorConverter.ConvertFromString("#80DEEA")),
            ("Magenta", (Color)ColorConverter.ConvertFromString("#F48FB1")),
        };

        public static readonly double[] FontSizes = { 10, 12, 14, 16, 18, 20, 24, 28, 32, 40 };

        // ── Toolbar ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a Bold/Italic/Underline + font-size + text-color + highlight
        /// toolbar wired to the given RichTextBox's current selection. Controls are
        /// 44px tall/wide per the project's WCAG AAA target-size convention.
        ///
        /// A plain StackPanel, not a ToolBar: WPF's ToolBar silently restyles any Button/
        /// ToggleButton/ComboBox placed directly in it via ToolBar.ButtonStyleKey /
        /// ToolBar.ToggleButtonStyleKey / ToolBar.ComboBoxStyleKey, which bypasses this app's
        /// own theming entirely — that's what made the font-size box render as a blank,
        /// unthemed white control with nothing selected (the "can't see the text options" bug).
        /// </summary>
        public static Panel BuildToolbar(RichTextBox target)
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = Brushes.Transparent,
            };

            var btnBold = MakeToggle("B", "Bold (Ctrl+B)", "Bold");
            var btnItalic = MakeToggle("I", "Italic (Ctrl+I)", "Italic");
            ((TextBlock)btnItalic.Content).FontStyle = FontStyles.Italic;
            var btnUnderline = MakeToggle("U", "Underline (Ctrl+U)", "Underline");
            ((TextBlock)btnUnderline.Content).TextDecorations = TextDecorations.Underline;

            btnBold.Click += (_, __) => ToggleProperty(target, TextElement.FontWeightProperty,
                FontWeights.Bold, FontWeights.Normal);
            btnItalic.Click += (_, __) => ToggleProperty(target, TextElement.FontStyleProperty,
                FontStyles.Italic, FontStyles.Normal);
            btnUnderline.Click += (_, __) => ToggleUnderline(target);

            var cboSize = new ComboBox
            {
                Width = 60, Height = 44, Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Font size",
            };
            ThemeToolbarControl(cboSize);
            System.Windows.Automation.AutomationProperties.SetName(cboSize, "Font size");
            var sizeLabels = FontSizes.Select(s => s.ToString("0")).ToArray();
            foreach (var label in sizeLabels) cboSize.Items.Add(label);

            // Pre-select the current selection's font size so the box is never blank on open —
            // falls back to the nearest known size (mixed-size selections don't map to one value).
            void SyncFontSizeCombo()
            {
                double size = target.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double d ? d : 14;
                cboSize.SelectedItem = sizeLabels.OrderBy(l => Math.Abs(double.Parse(l, System.Globalization.CultureInfo.InvariantCulture) - size)).First();
            }
            SyncFontSizeCombo();

            cboSize.SelectionChanged += (_, __) =>
            {
                if (cboSize.SelectedItem is string s && double.TryParse(s, out double size) && size > 0)
                    target.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                target.Focus();
            };

            var btnTextColor = MakeColorPicker("A", "Text color", TextColors, (c) =>
                target.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(c)));

            var btnHighlight = MakeColorPicker("▦", "Highlight color", HighlightColors, (c) =>
                target.Selection.ApplyPropertyValue(TextElement.BackgroundProperty,
                    c.HasValue ? new SolidColorBrush(c.Value) : null));

            bar.Children.Add(btnBold);
            bar.Children.Add(btnItalic);
            bar.Children.Add(btnUnderline);
            bar.Children.Add(cboSize);
            bar.Children.Add(btnTextColor);
            bar.Children.Add(btnHighlight);

            // Reflect current selection's formatting on the toggle buttons and the size box.
            target.SelectionChanged += (_, __) =>
            {
                btnBold.IsChecked = IsSet(target, TextElement.FontWeightProperty, FontWeights.Bold);
                btnItalic.IsChecked = IsSet(target, TextElement.FontStyleProperty, FontStyles.Italic);
                var deco = target.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
                btnUnderline.IsChecked = deco != null && deco.Count > 0;
                SyncFontSizeCombo();
            };

            return bar;
        }

        /// <summary>Applies the app's dark-theme brushes to a code-built toolbar control. These
        /// controls used to live in a ToolBar, whose special item-style-key lookups bypass the
        /// app's normal implicit/DynamicResource styling — see BuildToolbar's remarks. Now that
        /// it's a plain StackPanel that no longer applies, but Button/ToggleButton still have no
        /// app-wide implicit style (only named ones like FlatButton), so they'd otherwise fall
        /// back to bare OS chrome that doesn't match the note's dark theme.</summary>
        private static void ThemeToolbarControl(Control c)
        {
            c.SetResourceReference(Control.BackgroundProperty, "BrushSurf");
            c.SetResourceReference(Control.ForegroundProperty, "BrushText");
            c.SetResourceReference(Control.BorderBrushProperty, "BrushSurf2");
        }

        private static ToggleButton MakeToggle(string glyph, string tooltip, string automationName)
        {
            var tb = new ToggleButton
            {
                Width = 44, Height = 44, Margin = new Thickness(0, 0, 2, 0),
                BorderThickness = new Thickness(1),
                ToolTip = tooltip,
                Content = new TextBlock { Text = glyph, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            ThemeToolbarControl(tb);
            tb.Checked   += (_, __) => tb.SetResourceReference(Control.BackgroundProperty, "BrushBlue");
            tb.Unchecked += (_, __) => tb.SetResourceReference(Control.BackgroundProperty, "BrushSurf");
            System.Windows.Automation.AutomationProperties.SetName(tb, automationName);
            return tb;
        }

        private static Button MakeColorPicker<T>(string glyph, string tooltip, (string Name, T Value)[] options, Action<T> apply)
        {
            var btn = new Button
            {
                Width = 44, Height = 44, Margin = new Thickness(6, 0, 0, 0),
                BorderThickness = new Thickness(1),
                ToolTip = tooltip, Content = glyph,
            };
            ThemeToolbarControl(btn);
            System.Windows.Automation.AutomationProperties.SetName(btn, tooltip);
            var menu = new ContextMenu();
            foreach (var (name, value) in options)
            {
                var item = new MenuItem { Header = name };
                item.Click += (_, __) => apply(value);
                menu.Items.Add(item);
            }
            btn.Click += (_, __) => { menu.PlacementTarget = btn; menu.IsOpen = true; };
            return btn;
        }

        private static void ToggleProperty(RichTextBox target, DependencyProperty prop, object onValue, object offValue)
        {
            bool currentlyOn = IsSet(target, prop, onValue);
            target.Selection.ApplyPropertyValue(prop, currentlyOn ? offValue : onValue);
            target.Focus();
        }

        private static void ToggleUnderline(RichTextBox target)
        {
            var deco = target.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool currentlyOn = deco != null && deco.Count > 0;
            target.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
                currentlyOn ? null : TextDecorations.Underline);
            target.Focus();
        }

        private static bool IsSet(RichTextBox target, DependencyProperty prop, object onValue)
        {
            var val = target.Selection.GetPropertyValue(prop);
            return val != DependencyProperty.UnsetValue && Equals(val, onValue);
        }

        // ── Load / Save (XAML-if-formatted, plain-text otherwise) ────────────────

        public static void LoadContent(RichTextBox box, string stored)
        {
            box.Document.Blocks.Clear();
            string content = stored ?? "";
            if (!string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith("<"))
            {
                try
                {
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
                    var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
                    range.Load(ms, DataFormats.Xaml);
                    // Save-time default colour → inherit the box's (theme/note) Foreground again.
                    if (SavedDefaultForeground(content) is Color def) ClearLocalForeground(box.Document, def);
                    return;
                }
                catch { /* fall through to plain text */ }
            }
            box.Document.Blocks.Add(new Paragraph(new Run(content)));
        }

        public static string SaveContent(RichTextBox box)
        {
            try
            {
                using var ms = new MemoryStream();
                var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
                range.Save(ms, DataFormats.Xaml);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch
            {
                return new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;
            }
        }

        /// <summary>Plain-text rendition of a stored field, stripping XAML markup if present.
        /// Used for search/dedupe/word-count, where formatting is irrelevant.</summary>
        public static string StripToPlainText(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "";
            if (!stored.TrimStart().StartsWith("<")) return stored;
            try
            {
                var doc = new FlowDocument();
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(stored));
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                range.Load(ms, DataFormats.Xaml);
                return new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            }
            catch { return stored; }
        }

        // ── Note default text colour vs. user formatting ──────────────────────
        // TextRange.Save(DataFormats.Xaml) stamps the note's *effective* text colour at save time
        // onto the root <Section Foreground="…">, and TextRange.Load turns it into local Foreground
        // values on the loaded blocks/runs. Those locals outrank RichTextBox.Foreground, so the
        // note's text colour could never be changed after the first save. The Section value is the
        // "default at save time" marker: locals equal to it are cleared on load; genuinely
        // user-coloured runs (a different colour) are kept.

        private static readonly System.Text.RegularExpressions.Regex SectionForegroundRx = new(
            @"^\s*<Section\b[^>]*?\sForeground=""(#[0-9A-Fa-f]{6,8})""",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>The default text colour a saved rich-text note was stamped with, or null.</summary>
        public static Color? SavedDefaultForeground(string storedXaml)
        {
            if (string.IsNullOrEmpty(storedXaml)) return null;
            var m = SectionForegroundRx.Match(storedXaml);
            if (!m.Success) return null;
            try { return (Color)ColorConverter.ConvertFromString(m.Groups[1].Value); }
            catch { return null; }
        }

        /// <summary>Clears every local Foreground equal to <paramref name="c"/> (blocks and inlines),
        /// so that text inherits the host's colour again. Returns the number of values cleared.</summary>
        public static int ClearLocalForeground(FlowDocument doc, Color c)
        {
            if (doc == null) return 0;
            int n = 0;
            void Check(TextElement el)
            {
                if (el.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush b && b.Color == c)
                { el.ClearValue(TextElement.ForegroundProperty); n++; }
            }
            void Inlines(InlineCollection inlines)
            {
                foreach (var i in inlines)
                {
                    Check(i);
                    if (i is Span s) Inlines(s.Inlines);
                }
            }
            void Blocks(BlockCollection blocks)
            {
                foreach (var b in blocks)
                {
                    Check(b);
                    switch (b)
                    {
                        case Paragraph p: Inlines(p.Inlines); break;
                        case Section sec: Blocks(sec.Blocks); break;
                        case List l:
                            foreach (var li in l.ListItems) { Check(li); Blocks(li.Blocks); }
                            break;
                        case Table t:
                            foreach (var rg in t.RowGroups)
                                foreach (var row in rg.Rows)
                                    foreach (var cell in row.Cells) { Check(cell); Blocks(cell.Blocks); }
                            break;
                    }
                }
            }
            Blocks(doc.Blocks);
            return n;
        }

        // ── List-row preview rendering ────────────────────────────────────────

        /// <summary>
        /// Builds a truncated list of Inlines preserving Bold/Italic/Underline/
        /// Foreground/Background formatting, for display in a plain TextBlock list
        /// row (Recent list, note list, task list, ClipOverlay rows). Falls back to
        /// a single plain Run when the stored value isn't XAML.
        /// </summary>
        public static List<Inline> BuildPreviewInlines(string stored, int maxChars = 160)
        {
            var result = new List<Inline>();
            if (string.IsNullOrWhiteSpace(stored)) return result;

            if (stored.TrimStart().StartsWith("<"))
            {
                try
                {
                    var doc = new FlowDocument();
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(stored));
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    range.Load(ms, DataFormats.Xaml);
                    // Drop the save-time default colour so previews use the host row's (theme) colour;
                    // only user-applied run colours are carried into the preview.
                    if (SavedDefaultForeground(stored) is Color def) ClearLocalForeground(doc, def);

                    int budget = maxChars;
                    foreach (var block in doc.Blocks)
                    {
                        if (budget <= 0) break;
                        if (block is Paragraph p)
                        {
                            if (result.Count > 0) result.Add(new Run(" "));
                            WalkInlines(p.Inlines, result, ref budget, FontWeights.Normal, FontStyles.Normal, null, null, null);
                        }
                    }
                    return result;
                }
                catch { /* fall through to plain text */ }
            }

            string text = stored.Length > maxChars ? stored.Substring(0, maxChars) + "…" : stored;
            result.Add(new Run(text));
            return result;
        }

        private static void WalkInlines(IEnumerable<Inline> inlines, List<Inline> outList, ref int budget,
            FontWeight fw, FontStyle fst, TextDecorationCollection deco, Brush fg, Brush bg)
        {
            foreach (var inline in inlines)
            {
                if (budget <= 0) break;
                switch (inline)
                {
                    case Run run:
                    {
                        string t = run.Text ?? "";
                        if (t.Length == 0) continue;
                        bool truncated = false;
                        if (t.Length > budget) { t = t.Substring(0, budget) + "…"; budget = 0; truncated = true; }
                        else budget -= t.Length;

                        var newRun = new Run(t)
                        {
                            FontWeight = run.FontWeight != FontWeights.Normal ? run.FontWeight : fw,
                            FontStyle  = run.FontStyle  != FontStyles.Normal  ? run.FontStyle  : fst,
                        };
                        // Local values only — the effective value of a detached FlowDocument is the
                        // system default (black), which would override the list row's theme colour.
                        var effFg = run.ReadLocalValue(TextElement.ForegroundProperty) as Brush ?? fg;
                        var effBg = run.ReadLocalValue(TextElement.BackgroundProperty) as Brush ?? bg;
                        if (effFg != null) newRun.Foreground = effFg;
                        if (effBg != null) newRun.Background = effBg;
                        var effDeco = (run.TextDecorations != null && run.TextDecorations.Count > 0) ? run.TextDecorations : deco;
                        if (effDeco != null && effDeco.Count > 0) newRun.TextDecorations = effDeco;
                        outList.Add(newRun);
                        if (truncated) return;
                        break;
                    }
                    case LineBreak:
                        if (outList.Count > 0) outList.Add(new Run(" "));
                        break;
                    case Bold b:
                        WalkInlines(b.Inlines, outList, ref budget, FontWeights.Bold, fst, deco, fg, bg);
                        break;
                    case Italic it:
                        WalkInlines(it.Inlines, outList, ref budget, fw, FontStyles.Italic, deco, fg, bg);
                        break;
                    case Underline u:
                        WalkInlines(u.Inlines, outList, ref budget, fw, fst, TextDecorations.Underline, fg, bg);
                        break;
                    case Span s:
                        WalkInlines(s.Inlines, outList, ref budget, fw, fst, deco,
                            s.ReadLocalValue(TextElement.ForegroundProperty) as Brush ?? fg,
                            s.ReadLocalValue(TextElement.BackgroundProperty) as Brush ?? bg);
                        break;
                }
            }
        }
    }
}
