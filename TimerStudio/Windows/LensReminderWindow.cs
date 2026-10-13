using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimerStudio
{
    /// <summary>Reminder "X time remaining" lens popup. Inherits from LensPopupBase.</summary>
    public class LensReminderWindow : LensPopupBase
    {
        public LensReminderWindow(TimerModel model, string timeRemaining)
        {
            double titleSize = Math.Max(12, model.AlertFontSize);
            double subSize   = Math.Max(10, titleSize * 0.65);
            double badgeSize = Math.Max(9,  titleSize * 0.45);

            var outer = MakeOuterBorder(model.AlertColor);
            var inner = new StackPanel();

            // "REMINDER" badge
            inner.Children.Add(new TextBlock
            {
                Text       = "\u23F0  REMINDER",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = badgeSize,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromArgb(180, model.AlertTitleColor.R,
                        model.AlertTitleColor.G, model.AlertTitleColor.B)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            // Timer name (strip " reminder" suffix if present)
            inner.Children.Add(new TextBlock
            {
                Text         = model.Name.EndsWith(" reminder", StringComparison.OrdinalIgnoreCase)
                                ? model.Name.Substring(0, model.Name.Length - 9).TrimEnd()
                                : model.Name,
                FontFamily   = new FontFamily("Segoe UI"),
                FontSize     = titleSize,
                FontWeight   = FontWeights.Bold,
                Foreground   = new SolidColorBrush(model.AlertTitleColor),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 4),
                Effect       = TextShadow()
            });

            // Time remaining
            inner.Children.Add(new TextBlock
            {
                Text       = timeRemaining,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = subSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(
                    Color.FromArgb(230, model.AlertTitleColor.R,
                        model.AlertTitleColor.G, model.AlertTitleColor.B)),
                Margin = new Thickness(0, 0, 0, 14)
            });

            var btnOk = MakeDismissButton("OK", model.AlertColor, titleSize * 0.55);
            btnOk.Click += (s, e) => DoDismiss();
            inner.Children.Add(btnOk);

            var lblCountdown = MakeCountdownLabel(model.AlertTitleColor, titleSize * 0.5);
            int flashSecs = Math.Max(5, model.FlashSeconds);
            if (flashSecs > 0) inner.Children.Add(lblCountdown);

            outer.Child = inner;
            Content     = outer;

            SetWindowName(model.Name);
            InitPopup(model.LensPosition, flashSecs, lblCountdown, pulseMs: 600);
        }
    }
}
