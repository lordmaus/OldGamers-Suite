using System;
using System.Windows;

namespace NoteStation
{
    public partial class FatalErrorDialog : Window
    {
        public FatalErrorDialog(Exception ex, string logPath)
        {
            InitializeComponent();
            var root = ex;
            while (root.InnerException != null) root = root.InnerException;
            TxtError.Text   = $"{root.GetType().Name}: {root.Message}";
            TxtLogPath.Text = $"Error log: {logPath}";
            Loaded += (_, _) => BtnClose.Focus();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
