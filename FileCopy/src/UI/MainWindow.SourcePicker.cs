using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

#nullable enable

namespace FileCopy
{
    /// <summary>Single-parent-directory + inline checked-children source picker (Section 3).
    /// Replaces the old multi-root Sources ListBox and the standalone BulkDirectoryPickerDialog.
    /// SourceParent is the directory shown/dragged/browsed on the left; GridSourcePicker lists its
    /// immediate children (folders and loose files) so the user can narrow the copy without leaving
    /// nothing checked = copy the whole parent.</summary>
    public partial class MainWindow
    {
        private readonly System.Collections.ObjectModel.ObservableCollection<PickerRow> _pickerRows = new();
        private ICollectionView? _pickerView;

        // ── Init ─────────────────────────────────────────────────────────────
        private void InitSourcePicker()
        {
            GridSourcePicker.ItemsSource = _pickerRows;
            _pickerView = CollectionViewSource.GetDefaultView(_pickerRows);
            _pickerView.Filter = FilterPickerRow;

            if (!string.IsNullOrWhiteSpace(Vm.Prefs.SourceParent))
                LoadPickerChildren();
            else
                UpdatePickerSummary();
        }

        // ── Parent directory selection ──────────────────────────────────────
        private void BtnPickSourceParent_Click(object sender, RoutedEventArgs e)
            => PickFolder(ApplySourceParent);

        private void TxtSourceParent_Drop(object sender, DragEventArgs e)
        {
            DropFolder(e, ApplySourceParent);
            CheckNetworkPathHint();
        }

        /// <summary>Sets SourceParent unconditionally — even for a path that doesn't currently exist
        /// (e.g. a stale job file / preset) — so ValidatePaths can surface its own "doesn't exist"
        /// warning on Scan rather than this silently dropping the bad path. Recent-folder MRU and the
        /// picker reload are gated on existence.</summary>
        private void ApplySourceParent(string path)
        {
            path ??= "";
            Vm.Prefs.SourceParent = path;
            Vm.Prefs.SourceCheckedChildren = new List<string>();
            Vm.Prefs.SourceList = string.IsNullOrWhiteSpace(path) ? new List<string>() : new List<string> { path };
            SyncLegacySourcePath();

            if (Directory.Exists(path))
                Vm.Prefs.RecentSources = AppPrefs.PushMru(Vm.Prefs.RecentSources, path);

            LoadPickerChildren();
        }

        // ── Loading children ─────────────────────────────────────────────────
        private void LoadPickerChildren()
        {
            _pickerRows.Clear();
            string parent = Vm.Prefs.SourceParent;

            if (string.IsNullOrWhiteSpace(parent))
            {
                TxtPickerStatus.Text = "";
                UpdatePickerSummary();
                return;
            }
            if (!Directory.Exists(parent))
            {
                TxtPickerStatus.Text = "Parent folder not found.";
                UpdatePickerSummary();
                return;
            }

            var checked_ = new HashSet<string>(Vm.Prefs.SourceCheckedChildren ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var rows = new List<PickerRow>();
            try
            {
                foreach (var dir in Directory.GetDirectories(parent))
                {
                    var row = new PickerRow(dir, isFile: false) { IsChecked = checked_.Contains(dir) };
                    row.PropertyChanged += PickerRow_PropertyChanged;
                    rows.Add(row);
                }
                foreach (var file in Directory.GetFiles(parent))
                {
                    var row = new PickerRow(file, isFile: true) { IsChecked = checked_.Contains(file) };
                    row.PropertyChanged += PickerRow_PropertyChanged;
                    rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                TxtPickerStatus.Text = "Couldn't read parent folder: " + ex.Message;
                UpdatePickerSummary();
                return;
            }

            foreach (var r in rows.OrderBy(r => r.IsFile).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
                _pickerRows.Add(r);

            UpdatePickerSummary();
            _ = ComputePickerRowsAsync();
        }

        /// <summary>Walks each folder row's size/file-count off the UI thread. Sibling folders are
        /// independent IO-bound work, so they run concurrently (bounded by core count) instead of
        /// one at a time — the old serial foreach was the actual bottleneck, not the per-folder walk
        /// itself. Each row's ComputeStats() is self-contained (own Stack&lt;string&gt;, no shared
        /// mutable state) so concurrent execution needs no extra locking.</summary>
        private async Task ComputePickerRowsAsync()
        {
            var folderRows = _pickerRows.Where(r => !r.IsFile).ToList();
            if (folderRows.Count == 0) return;

            TxtPickerStatus.Text = $"Scanning {folderRows.Count:N0} folder(s)…";
            int done = 0;
            int maxConcurrency = Math.Max(2, Environment.ProcessorCount);
            using var gate = new System.Threading.SemaphoreSlim(maxConcurrency);

            var tasks = folderRows.Select(async row =>
            {
                await gate.WaitAsync();
                try
                {
                    await Task.Run(row.ComputeStats);
                }
                finally
                {
                    gate.Release();
                }

                // Already back on the UI thread here — every await in this method uses the
                // captured WPF SynchronizationContext (no ConfigureAwait(false) anywhere), so
                // Dispatcher.Invoke was pure marshaling overhead on a per-folder-row fan-out.
                row.RaiseStatsChanged();
                UpdatePickerSummary();
                int n = ++done;
                TxtPickerStatus.Text = n < folderRows.Count
                    ? $"Scanning… {n:N0} of {folderRows.Count:N0} folder(s)"
                    : "";
            });

            await Task.WhenAll(tasks);
        }

        private void PickerRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PickerRow.IsChecked)) return;
            RecomputeDerivedSources();
            UpdatePickerSummary();
        }

        /// <summary>Rebuilds Prefs.SourceCheckedChildren from checked rows, and Prefs.SourceList —
        /// [SourceParent] alone when nothing's checked, else just the checked folder paths. Checked
        /// files are excluded from SourceList (CopyEngine.Enumerate only walks directories); they're
        /// appended directly as FileItems in ScanAsync via BuildCheckedFileItems.</summary>
        private void RecomputeDerivedSources()
        {
            var checkedRows = _pickerRows.Where(r => r.IsChecked).ToList();
            Vm.Prefs.SourceCheckedChildren = checkedRows.Select(r => r.FullPath).ToList();

            var checkedFolders = checkedRows.Where(r => !r.IsFile).Select(r => r.FullPath).ToList();
            Vm.Prefs.SourceList = checkedFolders.Count > 0
                ? checkedFolders
                : (string.IsNullOrWhiteSpace(Vm.Prefs.SourceParent) ? new List<string>() : new List<string> { Vm.Prefs.SourceParent });
            SyncLegacySourcePath();
        }

        private void UpdatePickerSummary()
        {
            if (TxtPickerSummary == null) return;
            int total = _pickerRows.Count;
            int @checked = _pickerRows.Count(r => r.IsChecked);
            int visible = _pickerView?.Cast<PickerRow>().Count() ?? total;

            TxtPickerSummary.Text = total == 0
                ? "No items under this parent."
                : @checked == 0
                    ? $"{total:N0} item(s) ({visible:N0} shown) — none checked, whole parent will be copied."
                    : $"{@checked:N0} of {total:N0} checked ({visible:N0} shown).";
        }

        // ── Filtering ────────────────────────────────────────────────────────
        private void PickerFilter_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshPickerFilter();
        private void PickerFilter_DateChanged(object sender, EventArgs e) => RefreshPickerFilter();

        private void RefreshPickerFilter()
        {
            _pickerView?.Refresh();
            UpdatePickerSummary();
        }

        private bool FilterPickerRow(object obj)
        {
            if (obj is not PickerRow row) return false;

            if (TryParseDouble(TxtPickerSizeMin?.Text) is double minMb && row.SizeMb < minMb) return false;
            if (TryParseDouble(TxtPickerSizeMax?.Text) is double maxMb && row.SizeMb > maxMb) return false;
            if (TryParseInt(TxtPickerCountMin?.Text) is int minCount && row.FileCount < minCount) return false;
            if (TryParseInt(TxtPickerCountMax?.Text) is int maxCount && row.FileCount > maxCount) return false;
            if (PickerDateFrom?.SelectedDate is DateTime from && row.Modified < from) return false;
            if (PickerDateTo?.SelectedDate is DateTime to && row.Modified > to.AddDays(1).AddTicks(-1)) return false;

            return true;
        }

        private static double? TryParseDouble(string? s)
            => !string.IsNullOrWhiteSpace(s) && double.TryParse(s, out var v) ? v : null;
        private static int? TryParseInt(string? s)
            => !string.IsNullOrWhiteSpace(s) && int.TryParse(s, out var v) ? v : null;

        private void BtnPickerClearFilters_Click(object sender, RoutedEventArgs e)
        {
            TxtPickerSizeMin.Text = ""; TxtPickerSizeMax.Text = "";
            TxtPickerCountMin.Text = ""; TxtPickerCountMax.Text = "";
            PickerDateFrom.SelectedDate = null; PickerDateTo.SelectedDate = null;
            RefreshPickerFilter();
        }

        // ── Select all / none / invert (acts on visible/filtered rows only) ──
        private IEnumerable<PickerRow> VisiblePickerRows()
            => _pickerView?.Cast<PickerRow>() ?? _pickerRows;

        private void BtnPickerSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in VisiblePickerRows()) r.IsChecked = true;
        }

        private void BtnPickerSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in VisiblePickerRows()) r.IsChecked = false;
        }

        private void BtnPickerInvert_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in VisiblePickerRows()) r.IsChecked = !r.IsChecked;
        }

        // ── Right-click check/uncheck on highlighted rows ───────────────────
        private void PickerCheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in GridSourcePicker.SelectedItems.Cast<PickerRow>().ToList())
                r.IsChecked = true;
        }

        private void PickerUncheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in GridSourcePicker.SelectedItems.Cast<PickerRow>().ToList())
                r.IsChecked = false;
        }
    }
}
