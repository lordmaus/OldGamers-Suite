using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimerStudio
{
    /// <summary>Final "Time's up!" lens popup. Inherits all positioning/timing from LensPopupBase.</summary>
    public class LensAlertWindow : LensPopupBase
    {
        public LensAlertWindow(TimerModel model)
        {
            double titleSize = Math.Max(12, model.AlertFontSize);
            double subSize   = Math.Max(10, titleSize * 0.6);

            var outer = MakeOuterBorder(model.AlertColor);
            var inner = new StackPanel();

            inner.Children.Add(new TextBlock
            {
                Text         = model.Name,
                FontFamily   = new FontFamily("Segoe UI"),
                FontSize     = titleSize,
                FontWeight   = FontWeights.Bold,
                Foreground   = new SolidColorBrush(model.AlertTitleColor),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 4),
                Effect       = TextShadow()
            });
            var lblAlert = new TextBlock
            {
                Text       = "Time\u2019s up!",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = subSize,
                Foreground = new SolidColorBrush(
                    Color.FromArgb(220, model.AlertTitleColor.R,
                        model.AlertTitleColor.G, model.AlertTitleColor.B)),
                Margin = new Thickness(0, 0, 0, 14)
            };
            // Assertive: urgent alert \u2014 screen reader interrupts immediately to announce
            System.Windows.Automation.AutomationProperties.SetLiveSetting(
                lblAlert, System.Windows.Automation.AutomationLiveSetting.Assertive);
            inner.Children.Add(lblAlert);

            var btnDismiss = MakeDismissButton("Dismiss", model.AlertColor, titleSize * 0.55);
            btnDismiss.Click += (s, e) => DoDismiss();
            inner.Children.Add(btnDismiss);

            var lblCountdown = MakeCountdownLabel(model.AlertTitleColor, titleSize * 0.5);
            if (model.FlashSeconds > 0) inner.Children.Add(lblCountdown);

            outer.Child = inner;
            Content     = outer;

            SetWindowName(model.Name);
            InitPopup(model.LensPosition, model.FlashSeconds, lblCountdown);
        }
    }
}
