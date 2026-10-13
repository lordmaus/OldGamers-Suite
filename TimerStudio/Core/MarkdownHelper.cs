using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TimerStudio
{
    /// <summary>
    /// F22: Minimal Markdown → WPF FlowDocument converter.
    /// Supports: **bold**, *italic*, `code`, # H1/## H2, - bullets, blank-line paragraphs.
    /// Not a full parser — aims for the common note-taking subset.
    /// </summary>
    public static class MarkdownHelper
    {
        // Compiled patterns
        private static readonly Regex _boldRegex   = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex _italicRegex = new Regex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled);
        private static readonly Regex _codeRegex   = new Regex(@"`(.+?)`", RegexOptions.Compiled);

        /// <summary>
        /// Renders <paramref name="markdown"/> into the supplied <paramref name="target"/> RichTextBox.
        /// Clears existing content first.
        /// </summary>
        public static void RenderInto(string markdown, RichTextBox target, Brush textBrush, Brush mutedBrush, Brush codeBrush)
        {
            if (target == null) return;
            var doc = new FlowDocument();
            doc.FontFamily = new FontFamily("Segoe UI");
            doc.FontSize   = 13;
            doc.Foreground = textBrush;
            doc.PagePadding = new Thickness(0);

            if (string.IsNullOrEmpty(markdown))
            {
                target.Document = doc;
                return;
            }

            var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            Paragraph currentPara = null;

            foreach (string rawLine in lines)
            {
                string line = rawLine;

                // Blank line → flush paragraph
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentPara != null) { doc.Blocks.Add(currentPara); currentPara = null; }
                    continue;
                }

                // ── Headings ─────────────────────────────────────────────────
                if (line.StartsWith("### "))
                {
                    FlushPara(doc, ref currentPara);
                    var h = new Paragraph { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = textBrush, Margin = new Thickness(0, 6, 0, 2) };
                    AddInlines(h.Inlines, line.Substring(4), textBrush, codeBrush);
                    doc.Blocks.Add(h);
                    continue;
                }
                if (line.StartsWith("## "))
                {
                    FlushPara(doc, ref currentPara);
                    var h = new Paragraph { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = textBrush, Margin = new Thickness(0, 8, 0, 2) };
                    AddInlines(h.Inlines, line.Substring(3), textBrush, codeBrush);
                    doc.Blocks.Add(h);
                    continue;
                }
                if (line.StartsWith("# "))
                {
                    FlushPara(doc, ref currentPara);
                    var h = new Paragraph { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = textBrush, Margin = new Thickness(0, 10, 0, 4) };
                    AddInlines(h.Inlines, line.Substring(2), textBrush, codeBrush);
                    doc.Blocks.Add(h);
                    continue;
                }

                // ── Bullet list (- or * at start) ────────────────────────────
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    FlushPara(doc, ref currentPara);
                    var p = new Paragraph { Margin = new Thickness(12, 1, 0, 1) };
                    p.Inlines.Add(new Run("• ") { Foreground = mutedBrush });
                    AddInlines(p.Inlines, line.Substring(2), textBrush, codeBrush);
                    doc.Blocks.Add(p);
                    continue;
                }

                // ── Horizontal rule ───────────────────────────────────────────
                if (line == "---" || line == "***" || line == "___")
                {
                    FlushPara(doc, ref currentPara);
                    doc.Blocks.Add(new BlockUIContainer(new System.Windows.Shapes.Rectangle
                        { Height = 1, Fill = mutedBrush, Margin = new Thickness(0, 4, 0, 4) }));
                    continue;
                }

                // ── Normal paragraph / continuation ───────────────────────────
                if (currentPara == null) currentPara = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                else currentPara.Inlines.Add(new LineBreak());
                AddInlines(currentPara.Inlines, line, textBrush, codeBrush);
            }

            FlushPara(doc, ref currentPara);

            target.Document = doc;
        }

        private static void FlushPara(FlowDocument doc, ref Paragraph p)
        {
            if (p != null) { doc.Blocks.Add(p); p = null; }
        }

        /// <summary>Parses inline formatting tokens and adds them to <paramref name="inlines"/>.</summary>
        private static void AddInlines(InlineCollection inlines, string text, Brush textBrush, Brush codeBrush)
        {
            // We process token by token using a simple state machine.
            // Patterns: **bold**, *italic*, `code`
            int i = 0;
            int len = text.Length;
            var plain = new System.Text.StringBuilder();

            void FlushPlain()
            {
                if (plain.Length > 0)
                {
                    inlines.Add(new Run(plain.ToString()) { Foreground = textBrush });
                    plain.Clear();
                }
            }

            while (i < len)
            {
                // **bold**
                if (i + 1 < len && text[i] == '*' && text[i + 1] == '*')
                {
                    int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        FlushPlain();
                        inlines.Add(new Run(text.Substring(i + 2, end - i - 2))
                            { FontWeight = FontWeights.Bold, Foreground = textBrush });
                        i = end + 2;
                        continue;
                    }
                }
                // *italic* (not **)
                if (text[i] == '*' && (i + 1 >= len || text[i + 1] != '*'))
                {
                    int end = -1;
                    for (int j = i + 1; j < len; j++)
                        if (text[j] == '*' && (j + 1 >= len || text[j + 1] != '*'))
                        { end = j; break; }
                    if (end >= 0)
                    {
                        FlushPlain();
                        inlines.Add(new Run(text.Substring(i + 1, end - i - 1))
                            { FontStyle = FontStyles.Italic, Foreground = textBrush });
                        i = end + 1;
                        continue;
                    }
                }
                // `code`
                if (text[i] == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end >= 0)
                    {
                        FlushPlain();
                        var run = new Run(text.Substring(i + 1, end - i - 1))
                        {
                            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                            FontSize   = 12,
                            Foreground = codeBrush
                        };
                        var span = new Span(run);
                        span.Background = new SolidColorBrush(Color.FromArgb(40, 100, 100, 150));
                        inlines.Add(span);
                        i = end + 1;
                        continue;
                    }
                }
                plain.Append(text[i++]);
            }
            FlushPlain();
        }
    }
}
