using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;

namespace ReticleOverlay
{
    /// <summary>
    /// Single source of truth for all reticle drawing.
    /// Used by OverlayWindow (live) and MainWindow (preview canvas).
    /// Supports two independent layers (#38) and all reticle types including new shapes.
    /// </summary>
    public static class ReticleRenderer
    {
        // ── Shared BitmapImage cache (LRU, max 20 entries) ───────────────────
        private const int MaxCacheSize = 20;
        private static readonly Dictionary<string, BitmapImage> _cache
            = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _cacheOrder
            = new List<string>(MaxCacheSize + 1);

        public static void InvalidateCache(string path)
        {
            _cache.Remove(path);
            _cacheOrder.Remove(path);
        }

        private static void CacheSet(string path, BitmapImage bmp)
        {
            // Promote on hit or insert on miss
            _cacheOrder.Remove(path);
            _cacheOrder.Insert(0, path);
            _cache[path] = bmp;
            // Evict LRU tail when over limit
            while (_cacheOrder.Count > MaxCacheSize)
            {
                string lru = _cacheOrder[_cacheOrder.Count - 1];
                _cacheOrder.RemoveAt(_cacheOrder.Count - 1);
                _cache.Remove(lru);
            }
        }

        // ── Primary entry points ──────────────────────────────────────────────
        public static void Draw(Canvas canvas, double w, double h, AppPrefs p)
        {
            canvas.Children.Clear();
            if (!p.ReticleVisible) return;

            double cx = w / 2.0 + p.OffsetX;
            double cy = h / 2.0 + p.OffsetY;

            // Layer 1
            DrawLayer(canvas, p.ReticleType, p.CustomImagePath,
                Math.Max(4, p.ReticleSize), p.ReticleThickness, p.CentreGap, p.Rotation,
                p.ReticleColor, p.ReticleColor2, p.UseColor2,
                p.OutlineColor, p.ShowOutline, p.Opacity, cx, cy);

            // Layer 2 (#38)
            if (p.Layer2Enabled)
                DrawLayer(canvas, p.Layer2Type, "",
                    Math.Max(4, p.Layer2Size), p.Layer2Thickness, p.Layer2CentreGap, p.Layer2Rotation,
                    p.Layer2Color, p.Layer2Color, false,
                    p.OutlineColor, false, p.Layer2Opacity, cx, cy);
        }

        public static void DrawPreview(Canvas canvas, double w, double h, AppPrefs p)
        {
            canvas.Children.Clear();
            if (w < 10 || h < 10 || !p.ReticleVisible) return;

            double maxDim = Math.Max(p.Layer2Enabled ? Math.Max(p.ReticleSize, p.Layer2Size) : p.ReticleSize, 1);
            double scale  = Math.Min(w, h) * 0.65 / maxDim;
            double previewSize1 = Math.Max(4, p.ReticleSize  * scale);
            double previewSize2 = Math.Max(4, p.Layer2Size   * scale);

            double cx = w / 2.0 + p.OffsetX * 0.25;
            double cy = h / 2.0 + p.OffsetY * 0.25;
            cx = Math.Max(10, Math.Min(w - 10, cx));
            cy = Math.Max(10, Math.Min(h - 10, cy));

            // Layer 1
            DrawLayer(canvas, p.ReticleType, p.CustomImagePath,
                previewSize1, p.ReticleThickness, p.CentreGap, p.Rotation,
                p.ReticleColor, p.ReticleColor2, p.UseColor2,
                p.OutlineColor, p.ShowOutline, p.Opacity, cx, cy);

            // Layer 2
            if (p.Layer2Enabled)
                DrawLayer(canvas, p.Layer2Type, "",
                    previewSize2, p.Layer2Thickness, p.Layer2CentreGap, p.Layer2Rotation,
                    p.Layer2Color, p.Layer2Color, false,
                    p.OutlineColor, false, p.Layer2Opacity, cx, cy);
        }

        // ── Internal dispatcher ───────────────────────────────────────────────
        // Brushes are created once per DrawLayer call (3 total) and passed into all
        // shape primitives, eliminating per-line/per-ellipse brush allocation at 60 Hz.
        private static void DrawLayer(Canvas c,
            string type, string imagePath,
            double size, double thick, double gapPct, double rotDeg,
            string color, string color2, bool useColor2,
            string outlineColor, bool showOutline, double opacity,
            double cx, double cy)
        {
            var mainBrush    = MakeBrush(color,        Colors.Lime);
            var main2Brush   = useColor2 ? MakeBrush(color2, Colors.Lime) : mainBrush;
            var outlineBrush = MakeBrush(outlineColor, Colors.Black);
            double gap = size * Math.Max(0, Math.Min(49, gapPct)) / 100.0;
            double rot = rotDeg * Math.PI / 180.0;
            double op  = Math.Max(0.05, Math.Min(1.0, opacity));

            switch (type)
            {
                case "Dot":        DrawDot(c, cx, cy, size, thick, mainBrush, outlineBrush, showOutline, op); break;
                case "Circle":     DrawCircle(c, cx, cy, size, thick, mainBrush, outlineBrush, showOutline, op); break;
                case "CrossDot":
                    DrawCrosshair(c, cx, cy, size, thick, gap, rot, mainBrush, main2Brush, outlineBrush, showOutline, op);
                    DrawDot(c, cx, cy, size * 0.12, thick, mainBrush, outlineBrush, showOutline, op);
                    break;
                case "T-Shape":    DrawTShape(c, cx, cy, size, thick, rot, mainBrush, outlineBrush, showOutline, op); break;
                case "Chevron":    DrawChevron(c, cx, cy, size, thick, rot, mainBrush, outlineBrush, showOutline, op); break;
                case "Diamond":    DrawDiamond(c, cx, cy, size, thick, rot, mainBrush, outlineBrush, showOutline, op); break;
                case "Arrow":      DrawArrow(c, cx, cy, size, thick, rot, mainBrush, outlineBrush, showOutline, op); break;
                case "Hexagon":    DrawNGon(c, cx, cy, size, thick, 6, rot, mainBrush, outlineBrush, showOutline, op); break;
                case "Octagon":    DrawNGon(c, cx, cy, size, thick, 8, rot, mainBrush, outlineBrush, showOutline, op); break;
                case "CircleCross":
                    DrawCircle(c, cx, cy, size, thick, mainBrush, outlineBrush, showOutline, op);
                    DrawCrosshair(c, cx, cy, size, thick, size * 0.42, rot, mainBrush, main2Brush, outlineBrush, showOutline, op);
                    break;
                case "CustomPNG":  DrawCustomImage(c, cx, cy, size, rotDeg, imagePath, op); break;
                default:           DrawCrosshair(c, cx, cy, size, thick, gap, rot, mainBrush, main2Brush, outlineBrush, showOutline, op); break;
            }
        }

        // ── Shape primitives (all take pre-created Brush — no per-line allocation) ──
        private static void DrawCrosshair(Canvas c, double cx, double cy,
            double size, double thick, double gap, double rot,
            Brush main, Brush main2, Brush outline, bool showOutline, double op)
        {
            double half = size / 2.0;
            var (lx1,ly1)=Rot(-half,0,rot,cx,cy); var (lx2,ly2)=Rot(-gap,0,rot,cx,cy);
            var (rx1,ry1)=Rot(gap,0,rot,cx,cy);   var (rx2,ry2)=Rot(half,0,rot,cx,cy);
            var (ux1,uy1)=Rot(0,-half,rot,cx,cy); var (ux2,uy2)=Rot(0,-gap,rot,cx,cy);
            var (dx1,dy1)=Rot(0,gap,rot,cx,cy);   var (dx2,dy2)=Rot(0,half,rot,cx,cy);
            if (showOutline)
            {
                AddLine(c,lx1,ly1,lx2,ly2,outline,thick+2,op);
                AddLine(c,rx1,ry1,rx2,ry2,outline,thick+2,op);
                AddLine(c,ux1,uy1,ux2,uy2,outline,thick+2,op);
                AddLine(c,dx1,dy1,dx2,dy2,outline,thick+2,op);
            }
            AddLine(c,lx1,ly1,lx2,ly2,main, thick,op);
            AddLine(c,rx1,ry1,rx2,ry2,main, thick,op);
            AddLine(c,ux1,uy1,ux2,uy2,main2,thick,op);
            AddLine(c,dx1,dy1,dx2,dy2,main2,thick,op);
        }

        private static void DrawDot(Canvas c, double cx, double cy,
            double size, double thick, Brush main, Brush outline, bool showOutline, double op)
        {
            double r = Math.Max(2, size * 0.18);
            if (showOutline) AddEllipse(c, cx, cy, r + thick, outline, 0, op, fill: true);
            AddEllipse(c, cx, cy, r, main, 0, op, fill: true);
        }

        private static void DrawCircle(Canvas c, double cx, double cy,
            double size, double thick, Brush main, Brush outline, bool showOutline, double op)
        {
            double r = size / 2.0;
            if (showOutline) AddEllipse(c, cx, cy, r + thick, outline, thick + 2, op, fill: false);
            AddEllipse(c, cx, cy, r, main, thick, op, fill: false);
        }

        private static void DrawTShape(Canvas c, double cx, double cy,
            double size, double thick, double rot, Brush main, Brush outline, bool showOutline, double op)
        {
            double half = size / 2.0;
            var (lx1,ly1)=Rot(-half,0,rot,cx,cy); var (lx2,ly2)=Rot(half,0,rot,cx,cy);
            var (dx1,dy1)=Rot(0,0,rot,cx,cy);     var (dx2,dy2)=Rot(0,half,rot,cx,cy);
            if (showOutline)
            {
                AddLine(c,lx1,ly1,lx2,ly2,outline,thick+2,op);
                AddLine(c,dx1,dy1,dx2,dy2,outline,thick+2,op);
            }
            AddLine(c,lx1,ly1,lx2,ly2,main,thick,op);
            AddLine(c,dx1,dy1,dx2,dy2,main,thick,op);
        }

        private static void DrawChevron(Canvas c, double cx, double cy,
            double size, double thick, double rot, Brush main, Brush outline, bool showOutline, double op)
        {
            double h = size * 0.45, w = size * 0.5;
            var (ax,ay)=Rot(-w,-h,rot,cx,cy);
            var (bx,by)=Rot(0,0,rot,cx,cy);
            var (ccx,ccy)=Rot(w,-h,rot,cx,cy);
            if (showOutline)
            {
                AddLine(c,ax,ay,bx,by,outline,thick+2,op);
                AddLine(c,bx,by,ccx,ccy,outline,thick+2,op);
            }
            AddLine(c,ax,ay,bx,by,main,thick,op);
            AddLine(c,bx,by,ccx,ccy,main,thick,op);
        }

        private static void DrawDiamond(Canvas c, double cx, double cy,
            double size, double thick, double rot, Brush main, Brush outline, bool showOutline, double op)
        {
            double h = size / 2.0;
            var pts = new (double, double)[]
            {
                Rot(0,-h,rot,cx,cy), Rot(h,0,rot,cx,cy),
                Rot(0,h,rot,cx,cy),  Rot(-h,0,rot,cx,cy)
            };
            for (int i = 0; i < 4; i++)
            {
                var (x1,y1) = pts[i]; var (x2,y2) = pts[(i+1)%4];
                if (showOutline) AddLine(c,x1,y1,x2,y2,outline,thick+2,op);
                AddLine(c,x1,y1,x2,y2,main,thick,op);
            }
        }

        private static void DrawArrow(Canvas c, double cx, double cy,
            double size, double thick, double rot, Brush main, Brush outline, bool showOutline, double op)
        {
            double h = size / 2.0, w = size * 0.35;
            var (sx1,sy1)=Rot(0,h*0.2,rot,cx,cy); var (sx2,sy2)=Rot(0,h,rot,cx,cy);
            var (lx1,ly1)=Rot(0,-h,rot,cx,cy);    var (lx2,ly2)=Rot(-w,0,rot,cx,cy);
            var (rx1,ry1)=Rot(0,-h,rot,cx,cy);    var (rx2,ry2)=Rot(w,0,rot,cx,cy);
            if (showOutline)
            {
                AddLine(c,sx1,sy1,sx2,sy2,outline,thick+2,op);
                AddLine(c,lx1,ly1,lx2,ly2,outline,thick+2,op);
                AddLine(c,rx1,ry1,rx2,ry2,outline,thick+2,op);
            }
            AddLine(c,sx1,sy1,sx2,sy2,main,thick,op);
            AddLine(c,lx1,ly1,lx2,ly2,main,thick,op);
            AddLine(c,rx1,ry1,rx2,ry2,main,thick,op);
        }

        private static void DrawNGon(Canvas c, double cx, double cy,
            double size, double thick, int sides, double rot, Brush main, Brush outline, bool showOutline, double op)
        {
            double r = size / 2.0;
            double step = 2 * Math.PI / sides;
            double baseAngle = rot - Math.PI / 2.0;
            var pts = new (double, double)[sides];
            for (int i = 0; i < sides; i++)
            {
                double a = baseAngle + i * step;
                pts[i] = (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }
            for (int i = 0; i < sides; i++)
            {
                var (x1,y1) = pts[i]; var (x2,y2) = pts[(i+1)%sides];
                if (showOutline) AddLine(c,x1,y1,x2,y2,outline,thick+2,op);
                AddLine(c,x1,y1,x2,y2,main,thick,op);
            }
        }

        private static void DrawCustomImage(Canvas c, double cx, double cy,
            double size, double rotDeg, string imagePath, double op)
        {
            if (!ImagePathValidator.IsSafeLocalPath(imagePath) || !File.Exists(imagePath)) return;
            try
            {
                if (!_cache.TryGetValue(imagePath, out var bmp))
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource   = new Uri(imagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    // Bug #4 (2026-09-07 review): cap decode resolution so an 8K reference
                    // image used as a custom reticle does not decode at full native size --
                    // the rendered reticle never exceeds ~400px regardless of source size.
                    bmp.DecodePixelWidth = 512;
                    bmp.EndInit();
                    bmp.Freeze();
                    CacheSet(imagePath, bmp);
                }
                else
                {
                    // Promote to MRU position on cache hit
                    CacheSet(imagePath, bmp);
                }
                var img = new System.Windows.Controls.Image
                {
                    Source = bmp, Width = size, Height = size, Opacity = op,
                    Stretch = Stretch.Uniform, IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                if (rotDeg != 0) img.RenderTransform = new RotateTransform(rotDeg);
                Canvas.SetLeft(img, cx - size / 2.0);
                Canvas.SetTop(img,  cy - size / 2.0);
                c.Children.Add(img);
            }
            catch { }
        }

        // ── Canvas helpers ────────────────────────────────────────────────────
        private static void AddLine(Canvas c,
            double x1, double y1, double x2, double y2, Brush brush, double thick, double op)
        {
            c.Children.Add(new Shapes.Line
            {
                X1=x1, Y1=y1, X2=x2, Y2=y2,
                Stroke=brush, StrokeThickness=thick, Opacity=op,
                StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round,
                IsHitTestVisible=false
            });
        }

        private static void AddEllipse(Canvas c, double cx, double cy,
            double r, Brush brush, double strokeThick, double op, bool fill)
        {
            var el = new Shapes.Ellipse
            {
                Width=r*2, Height=r*2, Opacity=op, IsHitTestVisible=false,
                Fill            = fill ? brush : Brushes.Transparent,
                Stroke          = fill ? null  : brush,
                StrokeThickness = strokeThick
            };
            Canvas.SetLeft(el, cx - r); Canvas.SetTop(el, cy - r);
            c.Children.Add(el);
        }

        // Creates a frozen SolidColorBrush — frozen brushes skip WPF change-notification
        // overhead and are safe to share across UI elements on the same thread.
        private static SolidColorBrush MakeBrush(string hex, Color fallback)
        {
            var b = new SolidColorBrush(ParseColor(hex, fallback));
            b.Freeze();
            return b;
        }

        // ── Math ──────────────────────────────────────────────────────────────
        private static (double x, double y) Rot(double dx, double dy, double rad, double cx, double cy)
        { double co = Math.Cos(rad), si = Math.Sin(rad); return (cx + dx*co - dy*si, cy + dx*si + dy*co); }

        private static Color ParseColor(string hex, Color fallback)
            => ColorUtils.Parse(hex, fallback);
    }
}
