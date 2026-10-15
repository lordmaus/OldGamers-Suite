using System;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Media;

namespace BadgeOverlay
{
    /// <summary>
    /// Background-thread UI Automation detector.
    ///
    /// Polls AutomationElement.FromPoint() on a dedicated thread throttled to
    /// UIAThrottleMs. The overlay timer reads the last result via a volatile
    /// snapshot — no blocking, no UI thread involvement.
    ///
    /// Covers everything Win32 GetWindowText misses:
    ///   • Office ribbon buttons  (NetUIRibbonButton / NetUIElement)
    ///   • Chrome / Edge toolbar  (ChromeRenderWidgetHostHWND)
    ///   • WPF / UWP controls     (HwndWrapper / Windows.UI.Core)
    ///   • File Explorer commands
    ///   • Windows 11 taskbar / Start
    ///   • Any app that sets AutomationProperties.Name
    /// </summary>
    public sealed class UIADetector : IDisposable
    {
        // ── Shared state — written by BG thread, read by UI thread ────────────

        private volatile DetectedElement _latest;
        private volatile bool      _enabled = true;
        private volatile int       _throttleMs = 150;
        private volatile int       _granularity = 0; // DetectionGranularity cast to int (volatile enum n/a)

        // ── Threading ─────────────────────────────────────────────────────────

        private readonly Thread                  _thread;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        /// <summary>Signalled to request an immediate wake (shutdown / settings).</summary>
        private readonly ManualResetEventSlim    _wake = new ManualResetEventSlim(false);
        private volatile bool                    _disposed;
        private          int                     _lastX, _lastY;
        private          int                     _queryCount;

        // ── Constructor ───────────────────────────────────────────────────────

        public UIADetector()
        {
            _thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name         = "UIADetector",
                Priority     = ThreadPriority.BelowNormal,
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        // ── Public API (called from UI thread) ────────────────────────────────

        /// <summary>Apply updated settings without restarting the thread.</summary>
        public void ApplySettings(AppSettings s)
        {
            _enabled     = s.EnableUIADetection;
            // Throttle floor 100ms for responsiveness on browser buttons.
            // Combined with cursor-dwell + single-query-per-dwell, this keeps
            // UIA traffic minimal while giving a snappy badge response.
            _throttleMs  = Math.Max(100, Math.Min(2000, s.UIAThrottleMs));

            int newGran = (int)s.Granularity;
            if (newGran != _granularity)
            {
                // Drop the cached snapshot so the UI doesn't show a stale
                // element that the new filter would have rejected. The
                // next worker tick re-queries under the new filter.
                _latest = null;
                _granularity = newGran;
            }

            // Wake the worker so a throttle change or enable toggle takes
            // effect immediately instead of after the current Sleep returns.
            _wake.Set();
        }

        /// <summary>
        /// Returns the latest UIA result, or null if UIA found nothing useful
        /// or is disabled. Called from OverlayWindow.OnTick() on the UI thread.
        ///
        /// Callers that care about staleness should use
        /// <see cref="GetLatestFor"/> instead — it returns null when the
        /// cursor has moved far enough from where UIA last queried that the
        /// snapshot no longer applies to what's under the cursor now.
        /// </summary>
        public DetectedElement Latest => _latest;

        /// <summary>
        /// Maximum pixel distance (Chebyshev) between a UIA snapshot's origin
        /// and the current cursor for the snapshot to still be considered
        /// valid. Beyond this, the snapshot is "stale" and must not be
        /// displayed as the label for the current cursor position.
        /// </summary>
        public const int StalenessPixels = 8;

        /// <summary>
        /// Cursor movement (in pixels, Chebyshev) below this threshold is
        /// treated as jitter — we reuse the existing UIA snapshot rather
        /// than re-querying cross-process. Set smaller than
        /// <see cref="StalenessPixels"/> so the consumer's staleness
        /// guard still trips when the cursor genuinely drifts.
        /// </summary>
        public const int JitterPixels = 2;

        /// <summary>
        /// Returns the latest UIA result IF it applies to the current
        /// cursor position (cursor has moved &lt; StalenessPixels since
        /// the snapshot was taken). Otherwise null.
        /// </summary>
        public DetectedElement GetLatestFor(int cursorX, int cursorY)
        {
            DetectedElement r = _latest;
            if (r == null) return null;
            int dx = Math.Abs(cursorX - r.OriginX);
            int dy = Math.Abs(cursorY - r.OriginY);
            return (dx <= StalenessPixels && dy <= StalenessPixels) ? r : null;
        }

        // ── Power state ───────────────────────────────────────────────────

        /// <summary>
        /// True when the OS reports running on battery (AC power
        /// disconnected). Used by the worker loop to slow the UIA poll
        /// rate from <see cref="UiConstants.UiaThrottleMs"/> to
        /// <see cref="UiConstants.UiaThrottleBatteryMs"/>. Cached for
        /// 5 s — power state changes are infrequent and the
        /// GetSystemPowerStatus syscall, while cheap, isn't free.
        /// Returns false (= treat as AC) on syscall failure.
        /// </summary>
        private static bool IsOnBattery()
        {
            try
            {
                int now = Environment.TickCount;   // int wraps every ~25 days; fine for 5 s cache
                if (now - _powerCacheStamp < 5_000) return _powerCachedBattery;

                if (NativeMethods.GetSystemPowerStatus(out var st))
                {
                    // ACLineStatus: 0 = offline (battery), 1 = online (AC),
                    // 255 = unknown. Treat unknown as AC (conservative —
                    // user's likely on a desktop without battery sensor).
                    _powerCachedBattery = st.ACLineStatus == 0;
                }
                else
                {
                    _powerCachedBattery = false;
                }
                _powerCacheStamp = now;
                return _powerCachedBattery;
            }
            catch
            {
                return false;
            }
        }
        private static volatile int  _powerCacheStamp;
        private static volatile bool _powerCachedBattery;

        // ── Background worker ─────────────────────────────────────────────────

        private void WorkerLoop()
        {
            // Warm up UIA — first call loads UIAutomationCore.dll cross-process
            try { var _ = AutomationElement.RootElement; } catch { }

            CancellationToken ct = _cts.Token;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Cancellable wait. Re-read _throttleMs every iteration so
                    // a mid-wait settings change takes effect on the next tick
                    // rather than waiting out the old (potentially 2 s) value.
                    //
                    // Battery adaptation: when the OS reports we're on
                    // battery, slow the poll rate to UiaThrottleBatteryMs
                    // (250 ms vs. 150 ms default). Saves measurable CPU
                    // wake-ups on laptops without making the badge feel
                    // sluggish; UIA queries are the dominant CPU cost.
                    // Powerline status is cached by the Win32 GetSystemPowerStatus
                    // call — overhead is negligible per tick.
                    int thisThrottle = _throttleMs;
                    if (IsOnBattery() && thisThrottle < UiConstants.UiaThrottleBatteryMs)
                        thisThrottle = UiConstants.UiaThrottleBatteryMs;
                    _wake.Wait(thisThrottle, ct);
                    // Always clear — even if wait returned by timeout. Leaving
                    // it set after a cancellation-during-wake causes the next
                    // (already-cancelled) loop to spin once unnecessarily.
                    if (_wake.IsSet) _wake.Reset();
                    if (ct.IsCancellationRequested) break;

                    if (!_enabled) { _latest = null; continue; }

                    NativeMethods.POINT pt;
                    if (!NativeMethods.GetCursorPos(out pt)) continue;

                    // Skip re-query if cursor hasn't moved and we already
                    // have a valid result — saves the cross-process COM
                    // round-trip. No dwell-wait — we want fast detection
                    // over taskbar / tray / icons.
                    // Skip re-query if the cursor has only moved a pixel or
                    // two since the last successful query AND we still have
                    // a recent result. Human pointing hands oscillate 1-2 px
                    // naturally; re-querying UIA for every jitter wastes a
                    // full cross-process COM round-trip. Exact match above
                    // covered the no-move case; this extends it to minor
                    // jitter. Larger moves still fall through to a fresh
                    // query so badge accuracy isn't harmed.
                    if (_latest != null)
                    {
                        int dx = pt.X - _lastX;
                        int dy = pt.Y - _lastY;
                        if (Math.Abs(dx) <= JitterPixels && Math.Abs(dy) <= JitterPixels)
                            continue;
                    }

                    _lastX = pt.X;
                    _lastY = pt.Y;

                    DetectedElement result = QueryPoint(pt.X, pt.Y, _granularity);
                    if (result != null)
                    {
                        // Stamp origin so consumers can detect staleness when
                        // the cursor has moved far from where we queried.
                        result.OriginX = pt.X;
                        result.OriginY = pt.Y;
                    }
                    _latest = result;

                    // ── Periodic COM RCW flush ────────────────────────────
                    // Each QueryPoint call creates up to ~70 AutomationElement
                    // COM-backed managed wrappers (1 from FromPoint + up to
                    // DrillMaxNodes=64 from tree walk + pattern objects).
                    // SustainedLowLatency GC mode suppresses Gen2 to reduce
                    // UI hitches, which lets Gen0/1 objects promote instead
                    // of being swept — causing hundreds of MB to accumulate
                    // over long sessions.
                    //
                    // Every 100 successful queries we ask for an optimised
                    // Gen1 collection on this background MTA thread.
                    // blocking:false = concurrent sweep → no thread stalls.
                    // Gen1 covers recently-promoted COM wrappers without
                    // the cost of a full Gen2 compaction.
                    if (++_queryCount >= 100)
                    {
                        _queryCount = 0;
                        GC.Collect(1, GCCollectionMode.Optimized,
                                   blocking:   false,
                                   compacting: false);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ThreadAbortException)       { break; }
                catch (Exception ex)
                {
                    // UIA can throw transiently on process exit, UAC switch,
                    // or a broken provider. Log (deduped by AppLog) and
                    // continue — do not kill the worker. AppLog.Error
                    // is self-swallowing, no outer guard needed.
                    AppLog.Error("UIADetector.WorkerLoop", ex);
                }
            }
        }

        // ── UIA query ─────────────────────────────────────────────────────────

        private static DetectedElement QueryPoint(int x, int y, int granularity)
        {
            AutomationElement el;
            try
            {
                el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            }
            catch { return null; }

            if (el == null) return null;

            // If the immediate hit is a generic container (Window / Pane /
            // Group / Document) with no useful name, walk ancestors and
            // the focused/children tree to find a meaningful labelled
            // element. Common on taskbar, tray, File Explorer list views,
            // and custom frames.
            el = ResolveUsefulElement(el);
            if (el == null) return null;

            string name      = SafeGet(() => el.Current.Name)?.Trim()         ?? "";
            string className = SafeGet(() => el.Current.ClassName)?.Trim()    ?? "";
            int    ctrlType  = SafeGet(() => el.Current.ControlType.Id);
            bool   enabled   = SafeGet(() => el.Current.IsEnabled);
            string helpText  = SafeGet(() => el.Current.HelpText)?.Trim()     ?? "";

            // Skip our own overlay window
            if (className.StartsWith("HwndWrapper") && name.Contains("Badge Overlay"))
                return null;

            // Skip tooltips
            if (ctrlType == ControlType.ToolTip.Id) return null;

            // If still no name after walk, nothing useful here
            if (string.IsNullOrWhiteSpace(name) || name.Length <= 1) return null;

            // ── Granularity filter ────────────────────────────────────────────
            // Cheap check BEFORE we read any more UIA properties. When the
            // filter rejects, we short-circuit and avoid the 6-9 extra
            // cross-process COM reads for patterns and metadata.
            if (!PassesGranularityFilter(el, ctrlType, granularity)) return null;

            string category = ClassifyUIA(name, className, ctrlType, enabled);
            string icon     = IconForCategory(category, ctrlType);

            // ── Extended metadata ─────────────────────────────────────────────
            // Each SafeGet is a cross-process COM call. Keep the set small
            // and exit fast on failure. Total added cost is 6–9 property
            // reads; at 30 Hz with per-throttle gating this is well within
            // budget in practice.
            string accel       = SafeGet(() => el.Current.AcceleratorKey)?.Trim() ?? "";
            string itemType    = SafeGet(() => el.Current.ItemType)?.Trim()       ?? "";
            string itemStatus  = SafeGet(() => el.Current.ItemStatus)?.Trim()     ?? "";

            // IsRequiredForForm — set by form builders and modern web
            // inputs. Cheap read; worth it for form-heavy users.
            bool isRequired    = SafeGet(() => el.Current.IsRequiredForForm);

            // Hyperlink URL — UIA exposes it via LegacyIAccessible's DefaultAction
            // sometimes, but most providers put it in the Name or use a
            // provider-specific property. The safest source is the
            // "LocalizedControlType == Hyperlink" element's Name, which
            // Chrome/Edge populate with the URL. Only read when we're
            // actually on a hyperlink.
            string linkUrl = "";
            if (ctrlType == ControlType.Hyperlink.Id)
                linkUrl = name;  // for browsers Name often IS the URL

            // Toggle state — only valid when TogglePattern is supported
            string toggle = "";
            var tp = SafeGetPattern<TogglePattern>(el, TogglePattern.Pattern);
            if (tp != null)
            {
                switch (tp.Current.ToggleState)
                {
                    case ToggleState.On:            toggle = "On";            break;
                    case ToggleState.Off:           toggle = "Off";           break;
                    case ToggleState.Indeterminate: toggle = "Indeterminate"; break;
                }
            }

            // Selection state — list / grid / tree items
            string selection = "";
            var sip = SafeGetPattern<SelectionItemPattern>(el, SelectionItemPattern.Pattern);
            if (sip != null)
                selection = sip.Current.IsSelected ? "Selected" : "";
            // Only show "Not selected" when the element is a clear
            // selectable item — reduces noise on every list row the
            // cursor passes over.

            // Value — Value OR RangeValue patterns. RangeValue wins
            // because it carries min/max which makes the number meaningful
            // ("65 / 100" vs just "65").
            string valueText = "";
            var rvp = SafeGetPattern<RangeValuePattern>(el, RangeValuePattern.Pattern);
            if (rvp != null)
            {
                try
                {
                    double v = rvp.Current.Value, min = rvp.Current.Minimum, max = rvp.Current.Maximum;
                    valueText = max > min
                        ? $"{v:0.##} / {max:0.##}"
                        : $"{v:0.##}";
                }
                catch { }
            }
            else
            {
                var vp = SafeGetPattern<ValuePattern>(el, ValuePattern.Pattern);
                if (vp != null)
                {
                    string v = SafeGet(() => vp.Current.Value) ?? "";
                    if (!string.IsNullOrEmpty(v) && v.Length <= 80)
                        valueText = v;
                }
            }

            // Scroll position — only meaningful when the provider
            // actually exposes ScrollPattern. Typical on scrollable
            // documents, lists, and terminals. Skip the horizontal
            // axis when the content doesn't scroll horizontally
            // (UIA reports -1 for not-scrollable axes).
            string scrollText = "";
            var sp = SafeGetPattern<ScrollPattern>(el, ScrollPattern.Pattern);
            if (sp != null)
            {
                try
                {
                    double h = sp.Current.HorizontalScrollPercent;
                    double v = sp.Current.VerticalScrollPercent;
                    bool hasH = h >= 0 && sp.Current.HorizontallyScrollable;
                    bool hasV = v >= 0 && sp.Current.VerticallyScrollable;
                    if (hasH && hasV) scrollText = $"H {h:0}% · V {v:0}%";
                    else if (hasV)    scrollText = $"V {v:0}%";
                    else if (hasH)    scrollText = $"H {h:0}%";
                }
                catch { }
            }
            string tabUrl = "";
            if (ctrlType == ControlType.TabItem.Id
                && !string.IsNullOrEmpty(helpText)
                && (helpText.StartsWith("http://",  StringComparison.OrdinalIgnoreCase)
                 || helpText.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                tabUrl = helpText;
            }

            return new DetectedElement
            {
                Label          = name,
                HelpText       = helpText,
                ClassName      = className,
                Category       = category,
                Icon           = icon,
                IsEnabled      = enabled,
                Source         = DetectionSource.UIA,
                AcceleratorKey = accel,
                LinkUrl        = linkUrl,
                ToggleState    = toggle,
                SelectionState = selection,
                ValueText      = valueText,
                ItemType       = itemType,
                ItemStatus     = itemStatus,
                IsRequired     = isRequired,
                TabUrl         = tabUrl,
                ScrollPercent  = scrollText,
            };
        }

        /// <summary>
        /// Fetches a UIA pattern by id, swallowing ElementNotAvailable and
        /// InvalidOperation exceptions that providers throw when a pattern
        /// isn't supported. Avoids turning a missing pattern into a noisy
        /// crash-log entry — happens on virtually every element.
        /// </summary>
        /// <summary>
        /// Returns true when the element passes the current
        /// <see cref="DetectionGranularity"/> filter. Called before the
        /// expensive metadata reads so rejection is cheap.
        ///
        /// <para><b>AllNamed</b> — always true (no filtering).</para>
        /// <para><b>InteractiveOnly</b> — true when the element supports
        /// Invoke, Toggle, SelectionItem, ExpandCollapse, RangeValue, or
        /// Value patterns, OR is keyboard-focusable. Captures the
        /// "anything you can actually do something with" intuition.</para>
        /// <para><b>ButtonsOnly</b> — true only for Button /
        /// SplitButton / MenuItem control types. Strictest filter.</para>
        /// </summary>
        private static bool PassesGranularityFilter(AutomationElement el, int ctrlType, int granularity)
        {
            var mode = (DetectionGranularity)granularity;
            if (mode == DetectionGranularity.AllNamed) return true;

            if (mode == DetectionGranularity.ButtonsOnly)
            {
                return ctrlType == ControlType.Button.Id
                    || ctrlType == ControlType.SplitButton.Id
                    || ctrlType == ControlType.MenuItem.Id;
            }

            // InteractiveOnly — actionable-pattern test. Fast IsKeyboardFocusable
            // check first, then try a handful of common interactive patterns.
            try
            {
                bool focusable = SafeGet(() => el.Current.IsKeyboardFocusable);
                if (focusable) return true;
            }
            catch { }

            // Keep this list short — each supported-pattern check is a
            // cross-process COM call. Order: most common first so most
            // elements resolve in 1-2 calls.
            object dummy;
            if (el.TryGetCurrentPattern(InvokePattern.Pattern,         out dummy)) return true;
            if (el.TryGetCurrentPattern(TogglePattern.Pattern,         out dummy)) return true;
            if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern,  out dummy)) return true;
            if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out dummy)) return true;
            if (el.TryGetCurrentPattern(RangeValuePattern.Pattern,     out dummy)) return true;
            if (el.TryGetCurrentPattern(ValuePattern.Pattern,          out dummy)) return true;
            return false;
        }

        private static T SafeGetPattern<T>(AutomationElement el, AutomationPattern pattern)
            where T : class
        {
            try
            {
                if (el.TryGetCurrentPattern(pattern, out object p))
                    return p as T;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// When the hit-test element is a generic container (Window, Pane,
        /// Group, Document) with no useful Name, drill down to find a
        /// labelled descendant at the cursor point. Covers taskbar buttons
        /// (which report as Pane with name "Start" / "Task View" / app name),
        /// tray icons (Button with app name), Start tiles (ListItem).
        /// </summary>
        private static AutomationElement ResolveUsefulElement(AutomationElement el)
        {
            try
            {
                string name = SafeGet(() => el.Current.Name)?.Trim() ?? "";
                int    ct   = SafeGet(() => el.Current.ControlType.Id);

                // If immediate hit already has a useful name, use it.
                if (!string.IsNullOrWhiteSpace(name) && name.Length > 1
                    && ct != ControlType.Window.Id
                    && ct != ControlType.Document.Id)
                    return el;

                // Walk children depth-first up to a small limit — find the
                // deepest named interactive element.
                var walker = TreeWalker.ControlViewWalker;
                AutomationElement best = null;
                int bestDepth = -1;
                int visited = 0;
                Drill(el, walker, 0, DrillMaxDepth, ref best, ref bestDepth, ref visited);
                return best ?? el;
            }
            catch { return el; }
        }

        private const int DrillMaxDepth  = 4;
        private const int DrillMaxNodes  = 64;   // safety ceiling for dense UIA trees

        private static void Drill(AutomationElement node, TreeWalker walker,
                                  int depth, int maxDepth,
                                  ref AutomationElement best, ref int bestDepth,
                                  ref int visited)
        {
            if (node == null || depth > maxDepth) return;
            if (visited >= DrillMaxNodes) return;   // budget exhausted
            visited++;
            try
            {
                string n  = SafeGet(() => node.Current.Name)?.Trim() ?? "";
                int    ct = SafeGet(() => node.Current.ControlType.Id);
                bool useful = !string.IsNullOrWhiteSpace(n) && n.Length > 1
                    && ct != ControlType.Window.Id
                    && ct != ControlType.Document.Id
                    && ct != ControlType.Pane.Id;
                if (useful && depth > bestDepth)
                {
                    best = node;
                    bestDepth = depth;
                }
                // Continue drilling — a deeper labelled node beats a shallow one
                AutomationElement child = walker.GetFirstChild(node);
                while (child != null && visited < DrillMaxNodes)
                {
                    Drill(child, walker, depth + 1, maxDepth, ref best, ref bestDepth, ref visited);
                    child = walker.GetNextSibling(child);
                }
            }
            catch { }
        }

        // ── Classification ────────────────────────────────────────────────────

        private static string ClassifyUIA(string name, string cls, int ctrlType, bool enabled)
        {
            if (!enabled) return "Disabled";

            string n = name.ToLowerInvariant();

            // ── Chrome / Edge browser toolbar patterns ────────────────────────
            // These exact names come from AutomationElement.Name on Chrome/Edge UI
            if (n == "back" || n == "back (alt+left arrow)")               return "Nav";
            if (n == "forward" || n == "forward (alt+right arrow)")        return "Nav";
            if (n == "reload" || n == "reload page" || n == "stop loading page"
                || n == "reload tab" || n == "stop")                       return "Nav";
            if (n == "home" || n == "go to homepage")                      return "Nav";
            if (n == "new tab" || n == "open new tab")                     return "Action";
            if (n == "address and search bar" || n == "address bar"
                || n == "location bar" || n == "search bar")               return "Nav";
            if (n == "extensions" || n == "extensions menu")               return "Settings";
            if (n == "browser menu" || n == "customize and control google chrome"
                || n == "settings and more" || n == "customize microsoft edge")
                                                                           return "Settings";
            if (n == "downloads" || n == "show downloads")                 return "Action";
            if (n == "bookmarks" || n == "reading list" || n == "favorites"
                || n == "show favorites bar")                               return "Nav";
            if (n == "zoom" || Contains(n, "zoom in", "zoom out"))         return "Control";
            if (n == "find" || n == "find in page")                        return "Nav";
            if (n == "print")                                              return "Action";
            if (n == "new window" || n == "new incognito window"
                || n == "new inprivate window")                            return "Action";
            if (Contains(n, "translate", "translation"))                   return "Action";
            if (n == "cast" || n == "cast media to device")                return "Action";
            if (n == "save page" || n == "save as")                        return "Action";
            if (n == "view page source" || n == "developer tools")         return "Settings";
            if (Contains(n, "profile") && ctrlType == ControlType.Button.Id) return "Account";
            if (n == "copilot" || n == "open sidebar")                     return "Action";
            // Tab-bar items
            if (ctrlType == ControlType.TabItem.Id && !string.IsNullOrEmpty(name))
                                                                           return "Tab";
            // Webpage buttons (generic web content)
            if (Contains(n, "sign in","log in","login"))                   return "Account";
            if (Contains(n, "sign up","register","create account"))        return "Account";
            if (Contains(n, "buy","purchase","checkout","add to cart","order"))
                                                                           return "Action";
            if (Contains(n, "play","pause","mute","unmute","fullscreen","volume"))
                                                                           return "Control";

            // ── Danger / destructive ──────────────────────────────────────────
            // Shared with ButtonDetector.BT()/Theme() via DetectorLabels.DestructiveKeywords.
            if (Contains(n, DetectorLabels.DestructiveKeywords))
                return "Danger";

            // ── Close / window controls ───────────────────────────────────────
            if (Contains(n, "close","dismiss","exit","quit"))              return "Close";
            if (Contains(n, "minimize","minimise"))                        return "Minimize";
            if (Contains(n, "maximize","maximise","restore down","restore up")) return "Maximize";

            // ── Submit / confirm ──────────────────────────────────────────────
            if (Contains(n, "ok","yes","confirm","accept","agree","apply","submit","finish","done","complete"))
                return "Action";

            // ── Save / export ─────────────────────────────────────────────────
            if (Contains(n, "save","save as","export","publish","upload","send","share"))
                return "Action";

            // ── Create / new ──────────────────────────────────────────────────
            if (Contains(n, "new","new email","new message","compose","create","add","insert","record"))
                return "Action";

            // ── Reply / forward (email) ───────────────────────────────────────
            if (Contains(n, "reply all","reply","forward","respond"))      return "Nav";

            // ── Navigation ────────────────────────────────────────────────────
            if (Contains(n, "back","forward","previous","next","navigate","go to","home","refresh","reload","stop"))
                return "Nav";
            if (Contains(n, "search","find","filter","sort","view"))       return "Nav";

            // ── Archive / file ops ────────────────────────────────────────────
            if (Contains(n, "archive","move","copy","cut","paste","rename","open","print","attach","download"))
                return "Action";

            // ── Flag / mark (email) ───────────────────────────────────────────
            if (Contains(n, "flag","mark as","junk","spam","block","report"))
                return "Action";

            // ── Settings ──────────────────────────────────────────────────────
            if (Contains(n, "setting","option","preference","configure","manage","property","properties","format"))
                return "Settings";

            // ── Help / info ───────────────────────────────────────────────────
            if (Contains(n, "help","about","info","what's new","feedback","contact"))
                return "Info";

            // ── Sign in / account ─────────────────────────────────────────────
            if (Contains(n, "sign in","log in","login","sign out","log out","account","profile","switch user"))
                return "Account";

            // ── Control type fallback ─────────────────────────────────────────
            if (ctrlType == ControlType.Button.Id)       return "Control";
            if (ctrlType == ControlType.CheckBox.Id)     return "Control";
            if (ctrlType == ControlType.RadioButton.Id)  return "Control";
            if (ctrlType == ControlType.ComboBox.Id)     return "Control";
            if (ctrlType == ControlType.Hyperlink.Id)    return "Nav";
            if (ctrlType == ControlType.MenuItem.Id)     return "Menu";
            if (ctrlType == ControlType.ListItem.Id)     return "Icon";   // desktop / Explorer icons
            if (ctrlType == ControlType.TreeItem.Id)     return "Icon";   // folder tree nodes
            if (ctrlType == ControlType.Image.Id)        return "Icon";
            if (ctrlType == ControlType.Edit.Id)         return "Text";
            if (ctrlType == ControlType.Slider.Id)       return "Control";
            if (ctrlType == ControlType.ProgressBar.Id)  return "Control";
            if (ctrlType == ControlType.TabItem.Id)      return "Tab";
            if (ctrlType == ControlType.SplitButton.Id)  return "Action";
            if (ctrlType == ControlType.DataItem.Id)     return "Icon";   // ListView data rows

            return "Control";
        }

        private static string IconForCategory(string cat, int ctrlType)
        {
            switch (cat)
            {
                case "Danger":   return "🗑";
                case "Close":    return "✕";
                case "Minimize": return "─";
                case "Maximize": return "⬜";
                case "Action":   return "✓";
                case "Nav":      return "▶";
                case "Settings": return "⚙";
                case "Info":     return "ℹ";
                case "Account":  return "🔑";
                case "Menu":     return "☰";
                case "Tab":      return "📑";
                case "Icon":     return "📄";   // file / desktop icon / image
                case "Text":     return "✎";    // edit / input field
                case "Disabled": return "⊘";
                default:
                    if (ctrlType == ControlType.CheckBox.Id)    return "☑";
                    if (ctrlType == ControlType.RadioButton.Id) return "◉";
                    if (ctrlType == ControlType.ComboBox.Id)    return "▼";
                    return "◉";
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool Contains(string haystack, params string[] needles)
        {
            foreach (string n in needles)
                if (haystack.Contains(n)) return true;
            return false;
        }

        private static T SafeGet<T>(Func<T> getter)
        {
            try { return getter(); } catch { return default; }
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts.Cancel(); } catch { }
            try { _wake.Set(); }   catch { }
            // Give the worker up to 500ms to exit cleanly. If it's hung in a
            // cross-process UIA COM call we let it go — background thread,
            // process will terminate it.
            try { _thread?.Join(500); } catch { }
            try { _cts.Dispose(); }    catch { }
            try { _wake.Dispose(); }   catch { }
        }
    }
}
