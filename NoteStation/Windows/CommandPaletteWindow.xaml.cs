using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NoteStation
{
    public partial class CommandPaletteWindow : Window
    {
        // Called when user selects an item. Args: type ("Clip"|"Note"|"Task"), id
        public event Action<string, int> ItemSelected;

        private List<ClipboardItem> _clips   = new();
        private List<StickyNote>    _notes   = new();
        private List<TaskItem>      _tasks   = new();
        private int _selectedIndex = 0;
        private List<(string type, int id, Border row)> _rows = new();

        public CommandPaletteWindow()
        {
            InitializeComponent();
            KeyDown += Window_KeyDown;
            Deactivated += (_, _) => Hide();
        }

        public void ShowWithData(
            List<ClipboardItem> clips,
            List<StickyNote> notes,
            List<TaskItem> tasks)
        {
            _clips = clips; _notes = notes; _tasks = tasks;
            TxtSearch.Clear();
            RebuildResults("");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Show();
            Activate();
            TxtSearch.Focus();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
            => RebuildResults(TxtSearch.Text.Trim());

        private void RebuildResults(string query)
        {
            ResultsPanel.Children.Clear();
            _rows.Clear();
            _selectedIndex = 0;

            IEnumerable<(string type, int id, string icon, string title, string subtitle)> matches;

            if (string.IsNullOrEmpty(query))
            {
                // Default: show 5 most recent clips, 5 most recent notes, 5 most recent tasks
                var recentClips  = _clips .Where(c => !c.IsImage).Take(5).Select(c => ("Clip",  c.Id, "clipboard", string.IsNullOrEmpty(c.Title) ? c.Preview : c.Title, c.CapturedAt.ToString("MMM d")));
                var recentNotes  = _notes .Take(5).Select(n => ("Note", n.Id, "stickyNote", n.Title, n.SafeContentPreview));
                var recentTasks  = _tasks .Where(t => !t.IsCompleted && !t.IsArchived).Take(5).Select(t => ("Task", t.Id, "tasks", t.Title, t.DueDateDisplay));
                matches = recentClips.Concat(recentNotes).Concat(recentTasks);
            }
            else
            {
                string q = query.ToLowerInvariant();
                var clipMatches  = _clips.Where(c => !c.IsImage && (c.Title?.ToLowerInvariant().Contains(q) == true || c.Text?.ToLowerInvariant().Contains(q) == true)).Take(5).Select(c => ("Clip",  c.Id, "clipboard", string.IsNullOrEmpty(c.Title) ? c.Preview : c.Title, c.Preview?.Length > 60 ? c.Preview[..60]+"…" : c.Preview ?? ""));
                var noteMatches  = _notes.Where(n => n.Title?.ToLowerInvariant().Contains(q) == true || n.Content?.ToLowerInvariant().Contains(q) == true).Take(5).Select(n => ("Note", n.Id, "stickyNote", n.Title, n.SafeContentPreview));
                var taskMatches  = _tasks.Where(t => t.Title?.ToLowerInvariant().Contains(q) == true).Take(5).Select(t => ("Task", t.Id, "tasks", t.Title, t.DueDateDisplay));
                matches = clipMatches.Concat(noteMatches).Concat(taskMatches);
            }

            foreach (var m in matches)
            {
                var row = BuildRow(m.type, m.id, m.icon, m.title, m.subtitle);
                ResultsPanel.Children.Add(row);
                _rows.Add((m.type, m.id, row));
            }

            TxtEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSelection();
        }

        private Border BuildRow(string type, int id, string icon, string title, string subtitle)
        {
            var row = new Border
            {
                MinHeight = 44,
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = Cursors.Hand,
                Tag = (type, id),
            };
            row.MouseLeftButtonDown += (_, _) => SelectItem(type, id);
            row.MouseEnter += (_, _) => { if (_rows.FirstOrDefault(r => r.row == row) is var match && match != default) { _selectedIndex = _rows.IndexOf(match); UpdateSelection(); } };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconTb = JtIcons.Img(icon, 20);
            iconTb.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(iconTb, 0);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleTb = new TextBlock { Text = title ?? "", FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
            titleTb.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            sp.Children.Add(titleTb);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var subTb = new TextBlock { Text = subtitle, FontSize = NoteStation.Controls.TypeRamp.Caption, TextTrimming = TextTrimming.CharacterEllipsis };
                subTb.SetResourceReference(TextBlock.ForegroundProperty, "BrushMuted");
                sp.Children.Add(subTb);
            }
            Grid.SetColumn(sp, 1);

            g.Children.Add(iconTb);
            g.Children.Add(sp);
            row.Child = g;
            return row;
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                bool sel = i == _selectedIndex;
                var row = _rows[i].row;
                // Selected = Surf2 + 4px BlueText bar (suite list-selection rule), not a colour tint alone.
                if (sel) { row.SetResourceReference(Border.BackgroundProperty, "BrushSurf2"); row.SetResourceReference(Border.BorderBrushProperty, "BrushBlueText"); }
                else     { row.Background = Brushes.Transparent; row.BorderBrush = Brushes.Transparent; }
                row.BorderThickness = new Thickness(sel ? 4 : 0, 0, 0, 0);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:   Hide();                                              e.Handled = true; break;
                case Key.Down:     _selectedIndex = Math.Min(_selectedIndex + 1, _rows.Count - 1); UpdateSelection(); e.Handled = true; break;
                case Key.Up:       _selectedIndex = Math.Max(_selectedIndex - 1, 0);               UpdateSelection(); e.Handled = true; break;
                case Key.Enter:    if (_rows.Count > 0) { var r = _rows[_selectedIndex]; SelectItem(r.type, r.id); } e.Handled = true; break;
            }
        }

        private void SelectItem(string type, int id)
        {
            Hide();
            ItemSelected?.Invoke(type, id);
        }
    }
}
