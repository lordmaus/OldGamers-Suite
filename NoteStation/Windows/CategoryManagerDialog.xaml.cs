using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NoteStation
{
    /// <summary>
    /// Manages the shared category list (names + colors).
    /// Categories are shared across Clipboard, Notes, and Tasks.
    /// </summary>
    public partial class CategoryManagerDialog : Window
    {
        // ── View model row ────────────────────────────────────────────────────
        public class CategoryRow : INotifyPropertyChanged
        {
            private SolidColorBrush _colorBrush;

            public string Name  { get; set; }
            public int    Count { get; set; }

            public SolidColorBrush ColorBrush
            {
                get => _colorBrush;
                set { _colorBrush = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorBrush))); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        // ── State ─────────────────────────────────────────────────────────────
        private readonly ObservableCollection<CategoryRow> _rows = new();
        private readonly Func<List<string>> _getAllCategories;
        private readonly Func<string, int>  _getCount;

        /// <summary>Fired whenever categories or colors change.</summary>
        public event EventHandler CategoriesChanged;

        // ── Constructor ───────────────────────────────────────────────────────
        public CategoryManagerDialog(
            Func<List<string>> getAllCategories,
            Func<string, int>  getCount,
            Window             owner)
        {
            InitializeComponent();
            WindowSizeMemory.Attach(this, "CategoryManagerDialog");
            Owner              = owner;
            _getAllCategories   = getAllCategories;
            _getCount          = getCount;
            LvCategories.ItemsSource = _rows;
            Refresh();
        }

        // ── Refresh list ──────────────────────────────────────────────────────
        public void Refresh()
        {
            _rows.Clear();
            foreach (string cat in _getAllCategories())
            {
                var color = App.CategoryColor(cat);
                _rows.Add(new CategoryRow
                {
                    Name       = cat,
                    Count      = _getCount(cat),
                    ColorBrush = new SolidColorBrush(color),
                });
            }
        }

        // ── Color button ──────────────────────────────────────────────────────
        private void BtnColor_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button)?.Tag is not string catName) return;

            var current = App.CategoryColor(catName);
            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color          = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
                FullOpen       = true,
                AnyColor       = true,
                SolidColorOnly = true,
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var picked = Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                App.SetCategoryColor(catName, picked);
                Refresh();
                CategoriesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // ── Add ───────────────────────────────────────────────────────────────
        private void BtnAdd_Click(object sender, RoutedEventArgs e) => AddCategory();

        private void TxtNewCategory_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddCategory();
        }

        private void AddCategory()
        {
            string name = TxtNewCategory.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) return;

            var prefs = AppPrefs.Instance;
            // Dedupe (case-insensitive)
            foreach (string existing in prefs.Categories)
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) return;

            prefs.Categories.Add(name);
            prefs.Save();
            TxtNewCategory.Clear();
            Refresh();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Rename ────────────────────────────────────────────────────────────
        private void BtnRename_Click(object sender, RoutedEventArgs e)
        {
            if (LvCategories.SelectedItem is not CategoryRow row) return;

            var dlg = new RenameDialog(row.Name, Owner: this);
            if (dlg.ShowDialog() != true) return;
            string newName = dlg.NewName?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName) || newName == row.Name) return;

            var prefs = AppPrefs.Instance;

            // Update persisted list
            for (int i = 0; i < prefs.Categories.Count; i++)
                if (string.Equals(prefs.Categories[i], row.Name, StringComparison.OrdinalIgnoreCase))
                    prefs.Categories[i] = newName;

            // Transfer color
            if (prefs.CategoryColors.TryGetValue(row.Name, out string hex))
            {
                prefs.CategoryColors[newName] = hex;
                prefs.CategoryColors.Remove(row.Name);
            }

            prefs.Save();
            Refresh();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Delete ────────────────────────────────────────────────────────────
        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (LvCategories.SelectedItem is not CategoryRow row) return;

            var result = AppDialog.Show(
                $"Delete category \"{row.Name}\"?\n\nItems assigned to it will become uncategorised.",
                "Delete Category",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var prefs = AppPrefs.Instance;
            prefs.Categories.RemoveAll(c =>
                string.Equals(c, row.Name, StringComparison.OrdinalIgnoreCase));
            prefs.CategoryColors.Remove(row.Name);
            prefs.Save();
            Refresh();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Close ─────────────────────────────────────────────────────────────
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }

    // ── Simple inline rename dialog ───────────────────────────────────────────
    internal class RenameDialog : Window
    {
        public string NewName => _tb.Text;
        private readonly System.Windows.Controls.TextBox _tb;

        public RenameDialog(string current, Window Owner)
        {
            this.Owner = Owner;
            Title      = "Rename Category";
            Width      = 360;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode  = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background  = App.Current.Resources["BrushBg"] as System.Windows.Media.Brush
                          ?? System.Windows.Media.Brushes.Black;

            _tb = new System.Windows.Controls.TextBox
            {
                Text     = current,
                MinHeight = 44,
                Margin   = new Thickness(16, 16, 16, 8),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize   = 13,
            };
            _tb.SelectAll();
            _tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) DialogResult = true; };

            var btnOk = new System.Windows.Controls.Button
            {
                Content   = "OK",
                MinWidth  = 80, MinHeight = 44,
                Margin    = new Thickness(16, 0, 8, 16),
                IsDefault = true,
            };
            btnOk.Click += (s, e) => DialogResult = true;

            var btnCancel = new System.Windows.Controls.Button
            {
                Content   = "Cancel",
                MinWidth  = 80, MinHeight = 44,
                Margin    = new Thickness(0, 0, 16, 16),
                IsCancel  = true,
            };
            btnCancel.Click += (s, e) => DialogResult = false;

            var btnRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCancel);

            var root = new System.Windows.Controls.StackPanel();
            root.Children.Add(_tb);
            root.Children.Add(btnRow);
            Content = root;
        }
    }
}
