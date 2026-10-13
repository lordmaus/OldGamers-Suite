using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NoteStation
{
    public partial class TaskDetailDialog : Window
    {
        public bool Saved { get; private set; }

        private readonly TaskItem _task;
        private List<ClipboardItem> _linkedClips;
        private List<ClipboardItem> _allClips;
        private List<TaskItem> _allOtherTasks = new List<TaskItem>();

        private static readonly (string hex, string name)[] _accentColors = new[]
        {
            ("#00000000", "None"),
            ("#FFD700",   "Gold"),
            ("#4FC3F7",   "Cyan"),
            ("#81C784",   "Green"),
            ("#FF8A65",   "Orange"),
            ("#F48FB1",   "Pink"),
            ("#CE93D8",   "Purple"),
            ("#EF5350",   "Red"),
        };

        private string _selectedAccent = "#00000000";

        public TaskDetailDialog(TaskItem task, IEnumerable<ClipboardItem> allClips = null, IEnumerable<TaskItem> allTasks = null)
        {
            InitializeComponent();
            WindowSizeMemory.Attach(this, "TaskDetailDialog");
            _task = task;
            _allClips   = allClips?.ToList() ?? new List<ClipboardItem>();
            _linkedClips = _allClips.Where(c => c.LinkedTaskId == _task.Id).ToList();
            RefreshLinkedClips();

            if (allTasks != null)
            {
                _allOtherTasks = allTasks.Where(t => t.Id != task.Id && !t.IsArchived).OrderBy(t => t.Title).ToList();
                PopulateRelationLists();
            }

            TxtTitle.Text             = task.Title;
            RichTextFormatting.LoadContent(TxtNotes, task.Notes);
            NotesToolbarHost.Child    = RichTextFormatting.BuildToolbar(TxtNotes);
            ChkStarred.IsChecked      = task.IsStarred;
            _selectedAccent           = task.AccentColor ?? "#00000000";

            // D3-4: Priority
            CboPriority.Items.Add("○  None");
            CboPriority.Items.Add("▼  Low");
            CboPriority.Items.Add("▲  Medium");
            CboPriority.Items.Add("▲▲  High");
            CboPriority.SelectedIndex = Math.Clamp(task.Priority, 0, 3);
            TxtPriorityRank.Text = task.PriorityRank > 0 ? task.PriorityRank.ToString() : "";

            // Populate hour / minute combos
            for (int h = 0; h < 24; h++)  CboHour.Items.Add(h.ToString("D2"));
            for (int m = 0; m < 60; m += 5) CboMinute.Items.Add(m.ToString("D2"));

            if (task.HasDueDate && task.DueDate.HasValue)
                DetailDatePicker.SelectedDate = task.DueDate.Value;

            if (task.HasDueTime && task.DueTime.HasValue)
            {
                CboHour.SelectedIndex   = task.DueTime.Value.Hours;
                // round to nearest 5-min slot
                CboMinute.SelectedIndex = task.DueTime.Value.Minutes / 5;
            }
            else
            {
                CboHour.SelectedIndex   = 0;
                CboMinute.SelectedIndex = 0;
            }

            BuildAccentChips();

            // Subtasks
            SubtaskList.ItemsSource = _task.SubTasks;
        }

        private void BuildAccentChips()
        {
            AccentChips.Children.Clear();
            foreach (var (hex, name) in _accentColors)
            {
                bool isNone  = hex == "#00000000";
                var chip = new Button
                {
                    Width   = 44, Height = 44,
                    Margin  = new Thickness(0, 0, 6, 0),
                    ToolTip = name,
                    Tag     = hex,
                    Style   = (Style)TryFindResource("FlatButton"),
                };
                System.Windows.Automation.AutomationProperties.SetName(chip, $"Accent color {name}");

                if (isNone)
                {
                    chip.Content = "✕";
                }
                else
                {
                    chip.Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(hex));
                    chip.Content    = "";
                }

                if (hex == _selectedAccent)
                    chip.BorderThickness = new Thickness(2);

                chip.Click += AccentChip_Click;
                AccentChips.Children.Add(chip);
            }
        }

        private void AccentChip_Click(object sender, RoutedEventArgs e)
        {
            _selectedAccent = (string)((Button)sender).Tag;
            // Update border on all chips
            foreach (Button btn in AccentChips.Children)
            {
                bool active = (string)btn.Tag == _selectedAccent;
                btn.BorderThickness = active ? new Thickness(2) : new Thickness(0);
            }
        }

        private void BtnClearDue_Click(object sender, RoutedEventArgs e)
        {
            DetailDatePicker.SelectedDate = null;
            CboHour.SelectedIndex   = 0;
            CboMinute.SelectedIndex = 0;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string title = TxtTitle.Text.Trim();
            if (string.IsNullOrEmpty(title))
            {
                AppDialog.Show("Task title cannot be empty.", "NoteStation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTitle.Focus();
                return;
            }

            _task.Title      = title;
            _task.Notes      = RichTextFormatting.SaveContent(TxtNotes);
            _task.IsStarred  = ChkStarred.IsChecked == true;
            _task.Priority   = CboPriority.SelectedIndex;
            _task.PriorityRank = int.TryParse(TxtPriorityRank.Text.Trim(), out int rank) ? Math.Max(0, rank) : 0;
            _task.AccentColor = _selectedAccent == "#00000000" ? "" : _selectedAccent;
            _task.ModifiedAt = DateTime.Now;

            DateTime? due = DetailDatePicker.SelectedDate;
            _task.HasDueDate = due.HasValue;
            _task.DueDate    = due;

            // Time is saved whenever a date is selected (00:00 midnight is valid)
            if (due.HasValue &&
                CboHour.SelectedItem   is string hs && int.TryParse(hs, out int hour) &&
                CboMinute.SelectedItem is string ms && int.TryParse(ms, out int minute))
            {
                _task.HasDueTime = true;
                _task.DueTime    = new TimeSpan(hour, minute, 0);
            }
            else
            {
                _task.HasDueTime = false;
                _task.DueTime    = null;
            }

            // F17: Save DependsOn selections
            _task.DependsOnIds.Clear();
            foreach (var item in LstDependsOn.ItemContainerGenerator.Items)
            {
                var container = LstDependsOn.ItemContainerGenerator.ContainerFromItem(item)
                    as System.Windows.Controls.ListBoxItem;
                if (container?.IsSelected == true && item is TaskItem t)
                    _task.DependsOnIds.Add(t.Id);
            }

            Saved = true;
            DialogResult = true; // setting this closes the window; caller checks
                                  // ShowDialog() == true to decide whether to persist.
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void TxtPriorityRank_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
            => e.Handled = !e.Text.All(char.IsDigit);

        private void BtnClearRank_Click(object sender, RoutedEventArgs e) => TxtPriorityRank.Text = "";

        private void BtnAddSubtask_Click(object sender, RoutedEventArgs e)
        {
            _task.SubTasks.Add(new SubTaskItem { Text = "", IsDone = false });
            // Force ItemsControl refresh
            SubtaskList.ItemsSource = null;
            SubtaskList.ItemsSource = _task.SubTasks;
        }

        private void RemoveSubtask_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is SubTaskItem sub)
            {
                _task.SubTasks.Remove(sub);
                SubtaskList.ItemsSource = null;
                SubtaskList.ItemsSource = _task.SubTasks;
            }
        }

        private void PopulateRelationLists()
        {
            // DependsOn list — all other open tasks as selectable items
            LstDependsOn.ItemsSource = _allOtherTasks;

            // Re-select after containers are generated
            LstDependsOn.ItemContainerGenerator.StatusChanged += (s, e) =>
            {
                if (LstDependsOn.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    foreach (var t in _allOtherTasks.Where(t => _task.DependsOnIds.Contains(t.Id)))
                    {
                        var container = LstDependsOn.ItemContainerGenerator.ContainerFromItem(t)
                            as System.Windows.Controls.ListBoxItem;
                        if (container != null) container.IsSelected = true;
                    }
                }
            };

            // Blocks list — read-only display of tasks that list this task in their DependsOnIds
            var blocked = _allOtherTasks.Where(t => t.DependsOnIds.Contains(_task.Id)).ToList();
            LstBlocks.ItemsSource = blocked;
        }

        private void RefreshLinkedClips()
        {
            _linkedClips = _allClips.Where(c => c.LinkedTaskId == _task.Id).ToList();
            LinkedClipsList.ItemsSource = null;
            LinkedClipsList.ItemsSource = _linkedClips;
            TxtNoLinkedClips.Visibility = _linkedClips.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RemoveLinkedClip_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button)?.Tag is ClipboardItem clip)
            {
                clip.LinkedTaskId = 0;
                RefreshLinkedClips();
            }
        }
    }
}
