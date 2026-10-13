using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;

namespace NoteStation
{
    /// <summary>
    /// Per-item JSON storage for clipboard history, sticky notes, and task list.
    /// Each item is its own file under %AppData%\NoteStation\clips|notes|tasks\.
    /// Soft-deleted items keep their file but carry IsDeleted=true + DeletedAt.
    /// Backup/restore bundles all three into a single .nsbak JSON.
    /// </summary>
    public static class DataStore
    {
        private static readonly string _dir = App.AppDataDir;

        // Per-item directories
        private static readonly string _clipsDir = Path.Combine(_dir, "clips");
        private static readonly string _notesDir = Path.Combine(_dir, "notes");
        private static readonly string _tasksDir = Path.Combine(_dir, "tasks");

        // Legacy single-file paths (used for migration only)
        private static readonly string _legacyClipFile  = Path.Combine(_dir, "clipboard.json");
        private static readonly string _legacyNotesFile = Path.Combine(_dir, "notes.json");
        private static readonly string _legacyTasksFile = Path.Combine(_dir, "tasks.json");

        private static readonly string _templatesFile   = Path.Combine(_dir, "templates.json");

        // ── Write freeze ──────────────────────────────────────────────────────
        // Set by the fatal-error path (App.FatalShutdown / AppDomain handler): after an
        // unknown exception in-memory state may be half-mutated, so nothing more is written
        // or deleted — the last good per-item files on disk stay intact.
        private static volatile bool _frozen;
        public static bool IsFrozen => _frozen;
        public static void Freeze()
        {
            if (_frozen) return;
            _frozen = true;
            AppLog.Warn("DataStore frozen after fatal error — further writes skipped.");
        }
        private static readonly string _savedViewsFile  = Path.Combine(_dir, "savedviews.json");

        // ── Migration ─────────────────────────────────────────────────────────
        /// <summary>
        /// Called once on startup before any Load*.  Migrates old single-file JSON
        /// arrays to individual per-item files when upgrading from an earlier build.
        /// </summary>
        public static void MigrateIfNeeded()
        {
            MigrateFile<ClipboardItemDto>(_legacyClipFile,  _clipsDir,
                NsJsonContext.Default.ListClipboardItemDto,
                dto => dto.Id.ToString());
            MigrateFile<StickyNoteDto>(_legacyNotesFile, _notesDir,
                NsJsonContext.Default.ListStickyNoteDto,
                dto => dto.Id.ToString());
            MigrateFile<TaskItemDto>(_legacyTasksFile,   _tasksDir,
                NsJsonContext.Default.ListTaskItemDto,
                dto => dto.Id.ToString());
        }

        private static void MigrateFile<T>(string legacyPath, string targetDir,
            System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> typeInfo,
            Func<T, string> idSelector)
        {
            if (!File.Exists(legacyPath)) return;
            if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir, "*.json").Length > 0) return;

            try
            {
                Directory.CreateDirectory(targetDir);
                string json = File.ReadAllText(legacyPath, Encoding.UTF8);
                var items   = JsonSerializer.Deserialize(json, typeInfo);
                if (items == null) return;
                foreach (var item in items)
                {
                    string itemJson = JsonSerializer.Serialize(item, GetSingleTypeInfo<T>());
                    WriteAtomic(Path.Combine(targetDir, $"{idSelector(item)}.json"), itemJson);
                }
                // Rename legacy file so migration won't run again
                File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
                Debug.WriteLine($"[NoteStation] Migrated {legacyPath} → {targetDir}");
            }
            catch (Exception ex) { Debug.WriteLine($"[NoteStation] Migration failed for {legacyPath}: {ex.Message}"); AppLog.Error($"Migrate {legacyPath}", ex); }
        }

        // Resolves the per-item JsonTypeInfo from the source-gen context
        private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> GetSingleTypeInfo<T>()
        {
            if (typeof(T) == typeof(ClipboardItemDto))
                return (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)(object)NsJsonContext.Default.ClipboardItemDto;
            if (typeof(T) == typeof(StickyNoteDto))
                return (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)(object)NsJsonContext.Default.StickyNoteDto;
            if (typeof(T) == typeof(TaskItemDto))
                return (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)(object)NsJsonContext.Default.TaskItemDto;
            throw new InvalidOperationException($"No source-gen type info for {typeof(T).Name}");
        }

        // ── Purge soft-deleted items ──────────────────────────────────────────
        /// <summary>
        /// Physically deletes files for items that were soft-deleted more than
        /// <paramref name="retentionDays"/> days ago.  0 = keep forever.
        /// Also trims from the supplied in-memory deleted lists.
        /// </summary>
        public static void PurgeDeleted(
            int retentionDays,
            List<ClipboardItem> deletedClips,
            List<StickyNote>    deletedNotes,
            List<TaskItem>      deletedTasks)
        {
            if (retentionDays <= 0 || _frozen) return;
            var cutoff = DateTime.Now.AddDays(-retentionDays);

            PurgeDir(_clipsDir,  deletedClips,  c => c.IsDeleted && c.DeletedAt < cutoff, c => c.Id);
            PurgeDir(_notesDir,  deletedNotes,  n => n.IsDeleted && n.DeletedAt < cutoff, n => n.Id);
            PurgeDir(_tasksDir,  deletedTasks,  t => t.IsDeleted && t.DeletedAt < cutoff, t => t.Id);
        }

        private static void PurgeDir<T>(string dir, List<T> list,
            Func<T, bool> shouldPurge, Func<T, int> getId)
        {
            if (!Directory.Exists(dir)) return;
            var toRemove = list.Where(shouldPurge).ToList();
            foreach (var item in toRemove)
            {
                try
                {
                    string path = Path.Combine(dir, $"{getId(item)}.json");
                    if (File.Exists(path)) File.Delete(path);
                    if (File.Exists(path + Core.ClipVault.EncSuffix)) File.Delete(path + Core.ClipVault.EncSuffix);
                    list.Remove(item);
                }
                catch { /* best-effort */ }
            }
        }

        // ── Clipboard ─────────────────────────────────────────────────────────
        public static List<ClipboardItem> LoadClipboard()
        {
            Directory.CreateDirectory(_clipsDir);
            SyncClipEncryption(AppPrefs.Instance.EncryptClipboardAtRest);
            var files = Directory.GetFiles(_clipsDir, "*.json")
                .Concat(Directory.GetFiles(_clipsDir, "*.json" + Core.ClipVault.EncSuffix)).ToArray();
            // Read + deserialize files in parallel (I/O-bound); STJ + MapClip are thread-safe.
            var bag = new System.Collections.Concurrent.ConcurrentBag<(string file, ClipboardItem item)>();
            System.Threading.Tasks.Parallel.ForEach(files, file =>
            {
                try
                {
                    var dto = JsonSerializer.Deserialize(
                        Core.ClipVault.ReadText(file),
                        NsJsonContext.Default.ClipboardItemDto);
                    if (dto != null) bag.Add((file, MapClip(dto)));
                }
                catch (Exception ex) { Debug.WriteLine($"[NoteStation] Clip load {file}: {ex.Message}"); AppLog.Error($"Clip load {file}", ex); }
            });
            return bag.OrderBy(x => x.file, StringComparer.Ordinal)
                      .Select(x => x.item)
                      .GroupBy(c => c.Id).Select(g => g.First())   // .json + .json.enc both present after an interrupted migration
                      .ToList();
        }

        public static void SaveClip(ClipboardItem item)
        {
            try
            {
                Directory.CreateDirectory(_clipsDir);
                string json = JsonSerializer.Serialize(
                    MapClipDto(item),
                    NsJsonContext.Default.ClipboardItemDto);
                WriteClipFile(item.Id, json, AppPrefs.Instance.EncryptClipboardAtRest);
            }
            catch (Exception ex) { Debug.WriteLine($"[NoteStation] SaveClip {item.Id}: {ex.Message}"); AppLog.Error($"SaveClip {item.Id}", ex); }
        }

        /// <summary>
        /// Bulk-write all supplied clips.  Does NOT delete files for items not in the list —
        /// use <see cref="DeleteClip"/> for explicit removals.
        /// </summary>
        public static void SaveClipboard(IEnumerable<ClipboardItem> items)
        {
            foreach (var c in items) SaveClip(c);
        }

        public static void DeleteClip(int id)
        {
            if (_frozen) return;
            try
            {
                string path = Path.Combine(_clipsDir, $"{id}.json");
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + Core.ClipVault.EncSuffix)) File.Delete(path + Core.ClipVault.EncSuffix);
            }
            catch (Exception ex) { Debug.WriteLine($"[NoteStation] DeleteClip {id}: {ex.Message}"); AppLog.Error($"DeleteClip {id}", ex); }
        }

        private static ClipboardItem MapClip(ClipboardItemDto d) => new ClipboardItem
        {
            Id         = d.Id,
            Title      = d.Title ?? "",
            Text       = d.Text,
            Preview    = d.Preview,
            CapturedAt = TryParseDate(d.CapturedAt),
            IsPinned   = d.IsPinned,
            Tag        = d.Tag,
            Category   = d.Category,
            FolderId   = d.FolderId,
            IsImage    = d.IsImage,
            ImagePath  = d.IsImage && !string.IsNullOrEmpty(d.ImagePath) && !Path.IsPathRooted(d.ImagePath)
                         ? Path.Combine(App.AppDataDir, d.ImagePath)
                         : d.ImagePath ?? "",
            SourceApp    = d.SourceApp    ?? "",
            LinkedTaskId = d.LinkedTaskId,
            ParsedTags   = d.ParsedTags  ?? new List<string>(),
            IsDeleted    = d.IsDeleted,
            DeletedAt    = TryParseDate(d.DeletedAt),
            HiddenFromRecent = d.HiddenFromRecent,
            EditWidth  = d.EditWidth,
            EditHeight = d.EditHeight,
        };

        private static ClipboardItemDto MapClipDto(ClipboardItem i) => new ClipboardItemDto
        {
            Id = i.Id, Title = i.Title, Text = i.Text, Preview = i.Preview,
            CapturedAt = i.CapturedAt.ToString("o"),
            IsPinned   = i.IsPinned, Tag = i.Tag,
            Category   = i.Category,  FolderId = i.FolderId,
            IsImage    = i.IsImage,
            ImagePath  = i.IsImage && !string.IsNullOrEmpty(i.ImagePath)
                         ? Path.Combine("clipimages", Path.GetFileName(i.ImagePath))
                         : "",
            SourceApp    = i.SourceApp    ?? "",
            LinkedTaskId = i.LinkedTaskId,
            ParsedTags   = i.ParsedTags  ?? new List<string>(),
            IsDeleted    = i.IsDeleted,
            DeletedAt    = i.IsDeleted ? i.DeletedAt.ToString("o") : "",
            HiddenFromRecent = i.HiddenFromRecent,
            EditWidth  = i.EditWidth,
            EditHeight = i.EditHeight,
        };

        // ── Sticky Notes ──────────────────────────────────────────────────────
        public static List<StickyNote> LoadNotes()
        {
            Directory.CreateDirectory(_notesDir);
            var files = Directory.GetFiles(_notesDir, "*.json");
            var bag = new System.Collections.Concurrent.ConcurrentBag<(string file, StickyNote item)>();
            System.Threading.Tasks.Parallel.ForEach(files, file =>
            {
                try
                {
                    var dto = JsonSerializer.Deserialize(
                        File.ReadAllText(file, Encoding.UTF8),
                        NsJsonContext.Default.StickyNoteDto);
                    if (dto != null) bag.Add((file, MapNote(dto)));
                }
                catch (Exception ex) { Debug.WriteLine($"[NoteStation] Note load {file}: {ex.Message}"); AppLog.Error($"Note load {file}", ex); }
            });
            return bag.OrderBy(x => x.file, StringComparer.Ordinal).Select(x => x.item).ToList();
        }

        public static void SaveNote(StickyNote note)
        {
            try
            {
                Directory.CreateDirectory(_notesDir);
                string json = JsonSerializer.Serialize(
                    MapNoteDto(note),
                    NsJsonContext.Default.StickyNoteDto);
                WriteAtomic(Path.Combine(_notesDir, $"{note.Id}.json"), json);
            }
            catch (Exception ex) { Debug.WriteLine($"[NoteStation] SaveNote {note.Id}: {ex.Message}"); AppLog.Error($"SaveNote {note.Id}", ex); }
        }

        public static void SaveNotes(IEnumerable<StickyNote> notes)
        {
            foreach (var n in notes) SaveNote(n);
        }

        public static void DeleteNote(int id)
        {
            if (_frozen) return;
            try
            {
                string path = Path.Combine(_notesDir, $"{id}.json");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) { Debug.WriteLine($"[NoteStation] DeleteNote {id}: {ex.Message}"); AppLog.Error($"DeleteNote {id}", ex); }
        }

        private static StickyNote MapNote(StickyNoteDto d) => new StickyNote
        {
            Id              = d.Id,
            Title           = d.Title,
            Content         = d.Content,
            IsVisible       = d.IsVisible,
            ShowOnStartup   = d.ShowOnStartup,
            Left            = d.Left,
            Top             = d.Top,
            Width           = d.Width,
            Height          = d.Height,
            BackgroundColor = d.BackgroundColor,
            TextColor       = d.TextColor,
            FontFamily      = d.FontFamily,
            FontSize        = d.FontSize,
            FontWeight      = d.FontWeight,
            FontStyle       = d.FontStyle,
            Category        = d.Category,
            FolderId        = d.FolderId,
            IsPinned        = d.IsPinned,
            IsTopmost       = d.IsTopmost,
            NoteOpacity     = d.NoteOpacity > 0 ? d.NoteOpacity : 1.0,
            CreatedAt       = TryParseDate(d.CreatedAt),
            ModifiedAt      = TryParseDate(d.ModifiedAt),
            PinHash             = d.PinHash ?? "",
            IsLocked            = !string.IsNullOrEmpty(d.PinHash),
            AutoBlurMinutes     = d.AutoBlurMinutes,
            ExcludeFromAutoBlur = d.ExcludeFromAutoBlur,
            TitleFontSize       = d.TitleFontSize > 0 ? d.TitleFontSize : 14,
            ReminderAt          = !string.IsNullOrEmpty(d.ReminderAt) && DateTime.TryParse(d.ReminderAt, null,
                                    System.Globalization.DateTimeStyles.RoundtripKind, out var ra) ? ra : (DateTime?)null,
            AttachedWindowTitle = d.AttachedWindowTitle ?? "",
            NoteTexture         = d.NoteTexture ?? "",
            HideTitleBar        = d.HideTitleBar,
            IsFolded            = d.IsFolded,
            IsDeleted           = d.IsDeleted,
            DeletedAt           = TryParseDate(d.DeletedAt),
            HiddenFromRecent    = d.HiddenFromRecent,
            ShowInSidebar       = d.ShowInSidebar,
        };

        private static StickyNoteDto MapNoteDto(StickyNote n) => new StickyNoteDto
        {
            Id = n.Id, Title = n.Title, Content = n.Content,
            IsVisible = n.IsVisible, ShowOnStartup = n.ShowOnStartup, Left = n.Left, Top = n.Top,
            Width = n.Width, Height = n.Height,
            BackgroundColor = n.BackgroundColor, TextColor = n.TextColor,
            FontFamily = n.FontFamily, FontSize = n.FontSize,
            FontWeight = n.FontWeight, FontStyle = n.FontStyle,
            Category    = n.Category,   FolderId = n.FolderId,   IsPinned = n.IsPinned,
            IsTopmost   = n.IsTopmost,  NoteOpacity = n.NoteOpacity,
            CreatedAt   = n.CreatedAt.ToString("o"),
            ModifiedAt  = n.ModifiedAt.ToString("o"),
            PinHash             = n.PinHash ?? "",
            AutoBlurMinutes     = n.AutoBlurMinutes,
            ExcludeFromAutoBlur = n.ExcludeFromAutoBlur,
            TitleFontSize       = n.TitleFontSize > 0 ? n.TitleFontSize : 14,
            ReminderAt          = n.ReminderAt?.ToString("o") ?? "",
            AttachedWindowTitle = n.AttachedWindowTitle ?? "",
            NoteTexture         = n.NoteTexture ?? "",
            HideTitleBar        = n.HideTitleBar,
            IsFolded            = n.IsFolded,
            IsDeleted           = n.IsDeleted,
            DeletedAt           = n.IsDeleted ? n.DeletedAt.ToString("o") : "",
            HiddenFromRecent    = n.HiddenFromRecent,
            ShowInSidebar       = n.ShowInSidebar,
        };

        // ── Tasks ─────────────────────────────────────────────────────────────
        public static List<TaskItem> LoadTasks()
        {
            Directory.CreateDirectory(_tasksDir);
            var files = Directory.GetFiles(_tasksDir, "*.json");
            var bag = new System.Collections.Concurrent.ConcurrentBag<(string file, TaskItem item)>();
            System.Threading.Tasks.Parallel.ForEach(files, file =>
            {
                try
                {
                    var dto = JsonSerializer.Deserialize(
                        File.ReadAllText(file, Encoding.UTF8),
                        NsJsonContext.Default.TaskItemDto);
                    if (dto != null) bag.Add((file, MapTask(dto)));
                }
                catch (Exception ex) { Debug.WriteLine($"[NoteStation] Task load {file}: {ex.Message}"); AppLog.Error($"Task load {file}", ex); }
            });
            return bag.OrderBy(x => x.file, StringComparer.Ordinal).Select(x => x.item).ToList();
        }

        public static void SaveTask(TaskItem task)
        {
            try
            {
                Directory.CreateDirectory(_tasksDir);
                string json = JsonSerializer.Serialize(
                    MapTaskDto(task),
                    NsJsonContext.Default.TaskItemDto);
                WriteAtomic(Path.Combine(_tasksDir, $"{task.Id}.json"), json);
            }
            catch (Exception ex) { Debug.WriteLine($"[NoteStation] SaveTask {task.Id}: {ex.Message}"); AppLog.Error($"SaveTask {task.Id}", ex); }
        }

        public static void SaveTasks(IEnumerable<TaskItem> tasks)
        {
            foreach (var t in tasks) SaveTask(t);
        }

        /// <summary>
        /// L-4: fire-and-forget task save for high-frequency callers (drag-reorder,
        /// move up/down) — SaveTasks() rewrites every task file synchronously and
        /// visibly stalls the UI on a full task list. Snapshots the DTOs on the
        /// calling (UI) thread first so a later in-place edit to a TaskItem can't
        /// race the background write. Not used on the shutdown path, which still
        /// needs a synchronous save to guarantee it completes before exit.
        /// </summary>
        // M-3: per-task write sequence. When two SaveTasksAsync calls race for the
        // same task id (rapid drag-reorders), each snapshot's write only proceeds
        // if it's still the *latest* one queued for that id by the time its
        // background write runs — an older, stale write yields to a newer one
        // instead of racing it and possibly landing last.
        private static readonly ConcurrentDictionary<int, long> _taskWriteSeq = new();

        public static void SaveTasksAsync(IEnumerable<TaskItem> tasks)
        {
            var dtos = new List<(int id, TaskItemDto dto, long seq)>();
            foreach (var t in tasks)
            {
                long seq = _taskWriteSeq.AddOrUpdate(t.Id, 1, (_, v) => v + 1);
                dtos.Add((t.Id, MapTaskDto(t), seq));
            }
            System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var (id, dto, seq) in dtos)
                {
                    try
                    {
                        if (_taskWriteSeq.TryGetValue(id, out var latest) && latest != seq) continue;
                        Directory.CreateDirectory(_tasksDir);
                        string json = JsonSerializer.Serialize(dto, NsJsonContext.Default.TaskItemDto);
                        WriteAtomic(Path.Combine(_tasksDir, $"{id}.json"), json);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[NoteStation] SaveTasksAsync {id}: {ex.Message}"); AppLog.Error($"SaveTasksAsync {id}", ex); }
                }
            });
        }

        public static void DeleteTask(int id)
        {
            if (_frozen) return;
            try
            {
                string path = Path.Combine(_tasksDir, $"{id}.json");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) { Debug.WriteLine($"[NoteStation] DeleteTask {id}: {ex.Message}"); AppLog.Error($"DeleteTask {id}", ex); }
        }

        private static TaskItem MapTask(TaskItemDto d)
        {
            var t = new TaskItem
            {
                Id           = d.Id,
                SortOrder    = d.SortOrder,
                Title        = d.Title,
                Notes        = d.Notes,
                IsCompleted  = d.IsCompleted,
                IsStarred    = d.IsStarred,
                HasDueDate   = d.HasDueDate,
                HasDueTime   = d.HasDueTime,
                PushedToTimer = d.PushedToTimer,
                TimerMinutes  = d.TimerMinutes,
                AccentColor   = d.AccentColor,
                Category      = d.Category,
                FolderId      = d.FolderId,
                CreatedAt     = TryParseDate(d.CreatedAt),
                ModifiedAt    = TryParseDate(d.ModifiedAt),
                Priority      = d.Priority,
                PriorityRank  = d.PriorityRank,
                IsArchived    = d.IsArchived,
                RecurrenceRule = d.RecurrenceRule ?? "None",
                IsHabit    = d.IsHabit,
                HabitStreak = d.HabitStreak,
                IsDeleted   = d.IsDeleted,
                DeletedAt   = TryParseDate(d.DeletedAt),
                HiddenFromRecent = d.HiddenFromRecent,
            };
            if (d.HasDueDate && DateTime.TryParse(d.DueDate, out var dd)) t.DueDate = dd;
            if (d.HasDueTime && TimeSpan.TryParse(d.DueTime, out var dt)) t.DueTime = dt;
            if (!string.IsNullOrEmpty(d.CompletedAt) && DateTime.TryParse(d.CompletedAt, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var ca)) t.CompletedAt = ca;
            if (!string.IsNullOrEmpty(d.LastHabitCompleted) && DateTime.TryParse(d.LastHabitCompleted, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var lhc)) t.LastHabitCompleted = lhc;
            if (d.LinkedClipIds != null) t.LinkedClipIds = new List<int>(d.LinkedClipIds);
            if (d.SubTasks != null)
                foreach (var s in d.SubTasks)
                    t.SubTasks.Add(new SubTaskItem { Text = s.Text, IsDone = s.IsDone });
            t.DependsOnIds = d.DependsOnIds ?? new List<int>();
            t.BlocksIds    = d.BlocksIds    ?? new List<int>();
            return t;
        }

        private static TaskItemDto MapTaskDto(TaskItem t) => new TaskItemDto
        {
            Id = t.Id, SortOrder = t.SortOrder, Title = t.Title, Notes = t.Notes,
            IsCompleted = t.IsCompleted, IsStarred = t.IsStarred,
            HasDueDate  = t.HasDueDate,
            DueDate     = t.DueDate?.ToString("yyyy-MM-dd") ?? "",
            HasDueTime  = t.HasDueTime,
            DueTime     = t.DueTime?.ToString(@"HH\:mm") ?? "",
            PushedToTimer = t.PushedToTimer, TimerMinutes = t.TimerMinutes,
            AccentColor   = t.AccentColor,
            Category      = t.Category,
            FolderId      = t.FolderId,
            CreatedAt     = t.CreatedAt.ToString("o"),
            ModifiedAt    = t.ModifiedAt.ToString("o"),
            CompletedAt   = t.CompletedAt?.ToString("o") ?? "",
            Priority      = t.Priority,
            PriorityRank  = t.PriorityRank,
            IsArchived    = t.IsArchived,
            RecurrenceRule = t.RecurrenceRule ?? "None",
            IsHabit            = t.IsHabit,
            HabitStreak        = t.HabitStreak,
            LastHabitCompleted = t.LastHabitCompleted?.ToString("o") ?? "",
            LinkedClipIds      = new List<int>(t.LinkedClipIds ?? new List<int>()),
            SubTasks = (t.SubTasks ?? new List<SubTaskItem>())
                       .Select(s => new SubTaskItemDto { Text = s.Text, IsDone = s.IsDone })
                       .ToList(),
            DependsOnIds = t.DependsOnIds ?? new List<int>(),
            BlocksIds    = t.BlocksIds    ?? new List<int>(),
            IsDeleted    = t.IsDeleted,
            DeletedAt    = t.IsDeleted ? t.DeletedAt.ToString("o") : "",
            HiddenFromRecent = t.HiddenFromRecent,
        };

        // ── Templates ─────────────────────────────────────────────────────────
        public static List<TemplateItem> LoadTemplates()
        {
            var result = new List<TemplateItem>();
            if (!File.Exists(_templatesFile)) return result;
            try
            {
                string json = File.ReadAllText(_templatesFile, Encoding.UTF8);
                var dtos = JsonSerializer.Deserialize(json, NsJsonContext.Default.ListTemplateItemDto);
                if (dtos == null) return result;
                foreach (var d in dtos)
                    result.Add(new TemplateItem
                    {
                        Id        = d.Id,
                        Title     = d.Title ?? "",
                        Text      = d.Text  ?? "",
                        Category  = d.Category ?? "",
                        FolderId  = d.FolderId,
                        SortOrder = d.SortOrder,
                        CreatedAt  = TryParseDate(d.CreatedAt),
                        ModifiedAt = TryParseDate(d.ModifiedAt),
                    });
            }
            catch (Exception ex) { Debug.WriteLine("[NoteStation] Templates load: " + ex.Message); AppLog.Error("Templates load", ex); }
            return result;
        }

        public static void SaveTemplates(IEnumerable<TemplateItem> templates)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                var dtos = templates.Select(t => new TemplateItemDto
                {
                    Id        = t.Id,
                    Title     = t.Title,
                    Text      = t.Text,
                    Category  = t.Category,
                    FolderId  = t.FolderId,
                    SortOrder = t.SortOrder,
                    CreatedAt  = t.CreatedAt.ToString("o"),
                    ModifiedAt = t.ModifiedAt.ToString("o"),
                }).ToList();
                WriteAtomic(_templatesFile,
                    JsonSerializer.Serialize(dtos, NsJsonContext.Default.ListTemplateItemDto));
            }
            catch (Exception ex) { Debug.WriteLine("[NoteStation] Templates save: " + ex.Message); AppLog.Error("Templates save", ex); }
        }

        // ── Saved Views ───────────────────────────────────────────────────────
        public static List<SavedView> LoadSavedViews()
        {
            var result = new List<SavedView>();
            if (!File.Exists(_savedViewsFile)) return result;
            try
            {
                string json = File.ReadAllText(_savedViewsFile, Encoding.UTF8);
                var dtos = JsonSerializer.Deserialize(json, NsJsonContext.Default.ListSavedViewDto);
                if (dtos == null) return result;
                foreach (var d in dtos)
                    result.Add(new SavedView
                    {
                        Id               = d.Id,
                        Name             = d.Name ?? "",
                        FilterExpression = d.FilterExpression ?? "",
                        Scope            = string.IsNullOrEmpty(d.Scope) ? "All" : d.Scope,
                        CreatedAt        = TryParseDate(d.CreatedAt),
                    });
            }
            catch (Exception ex) { Debug.WriteLine("[NoteStation] SavedViews load: " + ex.Message); AppLog.Error("SavedViews load", ex); }
            return result;
        }

        public static void SaveSavedViews(IEnumerable<SavedView> views)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                var dtos = views.Select(v => new SavedViewDto
                {
                    Id               = v.Id,
                    Name             = v.Name,
                    FilterExpression = v.FilterExpression,
                    Scope            = v.Scope,
                    CreatedAt        = v.CreatedAt.ToString("o"),
                }).ToList();
                WriteAtomic(_savedViewsFile,
                    JsonSerializer.Serialize(dtos, NsJsonContext.Default.ListSavedViewDto));
            }
            catch (Exception ex) { Debug.WriteLine("[NoteStation] SavedViews save: " + ex.Message); AppLog.Error("SavedViews save", ex); }
        }

        // ── Auto-backup rotation ──────────────────────────────────────────────
        private static readonly string _backupDir = Path.Combine(App.AppDataDir, "backups");
        private const int AutoBackupKeepCount = 3;

        public static void AutoBackup()
        {
            try
            {
                Directory.CreateDirectory(_backupDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path  = Path.Combine(_backupDir, $"nsbak_{stamp}.nsbak");
                ExportBackup(path);

                var existing = Directory.GetFiles(_backupDir, "nsbak_*.nsbak")
                                        .OrderByDescending(f => f).ToArray();
                for (int i = AutoBackupKeepCount; i < existing.Length; i++)
                {
                    try { File.Delete(existing[i]); }
                    catch { /* best-effort */ }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[NoteStation] AutoBackup: " + ex.Message); AppLog.Error("AutoBackup", ex); }
        }

        // ── Backup / Restore ──────────────────────────────────────────────────
        public static void ExportBackup(string path)
        {
            var clips = LoadClipboard();
            var notes = LoadNotes();
            var tasks = LoadTasks();

            var bundle = new JsonObject
            {
                ["version"]    = 1,
                ["exportedAt"] = DateTime.UtcNow.ToString("o"),
                ["clipboard"]  = JsonNode.Parse(JsonSerializer.Serialize(
                    clips.Select(c => MapClipDto(c)).ToList(),
                    NsJsonContext.Default.ListClipboardItemDto)),
                ["notes"]      = JsonNode.Parse(JsonSerializer.Serialize(
                    notes.Select(n => MapNoteDto(n)).ToList(),
                    NsJsonContext.Default.ListStickyNoteDto)),
                ["tasks"]      = JsonNode.Parse(JsonSerializer.Serialize(
                    tasks.Select(t => MapTaskDto(t)).ToList(),
                    NsJsonContext.Default.ListTaskItemDto)),
            };
            WriteAtomic(path, bundle.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public static string ImportBackup(string path)
        {
            string raw   = File.ReadAllText(path, Encoding.UTF8);
            var bundle   = JsonNode.Parse(raw) as JsonObject
                           ?? throw new InvalidDataException("Not a valid NoteStation backup file.");
            int version  = bundle["version"]?.GetValue<int>() ?? 0;
            if (version < 1) throw new InvalidDataException("Unrecognised backup version.");

            var clipsArr = (bundle["clipboard"] as JsonArray)?.ToJsonString();
            var notesArr = (bundle["notes"]     as JsonArray)?.ToJsonString();
            var tasksArr = (bundle["tasks"]     as JsonArray)?.ToJsonString();

            int cc = 0, nc = 0, tc = 0;

            if (clipsArr != null)
            {
                var dtos = JsonSerializer.Deserialize(clipsArr, NsJsonContext.Default.ListClipboardItemDto);
                if (dtos != null)
                {
                    // Clear existing clip files
                    if (Directory.Exists(_clipsDir))
                        foreach (var f in Directory.GetFiles(_clipsDir, "*.json"))
                            try { File.Delete(f); } catch { }
                    Directory.CreateDirectory(_clipsDir);
                    foreach (var d in dtos)
                    {
                        WriteAtomic(Path.Combine(_clipsDir, $"{d.Id}.json"),
                            JsonSerializer.Serialize(d, NsJsonContext.Default.ClipboardItemDto));
                    }
                    cc = dtos.Count;
                }
            }

            if (notesArr != null)
            {
                var dtos = JsonSerializer.Deserialize(notesArr, NsJsonContext.Default.ListStickyNoteDto);
                if (dtos != null)
                {
                    if (Directory.Exists(_notesDir))
                        foreach (var f in Directory.GetFiles(_notesDir, "*.json"))
                            try { File.Delete(f); } catch { }
                    Directory.CreateDirectory(_notesDir);
                    foreach (var d in dtos)
                    {
                        WriteAtomic(Path.Combine(_notesDir, $"{d.Id}.json"),
                            JsonSerializer.Serialize(d, NsJsonContext.Default.StickyNoteDto));
                    }
                    nc = dtos.Count;
                }
            }

            if (tasksArr != null)
            {
                var dtos = JsonSerializer.Deserialize(tasksArr, NsJsonContext.Default.ListTaskItemDto);
                if (dtos != null)
                {
                    if (Directory.Exists(_tasksDir))
                        foreach (var f in Directory.GetFiles(_tasksDir, "*.json"))
                            try { File.Delete(f); } catch { }
                    Directory.CreateDirectory(_tasksDir);
                    foreach (var d in dtos)
                    {
                        WriteAtomic(Path.Combine(_tasksDir, $"{d.Id}.json"),
                            JsonSerializer.Serialize(d, NsJsonContext.Default.TaskItemDto));
                    }
                    tc = dtos.Count;
                }
            }

            string exported = bundle["exportedAt"]?.GetValue<string>() ?? "unknown";
            return $"{cc} clipboard, {nc} notes, {tc} tasks (exported {exported})";
        }

        // ── Data management ───────────────────────────────────────────────────
        public static void ClearAllData()
        {
            if (_frozen) return;
            foreach (var dir in new[] { _clipsDir, _notesDir, _tasksDir })
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

            // Also nuke legacy files if present
            foreach (var f in new[] { _legacyClipFile, _legacyNotesFile, _legacyTasksFile })
                if (File.Exists(f)) try { File.Delete(f); } catch { }

            // Delete image captures
            string imgDir = Path.Combine(_dir, "clipimages");
            if (Directory.Exists(imgDir))
                Directory.Delete(imgDir, recursive: true);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static void WriteAtomic(string path, string content) =>
            WriteAtomicBytes(path, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray());

        private static void WriteAtomicBytes(string path, byte[] bytes)
        {
            if (_frozen) return;
            string tmp = path + ".tmp";
            // WriteThrough + Flush(true): data must reach disk before the rename, otherwise
            // NTFS can commit the Move first and a power cut leaves a zero-length file.
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }

        /// <summary>Writes one clip in the requested form and removes the other form's file.</summary>
        private static void WriteClipFile(int id, string json, bool encrypt)
        {
            if (_frozen) return;
            string plain = Path.Combine(_clipsDir, $"{id}.json");
            string enc   = plain + Core.ClipVault.EncSuffix;
            if (encrypt)
            {
                WriteAtomicBytes(enc, Core.ClipVault.Protect(Encoding.UTF8.GetBytes(json)));
                if (File.Exists(plain)) File.Delete(plain);
            }
            else
            {
                WriteAtomic(plain, json);
                if (File.Exists(enc)) File.Delete(enc);
            }
        }

        /// <summary>
        /// Brings every clip file (and its image) into the form selected by
        /// EncryptClipboardAtRest. Runs at load, so a toggle change applies to existing
        /// history on next start; new clips use the current setting immediately.
        /// </summary>
        private static void SyncClipEncryption(bool encrypt)
        {
            if (_frozen || !Directory.Exists(_clipsDir)) return;
            string wrongPattern = encrypt ? "*.json" : "*.json" + Core.ClipVault.EncSuffix;
            int converted = 0;
            foreach (var file in Directory.GetFiles(_clipsDir, wrongPattern))
            {
                try
                {
                    var dto = JsonSerializer.Deserialize(Core.ClipVault.ReadText(file), NsJsonContext.Default.ClipboardItemDto);
                    if (dto == null) continue;
                    if (dto.IsImage && !string.IsNullOrEmpty(dto.ImagePath))
                    {
                        string abs = Path.IsPathRooted(dto.ImagePath) ? dto.ImagePath : Path.Combine(_dir, dto.ImagePath);
                        string moved = Core.ClipVault.ConvertImage(abs, encrypt);
                        dto.ImagePath = Path.Combine("clipimages", Path.GetFileName(moved));
                    }
                    WriteClipFile(dto.Id, JsonSerializer.Serialize(dto, NsJsonContext.Default.ClipboardItemDto), encrypt);
                    converted++;
                }
                catch (Exception ex) { AppLog.Error($"Clip encryption sync {Path.GetFileName(file)}", ex); }
            }
            if (converted > 0) AppLog.Warn($"Clip encryption sync: {converted} file(s) → {(encrypt ? "encrypted" : "plain")}.");
        }

        private static DateTime TryParseDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d))
                return d;
            return DateTime.MinValue;
        }
    }
}
