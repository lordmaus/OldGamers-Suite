using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TimerStudio.Controls
{
    /// <summary>Edge an <see cref="AccessibleResizeGrip"/> resizes.</summary>
    public enum GripEdge { BottomRight, Left, Right }

    /// <summary>
    /// Suite-canonical bottom-right resize grip (WCAG 2.2 AAA: 2.1.3, 2.4.7/2.4.13, 2.5.5, 1.4.11).
    ///  - 44×44 hit area, glyph 16px in BrushMuted at full opacity (≥7:1 on Bg/Surf)
    ///  - Focusable; Arrow = 10px, Shift+Arrow = 1px; respects Min/Max Width/Height
    ///  - 2px FocusRingBrush ring on keyboard focus (no default dotted focus visual)
    ///  - UIA: ControlType Thumb, Name "Resize window" + HelpText
    ///  - Mouse drag hands off to the OS sizing loop (WM_NCLBUTTONDOWN / HTBOTTOMRIGHT)
    /// Usage: place last in the window's root Grid with Grid.RowSpan/ColumnSpan="99";
    /// it self-aligns Right/Bottom. Theme brushes are resource references, so theme swaps apply live.
    /// Copy this file unchanged into other suite apps (change only the namespace).
    /// <para><see cref="Edge"/> (added 2026-09-25, NoteStation): <c>Left</c>/<c>Right</c> turn it into a
    /// full-height, width-only edge grip (screen-edge-docked strips); Left/Right arrows only.</para>
    /// </summary>
    public sealed class AccessibleResizeGrip : UserControl
    {
        private const double Step = 10, FineStep = 1;
        private readonly Border _ring;
        private readonly Path _glyph;
        private GripEdge _edge = GripEdge.BottomRight;

        /// <summary>Which window edge this grip resizes. Default bottom-right corner.</summary>
        public GripEdge Edge
        {
            get => _edge;
            set { _edge = value; ApplyEdge(); }
        }

        public AccessibleResizeGrip()
        {
            Width = 44; Height = 44;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment   = VerticalAlignment.Bottom;
            Focusable = true;
            IsTabStop = true;
            FocusVisualStyle = null;
            Cursor = Cursors.SizeNWSE;
            Background = Brushes.Transparent;   // whole 44×44 is hit-testable
            AutomationProperties.SetName(this, "Resize window");
            AutomationProperties.SetHelpText(this,
                "Drag to resize, or focus and use arrow keys (Shift+arrow for 1 pixel steps)");
            ToolTip = "Resize — drag, or arrow keys when focused";
            Grid.SetRowSpan(this, 99);
            Grid.SetColumnSpan(this, 99);
            Panel.SetZIndex(this, 1000);

            var glyph = _glyph = new Path
            {
                Data = Geometry.Parse("M 16,2 L 2,16 M 16,7 L 7,16 M 16,12 L 12,16"),
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 18, Height = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 5, 5),
                IsHitTestVisible = false,
            };
            glyph.SetResourceReference(Shape.StrokeProperty, "BrushMuted");

            _ring = new Border
            {
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            _ring.SetResourceReference(Border.BorderBrushProperty, "FocusRingBrush");

            var root = new Grid();
            root.Children.Add(glyph);
            root.Children.Add(_ring);
            Content = root;
        }

        private void ApplyEdge()
        {
            bool corner = _edge == GripEdge.BottomRight;
            Width  = corner ? 44 : 12;
            Height = corner ? 44 : double.NaN;
            VerticalAlignment   = corner ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            HorizontalAlignment = _edge == GripEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            Cursor = corner ? Cursors.SizeNWSE : Cursors.SizeWE;
            _glyph.Data = Geometry.Parse(corner
                ? "M 16,2 L 2,16 M 16,7 L 7,16 M 16,12 L 12,16"
                : "M 3,0 L 3,24 M 9,0 L 9,24");
            _glyph.Width  = corner ? 18 : 12;
            _glyph.Height = corner ? 18 : 24;
            _glyph.HorizontalAlignment = corner ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            _glyph.VerticalAlignment   = corner ? VerticalAlignment.Bottom : VerticalAlignment.Center;
            _glyph.Margin = corner ? new Thickness(0, 0, 5, 5) : new Thickness(0);
            AutomationProperties.SetName(this, corner ? "Resize window" : "Resize width");
            AutomationProperties.SetHelpText(this, corner
                ? "Drag to resize, or focus and use arrow keys (Shift+arrow for 1 pixel steps)"
                : "Drag to resize the width, or focus and use Left/Right arrow keys (Shift+arrow for 1 pixel steps)");
            ToolTip = corner ? "Resize — drag, or arrow keys when focused" : "Resize width — drag, or Left/Right arrow keys when focused";
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            _ring.Opacity = 1;
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            _ring.Opacity = 0;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var w = Window.GetWindow(this);
            if (w == null || w.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize) return;
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            ReleaseMouseCapture();
            int ht = _edge switch { GripEdge.Left => HTLEFT, GripEdge.Right => HTRIGHT, _ => HTBOTTOMRIGHT };
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)ht, IntPtr.Zero);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;
            var w = Window.GetWindow(this);
            if (w == null || w.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize) return;
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? FineStep : Step;
            double dx = 0, dy = 0;
            switch (e.Key)
            {
                case Key.Right: dx =  step; break;
                case Key.Left:  dx = -step; break;
                case Key.Down:  dy =  step; break;
                case Key.Up:    dy = -step; break;
                default: return;
            }
            if (_edge == GripEdge.BottomRight) Resize(w, dx, dy);
            else if (dx != 0) ResizeWidth(w, dx, fromLeft: _edge == GripEdge.Left);
            else return;
            e.Handled = true;
        }

        /// <summary>Resize a window by (dx, dy) DIPs, clamped to its Min/Max; shared with KeyboardWindowMover.</summary>
        internal static void Resize(Window w, double dx, double dy)
        {
            if (w.SizeToContent != SizeToContent.Manual) w.SizeToContent = SizeToContent.Manual;
            if (w.WindowState != WindowState.Normal) return;
            if (dx != 0)
                w.Width  = Clamp(w.ActualWidth  + dx, w.MinWidth,  w.MaxWidth);
            if (dy != 0)
                w.Height = Clamp(w.ActualHeight + dy, w.MinHeight, w.MaxHeight);
        }

        /// <summary>Width-only resize. <paramref name="fromLeft"/>: the left edge moves (Left arrow widens).</summary>
        internal static void ResizeWidth(Window w, double dx, bool fromLeft)
        {
            if (w.WindowState != WindowState.Normal) return;
            if (w.SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight)
                w.SizeToContent = w.SizeToContent == SizeToContent.WidthAndHeight ? SizeToContent.Height : SizeToContent.Manual;
            double oldW = w.ActualWidth;
            double newW = Clamp(fromLeft ? oldW - dx : oldW + dx, w.MinWidth, w.MaxWidth);
            if (fromLeft) w.Left += oldW - newW;
            w.Width = newW;
        }

        private static double Clamp(double v, double min, double max)
            => Math.Max(min, double.IsInfinity(max) ? v : Math.Min(max, v));

        protected override AutomationPeer OnCreateAutomationPeer() => new GripPeer(this);

        private sealed class GripPeer(AccessibleResizeGrip owner) : FrameworkElementAutomationPeer(owner)
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;
            protected override string GetClassNameCore() => nameof(AccessibleResizeGrip);
            protected override bool IsContentElementCore() => true;
        }

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTLEFT           = 10;
        private const int HTRIGHT          = 11;
        private const int HTBOTTOMRIGHT    = 17;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
