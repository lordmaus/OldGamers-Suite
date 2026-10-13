using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace BadgeOverlay
{
    public static class ButtonDetector
    {
        // ── Colour helpers ────────────────────────────────────────────────────
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Color> _colorCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        private static Color C(string hex)
            => _colorCache.GetOrAdd(hex,
                static h => (Color)ColorConverter.ConvertFromString(h));
        private static Color Lighten(Color c)
            => Color.FromRgb(
                (byte)Math.Min(255, c.R + 30),
                (byte)Math.Min(255, c.G + 30),
                (byte)Math.Min(255, c.B + 30));

        /// <summary>
        /// Immutable snapshot of everything the detector reads. Published
        /// atomically so a tick never observes "new settings + old rules"
        /// (or vice versa). UI thread builds a new snapshot and swaps the
        /// reference via Interlocked.Exchange.
        /// </summary>
        internal sealed class DetectorState
        {
            public readonly AppSettings Settings;
            public readonly System.Collections.Generic.List<CustomRule> Rules;
            public DetectorState(AppSettings s, System.Collections.Generic.List<CustomRule> r)
            {
                Settings = s;
                Rules    = r ?? new System.Collections.Generic.List<CustomRule>();
            }
        }

        private static DetectorState _state = new DetectorState(null, null);

        public static AppSettings Settings =>
            System.Threading.Volatile.Read(ref _state).Settings;

        /// <summary>Returns the current atomic snapshot. Callers enumerate
        /// the snapshot's Rules list without further locking.</summary>
        internal static DetectorState State =>
            System.Threading.Volatile.Read(ref _state);

        /// <summary>
        /// Publishes a new atomic (Settings, Rules) pair. Preferred over
        /// calling the legacy setters separately — those can race.
        /// </summary>
        public static void Publish(AppSettings settings,
            System.Collections.Generic.List<CustomRule> rules)
        {
            System.Threading.Interlocked.Exchange(ref _state,
                new DetectorState(settings, rules));
        }

        /// <summary>
        /// Legacy setter — kept for existing callers. Replaces only the
        /// settings half of the snapshot, preserving current rules.
        /// Prefer <see cref="Publish"/> for consistency.
        /// </summary>
        public static void SetSettings(AppSettings settings)
        {
            DetectorState prev;
            DetectorState next;
            do
            {
                prev = System.Threading.Volatile.Read(ref _state);
                next = new DetectorState(settings, prev.Rules);
            } while (System.Threading.Interlocked.CompareExchange(ref _state, next, prev) != prev);
        }

        /// <summary>
        /// Legacy setter — kept for existing callers. Replaces only the
        /// rules half of the snapshot, preserving current settings.
        /// Prefer <see cref="Publish"/> for consistency.
        /// </summary>
        public static void ReloadCustomRules(System.Collections.Generic.List<CustomRule> rules)
        {
            DetectorState prev;
            DetectorState next;
            do
            {
                prev = System.Threading.Volatile.Read(ref _state);
                next = new DetectorState(prev.Settings, rules);
            } while (System.Threading.Interlocked.CompareExchange(ref _state, next, prev) != prev);
        }

        private static Color Col(string hex)  { try { return C(hex); } catch { return C("#89B4FA"); } }

        // Matches ButtonDetector's danger keywords against UIADetector's shared
        // DetectorLabels.DestructiveKeywords list (see that list's doc comment).
        // The list is lowercase; button labels here are original case (Title
        // Case from the OS), so we lowercase once per call.
        private static bool IsDestructive(string label)
        {
            string l = label.ToLowerInvariant();
            foreach (string kw in DetectorLabels.DestructiveKeywords)
                if (l.Contains(kw)) return true;
            return false;
        }

        // Category → colour resolver (reads live settings)
        private static (Color badge, Color border) Theme(string cat, string label)
        {
            if (Settings == null)
                return (C("#89B4FA"), C("#B4BEFE"));

            string hex = cat switch {
                "Chrome"  when label.Contains("Close")                              => Settings.BadgeColorClose,
                "Chrome"  when label.Contains("Minimize")                           => Settings.BadgeColorMinimize,
                "Chrome"                                                            => Settings.BadgeColorMaximize,
                "Action"  when IsDestructive(label)                                => Settings.BadgeColorDanger,
                "Action"                                                            => Settings.BadgeColorAction,
                "Nav"                                                               => Settings.BadgeColorNav,
                "Browser"                                                           => Settings.BadgeColorBrowser,
                _                                                                   => Settings.BadgeColorControl,
            };
            Color col = Col(hex);
            return (col, Lighten(col));
        }

        // Simpler per-label theme for direct callers
        private static DetectedElement B(string label, string cat, string icon, Color badge, Color border)
            => new DetectedElement(label, cat, icon, badge, border);

        private static DetectedElement BT(string label, string cat, string icon)
        {
            var (badge, border) = Theme(cat, label);
            // Override for specific chrome buttons regardless of category string
            if (Settings != null)
            {
                if (label.Contains("Close"))            { badge = Col(Settings.BadgeColorClose);    border = Lighten(badge); }
                else if (label.Contains("Minimize"))    { badge = Col(Settings.BadgeColorMinimize); border = Lighten(badge); }
                else if (label.Contains("Maximize") || label.Contains("Restore"))
                                                        { badge = Col(Settings.BadgeColorMaximize); border = Lighten(badge); }
                else if (cat == "Action" && IsDestructive(label))
                                                        { badge = Col(Settings.BadgeColorDanger);   border = Lighten(badge); }
                else if (cat == "Action")               { badge = Col(Settings.BadgeColorAction);   border = Lighten(badge); }
                else if (cat == "Nav")                  { badge = Col(Settings.BadgeColorNav);      border = Lighten(badge); }
                else if (cat == "Browser")              { badge = Col(Settings.BadgeColorBrowser);  border = Lighten(badge); }
                else                                    { badge = Col(Settings.BadgeColorControl);  border = Lighten(badge); }
            }
            return new DetectedElement(label, cat, icon, badge, border);
        }

        // ── P/Invoke ──────────────────────────────────────────────────────────
        [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr hwnd, NativeMethods.POINT pt, uint flags);

        // ── Per-hwnd SPI cache ────────────────────────────────────────────────
        // WindowFromPoint + GetClass + GetText are called every 33ms. When
        // the cursor lingers over the same window (common — users dwell on
        // buttons), GetClass and GetText return identical values. Caching
        // per hwnd with a short TTL eliminates hundreds of redundant
        // P/Invoke calls per second without making stale data visible.
        //
        // Cache invalidates after CacheTtlMs OR when more than CacheMaxSize
        // distinct hwnds have been seen (simple FIFO eviction to keep the
        // dictionary bounded). Not thread-safe — Detect() is called only
        // from the UI thread.
        private sealed class HwndInfo
        {
            public string Cls;
            public string Text;
            public long   Stamp;   // Environment.TickCount64
        }

        // Concurrent because OpenProcess + UIA worker may eventually call
        // these helpers from background threads; keeping the lock-free
        // ConcurrentDictionary is cheap insurance and avoids the per-tick
        // lock that a Dictionary + lock would impose. Capacity hint 64
        // is sized for typical desktop hwnd reuse over a few seconds.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, HwndInfo> _hwndCache
            = new System.Collections.Concurrent.ConcurrentDictionary<IntPtr, HwndInfo>(
                concurrencyLevel: 1, capacity: 64);
        private const int CacheTtlMs   = 250;   // ≈ 7-8 ticks at 30 Hz
        private const int CacheMaxSize = 128;

        // ── Process-name cache ────────────────────────────────────────────────
        // ProcessId almost never changes for a given hwnd (windows don't
        // migrate between processes), so we cache for much longer than the
        // class/text cache — 30 s is a reasonable balance between freshness
        // after app restart and avoiding GetProcessById on every tick.
        private sealed class ProcInfo
        {
            public string Name;
            public string Path;
            public long   Stamp;
        }
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, ProcInfo> _procCache
            = new System.Collections.Concurrent.ConcurrentDictionary<IntPtr, ProcInfo>(
                concurrencyLevel: 1, capacity: 64);
        private const int ProcCacheTtlMs = 30_000;

        /// <summary>
        /// Returns the friendly process name (no .exe, no path) for the
        /// window under a given hwnd — "chrome", "Code", "notepad".
        /// Cached for 30 s per hwnd. Returns empty string on any failure;
        /// never throws.
        /// </summary>
        public static string GetProcessName(IntPtr hwnd) => GetProcInfo(hwnd).Name;

        /// <summary>
        /// Returns the full executable path for the window under
        /// <paramref name="hwnd"/>. Shares the same 30 s cache as
        /// <see cref="GetProcessName"/>. Empty when access is denied.
        /// </summary>
        public static string GetProcessPath(IntPtr hwnd) => GetProcInfo(hwnd).Path;

        private static ProcInfo GetProcInfo(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return _emptyProcInfo;
            long now = Environment.TickCount64;
            if (_procCache.TryGetValue(hwnd, out var cached)
                && now - cached.Stamp <= ProcCacheTtlMs)
                return cached;

            string name = "", path = "";
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                {
                    // PROCESS_QUERY_LIMITED_INFORMATION (0x1000) is the
                    // minimum-privilege handle that still permits
                    // QueryFullProcessImageName. Granted even for elevated
                    // / protected targets where PROCESS_QUERY_INFORMATION
                    // would fail. No managed Process allocation; no
                    // MainModule read (which throws on cross-bitness or
                    // elevated targets).
                    IntPtr hProc = NativeMethods.OpenProcess(
                        NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                        false,
                        pid);
                    if (hProc != IntPtr.Zero)
                    {
                        try
                        {
                            var sb = new System.Text.StringBuilder(1024);
                            uint cap = (uint)sb.Capacity;
                            if (NativeMethods.QueryFullProcessImageNameW(hProc, 0, sb, ref cap))
                            {
                                path = sb.ToString();
                                // ProcessName == filename without extension.
                                int slash = path.LastIndexOf('\\');
                                int dot   = path.LastIndexOf('.');
                                int start = slash + 1;
                                int end   = dot > slash ? dot : path.Length;
                                if (end > start) name = path.Substring(start, end - start);
                            }
                        }
                        finally { NativeMethods.CloseHandle(hProc); }
                    }
                }
            }
            catch { /* swallow: hwnd may be from a process that exited mid-call */ }

            if (_procCache.Count >= CacheMaxSize) _procCache.Clear();
            var info = new ProcInfo { Name = name, Path = path, Stamp = now };
            _procCache[hwnd] = info;
            return info;
        }
        private static readonly ProcInfo _emptyProcInfo = new ProcInfo { Name = "", Path = "" };

        private static (string cls, string text) GetHwndInfo(IntPtr hwnd)
        {
            long now = Environment.TickCount64;
            if (_hwndCache.TryGetValue(hwnd, out var info))
            {
                if (now - info.Stamp <= CacheTtlMs)
                    return (info.Cls, info.Text);
            }
            // Miss or expired — refresh
            string cls  = NativeMethods.GetClass(hwnd);
            string text = NativeMethods.GetText(hwnd).Trim();
            if (_hwndCache.Count >= CacheMaxSize)
            {
                // Simple bounded-size eviction: clear the whole cache. Cheap
                // and avoids LRU bookkeeping. Users typically interact with
                // far fewer than 128 distinct windows per second.
                _hwndCache.Clear();
            }
            _hwndCache[hwnd] = new HwndInfo { Cls = cls, Text = text, Stamp = now };
            return (cls, text);
        }

        // ── Public entry ──────────────────────────────────────────────────────
        public static DetectedElement Detect(NativeMethods.POINT screenPt)
        {
            IntPtr hwnd = NativeMethods.WindowFromPoint(screenPt);
            if (hwnd == IntPtr.Zero) return null;

            var (cls, text) = GetHwndInfo(hwnd);

            // ── User-defined rules checked first ──────────────────────────────
            // Read the atomic (Settings, Rules) snapshot once. Guarantees
            // rules and settings come from the same publish and never
            // observe a torn pair mid-swap.
            var snapshot = State;
            var rules = snapshot.Rules;
            if (rules.Count > 0)
            {
                foreach (var rule in rules)
                {
                    if (string.IsNullOrEmpty(rule.MatchValue)) continue;
                    bool matched = rule.MatchType == CustomRule.MatchKind.Class
                        ? cls.Equals(rule.MatchValue, StringComparison.OrdinalIgnoreCase)
                        : text.IndexOf(rule.MatchValue, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (matched)
                        return BT(string.IsNullOrEmpty(rule.Label) ? rule.MatchValue : rule.Label,
                                  rule.Category, "◉");
                }
            }

            DetectedElement chrome = CheckTitleBarButtons(hwnd, screenPt)
                                 ?? CheckTitleBarButtons(NativeMethods.GetRootWindow(hwnd), screenPt);
            if (chrome != null) return chrome;

            return ClassifyControl(cls, text);
        }

        // ── Title-bar detection ───────────────────────────────────────────────
        private static DetectedElement CheckTitleBarButtons(IntPtr hwnd, NativeMethods.POINT pt)
        {
            if (hwnd == IntPtr.Zero) return null;
            NativeMethods.RECT wr;
            if (!NativeMethods.GetWindowRect(hwnd, out wr)) return null;
            if (wr.Width < 100 || wr.Height < 30) return null;

            uint dpi = NativeMethods.GetDpiForWindow(hwnd);
            if (dpi == 0) dpi = 96;
            double scale = dpi / 96.0;

            string cls = GetHwndInfo(hwnd).cls;
            bool isChrome = cls.IndexOf("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isCEF    = cls.IndexOf("CEF",              StringComparison.OrdinalIgnoreCase) >= 0;
            bool isElectron = cls.IndexOf("Electron",       StringComparison.OrdinalIgnoreCase) >= 0;
            bool isChromium = isChrome || isCEF || isElectron;

            // Chrome/Edge 2024+ caption height: tab strip takes 33px, caption buttons start at 0
            // Standard Win32 caption: 32px. Add slack for sub-pixel DPI rounding.
            int captionH = (int)((isChromium ? 33 : 32) * scale) + (int)(8 * scale);
            if (pt.Y < wr.Top || pt.Y > wr.Top + captionH) return null;

            // Chrome/Edge 2024+ button widths:
            //   Close = 46px, Maximize = 46px, Minimize = 46px (all at 96dpi)
            //   Edge has a slightly wider close button (50px) in some builds.
            int btnW  = (int)(46 * scale);
            int closeW = (int)(46 * scale);
            int r = wr.Right;  // no right inset — buttons flush to edge in Chromium

            if (pt.X >= r - closeW            && pt.X < r)                  return BT("Close Window",       "Chrome", "✕");
            if (pt.X >= r - closeW - btnW     && pt.X < r - closeW)         return BT("Maximize / Restore", "Chrome", "⬜");
            if (pt.X >= r - closeW - btnW * 2 && pt.X < r - closeW - btnW)  return BT("Minimize",           "Chrome", "─");
            return null;
        }

        // ── Control classification ────────────────────────────────────────────
        private static readonly Dictionary<string, Func<string, DetectedElement>> ClassMap
            = new Dictionary<string, Func<string, DetectedElement>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Button",             ClassifyButtonText },
            { "SysLink",            t => BT("Hyperlink",         "Nav",     "🔗") },
            { "SysTabControl32",    t => BT("Tab Strip",         "Tab",     "📑") },
            { "ToolbarWindow32",    t => BT(Or(t, DetectorLabels.Toolbar),  DetectorLabels.Cat.Toolbar, "🔧") },
            { "ScrollBar",          t => BT("Scroll Bar",        "Control", "↕")  },
            { "Edit",               t => BT("Text Input",        "Input",   "✏")  },
            { "RichEdit20W",        t => BT("Rich Text Editor",  "Input",   "📝") },
            { "RICHEDIT50W",        t => BT("Rich Text Editor",  "Input",   "📝") },
            { "ComboBox",           t => BT("Drop-Down",         "Control", "▼")  },
            { "ListBox",            t => BT("List Box",          "Control", "☰")  },
            { "#32768",             t => BT(Or(t,"Menu Item"),   "Menu",    "☰")  },
            { "msctls_progress32",  t => BT("Progress Bar",      "Control", "▬")  },
            { "msctls_trackbar32",  t => BT("Slider",            "Control", "⊟")  },
            { "msctls_updown32",    t => BT("Spin Control",      "Control", "⇅")  },
            { "SysListView32",      t => BT(DetectorLabels.ListView,   DetectorLabels.Cat.Control, "☰")  },
            { "SysTreeView32",      t => BT(DetectorLabels.TreeView,   DetectorLabels.Cat.Control, "🌲") },
            { "SysHeader32",        t => BT("Column Header",     "Control", "≡")  },
            { "SysDateTimePick32",  t => BT("Date Picker",       "Control", "📅") },
            { "SysMonthCal32",      t => BT("Calendar",          "Control", "📅") },
            { "tooltips_class32",   t => null },  // covers both casing variants via OrdinalIgnoreCase
            { "SysPager",           t => BT("Pager",             "Control", "◀▶") },
            { "msctls_statusbar32", t => BT("Status Bar",        "Control", "─")  },
            { "ReBarWindow32",      t => BT("Rebar",             "Control", "🔧") },
            { "#32769",             t => BT("Desktop",           "System",  "🖥")  },
            { "#32770",             t => BT("Dialog",            "Window",  "⬜") },
            { "Static",             t => string.IsNullOrWhiteSpace(t) ? null : BT(t, "Label", "🏷") },
            { "msctls_hotkey32",    t => BT("Hotkey Input",      "Input",   "⌨")  },
        };

        internal static DetectedElement ClassifyControl(string cls, string text)
        {
            Func<string, DetectedElement> factory;
            if (ClassMap.TryGetValue(cls, out factory))
                return factory(text);

            if (cls.IndexOf("Chrome",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("CEF",      StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("Electron", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Chrome_RenderWidgetHostHWND covers the entire page surface —
                // Win32 GetWindowText returns nothing useful for web buttons.
                // Return null here so the UIA fallback pipeline takes over and
                // reads AutomationElement.Name for the actual hovered element.
                // Title-bar buttons are already handled by CheckTitleBarButtons
                // above before we reach this branch.
                return null;
            }

            if (cls.IndexOf("Tab",      StringComparison.OrdinalIgnoreCase) >= 0) return BT("Tab Control",             DetectorLabels.Cat.Tab,     "📑");
            if (cls.IndexOf("Tree",     StringComparison.OrdinalIgnoreCase) >= 0) return BT(DetectorLabels.TreeView,   DetectorLabels.Cat.Control, "🌲");
            if (cls.IndexOf("List",     StringComparison.OrdinalIgnoreCase) >= 0) return BT(DetectorLabels.ListView,   DetectorLabels.Cat.Control, "☰");
            if (cls.IndexOf("Grid",     StringComparison.OrdinalIgnoreCase) >= 0) return BT(DetectorLabels.DataGrid,   DetectorLabels.Cat.Control, "⊞");
            if (cls.IndexOf("Ribbon",   StringComparison.OrdinalIgnoreCase) >= 0) return BT(DetectorLabels.Ribbon,     DetectorLabels.Cat.Toolbar, "🔧");
            if (cls.IndexOf("Status",   StringComparison.OrdinalIgnoreCase) >= 0) return BT("Status Bar",              DetectorLabels.Cat.Control, "─");
            if (cls.IndexOf("Toolbar",  StringComparison.OrdinalIgnoreCase) >= 0) return BT(DetectorLabels.Toolbar,    DetectorLabels.Cat.Toolbar, "🔧");
            if (cls.IndexOf("Menu",     StringComparison.OrdinalIgnoreCase) >= 0) return BT(Or(text, "Menu Item"),     DetectorLabels.Cat.Menu,    "☰");
            if (cls.IndexOf("Scroll",   StringComparison.OrdinalIgnoreCase) >= 0) return BT("Scroll Bar",              DetectorLabels.Cat.Control, "↕");

            // WPF / XAML native controls
            if (cls == "HwndWrapper" || cls.StartsWith("HwndWrapper"))            return null; // WPF host — classify children
            return null;
        }

        internal static DetectedElement ClassifyButtonText(string text)
        {
            string t = text.ToLowerInvariant();
            if (t.Contains("close"))                                                   return BT("Close",            "Chrome",  "✕");
            if (t.Contains("minimiz"))                                                 return BT("Minimize",          "Chrome",  "─");
            if (t.Contains("maximiz") || t.Contains("restor"))                         return BT("Maximize/Restore",  "Chrome",  "⬜");
            if (t.Contains("cancel"))                                                  return BT("Cancel",            "Action",  "✕");
            if (t.Contains("delete") || t.Contains("remove") || t.Contains("discard"))return BT(text,                "Action",  "🗑");
            if (t == "ok" || t == "yes" || t.Contains("confirm") || t.Contains("accept") || t.Contains("agree"))
                                                                                       return BT(text, "Action", "✓");
            if (t.Trim() == "no")                                                        return BT(text,  "Action",  "✕"); // exact match — Contains("no") misclassified "None"/"Not"/"Nod" as Cancel
            if (t.Contains("save"))                                                    return BT(text, "Action",   "💾");
            if (t.Contains("apply") || t.Contains("submit") || t.Contains("finish") || t.Contains("done"))
                                                                                       return BT(text, "Action", "✓");
            if (t.Contains("send"))                                                    return BT(text, "Action",   "✉");
            if (t.Contains("install") || t.Contains("update"))                         return BT(text, "Action",   "🔄");
            if (t.Contains("back") || t.Contains("prev"))                              return BT(text, "Nav",      "◀");
            if (t.Contains("next") || t.Contains("forward"))                           return BT(text, "Nav",      "▶");
            if (t.Contains("home"))                                                    return BT(text, "Nav",      "⌂");
            if (t.Contains("refresh") || t.Contains("reload"))                         return BT(text, "Nav",      "↺");
            if (t.Contains("new tab") || t.Contains("newtab"))                         return BT("New Tab","Tab",  "➕");
            if (t.Contains("tab"))                                                     return BT(text, "Tab",      "📑");
            if (t.Contains("open"))                                                    return BT(text, "Action",   "📂");
            if (t.Contains("new") || t.Contains("add") || t.Contains("create"))       return BT(text, "Action",   "+");
            if (t.Contains("import"))                                                  return BT(text, "Action",   "📥");
            if (t.Contains("export"))                                                  return BT(text, "Action",   "📤");
            if (t.Contains("print"))                                                   return BT(text, "Action",   "🖨");
            if (t.Contains("copy"))                                                    return BT(text, "Action",   "📋");
            if (t.Contains("paste"))                                                   return BT(text, "Action",   "📋");
            if (t.Contains("cut"))                                                     return BT(text, "Action",   "✂");
            if (t.Contains("undo"))                                                    return BT(text, "Action",   "↩");
            if (t.Contains("redo"))                                                    return BT(text, "Action",   "↪");
            if (t.Contains("search") || t.Contains("find"))                            return BT(text, "Nav",      "🔍");
            if (t.Contains("filter"))                                                  return BT(text, "Nav",      "⊏");
            if (t.Contains("sort"))                                                    return BT(text, "Control",  "⇅");
            if (t.Contains("setting") || t.Contains("option") || t.Contains("prefer") || t.Contains("config"))
                                                                                       return BT(text, "Settings", "⚙");
            if (t.Contains("help"))                                                    return BT(text, "Info",     "?");
            if (t.Contains("about"))                                                   return BT(text, "Info",     "ℹ");
            if (t.Contains("login") || t.Contains("sign in"))                          return BT(text, "Account",  "🔑");
            if (t.Contains("logout") || t.Contains("sign out"))                        return BT(text, "Account",  "🚪");
            if (t.Contains("browse") || t.Contains("...") || text == "...")            return BT(text, "Action",   "📂");
            if (t.Contains("expand") || t.Contains("more"))                            return BT(text, "Control",  "▼");
            if (t.Contains("collapse") || t.Contains("less"))                          return BT(text, "Control",  "▲");
            if (t.Contains("run") || t.Contains("start") || t.Contains("launch") || t.Contains("execute"))
                                                                                       return BT(text, "Action",   "▶");
            if (t.Contains("stop") || t.Contains("pause"))                             return BT(text, "Action",   "⏹");
            if (t.Contains("reset"))                                                   return BT(text, "Action",   "↺");
            if (!string.IsNullOrWhiteSpace(text))                                      return BT(text, "Control",  "◉");
            return BT("Button", "Control", "◉");
        }

        private static string Or(string text, string fallback)
            => string.IsNullOrWhiteSpace(text) ? fallback : text;
    }
}
