using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TimerStudio
{
    /// <summary>
    /// View-model row for the category list.
    /// </summary>
    public class CategoryItem
    {
        public string Name       { get; set; }
        public Brush  Color      { get; set; }
        public int    TimerCount { get; set; }
    }

    public partial class CategoryManagerDialog : Window
    {
        private readonly MainWindow _main;

        // Raised whenever categories change so MainWindow can refresh chips/dots
        public event EventHandler CategoriesChanged;

        public CategoryManagerDialog(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            LstCategories.SelectionChanged += (s, e) =>
            {
                bool sel = LstCategories.SelectedItem != null;
                BtnRenameCategory.IsEnabled = sel;
                BtnDeleteCategory.IsEnabled = sel;
                if (sel)
                    TxtCategoryName.Text = ((CategoryItem)LstCategories.SelectedItem).Name;
            };
            RefreshList();
        }

        private void RefreshList()
        {
            LstCategories.Items.Clear();
            var cats = _main.GetAllCategories();
            var counts = _main.GetCategoryCounts();
            for (int i = 0; i < cats.Count; i++)
            {
                string cat = cats[i];
                LstCategories.Items.Add(new CategoryItem
                {
                    Name       = cat,
                    Color      = new SolidColorBrush(MainWindow.CategoryColor(cat)),
                    TimerCount = counts.TryGetValue(cat, out int c) ? c : 0
                });
            }
        }

        private void BtnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtCategoryName.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            _main.AddCategory(name);
            TxtCategoryName.Clear();
            RefreshList();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnRenameCategory_Click(object sender, RoutedEventArgs e)
        {
            var item = LstCategories.SelectedItem as CategoryItem;
            if (item == null) return;
            string newName = TxtCategoryName.Text.Trim();
            if (string.IsNullOrEmpty(newName) || newName == item.Name) return;
            _main.RenameCategory(item.Name, newName);
            RefreshList();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            var item = LstCategories.SelectedItem as CategoryItem;
            if (item == null) return;
            var r = MessageBox.Show(
                "Delete category \"" + item.Name + "\"?\n\n" +
                (item.TimerCount > 0
                    ? item.TimerCount + " timer(s) will become uncategorised."
                    : "No timers are using this category."),
                "Delete Category", MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) return;
            _main.DeleteCategory(item.Name);
            TxtCategoryName.Clear();
            RefreshList();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnChangeColor_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            string catName = btn.Tag as string;
            if (string.IsNullOrEmpty(catName)) return;

            // Use System.Windows.Forms.ColorDialog (already referenced via System.Windows.Forms)
            var currentColor = MainWindow.CategoryColor(catName);
            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(
                    currentColor.R, currentColor.G, currentColor.B),
                FullOpen       = true,
                AnyColor       = true,
                SolidColorOnly = true
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var picked = System.Windows.Media.Color.FromRgb(
                    dlg.Color.R, dlg.Color.G, dlg.Color.B);
                MainWindow.SetCategoryColor(catName, picked);
                RefreshList();
                // Refresh all card dots and chips
                _main.RefreshAllCategoryDots();
                _main.RefreshCategoryChips();
                _main.ApplyFilter();
                CategoriesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void TxtCategoryName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnAddCategory_Click(sender, null);
        }

        private void BtnDone_Click(object sender, RoutedEventArgs e) => Close();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("CategoryManager", 380, 460);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("CategoryManager", Width, Height);
            base.OnClosed(e);
        }
    }
}
