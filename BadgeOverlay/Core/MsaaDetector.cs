using System;
using System.Runtime.InteropServices;

namespace BadgeOverlay
{
    /// <summary>
    /// Fallback element detector that uses the older MSAA (Microsoft
    /// Active Accessibility) API via <c>IAccessible</c> and
    /// <c>AccessibleObjectFromPoint</c>. UIA replaced MSAA in 2007 and is
    /// the primary surface used by <see cref="UIADetector"/>, but a
    /// non-trivial tail of apps still only expose MSAA:
    ///
    /// <list type="bullet">
    /// <item>Older Java Swing apps via Java Access Bridge</item>
    /// <item>Qt versions before 6 (UIA support landed in Qt 6)</item>
    /// <item>SWT (Eclipse) applications</item>
    /// <item>Some legacy Delphi / VB6 apps</item>
    /// </list>
    ///
    /// <para>We call this ONLY when UIA returns nothing useful — it's a
    /// fallback, not a replacement. MSAA has less information (no
    /// patterns, fewer properties, no pattern-based actions) and is more
    /// prone to cross-process hangs, so we timebox the call and swallow
    /// any failure rather than propagating.</para>
    ///
    /// <para>Thread safety: MSAA itself is STA-affinitised but
    /// <c>AccessibleObjectFromPoint</c> is safe to call from an MTA
    /// worker because it returns a proxy that marshals on use.
    /// OverlayWindow's caller relies on this: it runs the query via
    /// <c>Task.Run</c> on a background thread (not the UI thread) so a
    /// slow/hung COM provider can't block the 30 Hz tick loop, and shows
    /// the previous result while a fresh query is in flight.</para>
    /// </summary>
    internal static class MsaaDetector
    {
        // ── Native interop ────────────────────────────────────────────────────

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromPoint(
            NativeMethods.POINT pt,
            [MarshalAs(UnmanagedType.Interface)] out IAccessible acc,
            [MarshalAs(UnmanagedType.Struct)]    out object      childId);

        private const int CHILDID_SELF = 0;

        // MSAA role constants — the handful we actually classify.
        private const int ROLE_PUSHBUTTON   = 0x2B;
        private const int ROLE_CHECKBUTTON  = 0x2C;
        private const int ROLE_RADIOBUTTON  = 0x2D;
        private const int ROLE_LINK         = 0x1E;
        private const int ROLE_MENUITEM     = 0x0C;
        private const int ROLE_LISTITEM     = 0x22;
        private const int ROLE_OUTLINEITEM  = 0x24;
        private const int ROLE_STATICTEXT   = 0x29;
        private const int ROLE_TEXT         = 0x2A;
        private const int ROLE_COMBOBOX     = 0x2E;

        // MSAA state bits we care about.
        private const int STATE_UNAVAILABLE = 0x00000001;
        private const int STATE_CHECKED     = 0x00000010;
        private const int STATE_PRESSED     = 0x00000008;

        /// <summary>
        /// Queries MSAA for the element under the given screen point.
        /// Returns null when nothing accessible is reported, when the
        /// element has no meaningful name, or when COM throws.
        /// </summary>
        /// <summary>E_ACCESSDENIED — target runs at a higher integrity level (e.g. "Run as administrator").</summary>
        public const int E_ACCESSDENIED = unchecked((int)0x80070005);

        public static DetectedElement Query(int x, int y) => Query(x, y, out _);

        /// <summary>
        /// Same as <see cref="Query(int,int)"/> but reports the HRESULT of
        /// AccessibleObjectFromPoint (or of the exception) so callers can tell
        /// "nothing here" (0) from "can't read this app" (E_ACCESSDENIED etc.) — BO-02.
        /// </summary>
        public static DetectedElement Query(int x, int y, out int hresult)
        {
            hresult = 0;
            IAccessible acc = null;
            try
            {
                var pt = new NativeMethods.POINT { X = x, Y = y };
                int hr = AccessibleObjectFromPoint(pt, out acc, out object childId);
                hresult = hr;
                if (hr != 0 || acc == null) return null;

                object idArg = childId ?? CHILDID_SELF;

                string name = SafeGetString(() => acc.get_accName(idArg));
                if (string.IsNullOrWhiteSpace(name) || name.Length <= 1)
                    return null;

                int role  = SafeGetInt(() => Convert.ToInt32(acc.get_accRole(idArg)));
                int state = SafeGetInt(() => Convert.ToInt32(acc.get_accState(idArg)));
                string description = SafeGetString(() => acc.get_accDescription(idArg));
                string shortcut    = SafeGetString(() => acc.get_accKeyboardShortcut(idArg));

                bool enabled = (state & STATE_UNAVAILABLE) == 0;

                (string category, string icon) = ClassifyRole(role);

                string toggle = "";
                if (role == ROLE_CHECKBUTTON || role == ROLE_RADIOBUTTON)
                    toggle = (state & STATE_CHECKED) != 0 ? "On" : "Off";
                else if ((state & STATE_PRESSED) != 0)
                    toggle = "On";

                return new DetectedElement
                {
                    Label          = name,
                    HelpText       = description ?? "",
                    Category       = category,
                    Icon           = icon,
                    IsEnabled      = enabled,
                    Source         = DetectionSource.MSAA,
                    AcceleratorKey = shortcut ?? "",
                    ToggleState    = toggle,
                    OriginX        = x,
                    OriginY        = y,
                };
            }
            catch (Exception ex)
            {
                hresult = ex.HResult != 0 ? ex.HResult : E_ACCESSDENIED;
                return null;
            }
            finally
            {
                if (acc != null && Marshal.IsComObject(acc))
                    Marshal.ReleaseComObject(acc);
            }
        }

        private static (string cat, string icon) ClassifyRole(int role)
        {
            switch (role)
            {
                case ROLE_PUSHBUTTON:  return (DetectorLabels.Cat.Action,  "▶");
                case ROLE_CHECKBUTTON: return (DetectorLabels.Cat.Control, "☑");
                case ROLE_RADIOBUTTON: return (DetectorLabels.Cat.Control, "⦿");
                case ROLE_LINK:        return (DetectorLabels.Cat.Nav,     "🔗");
                case ROLE_MENUITEM:    return (DetectorLabels.Cat.Menu,    "☰");
                case ROLE_LISTITEM:    return (DetectorLabels.Cat.Control, "☰");
                case ROLE_OUTLINEITEM: return (DetectorLabels.Cat.Control, "🌲");
                case ROLE_COMBOBOX:    return (DetectorLabels.Cat.Control, "▼");
                case ROLE_STATICTEXT:
                case ROLE_TEXT:        return (DetectorLabels.Cat.Text,    "T");
                default:               return (DetectorLabels.Cat.Control, "◉");
            }
        }

        private static string SafeGetString(Func<string> f)
        {
            try { return (f() ?? "").Trim(); }
            catch { return ""; }
        }

        private static int SafeGetInt(Func<int> f)
        {
            try { return f(); }
            catch { return 0; }
        }
    }

    // ── IAccessible COM interface ─────────────────────────────────────────────
    // Minimal IAccessible declaration — we only call the four getters
    // MsaaDetector.Query actually needs. The interface is defined in
    // oleacc.dll / oleacc.h. Microsoft ships a full managed surface in
    // Accessibility.dll but pulling that in drags IDispatch machinery we
    // don't need.

    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    internal interface IAccessible
    {
        [DispId(-5000)] object  get_accParent();
        [DispId(-5001)] int     get_accChildCount();
        [DispId(-5002)] object  get_accChild(object varChild);
        [DispId(-5003)] string  get_accName(object varChild);
        [DispId(-5004)] string  get_accValue(object varChild);
        [DispId(-5005)] string  get_accDescription(object varChild);
        [DispId(-5006)] object  get_accRole(object varChild);
        [DispId(-5007)] object  get_accState(object varChild);
        [DispId(-5008)] string  get_accHelp(object varChild);
        [DispId(-5009)] int     get_accHelpTopic(out string pszHelpFile, object varChild);
        [DispId(-5010)] string  get_accKeyboardShortcut(object varChild);
        [DispId(-5011)] object  get_accFocus();
        [DispId(-5012)] object  get_accSelection();
        [DispId(-5013)] string  get_accDefaultAction(object varChild);
        [DispId(-5014)] void    accSelect(int flagsSelect, object varChild);
        [DispId(-5015)] void    accLocation(out int pxLeft, out int pyTop,
                                            out int pcxWidth, out int pcyHeight,
                                            object varChild);
        [DispId(-5016)] object  accNavigate(int navDir, object varStart);
        [DispId(-5017)] object  accHitTest(int xLeft, int yTop);
        [DispId(-5018)] void    accDoDefaultAction(object varChild);
        [DispId(-5019)] void    put_accName(object varChild, string pszName);
        [DispId(-5020)] void    put_accValue(object varChild, string pszValue);
    }
}
