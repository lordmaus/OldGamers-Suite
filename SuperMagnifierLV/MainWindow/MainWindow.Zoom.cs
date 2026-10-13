using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SuperMagnifier
{
    // Zoom logic extracted into a partial class for maintainability.
    // All fields referenced here are declared in MainWindow.xaml.cs.
    public partial class MainWindow
    {
        // ── Zoom ──────────────────────────────────────────────────────────────
        private void ZoomUpBtn_Click  (object s, RoutedEventArgs e) => ZoomBy(+_zoomStep);
        private void ZoomDownBtn_Click(object s, RoutedEventArgs e) => ZoomBy(-_zoomStep);

        private void ZoomPreset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn?.Tag is string tag && double.TryParse(tag, out double z))
                SetZoom(z);
        }

        private void ZoomBy(double delta)
        {
            double raw     = _zoom + delta;
            double snapped = Math.Round(Math.Round(raw / _zoomStep) * _zoomStep, 4);
            SetZoom(Math.Max(_zoomMin, Math.Min(_zoomMax, snapped)));
        }

        private void SetZoom(double z, bool animate = true)
        {
            // Clamp to global slider range first, then preset zoom lock
            z = Math.Max(1.25, Math.Min(20.0, z));
            z = Math.Max(_zoomMin, Math.Min(_zoomMax, z));

            if (_settings.ZoomAnimate && !_reduceMotion && animate && _active && Math.Abs(z - _zoom) > 0.05)
            {
                StartZoomAnimation(z);
                return;
            }
            ApplyZoomDirect(z);
        }

        private void ApplyZoomDirect(double z)
        {
            _zoom = z;
            if (ZoomSlider == null) return;
            SetZoomDisplay(_zoom);
            _sliderChanging  = true;
            ZoomSlider.Value = _zoom;
            _sliderChanging  = false;
            if (_engine == null || _lens == null) return;
            _engine.ResetSmoothPosition();
            if (_active) _engine.ApplyZoom(_lens.MagHwnd, (float)_zoom);

            // Update live zoom label in lens title bar
            _lens.UpdateZoomLabel(_zoom);

            // Opacity follow zoom
            if (_settings.OpacityFollowZoom && _active)
            {
                double minZ = _zoomMin > 1.25 ? _zoomMin : 1.25;
                double maxZ = _zoomMax < 20.0  ? _zoomMax : 20.0;
                double t    = maxZ > minZ ? (_zoom - minZ) / (maxZ - minZ) : 1.0;
                double op   = 0.3 + t * 0.7; // 30% at min zoom, 100% at max zoom
                op = Math.Max(0.2, Math.Min(1.0, op));
                _lens.SetLensOpacity(op);
                if (LensOpacitySlider != null) LensOpacitySlider.Value = op;
                UpdateOpacityLabel(op);
            }

            // Per-monitor zoom memory — save current zoom for the active monitor
            if (_active && !string.IsNullOrEmpty(_lastMonitorName))
                _monitorZoomMap[_lastMonitorName] = _zoom;

            // Update zoom button enabled state at boundaries
            ZoomUpCommand?.RaiseCanExecuteChanged();
            ZoomDownCommand?.RaiseCanExecuteChanged();
        }

        // ── Zoom animation ────────────────────────────────────────────────────
        private void StartZoomAnimation(double target)
        {
            _zoomAnimStart   = _zoom;
            _zoomAnimTarget  = target;
            _zoomAnimElapsed = 0;

            if (_zoomAnimTimer == null)
            {
                _zoomAnimTimer = new DispatcherTimer(DispatcherPriority.Render)
                    { Interval = TimeSpan.FromMilliseconds(16) };
                _zoomAnimTimer.Tick += ZoomAnimTick;
            }
            _zoomAnimTimer.Start();
        }

        private void ZoomAnimTick(object sender, EventArgs e)
        {
            _zoomAnimElapsed += 16;
            double t = Math.Min(1.0, _zoomAnimElapsed / ZoomAnimDurationMs);
            // Ease-in-out cubic
            double ease = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            double z = _zoomAnimStart + (_zoomAnimTarget - _zoomAnimStart) * ease;

            ApplyZoomDirect(z);

            if (t >= 1.0)
            {
                _zoomAnimTimer.Stop();
                ApplyZoomDirect(_zoomAnimTarget); // snap to exact target
            }
        }

        private void SetZoomDisplay(double z)
        {
            if (ZoomLabel != null) ZoomLabel.Text = $"{z:F2}×";
            UpdateLensTitleBar();
        }

        internal void UpdateLensTitleBar()
        {
            if (_lens == null) return;
            string frozen = _lens.Frozen ? " · Frozen" : "";
            _lens.Title = $"Super Magnifier LV  {_zoom:F2}×{frozen}";
        }
        private void ZoomSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_sliderChanging) return;
            double snapped = Math.Round(Math.Round(e.NewValue / _zoomStep) * _zoomStep, 4);
            snapped = Math.Max(_zoomMin, Math.Min(_zoomMax, snapped));
            // Use SetZoom but guard against re-entry on the slider
            _sliderChanging = true;
            SetZoom(snapped);
            _sliderChanging = false;
        }

        // ── Step ──────────────────────────────────────────────────────────────
        private void SetStepRadio(double step)
        {
            // Radio buttons removed from main UI; just apply the value and update display label
            _zoomStep = step;
            if (StepDisplay != null)
                StepDisplay.Text = step == 0.1  ? "0.1×"
                                 : step == 0.5  ? "0.5×"
                                 : step == 1.0  ? "1×"
                                 : step == 2.0  ? "2×"
                                 : "0.25×";
            // WCAG 2.1.1: keep ZoomSlider keyboard step in sync with chosen step size
            if (ZoomSlider != null)
            {
                ZoomSlider.SmallChange = step;
                ZoomSlider.LargeChange = Math.Min(step * 4, 2.0);
            }
        }

    }
}
