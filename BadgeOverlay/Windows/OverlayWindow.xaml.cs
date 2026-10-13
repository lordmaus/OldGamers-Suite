using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BadgeOverlay
{
    /// <summary>
    /// Full-screen transparent, click-through WPF overlay.
    /// Renders ONLY the badge tooltip — never touches the system cursor.
    /// Positions the badge relative to GetCursorPos(), so it works alongside
    /// any cursor tool (Windows default, YoloMouse, custom .cur schemes).
    /// </summary>
    public partial class OverlayWindow : Window
    {
        // ── Fields ────────────────────────────────────────────────────────────
        // Timer interval: ~30 Hz (33 ms). Fast enough for a follow-cursor
        // badge to feel glued to the pointer, but slow enough that repeated
        // WindowFromPoint / GetClass calls don't race with 3rd-party cursor
        // hooks (YoloMouse) or trigger per-frame WM_SETCURSOR churn.
        private readonly DispatcherTimer _timer = new DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(33) };

        private int _topmostRecheckTick;
        private const int TopmostRecheckInterval = 15;     // 15 × 33ms ≈ 500 ms

        // Subscriptions returned by WinEventHookManager.Subscribe — disposed
        // in UnregisterWinEventHooks. Held strongly so the registry's
        // weak-reference-free design works (delegate keep-alive lives
        // inside the subscription object).
        private IDisposable _subForeground;
        private IDisposable _subMinRestore;
        private NativeMethods.WinEventDelegate _winEventDelegate;  // GC guard for shared callback

        private AppSettings    _s;
        private DetectedElement _lastDetected;

        /// <summary>Current badge label text, or empty string when nothing
        /// is detected. Used by the Copy-label hotkey in MainWindow so the
        /// user can put the name of what's under the cursor on the
        /// clipboard — a click-safe alternative to selecting the badge
        /// itself (which is deliberately click-through).</summary>
        public string LastLabel => _lastDetected?.Label ?? "";

        /// <summary>
        /// Returns null when the SAPI synthesizer is available, or a
        /// user-readable error string when it failed to initialise.
        /// Only populated after the first time Screen Reader Mode is
        /// enabled (the announcer is lazy-initialised). MainWindow
        /// polls this after <see cref="EnsureAnnouncer"/> to surface
        /// the failure in a dialog rather than silently doing nothing.
        /// </summary>
        public string SpeechInitError => _announcer?.IsAvailable == false
            ? _announcer.InitError : null;

        /// <summary>
        /// Public entry point that forces the SAPI announcer to
        /// initialise immediately. Called from MainWindow when SR Mode
        /// is first enabled so <see cref="SpeechInitError"/> is
        /// populated before we check it — the announcer is otherwise
        /// lazy-created on the first actual detection event.
        /// </summary>
        public void EnsureAnnouncerPublic() => EnsureAnnouncer();

        // ── Hover-delay gate state ────────────────────────────────────────────
        // Tracks a candidate label that must dwell for AppSettings.HoverDelayMs
        // before it replaces _lastDetected. Null when no candidate is pending.
        private string         _pendingLabel;
        private long           _pendingLabelAt;
        private DetectedElement _pendingDetected;
        private IntPtr         _hwndSelf;
        private UIADetector    _uia;

        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        // ── Fullscreen-pause throttle ─────────────────────────────────
        // IsFullscreenAppForeground makes 5 Win32 calls; cap at ~1 Hz.
        private long _fullscreenCheckStamp;
        private bool _fullscreenCached;

        // ── MSAA background query ─────────────────────────────────────
        // AccessibleObjectFromPoint can block 50-500ms on hung providers.
        // Run it off the UI thread; use the previous result while in flight.
        private volatile DetectedElement _msaaLatest;
        private volatile bool            _msaaInFlight;

        // ── Idle dim tracking ─────────────────────────────────────────────
        // _lastMoveX/Y is the cursor position at the last detected move
        // (>= 1 px change). _lastMoveTick is the Environment.TickCount64
        // timestamp at that move. Cleared / reset on every actual move
        // and on every Visibility flip so the dim doesn't trigger
        // immediately when the badge re-appears after pause/silent.
        private int  _lastMoveX = int.MinValue;
        private int  _lastMoveY = int.MinValue;
        private long _lastMoveTick;
        private bool _isDimmed;

        // ── Screen-reader announcer ───────────────────────────────────────
        // Lazy-allocated when ScreenReaderMode is first true; persists
        // for the rest of the session even if the user toggles back off
        // (cheap to keep the off-screen window live; recreating it on
        // every toggle would re-trigger UIA tree mutations).
        private ScreenReaderAnnouncer _announcer;

        // Dwell tracking for SpeechTriggerMode.Dwell — _dwellLabel is
        // the candidate (most-recently-detected non-neutral label),
        // _dwellSinceTick is when it became the candidate. When the
        // candidate has been stable for SpeechDwellSeconds, we fire.
        // _dwellSpoken=true means we already spoke this candidate;
        // prevents a single dwell-hover from speaking repeatedly.
        private string _dwellLabel;
        private long   _dwellSinceTick;
        private bool   _dwellSpoken;

        // Runtime mute flag — toggled by the "Toggle Speech On/Off"
        // hotkey. Not persisted: speech mute is contextual (meeting,
        // phone call, child napping), session-scoped rather than a
        // preference. Every launch starts unmuted; ScreenReaderMode
        // remains the persisted "do I want speech at all" toggle.
        private bool _speechMuted;

        private readonly object _announcerLock = new object();

        private void EnsureAnnouncer()
        {
            // Locked because EnsureAnnouncer can be reached from two
            // logical paths in OnTick (the labelChanged speech branch
            // AND the per-tick dwell branch) within the same dispatcher
            // pump — and via OS-driven re-entry during a DPI change
            // recompose. Doubly-instantiating ScreenReaderAnnouncer
            // would orphan one (no Dispose, SAPI handle leak).
            if (_announcer != null) return;
            lock (_announcerLock)
            {
                if (_announcer != null) return;   // double-checked
                try
                {
                    _announcer = new ScreenReaderAnnouncer();
                    // Apply voice + rate immediately so the very first
                    // announcement uses the user-configured settings
                    // rather than the ctor defaults.
                    if (_s != null) _announcer.ApplySettings(_s);
                }
                catch (Exception ex) { CrashLog.Write("EnsureAnnouncer", ex); }
            }
        }

        /// <summary>
        /// Resets the runtime mute flag and dwell tracker. Called
        /// when ScreenReaderMode is toggled on or off so a stale
        /// muted state from a previous SR-Mode session doesn't leak
        /// into the next one. Without this, a user could mute,
        /// disable SR Mode, re-enable SR Mode, and get silence with
        /// no obvious cause — every speech path correctly checks
        /// _speechMuted, so the symptom looks like "the feature
        /// is broken".
        /// </summary>
        public void ResetMuteState()
        {
            _speechMuted    = false;
            _dwellLabel     = null;
            _dwellSpoken    = false;
            try { _announcer?.CancelInFlight(); } catch { }
        }

        /// <summary>
        /// Public hotkey entry point — toggles whether Screen Reader
        /// Mode speech is currently active. Doesn't change
        /// <see cref="AppSettings.ScreenReaderMode"/>; that's the
        /// persisted "do I want speech" preference. This is the
        /// runtime mute switch on top — flip it to silence speech
        /// during a meeting / phone call without losing your
        /// configured trigger mode and dwell.
        ///
        /// <para>Audio confirmation:</para>
        /// Asterisk on unmute, Hand on mute — distinguishable cues
        /// without shipping custom assets. Same scheme used by the
        /// existing <see cref="MainWindow.PlayStateSound"/> for the
        /// overlay-toggle hotkey, so the audio language is consistent.
        /// </summary>
        public void ToggleSpeechMuted()
        {
            _speechMuted = !_speechMuted;
            // Cancel any in-flight phrase so muting takes effect
            // immediately even mid-utterance.
            if (_speechMuted)
            {
                try { _announcer?.CancelInFlight(); } catch { }
            }
            // Reset dwell tracker on either transition — un-muting
            // shouldn't immediately speak whatever was under the cursor
            // at mute time, and muting shouldn't leave a stale candidate.
            _dwellLabel  = null;
            _dwellSpoken = false;

            try
            {
                if (_speechMuted) System.Media.SystemSounds.Hand.Play();
                else              System.Media.SystemSounds.Asterisk.Play();
            }
            catch { /* swallow: audio device unavailable */ }
        }

        // Companion window — set externally by MainWindow
        public InfoOverlay InfoPanel { get; set; }

        // ── Detection health (BO-02) ──────────────────────────────────────────
        // MSAA "can't read" (E_ACCESSDENIED / exception) used to look exactly like
        // "nothing under the cursor". ~30 consecutive failed reads (≈1 s at 30 Hz)
        // flip DetectionDegraded; the first readable result flips it back.
        private const int DegradedThreshold = 30;
        private int _detectFailStreak;
        private volatile bool _detectionDegraded;
        private DateTime _lastDetectFailLog = DateTime.MinValue;
        public bool DetectionDegraded => _detectionDegraded;
        public event Action<bool> DetectionHealthChanged;

        private void TrackDetectionHealth(bool ok)
        {
            bool degraded;
            if (ok) { System.Threading.Interlocked.Exchange(ref _detectFailStreak, 0); degraded = false; }
            else    { degraded = System.Threading.Interlocked.Increment(ref _detectFailStreak) >= DegradedThreshold; }
            if (degraded == _detectionDegraded) return;
            _detectionDegraded = degraded;
            Dispatcher.InvokeAsync(() => OnDetectionHealthChanged(degraded));
        }

        private void LogDetectionFailureOnce(Exception ex)
        {
            if ((DateTime.UtcNow - _lastDetectFailLog).TotalMinutes < 10) return;
            _lastDetectFailLog = DateTime.UtcNow;
            CrashLog.Write("MSAA query failed (logged at most every 10 min)", ex);
        }

        public const string DetectionDegradedText =
            "Badge Overlay can't read this app. If it runs as administrator, restart Badge Overlay as administrator.";

        private void OnDetectionHealthChanged(bool degraded)
        {
            try
            {
                InfoPanel?.SetDetectionDegraded(degraded);
                if (degraded) _announcer?.Announce(DetectionDegradedText, force: true);
                DetectionHealthChanged?.Invoke(degraded);
            }
            catch (Exception ex) { CrashLog.Write("OnDetectionHealthChanged", ex); }
        }

        // ── Constructor ───────────────────────────────────────────────────────
        public OverlayWindow(AppSettings settings)
        {
            InitializeComponent();
            CursorPreserver.Attach(this);
            _s     = settings;
            Loaded += OnLoaded;
        }

        // ── Startup ───────────────────────────────────────────────────────────
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _hwndSelf = new WindowInteropHelper(this).Handle;
            MakeClickThrough();
            CoverAllMonitors();
            ReadDpiScale();
            RegisterWinEventHooks();

            if (BadgeBorder != null)
            {
                // Start visible — the badge is always shown unless silent mode.
                // Off-screen position until the first tick moves it to the cursor.
                Canvas.SetLeft(BadgeBorder, -10000);
                Canvas.SetTop (BadgeBorder, -10000);
                BadgeBorder.Opacity    = _s.BadgeOpacity;
                BadgeBorder.Visibility = (_s.OverlayEnabled && _s.ShowBadge && !_s.BadgeHidden)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            _uia = new UIADetector();
            _uia.ApplySettings(_s);

            _timer.Tick += OnTick;
            _timer.Start();
        }

        // ── Public API ────────────────────────────────────────────────────────
        public void ApplySettings(AppSettings s)
        {
            _s = s;
            InvalidateBadgeCache();
            _uia?.ApplySettings(s);

            // Re-apply SAPI voice + rate whenever settings are pushed
            // so live changes from the voice picker or rate slider take
            // effect immediately without restarting SR Mode.
            if (_announcer != null && s != null)
                _announcer.ApplySettings(s);

            // Stop the detection timer entirely when the overlay is disabled.
            // Saves ~30 ticks/sec of GetCursorPos + WindowFromPoint + GetClass
            // while the user has the overlay switched off. Also hides the
            // badge immediately instead of waiting for the next tick.
            if (s != null && s.OverlayEnabled)
            {
                if (!_timer.IsEnabled) _timer.Start();
            }
            else
            {
                if (_timer.IsEnabled) _timer.Stop();
                if (BadgeBorder != null && BadgeBorder.Visibility != Visibility.Collapsed)
                    BadgeBorder.Visibility = Visibility.Collapsed;
                _lastDetected = null;
            }

            if (_hwndSelf != IntPtr.Zero)
                NativeMethods.SetWindowPos(
                    _hwndSelf, NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING |
                    NativeMethods.SWP_NOOWNERZORDER);
        }

        // ── DPI ───────────────────────────────────────────────────────────────
        private void CoverAllMonitors()
        {
            Left   = SystemParameters.VirtualScreenLeft;
            Top    = SystemParameters.VirtualScreenTop;
            Width  = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        private void ReadDpiScale()
        {
            try
            {
                PresentationSource ps = PresentationSource.FromVisual(this);
                if (ps != null && ps.CompositionTarget != null)
                {
                    Matrix m   = ps.CompositionTarget.TransformFromDevice;
                    _dpiScaleX = m.M11;
                    _dpiScaleY = m.M22;
                    return;
                }
            }
            catch { }
            uint dpi = NativeMethods.GetDpiForWindow(_hwndSelf);
            if (dpi == 0) dpi = 96;
            _dpiScaleX = 96.0 / dpi;
            _dpiScaleY = 96.0 / dpi;
        }

        private int _dpiCheckTick;

        private void UpdateDpiFromCursorMonitor(NativeMethods.POINT pt)
        {
            _dpiCheckTick++;
            if (_dpiCheckTick < 15) return;  // 15 × 33ms ≈ 500ms
            _dpiCheckTick = 0;
            try
            {
                IntPtr hmon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (hmon == IntPtr.Zero) return;
                uint dpiX, dpiY;
                if (NativeMethods.GetDpiForMonitor(hmon, 0, out dpiX, out dpiY) == 0
                    && dpiX > 0 && dpiY > 0)
                {
                    double sx = 96.0 / dpiX;
                    double sy = 96.0 / dpiY;
                    if (Math.Abs(sx - _dpiScaleX) > 0.001 || Math.Abs(sy - _dpiScaleY) > 0.001)
                    {
                        _dpiScaleX = sx;
                        _dpiScaleY = sy;
                    }
                }
            }
            catch { }
        }

        // ── WinEvent hooks — Z-order enforcement only ─────────────────────────
        private void RegisterWinEventHooks()
        {
            try
            {
                // Reuse one delegate instance for both events so the
                // registry only has to keep one strong reference alive.
                // The registry's Subscribe holds its own keep-alive too,
                // but binding to the same delegate is cheap and lets the
                // OnWinEvent handler stay one method.
                _winEventDelegate = OnWinEvent;

                _subForeground = WinEventHookManager.Subscribe(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    _winEventDelegate);

                _subMinRestore = WinEventHookManager.Subscribe(
                    NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
                    NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
                    _winEventDelegate);
            }
            catch (Exception ex) { CrashLog.Write("RegisterWinEventHooks", ex); }
        }

        private void OnWinEvent(IntPtr hHook, uint eventType, IntPtr hwnd,
                                int idObject, int idChild,
                                uint dwEventThread, uint dwmsEventTime)
        {
            // Only re-assert topmost Z-order if WS_EX_TOPMOST was actually
            // stripped. Unconditional SetWindowPos on every foreground change
            // (taskbar click, tray hover, alt-tab, menu open) fires
            // WM_WINDOWPOSCHANGED which triggers WM_SETCURSOR on the window
            // under the pointer — this resets YoloMouse and other 3rd-party
            // cursor-hook tools back to the Windows default cursor.
            if (_hwndSelf == IntPtr.Zero) return;

            long exStyle = NativeMethods.GetWindowLong(_hwndSelf, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((exStyle & NativeMethods.WS_EX_TOPMOST) == 0)
            {
                NativeMethods.SetWindowPos(
                    _hwndSelf, NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING |
                    NativeMethods.SWP_NOOWNERZORDER);
            }
            _topmostRecheckTick = 0;
        }

        private void UnregisterWinEventHooks()
        {
            // Dispose the subscriptions; the registry handles both
            // UnhookWinEvent and the delegate keep-alive teardown.
            try { _subForeground?.Dispose(); _subForeground = null; } catch { }
            try { _subMinRestore?.Dispose(); _subMinRestore = null; } catch { }
            _winEventDelegate = null;
        }

        // ── Idle dim ──────────────────────────────────────────────────────

        /// <summary>
        /// Tracks cursor movement and fades the badge to
        /// <see cref="AppSettings.IdleDimOpacity"/> when the cursor has
        /// been stationary for <see cref="AppSettings.IdleDimSeconds"/>.
        /// Reverses immediately on the first move. Called once per
        /// <see cref="OnTick"/>; cost is negligible (TickCount64 +
        /// two int comparisons in the steady-state idle path).
        ///
        /// Why not a one-shot DispatcherTimer instead of polling the
        /// existing 33 ms tick: the badge tick is already running for
        /// detection, so adding logic to it is free; spinning a second
        /// timer just to detect idle would double the WPF dispatcher
        /// wake-ups for no benefit.
        /// </summary>
        private void ApplyIdleDim(NativeMethods.POINT pt)
        {
            if (_s == null || BadgeBorder == null) return;

            // Detect movement (≥ 1 px in any direction). Sub-pixel jitter
            // counts as no-movement so a stationary cursor with shaky
            // hardware doesn't spuriously reset the dim timer.
            bool moved = pt.X != _lastMoveX || pt.Y != _lastMoveY;
            if (moved)
            {
                _lastMoveX    = pt.X;
                _lastMoveY    = pt.Y;
                _lastMoveTick = Environment.TickCount64;

                // Restore full opacity instantly. Avoid setting it on
                // every move when we're not currently dimmed — saves a
                // dependency-property write per tick during continuous
                // motion (which is most ticks).
                if (_isDimmed)
                {
                    BadgeBorder.Opacity = _s.BadgeOpacity;
                    _isDimmed = false;
                }
                return;
            }

            // No movement this tick. Bail when the feature is off — but
            // do this *after* the move-detection above, so toggling the
            // setting while the cursor is stationary doesn't leave us
            // with a stale "idle" state on the next move.
            if (!_s.IdleDimEnabled) return;

            // Threshold expired? Apply dim. Idempotent: setting Opacity
            // to its current value is a no-op in WPF.
            int idleMs = _s.IdleDimSeconds * 1000;
            if (idleMs <= 0) return;
            if (Environment.TickCount64 - _lastMoveTick < idleMs) return;

            if (!_isDimmed)
            {
                BadgeBorder.Opacity = _s.IdleDimOpacity;
                _isDimmed = true;
            }
        }

        // ── Timer tick ────────────────────────────────────────────────────────
        private void OnTick(object sender, EventArgs e)
        {
            NativeMethods.POINT pt;
            if (!NativeMethods.GetCursorPos(out pt)) return;

            // Z-order enforcement — only when topmost bit has been stripped
            _topmostRecheckTick++;
            if (_topmostRecheckTick >= TopmostRecheckInterval)
            {
                _topmostRecheckTick = 0;
                long exStyle = NativeMethods.GetWindowLong(_hwndSelf, NativeMethods.GWL_EXSTYLE).ToInt64();
                if ((exStyle & NativeMethods.WS_EX_TOPMOST) == 0)
                {
                    NativeMethods.SetWindowPos(
                        _hwndSelf, NativeMethods.HWND_TOPMOST,
                        0, 0, 0, 0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE |
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING |
                        NativeMethods.SWP_NOOWNERZORDER);

                    // When a layered window temporarily loses its topmost
                    // flag (full-screen apps, UAC prompts, screen-sharing
                    // clients), WPF can leave BadgeBorder.Opacity at a
                    // stale composed value after the bit is restored.
                    // Force it back to whichever opacity is currently
                    // intended — full _s.BadgeOpacity normally, or the
                    // dimmed value when the idle-dim timer has fired.
                    if (BadgeBorder != null && _s != null)
                    {
                        BadgeBorder.Opacity = _isDimmed
                            ? _s.IdleDimOpacity
                            : _s.BadgeOpacity;
                    }
                }
            }

            UpdateDpiFromCursorMonitor(pt);

            // Idle-dim tracking — fades the badge after N seconds of no
            // cursor movement, restores instantly on first move. Detection
            // still runs; only opacity changes. Cheap on every tick (just
            // a TickCount64 read + two ints + one comparison).
            ApplyIdleDim(pt);

            double dipX = pt.X * _dpiScaleX;
            double dipY = pt.Y * _dpiScaleY;

            // Master gate — overlay entirely off, hide everything and exit.
            // (Silent mode is handled later: it lets detection run and
            // InfoOverlay update, but suppresses just the badge visual.
            // The checkbox label "detect but hide badge" promises this.)
            if (!_s.OverlayEnabled || !_s.ShowBadge)
            {
                if (BadgeBorder.Visibility != Visibility.Collapsed)
                    BadgeBorder.Visibility = Visibility.Collapsed;
                _lastDetected = null;
                return;
            }

            // ── Detection — everywhere, including shell surfaces ──────────────
            DetectedElement detected = ButtonDetector.Detect(pt);
            bool fromWin32 = detected != null;

            // Cache WindowFromPoint + GetRootWindow once for this tick.
            // These results are reused by shell detection, process annotation,
            // password guard, and background suppression below — avoiding
            // 3 redundant Win32 cross-process calls per tick (~90/sec at 30 Hz).
            IntPtr ptHwnd = IntPtr.Zero;
            IntPtr ptRoot = IntPtr.Zero;
            try
            {
                ptHwnd = NativeMethods.WindowFromPoint(pt);
                if (ptHwnd != IntPtr.Zero)
                    ptRoot = NativeMethods.GetRootWindow(ptHwnd);
            }
            catch { }

            // Shell surface override: when the cursor is over the Windows
            // taskbar or secondary tray, the Win32 WindowFromPoint hit can
            // leak through to title-bar buttons ("Maximize / Restore") of
            // background windows because the Win11 taskbar is partially
            // transparent to hit-testing. UIA correctly reports the
            // taskbar item itself. Force UIA whenever we detect we're
            // over a shell surface so the user sees the app / icon label,
            // not a nonsensical title-bar button.
            bool overShell = false;
            if (ptHwnd != IntPtr.Zero)
            {
                try
                {
                    string rootCls = NativeMethods.GetClass(ptRoot);
                    overShell = rootCls == "Shell_TrayWnd"
                             || rootCls == "Shell_SecondaryTrayWnd"
                             || rootCls == "Windows.UI.Core.CoreWindow";  // Win11 pill taskbar
                }
                catch { }
            }

            if (overShell)
            {
                detected = null;   // discard Win32 result, force UIA below
                fromWin32 = false;
            }

            // Use UIA whenever Win32 returned nothing OR returned a generic
            // container label where the UIA child has the real name (desktop
            // icons, Explorer items, browser buttons, tree nodes, list rows,
            // taskbar app buttons, tray icons, Start menu tiles).
            bool useUia = detected == null
                       || (_s.EnableUIADetection
                           && DetectorLabels.IsGenericContainer(detected.Label));

            if (useUia && _s.EnableUIADetection)
            {
                DetectedElement uia = _uia?.GetLatestFor(pt.X, pt.Y);

                // Shell-surface sanity filter — Windows 11's taskbar
                // occasionally lets UIA hit-test fall through to a title-
                // bar button of the active window behind it ("Minimize /
                // Maximize / Close / Restore"). These are structurally
                // correct but semantically wrong; the user is clearly
                // over a tray icon. Reject those specific labels when
                // we know we're on a shell surface.
                if (overShell && uia != null && IsTitleBarButtonLabel(uia.Label))
                    uia = null;

                if (uia != null)
                {
                    detected = UIAResultToElement(uia);
                    fromWin32 = false;   // UIA has produced the result we'll use
                }
            }

            // MSAA fallback — when both Win32 classification and UIA came
            // up empty, try the older IAccessible surface. Reaches Java
            // Swing, Qt-pre-6, SWT (Eclipse), and some legacy apps.
            //
            // AccessibleObjectFromPoint can block 50-500ms on hung COM
            // providers — run it on a background thread and use the
            // previous result while a fresh query is in flight. The
            // one-tick staleness (~33ms) is imperceptible and vastly
            // preferable to blocking the UI thread at 30 Hz.
            if (detected != null) TrackDetectionHealth(true);   // any readable result clears BO-02 banner
            if (detected == null && _s.EnableUIADetection)
            {
                var msaaSnap = _msaaLatest;
                if (msaaSnap != null)
                    detected = UIAResultToElement(msaaSnap);

                if (!_msaaInFlight)
                {
                    _msaaInFlight = true;
                    int qx = pt.X, qy = pt.Y;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            _msaaLatest = MsaaDetector.Query(qx, qy, out int hr);
                            TrackDetectionHealth(_msaaLatest != null || hr == 0);
                        }
                        catch (Exception ex)
                        {
                            _msaaLatest = null;
                            TrackDetectionHealth(false);
                            LogDetectionFailureOnce(ex);
                        }
                        finally { _msaaInFlight = false; }
                    });
                }
            }

            // Annotate with process name — useful diagnostic that users
            // appreciate seeing in the Info Overlay. Cached 30 s per hwnd
            // so cost is one Win32 call per ~900 ticks, negligible.
            // ptRoot cached above — no extra WindowFromPoint needed.
            if (detected != null && ptRoot != IntPtr.Zero)
            {
                try
                {
                    detected.ProcessName = ButtonDetector.GetProcessName(ptRoot);
                    detected.ProcessPath = ButtonDetector.GetProcessPath(ptRoot);
                }
                catch { }
            }

            // ── Security: always suppress over password fields ────────────────
            // Credential input surfaces get special treatment regardless of
            // user settings. Win32 EDIT controls with ES_PASSWORD mask their
            // own input; we honour that by hiding the badge. Prevents the
            // badge from drawing over login forms or RDP/remote-desktop
            // password prompts where its presence could be misinterpreted
            // as focus stealing.
            // ptHwnd cached above — no extra WindowFromPoint needed.
            if (ptHwnd != IntPtr.Zero)
            {
                try
                {
                    string undCls = NativeMethods.GetClass(ptHwnd);
                    if (string.Equals(undCls, "Edit", StringComparison.Ordinal))
                    {
                        long style = NativeMethods.GetWindowLong(ptHwnd, NativeMethods.GWL_STYLE).ToInt64();
                        if ((style & NativeMethods.ES_PASSWORD) != 0)
                        {
                            if (BadgeBorder.Visibility != Visibility.Collapsed)
                                BadgeBorder.Visibility = Visibility.Collapsed;
                            _lastDetected = null;
                            return;
                        }
                    }
                }
                catch { }
            }

            // ── Auto-pause on fullscreen (gaming / presentation) ──────────────
            // When a fullscreen app is foreground on the cursor's monitor,
            // suppress the badge so it doesn't overlay games, Teams/Zoom
            // presenter mode, or video playback. Detected by comparing the
            // foreground window's rect to its monitor's rect.
            if (_s.PauseOnFullscreen && IsFullscreenCached(pt))
            {
                if (BadgeBorder.Visibility != Visibility.Collapsed)
                    BadgeBorder.Visibility = Visibility.Collapsed;
                _lastDetected = null;
                return;
            }

            // ── Background-app suppression (opt-in) ───────────────────────────
            // When the target hwnd is not inside the foreground process's
            // top-level window tree, suppress the badge entirely. Useful
            // when the user is working in one app and doesn't want badges
            // over background windows (chat, email).
            if (_s.SuppressWhenBackground)
            {
                try
                {
                    // ptRoot cached above — no extra WindowFromPoint/GetRootWindow needed.
                    IntPtr fg = NativeMethods.GetForegroundWindow();
                    if (fg != IntPtr.Zero && ptRoot != IntPtr.Zero && ptRoot != fg)
                    {
                        if (BadgeBorder.Visibility != Visibility.Collapsed)
                            BadgeBorder.Visibility = Visibility.Collapsed;
                        _lastDetected = null;
                        return;
                    }
                }
                catch { /* any Win32 hiccup: fall through to normal rendering */ }
            }

            // Always have SOMETHING to show — the badge follows the cursor
            // continuously. When nothing specific is detected, show a neutral
            // "Cursor" pill so the user always has a visible indicator.
            if (detected == null)
                detected = _neutralBadge ??= BuildNeutralBadge();

            // ── Hover-delay gate ──────────────────────────────────────────────
            // Suppress rapid label switching when the cursor flies over many
            // elements. A candidate must dwell for HoverDelayMs before it
            // replaces the currently-shown label. 0 ms = legacy immediate.
            //
            // Exception: transitions TO the neutral badge (cursor moved off
            // everything) bypass the dwell so the UI doesn't keep showing a
            // stale real button when the user has clearly moved away.
            string newLabel  = detected.Label;
            string lastLabel = _lastDetected?.Label;

            bool labelChanged = newLabel != lastLabel;
            bool toNeutral    = ReferenceEquals(detected, _neutralBadge);

            if (labelChanged && !toNeutral && _s.HoverDelayMs > 0)
            {
                long now = Environment.TickCount64;
                if (_pendingLabel != newLabel)
                {
                    _pendingLabel     = newLabel;
                    _pendingLabelAt   = now;
                    _pendingDetected  = detected;
                    labelChanged      = false;     // wait for dwell
                }
                else if (now - _pendingLabelAt < _s.HoverDelayMs)
                {
                    labelChanged = false;          // still waiting
                }
                else
                {
                    detected = _pendingDetected ?? detected;
                    _pendingLabel = null;
                }
            }
            else
            {
                _pendingLabel = null;
            }

            if (labelChanged)
            {
                // Freeze mode: retain the currently-shown label regardless
                // of what the cursor is over now. Position + visibility
                // still follow normal logic below.
                if (_s.BadgeFrozen)
                {
                    labelChanged = false;
                }
                else
                {
                    _lastDetected = detected;
                    // Session counters — anything that's NOT the neutral
                    ApplyBadgeContent(detected);
                    if (InfoPanel != null && InfoPanel.IsVisible)
                        InfoPanel.UpdateContent(detected == _neutralBadge ? null : detected);

                    // Screen-reader mode — speak the label aloud through
                    // OS TTS. Two trigger modes:
                    //
                    //   Immediate — fire on every label change.
                    //   Dwell     — defer to the dwell tracker below;
                    //               nothing fires here, the cursor must
                    //               rest on the same label for N seconds.
                    //
                    // Both gated on _speechMuted, the runtime toggle
                    // bound to the "Toggle Speech On/Off" hotkey. Mute
                    // is checked here (not at the announcer) so the
                    // dwell tracker doesn't accumulate state we'd just
                    // throw away.
                    if (_s.ScreenReaderMode && !_speechMuted)
                    {
                        string lbl = detected == _neutralBadge ? null : detected.Label;
                        switch (_s.SpeechTrigger)
                        {
                            case SpeechTriggerMode.Immediate:
                                EnsureAnnouncer();
                                _announcer?.Announce(lbl);
                                break;
                            case SpeechTriggerMode.Dwell:
                                // New candidate — reset the dwell timer.
                                _dwellLabel     = lbl;
                                _dwellSinceTick = Environment.TickCount64;
                                _dwellSpoken    = false;
                                break;
                        }
                    }
                }
            }

            // Per-tick dwell evaluation. Outside the labelChanged
            // branch so we re-check every tick — required because the
            // initial label-set happened on a previous tick and we
            // need to detect when the dwell threshold expires.
            //
            // Suppressed while idle-dim is active: if the badge is
            // dimmed because the cursor's been still long enough, the
            // user's intent is "I'm not interacting right now" — that
            // should silence speech too. The candidate is preserved
            // (_dwellSpoken stays false) so movement-then-stop will
            // re-arm the timer cleanly via the labelChanged branch.
            if (_s.ScreenReaderMode && !_speechMuted && !_isDimmed &&
                _s.SpeechTrigger == SpeechTriggerMode.Dwell &&
                !_dwellSpoken &&
                !string.IsNullOrEmpty(_dwellLabel))
            {
                int dwellMs = Math.Max(1, _s.SpeechDwellSeconds) * 1000;
                if (Environment.TickCount64 - _dwellSinceTick >= dwellMs)
                {
                    EnsureAnnouncer();
                    _announcer?.Announce(_dwellLabel);
                    _dwellSpoken = true;
                }
            }

            // Position — pinned mode shows the badge at a fixed screen
            // location (WCAG 2.2.2 — users can request that motion stop).
            if (_s.BadgePinned)
                PositionBadgePinned();
            else
                PositionBadge(dipX, dipY);

            // Silent mode — detection ran, InfoOverlay was updated above
            // (around line 514), but the visible badge is suppressed.
            // This is what the "detect but hide badge" checkbox promises:
            // the detection pipeline keeps feeding the InfoOverlay so
            // low-vision users can still read the side panel without
            // the small inline badge cluttering the cursor.
            if (_s.BadgeHidden)
            {
                if (BadgeBorder.Visibility != Visibility.Collapsed)
                    BadgeBorder.Visibility = Visibility.Collapsed;
                return;
            }

            // Ensure visible (may have been collapsed by a previous hidden-mode tick).
            // Reset the idle-dim baseline on un-hide — coming out of
            // pause/silent should not immediately re-trigger the dim.
            if (BadgeBorder.Visibility != Visibility.Visible)
            {
                BadgeBorder.Opacity    = _isDimmed ? _s.IdleDimOpacity : _s.BadgeOpacity;
                BadgeBorder.Visibility = Visibility.Visible;
                _lastMoveTick = Environment.TickCount64;
            }
        }

        // Reusable neutral badge for the "no specific button detected" state
        private DetectedElement _neutralBadge;

        private DetectedElement BuildNeutralBadge()
        {
            Color c      = ParseColor(_s != null ? _s.BadgeColorControl : "#94E2D5");
            Color border = Lighten(c);
            return new DetectedElement("Cursor", "Badge Overlay", "◉", c, border);
        }

        // ── UIA result converter ──────────────────────────────────────────────
        private DetectedElement UIAResultToElement(DetectedElement uia)
        {
            if (uia == null) return null;
            Color badge  = ResolveCategory(uia.Category);
            Color border = Lighten(badge);
            string label = uia.Label;
            string category = string.IsNullOrEmpty(uia.HelpText)
                ? uia.Category
                : uia.HelpText.Length > 40
                    ? uia.HelpText.Substring(0, 37) + "…"
                    : uia.HelpText;

            // Carry extended metadata through so InfoOverlay can surface it.
            // Creating via the 5-arg ctor drops them, so we field-copy.
            var result = new DetectedElement(label, category, uia.Icon, badge, border)
            {
                HelpText       = uia.HelpText,
                ClassName      = uia.ClassName,
                IsEnabled      = uia.IsEnabled,
                Source         = uia.Source,
                AcceleratorKey = uia.AcceleratorKey,
                LinkUrl        = uia.LinkUrl,
                ToggleState    = uia.ToggleState,
                SelectionState = uia.SelectionState,
                ValueText      = uia.ValueText,
                ItemType       = uia.ItemType,
                ItemStatus     = uia.ItemStatus,
                IsRequired     = uia.IsRequired,
                TabUrl         = uia.TabUrl,
                ScrollPercent  = uia.ScrollPercent,
            };
            return result;
        }

        private Color ResolveCategory(string cat)
        {
            if (_s == null) return ParseColor("#89B4FA");
            switch (cat)
            {
                case "Danger":   return ParseColor(_s.BadgeColorDanger);
                case "Close":    return ParseColor(_s.BadgeColorClose);
                case "Minimize": return ParseColor(_s.BadgeColorMinimize);
                case "Maximize": return ParseColor(_s.BadgeColorMaximize);
                case "Action":   return ParseColor(_s.BadgeColorAction);
                case "Nav":      return ParseColor(_s.BadgeColorNav);
                case "Settings": return ParseColor(_s.BadgeColorControl);
                case "Info":     return ParseColor(_s.BadgeColorControl);
                default:         return ParseColor(_s.BadgeColorControl);
            }
        }

        private static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Color.FromRgb(137, 180, 250); }
        }

        private static Color Lighten(Color c) => Color.FromRgb(
            (byte)Math.Min(255, c.R + 30),
            (byte)Math.Min(255, c.G + 30),
            (byte)Math.Min(255, c.B + 30));

        // ── Badge brush / colour / size cache ─────────────────────────────────
        // Reused every tick — avoids allocations and repeated hex parsing
        // in the hot path. Invalidated when settings change via ApplySettings.
        private SolidColorBrush _cachedBorderBrush;
        private SolidColorBrush _cachedBadgeBrush;
        private DetectedElement  _cachedBrushFor;
        private double _cachedBw, _cachedBh;
        private double _cachedMeasuredScale = -1;
        /// <summary>Lengths of label + category at the time of the last
        /// Measure. When either changes, the cached width is stale even
        /// though scale hasn't changed, so we force a re-measure.</summary>
        private int    _cachedLabelLen;
        private int    _cachedCategoryLen;

        private int _cachedPaletteVersion = -1;

        private void InvalidateBadgeCache()
        {
            _cachedBorderBrush = null;
            _cachedBadgeBrush  = null;
            _cachedBrushFor    = null;
            _cachedMeasuredScale = -1;
            _cachedLabelLen    = -1;
            _cachedCategoryLen = -1;
            // Neutral badge bakes BadgeColorControl at build time; must be
            // rebuilt whenever settings change so colour updates take effect.
            _neutralBadge = null;
        }

        /// <summary>
        /// Ensures <see cref="_cachedBw"/> / <see cref="_cachedBh"/> are
        /// fresh for the given <paramref name="scale"/>. Re-measures the
        /// badge when the scale OR the displayed text length has changed
        /// since the last measure; reuses cache otherwise. Called from
        /// both <see cref="PositionBadge"/> and <see cref="PositionBadgePinned"/>
        /// — same logic, two callers.
        /// </summary>
        private void EnsureBadgeMeasured(double scale)
        {
            int labelLen = BadgeLabel?.Text?.Length ?? 0;
            int catLen   = BadgeCategory?.Text?.Length ?? 0;
            bool sizeInvalid =
                Math.Abs(_cachedMeasuredScale - scale) > 0.001 ||
                labelLen != _cachedLabelLen ||
                catLen   != _cachedCategoryLen;
            if (!sizeInvalid) return;

            BadgeBorder.LayoutTransform = new ScaleTransform(scale, scale);
            BadgeBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _cachedBw = BadgeBorder.DesiredSize.Width  * scale;
            _cachedBh = BadgeBorder.DesiredSize.Height * scale;
            _cachedMeasuredScale = scale;
            _cachedLabelLen      = labelLen;
            _cachedCategoryLen   = catLen;
        }

        // ── Badge ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Refreshes badge text, icon, and brushes. Does NOT affect visibility
        /// or position — those are handled by OnTick / PositionBadge.
        /// Called once when the detected button changes.
        /// </summary>
        private void ApplyBadgeContent(DetectedElement btn)
        {
            if (btn == null) return;

            BadgeLabel.Text    = btn.Label;
            BadgeCategory.Text = btn.Category;
            BadgeIcon.Text     = btn.Icon;

            bool colourChanged = _cachedBrushFor == null
                || _cachedBrushFor.BadgeColor  != btn.BadgeColor
                || _cachedBrushFor.BorderColor != btn.BorderColor
                || _cachedPaletteVersion != OverlayPalette.Version;
            if (colourChanged)
            {
                // Label colour falls back to OverlayText when the category colour is < 4.5:1 on the
                // current overlay background (Light style / MatchTheme) or HC is active.
                _cachedBorderBrush = new SolidColorBrush(OverlayPalette.BorderColor(btn.BorderColor));
                _cachedBadgeBrush  = OverlayPalette.LabelBrush(btn.BadgeColor);
                _cachedPaletteVersion = OverlayPalette.Version;
                _cachedBrushFor    = btn;
                _cachedMeasuredScale = -1;   // label width may have changed
            }

            BadgeBorder.BorderBrush = _cachedBorderBrush;
            BadgeLabel.Foreground   = _cachedBadgeBrush;
            BadgeIcon.Foreground    = _cachedBadgeBrush;
            // IconPillBg.Color only needs rewriting when the badge colour
            // changed. The colourChanged flag above already tracks this;
            // skipping the assign avoids a dependency-property write every
            // tick that was setting an identical Color.FromArgb result.
            if (colourChanged)
            {
                IconPillBg.Color = Color.FromArgb(50,
                    btn.BadgeColor.R, btn.BadgeColor.G, btn.BadgeColor.B);
            }

            // Live-region announcement (WCAG 4.1.3 Status Messages).
            // Updating AutomationProperties.Name on a LiveSetting=Polite
            // element causes UIA to fire a LiveRegionChanged event which
            // screen readers announce without stealing focus.
            //
            // Throttle to at most one announcement per 500 ms so users
            // flicking the cursor across many elements don't get a
            // firehose of utterances. If the user enables ReduceMotion
            // we also silence the intermediate announcements; the last
            // label shown at rest still announces after the throttle
            // window elapses on the next real change.
            try
            {
                long now = Environment.TickCount64;
                int throttleMs = _s != null && _s.ReduceMotion ? 1000 : 500;
                if (now - _lastLiveRegionTick >= throttleMs)
                {
                    string liveText = string.IsNullOrEmpty(btn.Category)
                        ? btn.Label
                        : $"{btn.Label} — {btn.Category}";
                    System.Windows.Automation.AutomationProperties.SetName(BadgeBorder, liveText);
                    _lastLiveRegionTick = now;
                }
            }
            catch { /* never throw from the hot path */ }
        }

        private long _lastLiveRegionTick;

        /// <summary>
        /// Structural fullscreen detection — returns true when the
        /// foreground window's bounds equal (or exceed) the bounds of the
        /// monitor that contains the cursor. Covers both exclusive
        /// fullscreen (games) and borderless fullscreen (Teams / Zoom
        /// presenter mode, fullscreen video).
        ///
        /// We exclude our own overlay window (which is topmost + layered
        /// but NOT the foreground) by comparing to <c>GetForegroundWindow</c>.
        /// Desktop / shell windows are also excluded — the taskbar or
        /// Progman covering the monitor is not "fullscreen" in the sense
        /// that matters here.
        /// </summary>
        /// <summary>
        /// True when the given UIA Name corresponds to one of the standard
        /// window-frame buttons. Used to filter out phantom hits that leak
        /// through the Win11 taskbar onto the active window's title bar.
        /// Covers English, and common localised variants where the
        /// English string has become the <c>AutomationId</c>-equivalent
        /// convention. Non-English locales may still get through — the
        /// cost is low (user sees wrong label, not a crash).
        /// </summary>
        private static bool IsTitleBarButtonLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            return label.Equals("Minimize",          StringComparison.OrdinalIgnoreCase)
                || label.Equals("Maximize",          StringComparison.OrdinalIgnoreCase)
                || label.Equals("Restore",           StringComparison.OrdinalIgnoreCase)
                || label.Equals("Restore Down",      StringComparison.OrdinalIgnoreCase)
                || label.Equals("Maximize / Restore",StringComparison.OrdinalIgnoreCase)
                || label.Equals("Close",             StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Cached wrapper around IsFullscreenAppForeground — updates at most
        /// once per second so its 5 Win32 calls (GetForegroundWindow, GetClass,
        /// GetWindowRect, MonitorFromPoint, GetMonitorInfo) don't run at 30 Hz.
        /// </summary>
        private bool IsFullscreenCached(NativeMethods.POINT pt)
        {
            long now = Environment.TickCount64;
            if (now - _fullscreenCheckStamp >= 1000)
            {
                _fullscreenCheckStamp = now;
                _fullscreenCached = IsFullscreenAppForeground(pt);
            }
            return _fullscreenCached;
        }

        private bool IsFullscreenAppForeground(NativeMethods.POINT pt)
        {
            try
            {
                IntPtr fg = NativeMethods.GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == _hwndSelf) return false;

                // Skip shell surfaces
                string cls = NativeMethods.GetClass(fg);
                if (cls == "Progman" || cls == "WorkerW" ||
                    cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                    return false;

                if (!NativeMethods.GetWindowRect(fg, out var wr)) return false;

                IntPtr mon = NativeMethods.MonitorFromPoint(pt,
                    NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (mon == IntPtr.Zero) return false;

                var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                if (!NativeMethods.GetMonitorInfo(mon, ref mi)) return false;

                // Foreground rect must cover the whole monitor rect (not
                // just the work area — fullscreen apps hide the taskbar).
                return wr.Left   <= mi.rcMonitor.Left
                    && wr.Top    <= mi.rcMonitor.Top
                    && wr.Right  >= mi.rcMonitor.Right
                    && wr.Bottom >= mi.rcMonitor.Bottom;
            }
            catch { return false; }
        }

        private void PositionBadge(double dipX, double dipY)
        {
            double scale = _s.BadgeScale > 0.5 ? _s.BadgeScale : 1.0;

            // Cache Measure() — re-measure when scale changes OR when the
            EnsureBadgeMeasured(scale);
            double bw = _cachedBw;
            double bh = _cachedBh;

            double off = _s.BadgeOffsetX > 0 ? _s.BadgeOffsetX : 20;

            // Canvas origin sits at (VirtualScreenLeft, VirtualScreenTop) in screen
            // DIPs — the overlay window is pinned there by CoverAllMonitors().
            // dipX/dipY are screen-DIP coordinates from GetCursorPos * _dpiScaleX/Y.
            // All placement maths must use canvas-local coords so the badge tracks
            // correctly when a secondary monitor extends to the LEFT (negative
            // VirtualScreenLeft) or above (negative VirtualScreenTop) the primary.
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth;
            double vh = SystemParameters.VirtualScreenHeight;
            double margin = 6;

            // Cursor position in canvas-local DIPs.
            double cx = dipX - vl;
            double cy = dipY - vt;

            bool nearRight  = cx + off + bw + margin > vw;
            bool nearLeft   = cx - off - bw - margin < 0;
            bool nearBottom = cy + off + bh + margin > vh;
            bool nearTop    = cy - off - bh - margin < 0;

            BadgeSide preferred = _s.BadgePosition;
            BadgeSide effective = preferred;

            if (preferred == BadgeSide.Right && nearRight)
            {
                if (nearBottom)      effective = BadgeSide.BottomLeft;
                else if (nearTop)    effective = BadgeSide.Left;
                else                 effective = BadgeSide.BottomRight;
            }
            else if (preferred == BadgeSide.BottomRight && nearRight && nearBottom)
                effective = BadgeSide.BottomLeft;
            else if (preferred == BadgeSide.BottomRight && nearRight)
                effective = BadgeSide.Bottom;
            else if (preferred == BadgeSide.BottomRight && nearBottom)
                effective = BadgeSide.Right;
            else if (preferred == BadgeSide.Bottom && nearBottom)
            {
                effective = nearRight ? BadgeSide.BottomLeft : BadgeSide.BottomRight;
            }
            else if (preferred == BadgeSide.BottomLeft && nearLeft && nearBottom)
                effective = BadgeSide.BottomRight;
            else if (preferred == BadgeSide.BottomLeft && nearLeft)
                effective = BadgeSide.Bottom;
            else if (preferred == BadgeSide.BottomLeft && nearBottom)
                effective = BadgeSide.Left;
            else if (preferred == BadgeSide.Left && nearLeft)
            {
                if (nearBottom)      effective = BadgeSide.BottomRight;
                else if (nearTop)    effective = BadgeSide.Right;
                else                 effective = BadgeSide.BottomLeft;
            }

            double bx, by;
            switch (effective)
            {
                case BadgeSide.Right:       bx = cx + off;       by = cy - bh / 2; break;
                case BadgeSide.BottomRight: bx = cx + off;       by = cy + off;    break;
                case BadgeSide.Bottom:      bx = cx - bw / 2;    by = cy + off;    break;
                case BadgeSide.BottomLeft:  bx = cx - bw - off;  by = cy + off;    break;
                case BadgeSide.Left:        bx = cx - bw - off;  by = cy - bh / 2; break;
                default:                    bx = cx + off;       by = cy + off;    break;
            }

            // Clamp within canvas extents (0..vw, 0..vh).
            bx = Math.Max(margin, Math.Min(bx, vw - bw - margin));
            by = Math.Max(margin, Math.Min(by, vh - bh - margin));

            Canvas.SetLeft(BadgeBorder, bx);
            Canvas.SetTop (BadgeBorder, by);
        }

        /// <summary>
        /// Pinned-mode positioning — badge sits at a fixed screen coordinate
        /// regardless of cursor position. Screen coords from AppSettings are
        /// in device pixels; we convert to DIPs using the cached DPI scale.
        /// Falls back to top-right corner of the primary monitor's work area
        /// on first use (BadgePinnedX/Y are NaN until the user drags).
        /// </summary>
        private void PositionBadgePinned()
        {
            double scale = _s.BadgeScale > 0.5 ? _s.BadgeScale : 1.0;

            EnsureBadgeMeasured(scale);
            double bw = _cachedBw;
            double bh = _cachedBh;

            // Canvas-local origin: overlay window is at (VirtualScreenLeft, VirtualScreenTop).
            // BadgePinnedX/Y are stored in device pixels (screen-absolute), so converting
            // to canvas DIPs requires two steps: device_px → screen_DIP (* _dpiScaleX),
            // then screen_DIP → canvas_DIP (- VirtualScreenLeft).
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth;
            double vh = SystemParameters.VirtualScreenHeight;

            double bx, by;
            // Sentinel = -1 ("not yet set") — also tolerate NaN from
            // settings.json files written by earlier builds.
            if (_s.BadgePinnedX < 0 || _s.BadgePinnedY < 0
                || double.IsNaN(_s.BadgePinnedX) || double.IsNaN(_s.BadgePinnedY))
            {
                // First-use default: top-right of primary work area, inset 16 px.
                // SystemParameters.WorkArea is in screen DIPs; convert to canvas DIPs.
                var wa = SystemParameters.WorkArea;
                double screenDipX = wa.Right - bw - 16;
                double screenDipY = wa.Top   + 16;
                bx = screenDipX - vl;
                by = screenDipY - vt;

                // Write the resolved default back to settings in device pixels so
                // subsequent ticks and the next launch both land in the same spot.
                _s.BadgePinnedX = screenDipX / _dpiScaleX;
                _s.BadgePinnedY = screenDipY / _dpiScaleY;
            }
            else
            {
                // BadgePinnedX/Y in device px → screen DIPs → canvas DIPs.
                bx = _s.BadgePinnedX * _dpiScaleX - vl;
                by = _s.BadgePinnedY * _dpiScaleY - vt;
            }

            // Clamp within canvas extents so a stale position on a disconnected
            // monitor doesn't hide the badge off-screen.
            bx = Math.Max(0, Math.Min(bx, vw - bw));
            by = Math.Max(0, Math.Min(by, vh - bh));

            Canvas.SetLeft(BadgeBorder, bx);
            Canvas.SetTop (BadgeBorder, by);
        }

        // ── Win32 click-through ───────────────────────────────────────────────
        private void MakeClickThrough()
        {
            long ex = NativeMethods.GetWindowLong(_hwndSelf, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLong(_hwndSelf, NativeMethods.GWL_EXSTYLE,
                new IntPtr(ex | NativeMethods.WS_EX_LAYERED
                              | NativeMethods.WS_EX_TRANSPARENT
                              | NativeMethods.WS_EX_TOOLWINDOW));

            IntPtr hiddenOwner = NativeMethods.GetWindow(_hwndSelf, NativeMethods.GW_OWNER);
            if (hiddenOwner != IntPtr.Zero)
                NativeMethods.SetWindowPos(hiddenOwner, NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING |
                    NativeMethods.SWP_NOOWNERZORDER);
        }

        // ── Cleanup ───────────────────────────────────────────────────────────
        private bool _shutdownRan;

        protected override void OnClosed(EventArgs e)
        {
            if (!_shutdownRan)
            {
                _shutdownRan = true;
                try { _timer.Stop(); }                 catch { }
                try { UnregisterWinEventHooks(); }     catch { }
                try { _uia?.Dispose(); }               catch { }
                try { _announcer?.Dispose(); _announcer = null; } catch { }
            }
            base.OnClosed(e);
        }
    }
}
