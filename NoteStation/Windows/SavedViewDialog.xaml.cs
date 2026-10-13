using System.Windows;
using System.Windows.Input;

namespace NoteStation.Windows
{
    public partial class SavedViewDialog : Window
    {
        public string ViewName       { get; private set; } = "";
        public string Scope          { get; private set; } = "All";
        public string FilterExpression { get; private set; } = "";
        public bool   Confirmed      { get; private set; } = false;

        public SavedViewDialog(SavedView existing = null)
        {
            InitializeComponent();
            // WCAG 2.1.3: borderless dialog — Alt+Arrow moves, Ctrl+Alt+Arrow resizes.
            NoteStation.Controls.KeyboardWindowMover.Attach(this, null, allowResize: true);
            if (existing != null)
            {
                TxtDialogTitle.Text = "Edit View";
                TxtName.Text        = existing.Name;
                TxtExpression.Text  = existing.FilterExpression;
                // Select the scope combobox item
                foreach (System.Windows.Controls.ComboBoxItem item in CmbScope.Items)
                    if (item.Tag?.ToString() == existing.Scope) { item.IsSelected = true; break; }
            }
            Loaded += (_, __) => TxtName.Focus();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Confirmed = false; Close(); } };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ViewName = TxtName.Text.Trim();
            if (string.IsNullOrEmpty(ViewName)) { TxtName.Focus(); return; }
            Scope          = (CmbScope.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "All";
            FilterExpression = TxtExpression.Text.Trim();
            Confirmed      = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { Confirmed = false; Close(); }
        private void BtnClose_Click(object sender, RoutedEventArgs e)  { Confirmed = false; Close(); }
    }
}
