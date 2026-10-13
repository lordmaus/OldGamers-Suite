using System.Windows;
using System.Windows.Controls;

namespace ReticleOverlay
{
    /// <summary>
    /// Simple single-line text input dialog for naming profiles.
    /// </summary>
    public class InputDialog : Window
    {
        private TextBox  _textBox;
        public  string   InputText => _textBox?.Text;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            Title           = title;
            Width           = 340;
            Height          = 160;
            MinWidth        = 340;
            MinHeight       = 160;
            // Bug #8 (2026-09-07 review): every other dialog in the suite is
            // CanResizeWithGrip; this was the one NoResize holdout.
            ResizeMode      = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background      = (System.Windows.Media.Brush)Application.Current.Resources["BrushBg"];
            ShowInTaskbar   = false;

            var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };

            var lbl = new TextBlock
            {
                Text       = prompt,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize   = 13,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["BrushText"],
                Margin     = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(lbl);

            _textBox = new TextBox
            {
                Text       = defaultValue,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize   = 13,
                Margin     = new Thickness(0, 0, 0, 14)
            };
            System.Windows.Automation.AutomationProperties.SetName(_textBox, prompt);
            _textBox.SelectAll();
            panel.Children.Add(_textBox);

            var btnRow = new Grid();
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btnCancel = new Button
            {
                Content   = "Cancel",
                Style     = (Style)Application.Current.Resources["FlatButton"],
                Padding   = new Thickness(14, 0, 14, 0),
                MinHeight = 44,
                FontSize  = 12
            };
            System.Windows.Automation.AutomationProperties.SetName(btnCancel, "Cancel");
            btnCancel.Click += (s, e) => { DialogResult = false; };
            Grid.SetColumn(btnCancel, 1);
            btnRow.Children.Add(btnCancel);

            var btnOk = new Button
            {
                Style     = (Style)Application.Current.Resources["PrimaryButton"],
                Padding   = new Thickness(14, 0, 14, 0),
                MinHeight = 44,
                FontSize  = 12,
                Margin    = new Thickness(8, 0, 0, 0),
                IsDefault = true
            };
            btnOk.Content = JtIcons.Label(btnOk, "Save", "save");
            System.Windows.Automation.AutomationProperties.SetName(btnOk, "Save profile name");
            btnOk.Click += (s, e) => { DialogResult = true; };
            Grid.SetColumn(btnOk, 2);
            btnRow.Children.Add(btnOk);

            panel.Children.Add(btnRow);
            Content = panel;

            Loaded += (s, e) => _textBox.Focus();
        }
    }
}
