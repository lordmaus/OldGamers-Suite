using System;
using System.Runtime.InteropServices;

namespace SuperMagnifier
{
    internal static class NativeMethods
    {
        // ── GDI — screen capture (used by LensWindow snapshot) ──────────────────
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]  public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]  public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")]  public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")]  public static extern bool   BitBlt(IntPtr dst, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")]  public static extern bool   DeleteDC(IntPtr hdc);
        public const uint SRCCOPY = 0x00CC0020;

        // ── Magnification API ────────────────────────────────────────────────
        [DllImport("Magnification.dll", SetLastError = true)]
        public static extern bool MagInitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        public static extern bool MagUninitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        public static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM pTransform);

        [DllImport("Magnification.dll", SetLastError = true)]
        public static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);

        [DllImport("Magnification.dll", SetLastError = true)]
        public static extern bool MagSetColorEffect(IntPtr hwnd, ref MAGCOLOREFFECT pEffect);

        /// <summary>
        /// Shows or hides the system cursor in all magnifier windows.
        /// Pass false to prevent the Magnification API from rendering the cursor
        /// inside the lens (it positions it incorrectly on high-DPI displays).
        /// </summary>
        [DllImport("Magnification.dll", SetLastError = true)]
        public static extern bool MagShowSystemCursor(bool fShowCursor);

        // ── User32 ──────────────────────────────────────────────────────────
        [DllImport("user32.dll")]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>Get the current cursor handle (the one currently displayed).
        /// Captures YoloMouse's custom cursor before DWM resets it.</summary>


        public const int SM_CXBORDER = 5;  // window border width

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>Set the visible/click-hit region of a window. Pass IntPtr.Zero to remove.</summary>
        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom,
                                                        int widthEllipse, int heightEllipse);

        /// <summary>Free a GDI object (region, brush, etc). Only call if SetWindowRgn failed —
        /// on success SetWindowRgn takes ownership of the region handle.</summary>
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);


        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        // Creates an invisible (blank) cursor
        public static IntPtr CreateBlankCursor()
        {
            // 1×1 AND mask (all 1s = transparent), 1×1 XOR mask (all 0s = black but transparent wins)
            byte[] andMask = { 0xFF };
            byte[] xorMask = { 0x00 };
            return CreateCursor(IntPtr.Zero, 0, 0, 1, 1, andMask, xorMask);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot,
            int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

        [DllImport("user32.dll")]
        public static extern bool DestroyCursor(IntPtr hCursor);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // C-6 (SuperMagnifierLV_CodeSecA11y_Review_2026-09-14): GetWindowText/GetClassName
        // removed — verified zero call sites anywhere in the project before removing.

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        public const uint GA_ROOT        = 2;
        public const uint WM_NCHITTEST   = 0x0084;
        public const int  HTCLOSE        = 20;
        public const int  HTMAXBUTTON    = 9;
        public const int  HTMINBUTTON    = 8;
        public const int  HTHELP         = 21;



        [DllImport("user32.dll")]
        public static extern bool SystemParametersInfo(
            uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        // Overload for reading values back (pvParam is ref so Windows can write into it)
        [DllImport("user32.dll")]
        public static extern bool SystemParametersInfo(
            uint uiAction, uint uiParam, ref IntPtr pvParam, uint fWinIni);

        // ── Kernel32 ────────────────────────────────────────────────────────
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        // ── DWM ─────────────────────────────────────────────────────────────
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        // ── Constants ───────────────────────────────────────────────────────
        public const uint WS_CHILD        = 0x40000000;
        public const uint WS_VISIBLE      = 0x10000000;
        public const uint WS_CLIPCHILDREN = 0x02000000;
        public const uint WS_CLIPSIBLINGS = 0x04000000;

        public const int GWL_EXSTYLE    = -20;
        public const int WS_EX_NOACTIVATE  = 0x08000000;
        public const int WS_EX_TOOLWINDOW  = 0x00000080;
        public const int WS_EX_TOPMOST     = 0x00000008;
        public const int WS_EX_LAYERED     = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;  // click-through

        public const uint SWP_NOMOVE      = 0x0002;
        public const uint SWP_NOSIZE      = 0x0001;
        public const uint SWP_NOZORDER    = 0x0004;
        public const uint SWP_NOACTIVATE  = 0x0010;
        public const uint SWP_SHOWWINDOW  = 0x0040;
        // Forces the window to recompute/repaint its frame with no actual move or
        // resize — used to flush WPF property changes (BorderThickness, GridLength
        // row heights) into a layered (AllowsTransparency) window's composited
        // surface. Those changes apply in the object model immediately but the
        // on-screen layered surface only fully refreshes on a hint like this one,
        // or on a genuine WM_MOVE/WM_SIZE (which is why dragging the window
        // "fixes" stale appearance otherwise).
        public const uint SWP_FRAMECHANGED = 0x0020;

        public static readonly IntPtr HWND_TOPMOST    = new IntPtr(-1);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        public const int SM_XVIRTUALSCREEN  = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        public const uint SPI_SETFONTSMOOTHING     = 0x004B;
        public const uint SPI_SETFONTSMOOTHINGTYPE = 0x200B;
        public const uint SPI_GETFONTSMOOTHING     = 0x004A;
        public const uint SPI_GETFONTSMOOTHINGTYPE = 0x200A;
        public const uint FE_FONTSMOOTHINGCLEARTYPE = 0x0002;
        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE    = 0x02;

        public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        // ── Structs ─────────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left, top, right, bottom;
            public RECT(int l, int t, int r, int b) { left=l; top=t; right=r; bottom=b; }
        }

        /// <summary>3×3 float transformation matrix for MagSetWindowTransform.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MAGTRANSFORM
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public float[] v;

            public static MAGTRANSFORM Scale(float zoom)
            {
                var t = new MAGTRANSFORM { v = new float[9] };
                t.v[0] = zoom;   // scale X
                t.v[4] = zoom;   // scale Y
                t.v[8] = 1.0f;
                return t;
            }
        }

        /// <summary>5×5 colour-effect matrix for MagSetColorEffect.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MAGCOLOREFFECT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] transform;

            public static MAGCOLOREFFECT Identity()
            {
                var e = new MAGCOLOREFFECT { transform = new float[25] };
                e.transform[0]  = 1f;
                e.transform[6]  = 1f;
                e.transform[12] = 1f;
                e.transform[18] = 1f;
                e.transform[24] = 1f;
                return e;
            }

            public static MAGCOLOREFFECT Sharpen()
            {
                // Diagonal-only sharpening: boost contrast per-channel without
                // cross-channel bleed (which causes colour fringing on edges).
                // Factor 1.25 with offset -0.125 keeps the midpoint (0.5) neutral.
                var e = new MAGCOLOREFFECT { transform = new float[25] };
                float s = 1.25f, o = -0.125f;
                e.transform[0]  = s;   // R → R*1.25
                e.transform[6]  = s;   // G → G*1.25
                e.transform[12] = s;   // B → B*1.25
                e.transform[18] = 1f;  // A unchanged
                e.transform[20] = o;   // R offset
                e.transform[21] = o;   // G offset
                e.transform[22] = o;   // B offset
                e.transform[24] = 1f;
                return e;
            }

            /// <summary>Inverts all colours (white-on-black).</summary>
            public static MAGCOLOREFFECT Invert()
            {
                var e = new MAGCOLOREFFECT { transform = new float[25] };
                e.transform[0]  = -1f;   // R → -R
                e.transform[6]  = -1f;   // G → -G
                e.transform[12] = -1f;   // B → -B
                e.transform[18] =  1f;   // A unchanged
                e.transform[20] =  1f;   // R offset +1
                e.transform[21] =  1f;   // G offset +1
                e.transform[22] =  1f;   // B offset +1
                e.transform[24] =  1f;
                return e;
            }

            /// <summary>Greyscale — removes all colour information.</summary>
            public static MAGCOLOREFFECT Grayscale()
            {
                var e = new MAGCOLOREFFECT { transform = new float[25] };
                // Luma coefficients (BT.709)
                float r = 0.2126f, g = 0.7152f, b = 0.0722f;
                e.transform[0]  = r; e.transform[1]  = r; e.transform[2]  = r;
                e.transform[5]  = g; e.transform[6]  = g; e.transform[7]  = g;
                e.transform[10] = b; e.transform[11] = b; e.transform[12] = b;
                e.transform[18] = 1f;
                e.transform[24] = 1f;
                return e;
            }

            /// <summary>High contrast: boosts contrast strongly.</summary>
            public static MAGCOLOREFFECT HighContrast()
            {
                var e = new MAGCOLOREFFECT { transform = new float[25] };
                float c = 2.5f;
                e.transform[0]  = c;
                e.transform[6]  = c;
                e.transform[12] = c;
                e.transform[18] = 1f;
                // offset = -(c-1)/2  so midpoint stays at 0.5
                float o = -(c - 1f) / 2f;
                e.transform[20] = o;
                e.transform[21] = o;
                e.transform[22] = o;
                e.transform[24] = 1f;
                return e;
            }

            /// <summary>Deuteranopia (red-green, green deficiency) simulation/correction.</summary>
            public static MAGCOLOREFFECT Deuteranopia()
            {
                var e = new MAGCOLOREFFECT { transform = new float[25] };
                e.transform[0]  =  0.625f; e.transform[1]  =  0.700f; e.transform[2]  =  0f;
                e.transform[5]  =  0.375f; e.transform[6]  =  0.300f; e.transform[7]  =  0f;
                e.transform[10] =  0f;     e.transform[11] =  0f;     e.transform[12] =  1f;
                e.transform[18] =  1f;     e.transform[24] =  1f;
                return e;
            }

            /// <summary>Protanopia (red-green, red deficiency) correction.</summary>
            public static MAGCOLOREFFECT Protanopia()
            {
                var e = new MAGCOLOREFFECT { transform = new float[25] };
                e.transform[0]  =  0.567f; e.transform[1]  =  0.558f; e.transform[2]  =  0f;
                e.transform[5]  =  0.433f; e.transform[6]  =  0.442f; e.transform[7]  =  0f;
                e.transform[10] =  0f;     e.transform[11] =  0f;     e.transform[12] =  1f;
                e.transform[18] =  1f;     e.transform[24] =  1f;
                return e;
            }

            /// <summary>Tritanopia (blue-yellow deficiency) correction.</summary>
            public static MAGCOLOREFFECT Tritanopia()
            {
                var e = new MAGCOLOREFFECT { transform = new float[25] };
                e.transform[0]  =  1f;    e.transform[1]  =  0f;    e.transform[2]  =  0f;
                e.transform[5]  =  0f;    e.transform[6]  =  0.733f;e.transform[7]  =  0.267f;
                e.transform[10] =  0f;    e.transform[11] =  0.267f;e.transform[12] =  0.733f;
                e.transform[18] =  1f;    e.transform[24] =  1f;
                return e;
            }

            /// <summary>Warm amber tint — reduces blue light, easier on photosensitive eyes.</summary>
            public static MAGCOLOREFFECT WarmAmber(float intensity = 0.5f)
            {
                var e = Identity();
                // Suppress blue channel proportionally, boost red slightly
                float b = Math.Max(0f, 1f - intensity * 0.7f);
                float g = Math.Max(0f, 1f - intensity * 0.25f);
                float r = Math.Min(1.15f, 1f + intensity * 0.05f);
                e.transform[0]  = r;   // R scale
                e.transform[6]  = g;   // G scale
                e.transform[12] = b;   // B scale
                return e;
            }

            /// <summary>Cool blue tint — increases contrast for some contrast-sensitivity conditions.</summary>
            public static MAGCOLOREFFECT CoolBlue(float intensity = 0.5f)
            {
                var e = Identity();
                float r = Math.Max(0f, 1f - intensity * 0.3f);
                float g = Math.Max(0f, 1f - intensity * 0.1f);
                float b = Math.Min(1.2f, 1f + intensity * 0.15f);
                e.transform[0]  = r;
                e.transform[6]  = g;
                e.transform[12] = b;
                return e;
            }

            public static MAGCOLOREFFECT FromMode(string mode, bool smooth, float tintIntensity = 0.5f)
            {
                switch (mode)
                {
                    case "invert":       return Invert();
                    case "grayscale":    return Grayscale();
                    case "highcontrast": return HighContrast();
                    case "deuteranopia": return Deuteranopia();
                    case "protanopia":   return Protanopia();
                    case "tritanopia":   return Tritanopia();
                    case "warmamber":    return WarmAmber(tintIntensity);
                    case "coolblue":     return CoolBlue(tintIntensity);
                    default:             return smooth ? Identity() : Sharpen();
                }
            }
        }

        // ── Window resizing via WM_SYSCOMMAND ────────────────────────────────────
        [DllImport("user32.dll")] public static extern bool   ReleaseCapture();
    }
}
