using System.Windows;
using System.Windows.Input;

namespace NoteStation
{
    public partial class PinSetupDialog : Window
    {
        /// <summary>The validated plain-text PIN on OK, or null on Cancel.</summary>
        public string ResultPin { get; private set; }

        public PinSetupDialog()
        {
            InitializeComponent();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var pin1 = PinBox1.Password;
            var pin2 = PinBox2.Password;

            if (pin1.Length < 4 || pin1.Length > 9)
            {
                ShowError("PIN must be 4–9 digits.");
                return;
            }
            foreach (char c in pin1)
            {
                if (!char.IsAsciiDigit(c))
                {
                    ShowError("PIN must contain digits only (0–9).");
                    return;
                }
            }
            if (pin1 != pin2)
            {
                ShowError("PINs do not match.");
                PinBox2.Clear();
                return;
            }

            ResultPin = pin1;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
