using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

#nullable enable

namespace FileCopy
{
    /// <summary>One immediate child (folder or file) of the selected source parent directory,
    /// shown in the inline source picker (Section 3) so the user can narrow the copy to specific
    /// subfolders/files instead of the whole parent tree. Replaces the old BulkDirectoryPickerDialog
    /// DirRow — same shape, plus IsFile.</summary>
    public sealed class PickerRow : INotifyPropertyChanged
    {
        public string FullPath { get; }
        public string Name { get; }
        public bool   IsFile { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked == value) return; _isChecked = value; OnChanged(nameof(IsChecked)); }
        }

        public long     SizeBytes   { get; private set; }
        public double   SizeMb      => SizeBytes / 1024.0 / 1024.0;
        public int       FileCount  { get; private set; }
        public DateTime  Modified   { get; private set; }
        public bool       IsComputing { get; private set; }

        public string DisplayName    => (IsFile ? "File " : "Folder ") + Name;
        public string IconKey        => IsFile ? "file" : "folder";
        public string SortKey        => (IsFile ? "1" : "0") + Name;
        public string SizeText       => IsComputing ? "…" : FormatSize(SizeBytes);
        public string FileCountText  => IsFile ? "—" : (IsComputing ? "…" : FileCount.ToString());

        public PickerRow(string fullPath, bool isFile)
        {
            FullPath = fullPath;
            IsFile   = isFile;
            Name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(Name)) Name = fullPath;

            if (isFile)
            {
                try
                {
                    var fi = new FileInfo(fullPath);
                    SizeBytes = fi.Length;
                    Modified  = fi.LastWriteTime;
                    FileCount = 1;
                }
                catch { /* tolerate unreadable file */ }
                IsComputing = false; // file stats are already known, no background walk needed
            }
            else
            {
                try { Modified = Directory.GetLastWriteTime(fullPath); } catch { Modified = DateTime.MinValue; }
                IsComputing = true; // size/count computed off-thread via ComputeStats()
            }
        }

        /// <summary>Recursively walks a folder to total size/file count. No-op for files
        /// (already known at construction). Tolerant of IO errors. Call off the UI thread.</summary>
        public void ComputeStats()
        {
            if (IsFile) return;
            long size = 0; int count = 0; DateTime latest = Modified;
            try
            {
                var stack = new Stack<string>();
                stack.Push(FullPath);
                while (stack.Count > 0)
                {
                    var dir = stack.Pop();
                    string[] files; string[] subdirs;
                    try { files = Directory.GetFiles(dir); } catch { files = Array.Empty<string>(); }
                    try { subdirs = Directory.GetDirectories(dir); } catch { subdirs = Array.Empty<string>(); }

                    foreach (var f in files)
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            size += fi.Length;
                            count++;
                            if (fi.LastWriteTime > latest) latest = fi.LastWriteTime;
                        }
                        catch { /* skip unreadable file */ }
                    }
                    foreach (var sd in subdirs) stack.Push(sd);
                }
            }
            catch { /* leave partial results */ }

            SizeBytes = size; FileCount = count; Modified = latest; IsComputing = false;
        }

        /// <summary>Call on the UI thread after ComputeStats() to push bound-column updates.</summary>
        public void RaiseStatsChanged()
        {
            OnChanged(nameof(SizeText));
            OnChanged(nameof(FileCountText));
            OnChanged(nameof(Modified));
            OnChanged(nameof(IsComputing));
        }

        private static string FormatSize(long bytes)
        {
            double mb = bytes / 1024.0 / 1024.0;
            if (mb >= 1024) return $"{mb / 1024.0:0.##} GB";
            return $"{mb:0.##} MB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
