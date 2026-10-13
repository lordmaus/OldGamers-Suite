using System.Windows;

namespace NoteStation
{
    public partial class CloseActionDialog : Window
    {
        /// <summary>"Exit" | "Taskbar" | "Tray" — set after OK.</summary>
        public string ChosenAction { get; private set; } = "Taskbar";

        public CloseActionDialog()
        {
            InitializeComponent();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (RbTray.IsChecked    == true) ChosenAction = "Tray";
            else if (RbExit.IsChecked == true) ChosenAction = "Exit";
            else                              ChosenAction = "Taskbar";

            DialogResult = true;
        }
    }
}
