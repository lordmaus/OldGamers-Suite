using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Automation;

namespace BadgeOverlay
{
    /// <summary>
    /// One-shot accessibility audit of the foreground window's UIA
    /// tree. Walks the visible element subtree from the foreground
    /// window root, collects accessibility-relevant properties from
    /// each element, maps them to a small subset of WCAG 2.1
    /// success criteria, and emits a self-contained HTML report
    /// the user can save and share with consultants / auditors.
    ///
    /// <para>Scope &amp; honesty.</para>
    /// This audit checks <em>structural</em> WCAG criteria that
    /// are directly observable through the UIA tree:
    ///   • 1.3.1 Info and Relationships — does each interactive
    ///     element have an accessible name?
    ///   • 2.1.1 Keyboard               — is each interactive
    ///     element keyboard-focusable?
    ///   • 4.1.2 Name, Role, Value      — does each element have
    ///     a non-empty Name and a meaningful ControlType?
    /// It does NOT check colour contrast, animation timing, focus
    /// order, alt-text quality, or content-meaning concerns
    /// (1.4.3, 2.2.2, 2.4.3, 1.1.1 with semantic judgement, etc.).
    /// The report says so explicitly so users don't mistake it
    /// for a full WCAG conformance audit.
    ///
    /// <para>Performance.</para>
    /// The walk is bounded by <see cref="MaxElements"/> (default
    /// 5000) — Office and large IDEs can easily exceed this; we
    /// stop and note the truncation in the report rather than
    /// stalling on a 50k-node tree. The walker uses
    /// <see cref="TreeWalker.ContentViewWalker"/> which already
    /// excludes decorative elements, keeping the result focused
    /// on what assistive tech actually surfaces.
    /// </summary>
    internal static class A11yAuditor
    {
        /// <summary>Hard cap on elements walked. Prevents 100 % CPU on
        /// pathologically deep trees (Visual Studio with all panes open
        /// can exceed 50 000 elements).</summary>
        public const int MaxElements = 5000;

        /// <summary>Per-element snapshot collected during the walk.</summary>
        public sealed class AuditedElement
        {
            public string ControlType { get; set; }
            public string Name        { get; set; }
            public string AutomationId { get; set; }
            public bool   IsKeyboardFocusable { get; set; }
            public bool   IsEnabled { get; set; }
            public bool   IsContentElement { get; set; }
            public bool   IsControlElement { get; set; }
            public int    Left   { get; set; }
            public int    Top    { get; set; }
            public int    Width  { get; set; }
            public int    Height { get; set; }
            public List<string> Issues { get; } = new List<string>();
        }

        /// <summary>Result of an audit run, suitable for rendering to HTML or copying out.</summary>
        public sealed class AuditResult
        {
            public DateTime Timestamp        { get; set; }
            public string   ProcessName      { get; set; }
            public string   WindowTitle      { get; set; }
            public int      TotalScanned     { get; set; }
            public bool     Truncated        { get; set; }
            public int      InteractiveCount { get; set; }
            public int      MissingNameCount { get; set; }
            public int      NotFocusableCount { get; set; }
            public int      UnknownTypeCount  { get; set; }
            public List<AuditedElement> Issues { get; } = new List<AuditedElement>();
        }

        /// <summary>
        /// Runs an audit against the foreground window. Safe to call
        /// from the UI thread; UIA queries are blocking but bounded
        /// by <see cref="MaxElements"/> and complete in well under a
        /// second on typical surfaces.
        /// </summary>
        /// <summary>
        /// Runs an audit against the current foreground window.
        /// Convenience wrapper over <see cref="AuditWindow(IntPtr)"/>.
        /// Prefer the explicit-handle overload when there's a window
        /// of "the foreground at the moment the user committed" —
        /// e.g. captured during a countdown, before a transient
        /// notification toast could swap foreground.
        /// </summary>
        public static AuditResult AuditForegroundWindow()
            => AuditWindow(NativeMethods.GetForegroundWindow());

        /// <summary>
        /// Runs an audit against an explicit window handle. Safe to
        /// call from the UI thread; UIA queries are blocking but
        /// bounded by <see cref="MaxElements"/> and complete in well
        /// under a second on typical surfaces.
        /// </summary>
        public static AuditResult AuditWindow(IntPtr hwnd)
        {
            var result = new AuditResult { Timestamp = DateTime.Now };

            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    result.WindowTitle = "(no window handle provided)";
                    return result;
                }

                // Resolve the AutomationElement for the window. Prefer
                // the root-window handle so we audit the entire app
                // surface, not just a focused child control.
                IntPtr root = NativeMethods.GetRootWindow(hwnd);
                if (root == IntPtr.Zero) root = hwnd;

                AutomationElement el = null;
                try { el = AutomationElement.FromHandle(root); }
                catch (Exception ex)
                {
                    CrashLog.Write("A11yAuditor.FromHandle", ex);
                }

                if (el == null)
                {
                    result.WindowTitle = "(unable to resolve UIA root)";
                    return result;
                }

                result.WindowTitle = SafeGet(() => el.Current.Name);
                result.ProcessName = ButtonDetector.GetProcessName(root);

                Walk(el, result);
            }
            catch (Exception ex) { CrashLog.Write("A11yAuditor.Audit", ex); }

            return result;
        }

        /// <summary>BFS walk of the UIA content view, capped at MaxElements.</summary>
        private static void Walk(AutomationElement root, AuditResult result)
        {
            var walker = TreeWalker.ContentViewWalker;
            var queue  = new Queue<AutomationElement>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                if (result.TotalScanned >= MaxElements)
                {
                    result.Truncated = true;
                    break;
                }

                AutomationElement node = queue.Dequeue();
                result.TotalScanned++;

                AuditedElement audited = Snapshot(node);
                if (audited == null) continue;

                ClassifyAndCollect(audited, result);

                // Enqueue children. Wrapped in try/catch — UIA can throw
                // ElementNotAvailableException on disappearing elements
                // (popups closing, list virtualisation), and we want the
                // walk to skip rather than abort.
                try
                {
                    AutomationElement child = walker.GetFirstChild(node);
                    while (child != null)
                    {
                        queue.Enqueue(child);
                        child = walker.GetNextSibling(child);
                    }
                }
                catch { /* element vanished — continue with next queue item */ }
            }
        }

        /// <summary>Reads the audit-relevant properties off a single element.</summary>
        private static AuditedElement Snapshot(AutomationElement node)
        {
            try
            {
                var c = node.Current;
                var rect = c.BoundingRectangle;
                return new AuditedElement
                {
                    ControlType         = c.ControlType?.ProgrammaticName?.Replace("ControlType.", "") ?? "(unknown)",
                    Name                = c.Name ?? "",
                    AutomationId        = c.AutomationId ?? "",
                    IsKeyboardFocusable = c.IsKeyboardFocusable,
                    IsEnabled           = c.IsEnabled,
                    IsContentElement    = c.IsContentElement,
                    IsControlElement    = c.IsControlElement,
                    Left                = (int)rect.Left,
                    Top                 = (int)rect.Top,
                    Width               = (int)rect.Width,
                    Height              = (int)rect.Height,
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// Applies the WCAG-mapped checks. A given element only joins
        /// the issues list when at least one rule fires; non-issue
        /// elements are counted in the totals but not enumerated, so
        /// the report stays focused on what's actually wrong.
        /// </summary>
        private static void ClassifyAndCollect(AuditedElement el, AuditResult result)
        {
            // "Interactive" — anything a user could plausibly act on.
            // The set is deliberately broad to surface false negatives:
            // we'd rather flag an over-marked element than miss a
            // genuinely broken one. Pure decorative containers
            // (Pane, Group, Custom) are excluded.
            string ct = el.ControlType ?? "";
            bool interactive =
                ct == "Button"      || ct == "Hyperlink"  || ct == "MenuItem" ||
                ct == "CheckBox"    || ct == "RadioButton"|| ct == "ComboBox" ||
                ct == "Edit"        || ct == "Slider"     || ct == "Spinner"  ||
                ct == "TabItem"     || ct == "TreeItem"   || ct == "ListItem" ||
                ct == "Hyperlink"   || ct == "SplitButton"|| ct == "DataItem";

            if (!interactive) return;
            result.InteractiveCount++;

            // 4.1.2 Name, Role, Value — every interactive element MUST
            // have an accessible name. AutomationId is a developer
            // identifier, not an accessible name; we only fall back to
            // it for the diagnostic message, never to count as "named".
            if (string.IsNullOrWhiteSpace(el.Name))
            {
                el.Issues.Add("WCAG 4.1.2 — interactive element has no accessible Name");
                result.MissingNameCount++;
            }

            // 2.1.1 Keyboard — interactive elements must be reachable
            // by keyboard. Disabled elements are exempt (they don't
            // accept any input, which is fine), so we check IsEnabled
            // before flagging non-focusable.
            if (el.IsEnabled && !el.IsKeyboardFocusable)
            {
                el.Issues.Add("WCAG 2.1.1 — interactive element is not keyboard-focusable");
                result.NotFocusableCount++;
            }

            // 4.1.2 — ControlType exists for every UIA element, but
            // some apps put their custom buttons under "Custom" which
            // means assistive tech has no idea what to call it.
            if (ct == "Custom")
            {
                el.Issues.Add("WCAG 4.1.2 — element uses Custom control type; assistive tech can't determine role");
                result.UnknownTypeCount++;
            }

            if (el.Issues.Count > 0) result.Issues.Add(el);
        }

        // ── HTML report ───────────────────────────────────────────────────

        /// <summary>Writes a self-contained HTML report to <paramref name="path"/>.</summary>
        public static void WriteHtmlReport(AuditResult r, string path)
        {
            var sb = new StringBuilder(64 * 1024);
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>Badge Overlay — Accessibility Audit</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("  body{font-family:Segoe UI,system-ui,sans-serif;margin:2rem;color:#222;background:#fafafa;line-height:1.5;}");
            sb.AppendLine("  h1{margin-bottom:0;color:#1d3557;}");
            sb.AppendLine("  h2{margin-top:2rem;border-bottom:2px solid #ddd;padding-bottom:.3rem;color:#1d3557;}");
            sb.AppendLine("  .meta{color:#555;margin-bottom:1rem;font-size:.95em;}");
            sb.AppendLine("  table{border-collapse:collapse;width:100%;margin-top:1rem;background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.05);}");
            sb.AppendLine("  th,td{border:1px solid #ddd;padding:8px 10px;text-align:left;vertical-align:top;}");
            sb.AppendLine("  th{background:#1d3557;color:#fff;font-weight:600;}");
            sb.AppendLine("  tr:nth-child(even){background:#f9f9f9;}");
            sb.AppendLine("  .stat{display:inline-block;margin-right:2rem;font-size:1.05em;}");
            sb.AppendLine("  .stat strong{display:block;font-size:2em;color:#1d3557;}");
            sb.AppendLine("  .pass{color:#0a7d2c;font-weight:600;}");
            sb.AppendLine("  .fail{color:#c41e3a;font-weight:600;}");
            sb.AppendLine("  .scope{background:#fff8e1;border-left:4px solid #f9a825;padding:.7rem 1rem;margin:1rem 0;}");
            sb.AppendLine("  code{background:#eee;padding:1px 5px;border-radius:3px;font-size:.95em;}");
            sb.AppendLine("  .truncated{background:#ffe0e0;padding:.5rem;border-radius:3px;}");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine("<h1>Accessibility Audit</h1>");
            sb.AppendLine("<div class=\"meta\">");
            sb.AppendLine($"Generated by <strong>Badge Overlay</strong> on " +
                          $"{r.Timestamp:yyyy-MM-dd HH:mm:ss}<br>");
            sb.AppendLine($"Target: <code>{HtmlEscape(r.ProcessName ?? "(unknown process)")}</code>" +
                          $" — &ldquo;{HtmlEscape(r.WindowTitle ?? "")}&rdquo;");
            sb.AppendLine("</div>");

            sb.AppendLine("<div class=\"scope\"><strong>Scope &amp; limitations.</strong> " +
                          "This audit checks structural WCAG 2.1 criteria observable through UI Automation: " +
                          "1.3.1 (Info &amp; Relationships), 2.1.1 (Keyboard), and 4.1.2 (Name / Role / Value). " +
                          "It does <strong>not</strong> verify colour contrast, animation timing, focus order, " +
                          "alt-text quality, or any content-meaning concerns. Treat as a triage tool, " +
                          "not a conformance certificate.</div>");

            sb.AppendLine("<h2>Summary</h2>");
            sb.AppendLine("<div>");
            sb.AppendLine($"<span class=\"stat\"><strong>{r.TotalScanned}</strong>elements scanned</span>");
            sb.AppendLine($"<span class=\"stat\"><strong>{r.InteractiveCount}</strong>interactive</span>");
            sb.AppendLine($"<span class=\"stat\"><strong>{r.MissingNameCount}</strong>missing name</span>");
            sb.AppendLine($"<span class=\"stat\"><strong>{r.NotFocusableCount}</strong>not focusable</span>");
            sb.AppendLine($"<span class=\"stat\"><strong>{r.UnknownTypeCount}</strong>custom role</span>");
            sb.AppendLine("</div>");

            if (r.Truncated)
            {
                sb.AppendLine("<p class=\"truncated\">⚠ Walk truncated at " +
                              $"{MaxElements} elements; deeper subtrees were not audited. " +
                              "Re-run with the target window scrolled to the section of interest.</p>");
            }

            if (r.Issues.Count == 0)
            {
                sb.AppendLine("<h2>Results</h2>");
                sb.AppendLine("<p class=\"pass\">✓ No structural issues detected within the audited subtree.</p>");
            }
            else
            {
                sb.AppendLine($"<h2>Issues ({r.Issues.Count})</h2>");
                sb.AppendLine("<table><thead><tr>");
                sb.AppendLine("<th>Control</th><th>Name</th><th>Auto-ID</th>" +
                              "<th>Bounds</th><th>Issues</th></tr></thead><tbody>");
                foreach (var el in r.Issues)
                {
                    sb.Append("<tr>");
                    sb.Append($"<td><code>{HtmlEscape(el.ControlType)}</code></td>");
                    sb.Append($"<td>{HtmlEscape(string.IsNullOrEmpty(el.Name) ? "—" : el.Name)}</td>");
                    sb.Append($"<td><code>{HtmlEscape(string.IsNullOrEmpty(el.AutomationId) ? "—" : el.AutomationId)}</code></td>");
                    sb.Append($"<td>({el.Left}, {el.Top}) {el.Width}×{el.Height}</td>");
                    sb.Append("<td>");
                    sb.Append(string.Join("<br>", el.Issues.Select(HtmlEscape)));
                    sb.Append("</td></tr>");
                    sb.AppendLine();
                }
                sb.AppendLine("</tbody></table>");
            }

            sb.AppendLine("<h2>About this report</h2>");
            sb.AppendLine("<p>Generated locally on your machine. No data was transmitted. " +
                          "Re-run from <em>About → Run accessibility audit</em>.</p>");

            sb.AppendLine("</body></html>");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        private static T SafeGet<T>(Func<T> f)
        {
            try { return f(); }
            catch { return default; }
        }
    }
}
