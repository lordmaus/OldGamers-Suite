using System;
using System.Windows;

namespace NoteStation
{
    /// <summary>
    /// Asks the user for a target date and time, then hands the result
    /// back to MainWindow for writing to TimerStudio's timers.json
    /// as a DateTime (Mode=2) calendar event.
    /// </summary>
    public partial class TimerPushDialog : Window
    {
        /// <summary>The confirmed target DateTime, or null if cancelled.</summary>
        public DateTime? ResultDateTime { get; private set; }

        public TimerPushDialog(string taskTitle, DateTime? prefillDateTime)
        {
            InitializeComponent();
            WindowSizeMemory.Attach(this, "TimerPushDialog");

            LblTaskTitle.Text = $"Task: {taskTitle}";

            // Populate hour / minute combos
            for (int h = 0; h < 24; h++)    CboHour.Items.Add(h.ToString("D2"));
            for (int m = 0; m < 60; m += 5) CboMinute.Items.Add(m.ToString("D2"));

            // Pre-fill from task due date/time, or use sensible defaults
            var target = prefillDateTime ?? DateTime.Now.AddHours(1);
            PushDatePicker.SelectedDate = target.Date;
            CboHour.SelectedIndex       = target.Hour;
            CboMinute.SelectedIndex     = (target.Minute / 5);
        }

        private void BtnPush_Click(object sender, RoutedEventArgs e) => TryConfirm();
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void TryConfirm()
        {
            if (PushDatePicker.SelectedDate is not DateTime date)
            {
                MessageBox.Show("Please select a target date.", "NoteStation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                PushDatePicker.Focus();
                return;
            }

            int hour   = CboHour.SelectedIndex;
            int minute = CboMinute.SelectedItem is string ms && int.TryParse(ms, out int mv) ? mv : 0;

            var target = date.Date.AddHours(hour).AddMinutes(minute);

            if (target <= DateTime.Now)
            {
                var res = MessageBox.Show(
                    "The selected date/time is in the past. Push anyway?",
                    "NoteStation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }

            ResultDateTime = target;
            DialogResult   = true;
        }
    }
}
