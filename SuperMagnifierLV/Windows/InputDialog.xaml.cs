using System.Windows;
using System.Windows.Input;

namespace SuperMagnifier
{
    public partial class InputDialog : Window
    {
        public string Result { get; private set; }

        public InputDialog(string prompt, string initial = "")
        {
            InitializeComponent();
            PromptLabel.Text = prompt;
            InputBox.Text    = initial;
            Loaded += (s, e) => { InputBox.Focus(); InputBox.SelectAll(); };
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = InputBox.Text.Trim();
            DialogResult = true;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  { Result = InputBox.Text.Trim(); DialogResult = true; }
            if (e.Key == Key.Escape) { DialogResult = false; }
        }
    }
}
