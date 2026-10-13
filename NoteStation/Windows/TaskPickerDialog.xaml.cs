using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace NoteStation.Windows
{
    public partial class TaskPickerDialog : Window
    {
        private List<TaskItem> _allTasks;
        private List<TaskItem> _filtered;

        /// <summary>The task the user selected. Null = user cancelled or unlinked.</summary>
        public TaskItem SelectedTask { get; private set; } = null;
        /// <summary>True = confirmed (Link or Unlink). False = cancelled.</summary>
        public bool Confirmed { get; private set; } = false;
        /// <summary>True = user clicked "Remove link" — caller should clear LinkedTaskId.</summary>
        public bool Unlinked  { get; private set; } = false;

        public TaskPickerDialog(IEnumerable<TaskItem> tasks, int currentLinkedTaskId = 0)
        {
            InitializeComponent();
            // WCAG 2.1.3: borderless dialog — Alt+Arrow moves, Ctrl+Alt+Arrow resizes.
            NoteStation.Controls.KeyboardWindowMover.Attach(this, null, allowResize: true);
            _allTasks = tasks.Where(t => !t.IsCompleted && !t.IsArchived).OrderBy(t => t.Title).ToList();
            _filtered  = new List<TaskItem>(_allTasks);
            LstTasks.ItemsSource = _filtered;
            if (currentLinkedTaskId > 0)
            {
                var current = _allTasks.FirstOrDefault(t => t.Id == currentLinkedTaskId);
                if (current != null) LstTasks.SelectedItem = current;
            }
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Confirmed = false; Close(); } };
        }

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string q = TxtSearch.Text.Trim().ToLowerInvariant();
            _filtered = string.IsNullOrEmpty(q)
                ? new List<TaskItem>(_allTasks)
                : _allTasks.Where(t => t.Title.ToLowerInvariant().Contains(q)).ToList();
            LstTasks.ItemsSource = _filtered;
        }

        private void LstTasks_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectedTask = LstTasks.SelectedItem as TaskItem;
            Confirmed = true;
            Close();
        }

        private void BtnUnlink_Click(object sender, RoutedEventArgs e)
        {
            SelectedTask = null;
            Unlinked  = true;
            Confirmed = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { Confirmed = false; Close(); }
        private void BtnClose_Click(object sender, RoutedEventArgs e)  { Confirmed = false; Close(); }
    }
}
