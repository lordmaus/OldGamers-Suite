using System;
using System.Diagnostics;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    internal sealed class MagnifierEngine : IDisposable
    {
        private bool _initialized;
        private bool _disposed;

        // DPI scale (physical px per logical px) — set once from LensWindow
        private float _dpiScaleX = 1f;
        private float _dpiScaleY = 1f;

        // Track previous source rect to avoid redundant InvalidateRect calls
        private RECT _lastSrc;
        private bool _lastSrcSet;

        // Track original font smoothing state so we can restore on exit
        private bool _fontSmoothingWasEnabled;
        private int  _fontSmoothingTypeWas;

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = MagInitialize();

            // Save current font smoothing state before overriding
            IntPtr val = IntPtr.Zero;
            SystemParametersInfo(SPI_GETFONTSMOOTHING, 0, ref val, 0);
            _fontSmoothingWasEnabled = val != IntPtr.Zero;
            SystemParametersInfo(SPI_GETFONTSMOOTHINGTYPE, 0, ref val, 0);
            _fontSmoothingTypeWas = val.ToInt32();

            // Ensure ClearType is on for sharpest text in the lens
            SystemParametersInfo(SPI_SETFONTSMOOTHING, 1, IntPtr.Zero,
                                 SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            SystemParametersInfo(SPI_SETFONTSMOOTHINGTYPE, 0,
                                 new IntPtr(FE_FONTSMOOTHINGCLEARTYPE),
                                 SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        /// <summary>Call after the lens window is shown so DPI is known.</summary>
        public void SetDpiScale(float scaleX, float scaleY)
        {
            _dpiScaleX = scaleX > 0 ? scaleX : 1f;
            _dpiScaleY = scaleY > 0 ? scaleY : 1f;
        }

        public void ApplyZoom(IntPtr magHwnd, float zoom)
        {
            if (magHwnd == IntPtr.Zero) return;
            var xf = MAGTRANSFORM.Scale(zoom);
            MagSetWindowTransform(magHwnd, ref xf);
        }

        public void ApplyColorEffect(IntPtr magHwnd, bool smooth, string colorMode = "normal", float tintIntensity = 0.5f)
        {
            if (magHwnd == IntPtr.Zero) return;
            var ce = MAGCOLOREFFECT.FromMode(colorMode, smooth, tintIntensity);
            MagSetColorEffect(magHwnd, ref ce);
            InvalidateRect(magHwnd, IntPtr.Zero, true);
        }

        // Last computed source rect — used by snapshot and mirror lens
        public int LastSrcLeft   { get; private set; }
        public int LastSrcTop    { get; private set; }
        public int LastSrcRight  { get; private set; }
        public int LastSrcBottom { get; private set; }
        /// <summary>
        /// Normalised cursor position within the last source rect (0..1, 0..1).
        /// When edge clamping shifts the source rect the cursor is no longer at 0.5,0.5.
        /// The reticle overlay uses this to draw at the correct position.
        /// </summary>
        public double LastCursorNormX { get; private set; } = 0.5;
        public double LastCursorNormY { get; private set; } = 0.5;

        // ── Smooth panning state ───────────────────────────────────────────────
        private double    _smoothCx  = -1;
        private double    _smoothCy  = -1;
        private Stopwatch _sw        = Stopwatch.StartNew();

        // ── Virtual screen bounds cache ────────────────────────────────────────
        // GetSystemMetrics is called at most once per source-rect change rather than
        // 4 times per tick at 60 fps.  Refreshed whenever the source rect moves.
        private int _vsLeft, _vsTop, _vsW, _vsH;
        private bool _vsCached;

        private void RefreshVirtualScreenBounds()
        {
            _vsLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            _vsTop  = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            _vsW    = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            _vsH    = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            _vsCached = true;
        }

        /// <summary>Call when a monitor is connected/disconnected to clear the bounds cache.</summary>
        public void InvalidateVirtualScreenCache() => _vsCached = false;
        public void ResetSmoothPosition()
        {
            _smoothCx = -1;
            _smoothCy = -1;
            _sw.Restart();
        }

        public void UpdateSource(IntPtr magHwnd, float zoom,
                                 int viewW, int viewH,
                                 int? overrideX = null, int? overrideY = null,
                                 bool smooth = false, double lag = 0.15,
                                 bool edgeDeadZone = true)
        {
            if (magHwnd == IntPtr.Zero) return;

            // Use GetClientRect to get the ACTUAL physical pixel size of the magnifier
            // child window. This is the approach used in Microsoft's own Magnification
            // API sample (magnification.cpp) and avoids all DPI coordinate space confusion:
            // - GetCursorPos returns physical screen pixels (PerMonitorV2 process)
            // - GetClientRect returns the window's physical client area
            // - MagSetWindowSource takes physical screen pixel coordinates
            // All three are in the same coordinate space so no DPI conversion is needed.
            int physW, physH;
            if (GetClientRect(magHwnd, out RECT clientRect) &&
                clientRect.right > 0 && clientRect.bottom > 0)
            {
                physW = clientRect.right;   // right = width (left is always 0)
                physH = clientRect.bottom;  // bottom = height (top is always 0)
            }
            else
            {
                // Fallback if GetClientRect fails: use logical size × DPI scale
                physW = Math.Max(4, (int)Math.Round(viewW * _dpiScaleX));
                physH = Math.Max(4, (int)Math.Round(viewH * _dpiScaleY));
            }

            physW = Math.Max(4, physW);
            physH = Math.Max(4, physH);

            // Source region: how many physical screen pixels to capture at this zoom level
            int sw = Math.Max(4, (int)Math.Round(physW / (double)zoom));
            int sh = Math.Max(4, (int)Math.Round(physH / (double)zoom));

            double rawCx, rawCy;
            if (overrideX.HasValue && overrideY.HasValue)
            {
                rawCx = overrideX.Value;
                rawCy = overrideY.Value;
            }
            else
            {
                GetCursorPos(out POINT pt);
                rawCx = pt.X;
                rawCy = pt.Y;
            }

            // Smooth follow: lerp normalised to elapsed time so feel is
            // identical at 15 fps and 60 fps.
            if (smooth)
            {
                double dtMs   = _sw.Elapsed.TotalMilliseconds;
                _sw.Restart();
                if (_smoothCx < 0)
                {
                    _smoothCx = rawCx;
                    _smoothCy = rawCy;
                }
                else
                {
                    // Normalise to 60 fps reference so lag setting is frame-rate independent
                    double frames  = Math.Max(0.5, dtMs / (1000.0 / 60.0));
                    double alpha   = Math.Max(0.02, Math.Min(1.0, 1.0 - lag));
                    double alphaDt = 1.0 - Math.Pow(1.0 - alpha, frames);
                    _smoothCx += (rawCx - _smoothCx) * alphaDt;
                    _smoothCy += (rawCy - _smoothCy) * alphaDt;
                }
            }
            else
            {
                _smoothCx = rawCx;
                _smoothCy = rawCy;
                _sw.Restart();
            }

            int cx = (int)Math.Round(_smoothCx);
            int cy = (int)Math.Round(_smoothCy);

            // Clamp to virtual screen (supports multi-monitor).
            // Bounds are cached and only refreshed when the display configuration changes.
            if (!_vsCached) RefreshVirtualScreenBounds();
            int vsLeft = _vsLeft, vsTop = _vsTop, vsW = _vsW, vsH = _vsH;

            int left   = cx - sw / 2;
            int top    = cy - sh / 2;
            int right  = left + sw;
            int bottom = top  + sh;

            if (edgeDeadZone)
            {
                if (left  < vsLeft)       left = vsLeft;
                if (top   < vsTop)        top  = vsTop;
                if (right > vsLeft + vsW) left = vsLeft + vsW - sw;
                if (bottom> vsTop  + vsH) top  = vsTop  + vsH - sh;
                right  = left + sw;
                bottom = top  + sh;
            }
            else
            {
                if (left   < vsLeft)       { left   = vsLeft;       right  = left   + sw; }
                if (top    < vsTop)        { top    = vsTop;        bottom = top    + sh; }
                if (right  > vsLeft + vsW) { right  = vsLeft + vsW; left   = right  - sw; }
                if (bottom > vsTop  + vsH) { bottom = vsTop  + vsH; top    = bottom - sh; }
            }

            LastSrcLeft = left; LastSrcTop = top;
            LastSrcRight = right; LastSrcBottom = bottom;

            // Compute where the cursor actually sits within the (possibly clamped) source rect.
            // When not clamped this is always 0.5,0.5. When clamped near an edge the cursor
            // moves off-centre — the reticle overlay uses this to track the cursor accurately.
            int srcW = right - left;
            int srcH = bottom - top;
            LastCursorNormX = srcW > 0 ? Math.Max(0, Math.Min(1, (cx - left) / (double)srcW)) : 0.5;
            LastCursorNormY = srcH > 0 ? Math.Max(0, Math.Min(1, (cy - top)  / (double)srcH)) : 0.5;

            var src = new RECT(left, top, right, bottom);
            MagSetWindowSource(magHwnd, src);

            // Only invalidate when the source rect actually changed — avoids
            // a redundant repaint every tick when the cursor is stationary.
            if (!_lastSrcSet ||
                src.left != _lastSrc.left || src.top  != _lastSrc.top ||
                src.right!= _lastSrc.right|| src.bottom!= _lastSrc.bottom)
            {
                InvalidateRect(magHwnd, IntPtr.Zero, true);
                _lastSrc    = src;
                _lastSrcSet = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Restore the font smoothing settings we found at startup
            if (_initialized)
            {
                SystemParametersInfo(SPI_SETFONTSMOOTHING,
                    _fontSmoothingWasEnabled ? 1u : 0u, IntPtr.Zero,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                SystemParametersInfo(SPI_SETFONTSMOOTHINGTYPE, 0,
                    new IntPtr(_fontSmoothingTypeWas),
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                MagUninitialize();
                _initialized = false;
            }
        }
    }
}
