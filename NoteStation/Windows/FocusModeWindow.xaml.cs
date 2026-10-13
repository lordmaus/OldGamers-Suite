using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace NoteStation.Windows
{
    public partial class FocusModeWindow : Window
    {
        private List<TaskItem> _tasks;
        private int _pomodoroMinutes;

        /// <summary>Fired when a task is completed in focus mode so the main window can save.</summary>
        public event Action<TaskItem> TaskCompleted;
        /// <summary>Fired when user clicks Focus (Pomodoro) on a task.</summary>
        public event Action<TaskItem, int> PomodoroRequested;

        public FocusModeWindow(IEnumerable<TaskItem> allTasks, int pomodoroMinutes = 25)
        {
            InitializeComponent();
            _pomodoroMinutes = pomodoroMinutes;

            // Show overdue + today tasks, not completed
            var today = DateTime.Today;
            _tasks = allTasks
                .Where(t => !t.IsCompleted && !t.IsArchived
                    && t.HasDueDate && t.DueDate.HasValue && t.DueDate.Value.Date <= today)
                .OrderBy(t => t.DueDate)
                .ThenByDescending(t => t.Priority)
                .ToList();

            // Also include high-priority starred tasks without due dates
            var extra = allTasks
                .Where(t => !t.IsCompleted && !t.IsArchived
                    && (!t.HasDueDate || t.DueDate == null)
                    && (t.Priority >= 3 || t.IsStarred))
                .OrderByDescending(t => t.Priority)
                .ToList();
            _tasks.AddRange(extra);

            TaskList.ItemsSource = _tasks;
            TxtDate.Text = $"— {DateTime.Now:dddd, MMMM d}";

            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private void Task_Checked(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.CheckBox)?.DataContext is TaskItem task)
            {
                task.IsCompleted = true;
                task.CompletedAt = DateTime.Now;
                TaskCompleted?.Invoke(task);
                // Refresh the list
                _tasks.Remove(task);
                TaskList.ItemsSource = null;
                TaskList.ItemsSource = _tasks;
            }
        }

        private void BtnPomodoro_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is TaskItem task)
                PomodoroRequested?.Invoke(task, _pomodoroMinutes);
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e) => Close();
    }
}
