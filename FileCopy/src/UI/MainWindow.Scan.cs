using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    public partial class MainWindow
    {
        // ── Scan / Plan ───────────────────────────────────────────────────────
        private async Task ScanAsync(CopySession? session = null)
        {
            session ??= CopySession.FromPrefs(AppPrefs.Instance);
            _activeSession = session;
            if (!ValidatePaths(session)) return;

            Vm.StatusText        = "Scanning…";
            Vm.FileProgressPct   = 0;
            Vm.TotalProgressPct  = 0;
            Vm.SpeedText         = "";
            DetachAllItemListeners();
            _items.Clear();

            _scanCts?.Dispose();
            _scanCts = new System.Threading.CancellationTokenSource();
            CommandManager.InvalidateRequerySuggested();
            try
            {
                var scanToken = _scanCts.Token;
                var roots = session.SourcePaths.Count > 0
                    ? session.SourcePaths
                    : (string.IsNullOrWhiteSpace(session.SourcePath) ? Array.Empty<string>() : new[] { session.SourcePath });

                // Multiple roots mirror each under Dest\<leaf-name>\... to avoid collisions.
                // A single root keeps the original flat behaviour (copies straight into Dest).
                bool mirror = roots.Count > 1;
                string[]? leafNames = mirror ? ComputeMirrorNames(roots) : null;

                // CopyEngine.Enumerate's internal "scanned" counter restarts at 0 for every call —
                // with multiple checked roots that made the status line restart per directory.
                // Track a running base count here so the displayed total only ever climbs, and
                // show which root is active alongside it.
                var list = new List<FileItem>();
                var skipped = new List<string>();   // FC-10
                ScanSkipPanel.Visibility = Visibility.Collapsed;
                long totalFound = 0;
                for (int i = 0; i < roots.Count; i++)
                {
                    scanToken.ThrowIfCancellationRequested();
                    string destRoot = mirror ? Path.Combine(session.DestPath, leafNames![i]) : session.DestPath;
                    string root = roots[i];

                    // Progress<T> captures this (UI) thread's SynchronizationContext at construction,
                    // so .Report() from the background scan thread marshals back safely.
                    long baseCount = totalFound;
                    var scanProgress = new Progress<int>(c =>
                    {
                        long total = baseCount + c;
                        Vm.StatusText = roots.Count > 1
                            ? $"Scanning… dir {i + 1} of {roots.Count} — {total:N0} files found"
                            : $"Scanning… {total:N0} files found";
                    });

                    var part = await Task.Run(() => _engine.Enumerate(
                        root, destRoot, session.IncludeSubdirs,
                        session.FileMaskInclude, session.FileMaskExclude, scanToken,
                        session.SymlinkPolicy, scanProgress, skipped));
                    list.AddRange(part);
                    totalFound += part.Count;
                }

                // Only the live, manual UI session reads Vm.Prefs.SourceCheckedChildren — scheduled/
                // preset CopySessions are immutable snapshots and must not pick up current picker state.
                bool isLivePrefsSession = IsLivePrefsSession(session);
                if (isLivePrefsSession)
                {
                    var checkedFiles = Vm.Prefs.SourceCheckedChildren.Where(File.Exists).ToList();
                    if (checkedFiles.Count > 0)
                        list.AddRange(BuildCheckedFileItems(checkedFiles, Vm.Prefs.SourceParent, session.DestPath, session.ConflictMode));
                }

                // PlanActions itself is pure in-memory and fast even at 100k+ items. The stale-temp
                // check below is not: HasStaleTempFile() calls File.Exists() per item against the
                // *destination*, and when Dest is a UNC/network path (as here) each call is a
                // round-trip. Doing that synchronously on the UI thread for 290k+ items is exactly
                // what froze the app ("Not Responding") — so both run off-thread now.
                Vm.StatusText = $"Planning… {list.Count:N0} files.";
                await Task.Run(() => _engine.PlanActions(list, session.ConflictMode));

                // Detect stale .fctemp files whose source has since changed
                Vm.StatusText = "Checking for stale partial transfers…";
                var staleItems = await Task.Run(() => list.Where(HasStaleTempFile).ToList());
                int stale = staleItems.Count;
                if (stale > 0)
                {
                    bool discard = session.IsScheduled; // auto-discard for scheduled runs; prompt manually
                    if (!session.IsScheduled)
                    {
                        var result = AppDialog.Show(this,
                            $"{stale:N0} file(s) have a partial (.fctemp) whose source has changed.\n\nDiscard stale partials and copy fresh?",
                            "Stale partial transfers", MessageBoxButton.YesNo,
                            MessageBoxImage.Question, MessageBoxResult.Yes);
                        discard = result == MessageBoxResult.Yes;
                    }
                    if (discard)
                        await Task.Run(() =>
                        {
                            foreach (var it in staleItems)
                                PartialFile.DiscardTemp(it.DestPath);
                        });
                }

                Vm.StatusText = "Building list…";
                BulkAddItems(list);
                UpdateSummary();
                RefreshFilter();
                // Only update MRU for manual (prefs-based) scans — not preset/scheduled runs
                if (isLivePrefsSession)
                {
                    var prefs = AppPrefs.Instance;
                    foreach (var root in roots)
                        prefs.RecentSources = AppPrefs.PushMru(prefs.RecentSources, root);
                    prefs.RecentDests = AppPrefs.PushMru(prefs.RecentDests, session.DestPath);
                }
                Vm.StatusText = $"Ready. {list.Count:N0} files.";
                ShowScanSkips(skipped);
                _scanCts?.Dispose(); _scanCts = null;
                CommandManager.InvalidateRequerySuggested();
                // Clear active session for manual scans — UI combo changes should now use AppPrefs
                if (isLivePrefsSession)
                    _activeSession = null;
            }
            catch (OperationCanceledException)
            {
                _scanCts?.Dispose(); _scanCts = null;
                _activeSession = null;
                CommandManager.InvalidateRequerySuggested();
                Vm.StatusText = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                _scanCts?.Dispose(); _scanCts = null;
                _activeSession = null;
                CommandManager.InvalidateRequerySuggested();
                AppDialog.Show(this, ex.Message, "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
                Vm.StatusText = "Scan failed.";
            }
        }

        private static bool HasStaleTempFile(FileItem it)
        {
            string tempPath = PartialFile.GetTempPath(it.DestPath);
            if (!File.Exists(tempPath)) return false;
            if (it.ResumeOffset > 0) return false; // valid partial
            return true;
        }

        private void BulkAddItems(List<FileItem> list)
        {
            // Clear any stale progress from a prior copy run
            foreach (var it in list) it.ProgressBytes = 0;
            _items.AddRange(list);
            foreach (var it in list) it.PropertyChanged += Item_PropertyChanged;
            GridFiles.ItemsSource = _itemsView;
        }

        private void ReplanSelected()
        {
            if (_items.Count == 0 || IsRunning) return;
            // Use the active session's conflict mode if one exists — prevents scheduler
            // runs from being re-planned with the main window's AppPrefs value
            var mode = _activeSession?.ConflictMode ?? AppPrefs.Instance.ConflictMode;
            _engine.PlanActions(_items, mode);
            UpdateSummary();
            RefreshFilter();
        }

        private void UpdateSummary()
        {
            int copy = 0, over = 0, skip = 0, ren = 0, res = 0;
            long bytes = 0;
            foreach (var x in _items)
            {
                switch (x.Action)
                {
                    case FileAction.Copy:      copy++; bytes += x.SizeBytes; break;
                    case FileAction.Overwrite: over++; bytes += x.SizeBytes; break;
                    case FileAction.Rename:    ren++;  bytes += x.SizeBytes; break;
                    case FileAction.Resume:    res++;  bytes += Math.Max(0, x.SizeBytes - x.ResumeOffset); break;
                    case FileAction.Skip:      skip++;                       break;
                    default:                           bytes += x.SizeBytes; break;
                }
            }
            string resumePart = res > 0 ? $", Resume {res:N0}" : "";
            Vm.SummaryText =
                $"{_items.Count:N0} files — Copy {copy:N0}, Overwrite {over:N0}, Rename {ren:N0}{resumePart}, Skip {skip:N0}.  " +
                $"Total to transfer: {SizeFormat.Format(bytes)}";
        }

        private bool ValidatePaths(CopySession? session = null)
        {
            session ??= CopySession.FromPrefs(AppPrefs.Instance);
            var roots = session.SourcePaths.Count > 0
                ? session.SourcePaths
                : (string.IsNullOrWhiteSpace(session.SourcePath) ? Array.Empty<string>() : new[] { session.SourcePath });

            if (roots.Count == 0)
            { AppDialog.Show(this, "Add at least one source folder.", "File Copy Studio", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
            if (string.IsNullOrWhiteSpace(session.DestPath))
            { AppDialog.Show(this, "Destination folder is required.", "File Copy Studio", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
            // Multiple sources mirror under Dest by leaf name (ComputeMirrorNames) — mixed-parent
            // roots produce a confusing layout, so require one common parent. Catches presets/
            // scheduler/CLI sessions too, not just the live UI add-paths.
            if (!RootsShareCommonParent(roots))
            { AppDialog.Show(this, "All source folders must be subfolders of the same parent directory.", "File Copy Studio", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }

            string d = Path.GetFullPath(session.DestPath).TrimEnd('\\');
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                { AppDialog.Show(this, $"Source folder does not exist:\n{root}", "File Copy Studio", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
                string s = Path.GetFullPath(root).TrimEnd('\\');
                if (string.Equals(s, d, StringComparison.OrdinalIgnoreCase))
                { AppDialog.Show(this, "Source and destination must differ.", "File Copy Studio", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
            }
            return true;
        }

        /// <summary>True when this session is exactly what AppPrefs currently holds — i.e. a manual,
        /// non-scheduled scan driven by the main window's live Source/Dest fields. Presets and the
        /// scheduler build sessions that won't match, so their runs don't pollute Recent MRU lists.</summary>
        private static bool IsLivePrefsSession(CopySession session)
        {
            if (session.IsScheduled) return false;
            var prefs = AppPrefs.Instance;
            var liveRoots = prefs.SourceList.Count > 0
                ? prefs.SourceList
                : (string.IsNullOrWhiteSpace(prefs.SourcePath) ? new List<string>() : new List<string> { prefs.SourcePath });
            return session.SourcePaths.SequenceEqual(liveRoots, StringComparer.OrdinalIgnoreCase)
                && string.Equals(session.DestPath, prefs.DestPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Computes a unique mirror folder name per root (its leaf name, disambiguated with
        /// " (2)", " (3)", ... when two roots share the same leaf, e.g. D:\A\Data and E:\B\Data).</summary>
        private static string[] ComputeMirrorNames(IReadOnlyList<string> roots)
        {
            var names = new string[roots.Count];
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < roots.Count; i++)
            {
                string leaf = Path.GetFileName(roots[i].TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(leaf)) leaf = roots[i].Replace(":", "").Replace('\\', '_').Replace('/', '_');

                if (!seen.TryGetValue(leaf, out int count))
                {
                    seen[leaf] = 1;
                    names[i] = leaf;
                }
                else
                {
                    count++;
                    seen[leaf] = count;
                    names[i] = $"{leaf} ({count})";
                }
            }
            return names;
        }

        /// <summary>Builds FileItems for individually-checked loose files (picker rows under
        /// SourceParent that are files, not folders) — CopyEngine.Enumerate only walks directories,
        /// so these are appended directly. Mirrors the dest-path/conflict-mode logic in
        /// GridFiles_Drop.</summary>
        private static List<FileItem> BuildCheckedFileItems(IReadOnlyList<string> filePaths, string srcRoot, string destRoot, ConflictMode mode)
        {
            var result = new List<FileItem>();
            string destRootNorm = destRoot.TrimEnd('\\', '/');
            string srcRootNorm  = srcRoot?.TrimEnd('\\', '/') ?? "";

            foreach (string srcPath in filePaths)
            {
                var fi = new FileInfo(srcPath);
                if (!fi.Exists) continue;

                string relative = (!string.IsNullOrEmpty(srcRootNorm) &&
                    srcPath.StartsWith(srcRootNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    ? srcPath.Substring(srcRootNorm.Length + 1)
                    : fi.Name;

                string destPath = Path.Combine(destRootNorm, relative);
                var    destFi   = File.Exists(destPath) ? new FileInfo(destPath) : null;
                bool   destExists = destFi != null;

                var item = new FileItem
                {
                    SourcePath     = srcPath,
                    DestPath       = destPath,
                    RelativePath   = relative,
                    Name           = fi.Name,
                    SizeBytes      = fi.Length,
                    SourceModified = fi.LastWriteTimeUtc,
                    DestModified   = destFi?.LastWriteTimeUtc,
                    DestExists     = destExists,
                    Action = CopyEngine.ResolveConflictAction(fi.LastWriteTimeUtc, destFi?.LastWriteTimeUtc, destExists, mode)
                };
                result.Add(item);
            }
            return result;
        }

        // ── CRC compute ───────────────────────────────────────────────────────
        private async void BtnComputeCrc_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var sel = GridFiles.SelectedItems.Cast<FileItem>().ToList();
            if (sel.Count == 0) sel = _items.ToList();
            await ComputeCrcForAsync(sel);
        }

        private async void CtxComputeCrc_Click(object sender, System.Windows.RoutedEventArgs e)
            => await ComputeCrcForAsync(CtxTargets());

        private async Task ComputeCrcForAsync(IEnumerable<FileItem> items)
        {
            int bufKb = AppPrefs.Instance.BufferSizeKb;
            Vm.StatusText = "Computing CRC…";
            try
            {
                foreach (var it in items)
                {
                    if (File.Exists(it.SourcePath))
                        it.SourceCrc = await CopyEngine.ComputeCrcAsync(it.SourcePath, bufKb * 1024, CancellationToken.None);
                    if (it.DestExists && File.Exists(it.DestPath))
                        it.DestCrc = await CopyEngine.ComputeCrcAsync(it.DestPath, bufKb * 1024, CancellationToken.None);
                }
                Vm.StatusText = "CRC computed.";
            }
            catch (Exception ex) { Vm.StatusText = "CRC error: " + ex.Message; }
        }

        // ── Filter ────────────────────────────────────────────────────────────
        private bool FilterItem(object obj)
        {
            if (obj is not FileItem it) return false;
            return _activeFilter switch
            {
                "Errors"  => it.Action == FileAction.Failed || !string.IsNullOrEmpty(it.ErrorText),
                "ToCopy"  => it.Action is FileAction.Copy or FileAction.Overwrite or FileAction.Rename or FileAction.Resume,
                "Skipped" => it.Action == FileAction.Skip,
                "Done"    => it.Action is FileAction.Done or FileAction.Verified,
                "Resume"  => it.Action == FileAction.Resume,
                _         => true
            };
        }

        private void RestoreFilterSelection()
        {
            string saved = AppPrefs.Instance.FileGridFilter ?? "All";
            _activeFilter = saved;
            foreach (ComboBoxItem item in CmbFilter.Items)
            {
                if (item.Tag is string tag && tag == saved) { CmbFilter.SelectedItem = item; break; }
            }
        }

        private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbFilter.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
            {
                _activeFilter = tag;
                AppPrefs.Instance.FileGridFilter = tag;
            }
            RefreshFilter(); // always go through the Background-priority path
        }

        private void RefreshFilter()
        {
            Dispatcher.InvokeAsync(() =>
            {
                _itemsView?.Refresh();
                if (LblFilterCount == null) return;
                int visible = _itemsView?.Cast<object>().Count() ?? 0;
                int total   = _items.Count;
                LblFilterCount.Text = _activeFilter == "All"
                    ? $"{total:N0} files"
                    : $"{visible:N0} of {total:N0}";
            });
        }

        // ── DataGrid columns ──────────────────────────────────────────────────
        private void RestoreFileGridColumns()
        {
            var p = AppPrefs.Instance;
            var widths = p.FileGridColumnWidths;
            if (widths != null && widths.Length > 0)
            {
                int n = Math.Min(widths.Length, GridFiles.Columns.Count);
                for (int i = 0; i < n; i++)
                    if (widths[i] > 0)
                        GridFiles.Columns[i].Width = new DataGridLength(widths[i]);
            }

            // Restore sort
            if (!string.IsNullOrEmpty(p.FileGridSortColumn))
            {
                var col = GridFiles.Columns.FirstOrDefault(c => c.Header?.ToString() == p.FileGridSortColumn);
                if (col != null)
                {
                    var dir = p.FileGridSortDirection == "Descending"
                        ? System.ComponentModel.ListSortDirection.Descending
                        : System.ComponentModel.ListSortDirection.Ascending;
                    col.SortDirection = dir;
                    _itemsView?.SortDescriptions.Clear();
                    // Map header to bound property name
                    string? prop = col.Header?.ToString() switch
                    {
                        "File"       => nameof(FileItem.RelativePath),
                        "Size"       => nameof(FileItem.SizeBytes),
                        "Modified"   => nameof(FileItem.SourceModified),
                        "Resume"     => nameof(FileItem.ResumeOffset),
                        "Dest exists"=> nameof(FileItem.DestExists),
                        "Dest mod"   => nameof(FileItem.DestModified),
                        "Src CRC"    => nameof(FileItem.SourceCrc),
                        "Dst CRC"    => nameof(FileItem.DestCrc),
                        "Action"     => nameof(FileItem.StatusText),
                        _            => null
                    };
                    if (prop != null)
                        _itemsView?.SortDescriptions.Add(
                            new System.ComponentModel.SortDescription(prop, dir));
                }
            }
        }

        private void SaveFileGridColumns()
        {
            var p   = AppPrefs.Instance;
            var arr = new double[GridFiles.Columns.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = GridFiles.Columns[i].ActualWidth;
            p.FileGridColumnWidths = arr;

            // Save sort
            var sortedCol = GridFiles.Columns.FirstOrDefault(c => c.SortDirection.HasValue);
            if (sortedCol != null)
            {
                p.FileGridSortColumn    = sortedCol.Header?.ToString() ?? "";
                p.FileGridSortDirection = sortedCol.SortDirection == System.ComponentModel.ListSortDirection.Descending
                    ? "Descending" : "Ascending";
            }
            else
            {
                p.FileGridSortColumn    = "";
                p.FileGridSortDirection = "Ascending";
            }
        }

        private void BtnResetColumns_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            foreach (var col in GridFiles.Columns) col.ClearValue(DataGridColumn.WidthProperty);
            AppPrefs.Instance.FileGridColumnWidths = Array.Empty<double>();
            Vm.StatusText = "Column widths reset to defaults.";
        }

        private void GridFiles_DragOver(object sender, DragEventArgs e)
        {
            bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void GridFiles_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (dropped == null) return;

            var files   = dropped.Where(p => File.Exists(p)).ToList();
            var folders = dropped.Where(p => Directory.Exists(p)).ToList();

            // Dropping a single folder → it becomes the new source parent; scan.
            if (folders.Count == 1 && files.Count == 0)
            {
                ApplySourceParent(folders[0]);
                SafeFireAsync(ScanAsync());
                return;
            }

            // Multiple folders no longer have a clean single-parent mapping — point at the inline picker.
            if (folders.Count > 1 && files.Count == 0)
            {
                AppDialog.Show(this,
                    "Drop a single parent folder, then use the inline picker below it to narrow the copy " +
                    "to specific subfolders.",
                    "File Copy Studio", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (files.Count == 0) return;

            if (string.IsNullOrWhiteSpace(Vm.Prefs.DestPath))
            {
                AppDialog.Show(this, "Set the destination folder first before dropping files.",
                    "Destination required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string destRoot    = Vm.Prefs.DestPath.TrimEnd('\\', '/');
            string srcRootNorm = Vm.Prefs.SourcePath?.TrimEnd('\\', '/') ?? "";
            var    mode        = AppPrefs.Instance.ConflictMode;
            int added = 0, skipped = 0;

            // O(1) dedup lookup — build once before the loop
            var existing = new HashSet<string>(_items.Select(x => x.SourcePath),
                StringComparer.OrdinalIgnoreCase);

            foreach (string srcPath in files)
            {
                if (!existing.Add(srcPath)) { skipped++; continue; }
                var fi     = new System.IO.FileInfo(srcPath);
                string rel = (!string.IsNullOrEmpty(srcRootNorm) &&
                    srcPath.StartsWith(srcRootNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    ? srcPath.Substring(srcRootNorm.Length + 1) : fi.Name;
                string dst   = Path.Combine(destRoot, rel);
                var    dstFi = File.Exists(dst) ? new System.IO.FileInfo(dst) : null;
                bool   exists = dstFi != null;
                var item = new FileItem
                {
                    SourcePath     = srcPath, DestPath = dst, RelativePath = rel,
                    Name           = fi.Name, SizeBytes = fi.Length,
                    SourceModified = fi.LastWriteTimeUtc,
                    DestModified   = dstFi?.LastWriteTimeUtc,
                    DestExists     = exists,
                    Action         = CopyEngine.ResolveConflictAction(fi.LastWriteTimeUtc, dstFi?.LastWriteTimeUtc, exists, mode)
                };
                _items.Add(item); added++;
            }
            UpdateSummary(); RefreshFilter();
            CommandManager.InvalidateRequerySuggested();
            Vm.StatusText = $"Added {added} file(s)." + (skipped > 0 ? $" {skipped} already listed." : "");
        }

        /// <summary>FC-10: log every skipped folder/link and show "⚠ N skipped — View log".</summary>
        private void ShowScanSkips(IReadOnlyCollection<string> skipped)
        {
            foreach (var line in skipped) _log.LogWarning("Scan skipped {Entry}", line);
            if (skipped.Count == 0) { ScanSkipPanel.Visibility = Visibility.Collapsed; return; }
            TxtScanSkipped.Text = skipped.Count == 1 ? "1 item skipped" : $"{skipped.Count:N0} items skipped";
            ScanSkipPanel.Visibility = Visibility.Visible;
        }

        private void BtnViewScanLog_Click(object sender, RoutedEventArgs e)
        {
            LogPanel.IsExpanded = true;
            LoadLogContent();
            LogPanel.BringIntoView();
            TxtLog.Focus();
        }
    }
}
