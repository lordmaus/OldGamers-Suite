using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace NoteStation
{
    // ══════════════════════════════════════════════════════════════════
    //  DTO — shape that STJ serialises; mirrors AppPrefs field by field
    // ══════════════════════════════════════════════════════════════════
    public class AppPrefsDto
    {
        // Window
        public double WindowLeft   { get; set; } = double.NaN;
        public double WindowTop    { get; set; } = double.NaN;
        public double WindowWidth  { get; set; } = 960;
        public double WindowHeight { get; set; } = 760;
        public bool   Maximized    { get; set; }

        // Theme
        public string SelectedTheme { get; set; } = "Dark";

        // UI scale
        public double UiScale { get; set; } = 1.0;

        // Active tab ("Clipboard" | "Notes" | "Tasks")
        public string ActiveTab { get; set; } = "Clipboard";

        // Clipboard
        public int  ClipboardMaxHistory   { get; set; } = 50;
        public bool ClipboardAutoCapture  { get; set; } = true;
        public bool ClipboardDedupeConsecutive { get; set; } = true;
        public bool AutoPasteOnOverlayClick { get; set; } = false;
        /// <summary>Opt-in DPAPI (CurrentUser) encryption of clip JSON + images on disk.</summary>
        public bool EncryptClipboardAtRest { get; set; } = false;

        // Mouse button assignments
        // 0=None,1=XButton1,2=XButton2,3=ScrollUp,4=ScrollDown
        public int MousePreviousClip { get; set; } = 1;   // XButton1 = cycle back
        public int MouseNextClip     { get; set; } = 0;   // XButton2 = cycle forward (off by default)
        public bool MouseScrollCycle { get; set; } = false;
        public bool MouseScrollRequiresModifier { get; set; } = true;

        // Hotkeys
        public bool   HotkeysEnabled      { get; set; } = true;
        public string HotkeyPrevClip      { get; set; } = "";
        public string HotkeyNextClip      { get; set; } = "";
        public string HotkeyPasteClip     { get; set; } = "";
        public string HotkeyNewNote       { get; set; } = "Ctrl+Shift+N";
        public string HotkeyNewTask       { get; set; } = "Ctrl+N";
        public string HotkeySettings      { get; set; } = "Alt+S";
        public string HotkeyTheme         { get; set; } = "Alt+T";
        public string HotkeyAbout         { get; set; } = "Alt+A";

        // Tasks
        public bool ShowCompletedTasks    { get; set; } = false;
        public int  AutoArchiveDays       { get; set; } = 0;

        // TimerStudio path (for push-to-timer feature)
        public string TimerStudioTimersPath { get; set; } = "";

        // Startup
        public bool StartWithWindows      { get; set; } = false;
        public bool StartMinimized        { get; set; } = false;

        // Show desktop overlays automatically when the app starts
        public bool ShowClipboardOverlayAtStartup { get; set; } = false;
        public bool ShowTaskOverlayAtStartup      { get; set; } = false;
        public bool ShowNotesSidebarAtStartup     { get; set; } = false;
        public string NotesSidebarEdge            { get; set; } = "Right";
        /// <summary>Vertical placement along that edge: "Top" | "Middle" | "Bottom".</summary>
        public string NotesSidebarPosition        { get; set; } = "Top";
        /// <summary>True when the sidebar is free-floating (right-click → Undock) instead of
        /// pinned to NotesSidebarEdge — NotesSidebarFloatLeft/Top then hold its position.</summary>
        public bool NotesSidebarUndocked          { get; set; } = false;
        public double NotesSidebarFloatLeft       { get; set; } = double.NaN;
        public double NotesSidebarFloatTop        { get; set; } = double.NaN;
        // Default is deliberately low — the sidebar should read as "a few notes stuck to the
        // edge of the screen", not a solid bar. Tabs themselves stay fully opaque either way
        // (see NotesSidebarWindow.SetBarOpacity); this only fades the chrome behind them.
        public double NotesSidebarBarOpacity      { get; set; } = 0.25;
        public double NotesSidebarWidth           { get; set; } = 52.0;
        public bool NotesSidebarShowAddButton     { get; set; } = true;

        // Tray
        public bool MinimizeToTray        { get; set; } = false;
        public bool CloseToTray           { get; set; } = false;

        /// <summary>What the ✕ close button does: "Exit" | "Taskbar" | "Tray".</summary>
        public string CloseButtonAction   { get; set; } = "Exit";

        // Global show/hide hotkey (e.g. "Ctrl+Shift+Space")
        public string HotkeyShowHide      { get; set; } = "Ctrl+Shift+Space";

        // Categories
        public List<string>               Categories     { get; set; } = new List<string>();
        public Dictionary<string, string> CategoryColors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Font sizes (per-tab content + folder panel)
        public double ClipFontSize   { get; set; } = 13;
        public double NoteFontSize   { get; set; } = 13;
        public double TaskFontSize   { get; set; } = 13;
        public double FolderFontSize { get; set; } = 12;

        // App-wide font family (low-vision support: OpenDyslexic, Atkinson Hyperlegible, Verdana, etc.)
        public string AppFontFamily  { get; set; } = "Segoe UI";

        // Category accent bar width in folder tree (0 = off, 1–8 px)
        public double FolderAccentBarWidth { get; set; } = 3;

        // Folders
        public List<AppFolderDto> ClipboardFolders { get; set; } = new List<AppFolderDto>();
        public List<AppFolderDto> NoteFolders      { get; set; } = new List<AppFolderDto>();
        public List<AppFolderDto> TaskFolders      { get; set; } = new List<AppFolderDto>();

        // Last-used sticky note colors
        public string LastNoteBackground { get; set; } = "#2A2A2A";
        public string LastNoteTextColor  { get; set; } = "#FFFFFF";

        // WCAG 2.2.3/2.2.1: undo-toast lifetime in seconds; 0 = until dismissed
        public int UndoToastSeconds { get; set; } = 15;

        // Clip overlay geometry + appearance
        public double ClipOverlayLeft     { get; set; } = 60;
        public double ClipOverlayTop      { get; set; } = 60;
        public double ClipOverlayWidth    { get; set; } = 300;
        public double ClipOverlayHeight   { get; set; } = 420;
        public double ClipOverlayOpacity  { get; set; } = 0.9;
        public double ClipOverlayFontSize { get; set; } = 24;
        // Task overlay geometry + appearance
        public double TaskOverlayLeft     { get; set; } = 380;
        public double TaskOverlayTop      { get; set; } = 60;
        public double TaskOverlayWidth    { get; set; } = 300;
        public double TaskOverlayHeight   { get; set; } = 420;
        public double TaskOverlayOpacity  { get; set; } = 0.9;
        public double TaskOverlayFontSize { get; set; } = 24;

        // Security
        public string MasterPassphraseHash { get; set; } = "";

        // Folder panel widths
        public double ClipFolderPanelWidth  { get; set; } = 180;
        public double NoteFolderPanelWidth  { get; set; } = 180;
        public double TaskFolderPanelWidth  { get; set; } = 180;

        // Clip expiry (0 = off, N = auto-delete unpinned clips older than N days)
        public int ClipExpiryDays { get; set; } = 0;

        // UI density (0=Comfortable, 1=Compact, 2=Spacious)
        public int DensityMode { get; set; } = 0;

        // Notes view (0=List, 1=Grid)
        public int NoteViewMode { get; set; } = 0;

        // Quick Capture + Paste-from-keyboard hotkeys
        public string HotkeyQuickCapture       { get; set; } = "Ctrl+Alt+N";
        public string HotkeyPasteFromKeyboard  { get; set; } = "Ctrl+Shift+V";

        // Pomodoro
        public int PomodoroMinutes      { get; set; } = 25;
        public int PomodoroBreakMinutes { get; set; } = 5;

        // Saved views
        public List<SavedViewDto> SavedViews { get; set; } = new List<SavedViewDto>();

        // Recently Deleted retention (days; 0 = keep forever)
        public int DeletedRetentionDays { get; set; } = 30;

        // Remembered last size for resizable dialogs that don't have their own
        // dedicated geometry fields. Key = dialog name, value = "W,H".
        public Dictionary<string, string> DialogSizes { get; set; } = new Dictionary<string, string>();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Folder DTO
    // ══════════════════════════════════════════════════════════════════
    public class AppFolderDto
    {
        public int    Id        { get; set; }
        public int?   ParentId  { get; set; }
        public string Name      { get; set; } = "Folder";
        public string Category  { get; set; } = "";
        public int    SortOrder { get; set; }
        public string Tab       { get; set; } = "";
        public string DefaultNoteBackground { get; set; } = "";
        public string Icon        { get; set; } = "";
        public string AccentColor { get; set; } = "";
        public bool   IsFavorite  { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════
    //  STJ source generation context
    // ══════════════════════════════════════════════════════════════════
    [JsonSerializable(typeof(AppPrefsDto))]
    [JsonSerializable(typeof(List<ClipboardItemDto>))]
    [JsonSerializable(typeof(List<StickyNoteDto>))]
    [JsonSerializable(typeof(List<TaskItemDto>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(List<AppFolderDto>))]
    [JsonSerializable(typeof(List<TemplateItemDto>))]
    [JsonSerializable(typeof(List<SubTaskItemDto>))]
    [JsonSerializable(typeof(List<SavedViewDto>))]
    [JsonSerializable(typeof(List<int>))]
    [JsonSerializable(typeof(ClipboardItemDto))]
    [JsonSerializable(typeof(StickyNoteDto))]
    [JsonSerializable(typeof(TaskItemDto))]
    internal partial class NsJsonContext : JsonSerializerContext { }

    // ══════════════════════════════════════════════════════════════════
    //  Data-layer DTOs (for persistence only)
    // ══════════════════════════════════════════════════════════════════
    public class ClipboardItemDto
    {
        public int    Id         { get; set; }
        public string Title      { get; set; } = "";
        public string Text       { get; set; } = "";
        public string Preview    { get; set; } = "";
        public string CapturedAt { get; set; } = "";
        public bool   IsPinned   { get; set; }
        public string Tag        { get; set; } = "";
        public string Category   { get; set; } = "";
        public int?   FolderId   { get; set; }
        public bool   IsImage    { get; set; }
        public string ImagePath  { get; set; } = "";
        public string SourceApp  { get; set; } = "";
        public int LinkedTaskId { get; set; } = 0;
        public List<string> ParsedTags { get; set; } = new List<string>();
        public bool     IsDeleted { get; set; } = false;
        public string   DeletedAt { get; set; } = "";
        public bool     HiddenFromRecent { get; set; } = false;
        public double?  EditWidth  { get; set; }
        public double?  EditHeight { get; set; }
    }

    public class StickyNoteDto
    {
        public int    Id              { get; set; }
        public string Title           { get; set; } = "";
        public string Content         { get; set; } = "";
        public bool   IsVisible       { get; set; } = true;
        public bool   ShowOnStartup   { get; set; } = false;
        public double Left            { get; set; } = double.NaN;
        public double Top             { get; set; } = double.NaN;
        public double Width           { get; set; } = 300;
        public double Height          { get; set; } = 200;
        public string BackgroundColor { get; set; } = "#2A2A2A";
        public string TextColor       { get; set; } = "#FFFFFF";
        public string FontFamily      { get; set; } = "Segoe UI";
        public double FontSize        { get; set; } = 14;
        public string FontWeight      { get; set; } = "Normal";
        public string FontStyle       { get; set; } = "Normal";
        public string Category        { get; set; } = "";
        public int?   FolderId        { get; set; }
        public bool   IsPinned        { get; set; }
        public bool   IsTopmost       { get; set; } = true;
        public double NoteOpacity     { get; set; } = 1.0;
        public string CreatedAt       { get; set; } = "";
        public string ModifiedAt      { get; set; } = "";
        public string PinHash            { get; set; } = "";
        public int    AutoBlurMinutes    { get; set; } = 0;
        public bool   ExcludeFromAutoBlur { get; set; } = false;
        public double TitleFontSize      { get; set; } = 14;
        public string ReminderAt         { get; set; } = "";
        public string AttachedWindowTitle { get; set; } = "";
        public string NoteTexture { get; set; } = "";
        public bool   HideTitleBar { get; set; } = false;
        public bool   IsFolded { get; set; } = false;
        public bool   IsDeleted  { get; set; } = false;
        public string DeletedAt  { get; set; } = "";
        public bool   HiddenFromRecent { get; set; } = false;
        public bool   ShowInSidebar { get; set; } = false;
    }

    public class SubTaskItemDto
    {
        public string Text   { get; set; } = "";
        public bool   IsDone { get; set; }
    }

    public class TemplateItemDto
    {
        public int    Id        { get; set; }
        public string Title     { get; set; } = "";
        public string Text      { get; set; } = "";
        public string Category  { get; set; } = "";
        public int?   FolderId  { get; set; }
        public int    SortOrder { get; set; }
        public string CreatedAt  { get; set; } = "";
        public string ModifiedAt { get; set; } = "";
    }

    public class SavedViewDto
    {
        public int    Id               { get; set; }
        public string Name             { get; set; } = "";
        public string FilterExpression { get; set; } = "";
        public string Scope            { get; set; } = "All";
        public string CreatedAt        { get; set; } = "";
    }

    public class TaskItemDto
    {
        public int    Id           { get; set; }
        public int    SortOrder    { get; set; }
        public string Title        { get; set; } = "";
        public string Notes        { get; set; } = "";
        public bool   IsCompleted  { get; set; }
        public bool   IsStarred    { get; set; }
        public bool   HasDueDate   { get; set; }
        public string DueDate      { get; set; } = "";
        public bool   HasDueTime   { get; set; }
        public string DueTime      { get; set; } = "";
        public bool   PushedToTimer { get; set; }
        public int    TimerMinutes  { get; set; }
        public string AccentColor   { get; set; } = "";
        public string Category      { get; set; } = "";
        public int?   FolderId      { get; set; }
        public string CreatedAt     { get; set; } = "";
        public string ModifiedAt    { get; set; } = "";
        public string CompletedAt   { get; set; } = "";
        public int    Priority      { get; set; } = 0;
        public int    PriorityRank  { get; set; } = 0;
        public bool   IsArchived    { get; set; }
        public string RecurrenceRule { get; set; } = "None";
        public bool   IsHabit             { get; set; } = false;
        public int    HabitStreak         { get; set; } = 0;
        public string LastHabitCompleted  { get; set; } = "";
        public List<int> LinkedClipIds    { get; set; } = new List<int>();
        public List<int> DependsOnIds { get; set; } = new List<int>();
        public List<int> BlocksIds    { get; set; } = new List<int>();
        public List<SubTaskItemDto> SubTasks { get; set; } = new List<SubTaskItemDto>();
        public bool   IsDeleted { get; set; } = false;
        public string DeletedAt { get; set; } = "";
        public bool   HiddenFromRecent { get; set; } = false;
    }

    // ══════════════════════════════════════════════════════════════════
    //  AppPrefs — singleton, wraps AppPrefsDto, handles load/save
    // ══════════════════════════════════════════════════════════════════
    public class AppPrefs
    {
        public static readonly AppPrefs Instance = new AppPrefs();

        private static readonly string _dir  = App.AppDataDir;
        private static readonly string _file = Path.Combine(_dir, "prefs.json");

        // ── Mirror all DTO properties here ────────────────────────────────────
        public double WindowLeft   { get; set; } = double.NaN;
        public double WindowTop    { get; set; } = double.NaN;
        public double WindowWidth  { get; set; } = 960;
        public double WindowHeight { get; set; } = 760;
        public bool   Maximized    { get; set; }
        public string SelectedTheme { get; set; } = "Dark";
        public double UiScale      { get; set; } = 1.0;
        public string ActiveTab    { get; set; } = "Clipboard";
        public int  ClipboardMaxHistory   { get; set; } = 50;
        public bool ClipboardAutoCapture  { get; set; } = true;
        public bool ClipboardDedupeConsecutive { get; set; } = true;
        public bool AutoPasteOnOverlayClick { get; set; } = false;
        /// <summary>Opt-in DPAPI (CurrentUser) encryption of clip JSON + images on disk.</summary>
        public bool EncryptClipboardAtRest { get; set; } = false;
        public int  MousePreviousClip { get; set; } = 1;
        public int  MouseNextClip     { get; set; } = 0;
        public bool MouseScrollCycle  { get; set; } = false;
        public bool MouseScrollRequiresModifier { get; set; } = true;
        public bool   HotkeysEnabled  { get; set; } = true;
        public string HotkeyPrevClip  { get; set; } = "";
        public string HotkeyNextClip  { get; set; } = "";
        public string HotkeyPasteClip { get; set; } = "";
        public string HotkeyNewNote   { get; set; } = "Ctrl+Shift+N";
        public string HotkeyNewTask   { get; set; } = "Ctrl+N";
        public string HotkeySettings  { get; set; } = "Alt+S";
        public string HotkeyTheme     { get; set; } = "Alt+T";
        public string HotkeyAbout     { get; set; } = "Alt+A";
        public bool ShowCompletedTasks  { get; set; } = false;
        /// <summary>Auto-archive completed tasks after N days. 0 = disabled.</summary>
        public int  AutoArchiveDays     { get; set; } = 0;
        public string TimerStudioTimersPath { get; set; } = "";
        public bool StartWithWindows  { get; set; } = false;
        public bool StartMinimized    { get; set; } = false;
        public bool ShowClipboardOverlayAtStartup { get; set; } = false;
        public bool ShowTaskOverlayAtStartup      { get; set; } = false;
        public bool ShowNotesSidebarAtStartup     { get; set; } = false;
        public string NotesSidebarEdge            { get; set; } = "Right";
        public string NotesSidebarPosition        { get; set; } = "Top";
        public bool NotesSidebarUndocked          { get; set; } = false;
        public double NotesSidebarFloatLeft       { get; set; } = double.NaN;
        public double NotesSidebarFloatTop        { get; set; } = double.NaN;
        public double NotesSidebarBarOpacity      { get; set; } = 0.25;
        public double NotesSidebarWidth           { get; set; } = 52.0;
        public bool NotesSidebarShowAddButton     { get; set; } = true;
        public bool MinimizeToTray    { get; set; } = false;
        public bool CloseToTray       { get; set; } = false;
        public string CloseButtonAction { get; set; } = "";
        public string HotkeyShowHide  { get; set; } = "Ctrl+Shift+Space";

        // ── Font sizes ────────────────────────────────────────────────────────
        public double ClipFontSize   { get; set; } = 13;
        public double NoteFontSize   { get; set; } = 13;
        public double TaskFontSize   { get; set; } = 13;
        public double FolderFontSize { get; set; } = 12;
        public double FolderAccentBarWidth { get; set; } = 3;

        // App-wide font family
        public string AppFontFamily  { get; set; } = "Segoe UI";

        // ── Categories ────────────────────────────────────────────────────────
        public List<string>               Categories     { get; set; } = new List<string>();
        public Dictionary<string, string> CategoryColors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ── Folders ───────────────────────────────────────────────────────────
        public List<AppFolder> ClipboardFolders { get; set; } = new List<AppFolder>();
        public List<AppFolder> NoteFolders      { get; set; } = new List<AppFolder>();
        public List<AppFolder> TaskFolders      { get; set; } = new List<AppFolder>();

        // ── Last-used sticky note colors ──────────────────────────────────────
        public string LastNoteBackground { get; set; } = "#2A2A2A";
        public string LastNoteTextColor  { get; set; } = "#FFFFFF";

        /// <summary>Undo-toast lifetime in seconds (task delete, note text delete). 0 = until dismissed.
        /// The countdown pauses while the toast is hovered or has keyboard focus (WCAG 2.2.1/2.2.3).</summary>
        public int UndoToastSeconds { get; set; } = 15;

        // ── Overlay geometry + appearance ─────────────────────────────────────
        public double ClipOverlayLeft     { get; set; } = 60;
        public double ClipOverlayTop      { get; set; } = 60;
        public double ClipOverlayWidth    { get; set; } = 300;
        public double ClipOverlayHeight   { get; set; } = 420;
        public double ClipOverlayOpacity  { get; set; } = 0.9;
        public double ClipOverlayFontSize { get; set; } = 24;
        public double TaskOverlayLeft     { get; set; } = 380;
        public double TaskOverlayTop      { get; set; } = 60;
        public double TaskOverlayWidth    { get; set; } = 300;
        public double TaskOverlayHeight   { get; set; } = 420;
        public double TaskOverlayOpacity  { get; set; } = 0.9;
        public double TaskOverlayFontSize { get; set; } = 24;

        // ── Security ──────────────────────────────────────────────────────────
        public string MasterPassphraseHash { get; set; } = "";

        // ── Folder panel widths ───────────────────────────────────────────────
        public double ClipFolderPanelWidth  { get; set; } = 180;
        public double NoteFolderPanelWidth  { get; set; } = 180;
        public double TaskFolderPanelWidth  { get; set; } = 180;

        // ── Clip expiry ───────────────────────────────────────────────────────
        /// <summary>Auto-delete unpinned clips older than N days. 0 = off.</summary>
        public int ClipExpiryDays { get; set; } = 0;

        // ── UI density ────────────────────────────────────────────────────────
        /// <summary>0=Comfortable (default), 1=Compact, 2=Spacious.</summary>
        public int DensityMode { get; set; } = 0;

        // ── Notes view mode ───────────────────────────────────────────────────
        /// <summary>0=List, 1=Grid.</summary>
        public int NoteViewMode { get; set; } = 0;

        // ── Quick Capture + Paste-from-keyboard hotkeys ───────────────────────
        public string HotkeyQuickCapture      { get; set; } = "Ctrl+Alt+N";
        public string HotkeyPasteFromKeyboard { get; set; } = "Ctrl+Shift+V";

        // ── Pomodoro ──────────────────────────────────────────────────────────
        public int PomodoroMinutes      { get; set; } = 25;
        public int PomodoroBreakMinutes { get; set; } = 5;

        // ── Saved views ───────────────────────────────────────────────────────
        public List<SavedView> SavedViews { get; set; } = new List<SavedView>();

        // ── Recently Deleted retention ────────────────────────────────────────
        /// <summary>Days to keep soft-deleted items before auto-purge. 0 = keep forever.</summary>
        public int DeletedRetentionDays { get; set; } = 30;

        // ── Remembered dialog sizes ───────────────────────────────────────────
        /// <summary>Key = dialog name (see WindowSizeMemory), value = "W,H".</summary>
        public Dictionary<string, string> DialogSizes { get; set; } = new Dictionary<string, string>();

        // ── Deferred save (debounced, 400 ms, off-UI-thread) ─────────────────
        private System.Threading.Timer _saveTimer;

        /// <summary>
        /// Schedules a save 400 ms from now, coalescing rapid successive calls.
        /// Safe to call from the UI thread — actual write runs on the ThreadPool.
        /// For immediate (shutdown-path) saves use <see cref="Save"/> directly.
        /// </summary>
        public void DeferredSave()
        {
            _saveTimer ??= new System.Threading.Timer(
                _ => Save(), null,
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);
            // Restart the 400 ms countdown
            _saveTimer.Change(400, System.Threading.Timeout.Infinite);
        }

        // ── Save ──────────────────────────────────────────────────────────────
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(_dir);
                var dto = ToDto();
                var ctx  = new NsJsonContext(new JsonSerializerOptions { WriteIndented = true });
                string json = JsonSerializer.Serialize(dto, ctx.AppPrefsDto);
                // Pretty-print via standard options since we use source gen
                string tmp = _file + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                File.Move(tmp, _file, overwrite: true);
            }
            catch (Exception ex) { Debug.WriteLine("[NoteStation] Prefs save: " + ex.Message); }
        }

        // ── Load ──────────────────────────────────────────────────────────────
        public void Load()
        {
            if (!File.Exists(_file)) return;
            try
            {
                string json = File.ReadAllText(_file, Encoding.UTF8);
                var dto = JsonSerializer.Deserialize(json, NsJsonContext.Default.AppPrefsDto);
                if (dto != null) FromDto(dto);
            }
            catch (Exception ex) { Debug.WriteLine("[NoteStation] Prefs load: " + ex.Message); }
        }

        // ── Window helpers ────────────────────────────────────────────────────
        public void ApplyTo(Window w)
        {
            w.Width  = WindowWidth;
            w.Height = WindowHeight;
            if (!double.IsNaN(WindowLeft) && !double.IsNaN(WindowTop))
            {
                double left = Math.Max(SystemParameters.VirtualScreenLeft,
                              Math.Min(WindowLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth  - 100));
                double top  = Math.Max(SystemParameters.VirtualScreenTop,
                              Math.Min(WindowTop,  SystemParameters.VirtualScreenTop  + SystemParameters.VirtualScreenHeight - 40));
                w.Left = left;
                w.Top  = top;
            }
            else { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; }
            if (Maximized) w.WindowState = WindowState.Maximized;
        }

        public void SnapshotFrom(Window w)
        {
            Maximized = w.WindowState == WindowState.Maximized;
            if (!Maximized) { WindowLeft = w.Left; WindowTop = w.Top; WindowWidth = w.Width; WindowHeight = w.Height; }
        }

        // ── DTO mapping ───────────────────────────────────────────────────────
        private AppPrefsDto ToDto() => new AppPrefsDto
        {
            WindowLeft   = WindowLeft,   WindowTop    = WindowTop,
            WindowWidth  = WindowWidth,  WindowHeight = WindowHeight,
            Maximized    = Maximized,    SelectedTheme = SelectedTheme,
            UiScale      = UiScale,      ActiveTab     = ActiveTab,
            ClipboardMaxHistory          = ClipboardMaxHistory,
            ClipboardAutoCapture         = ClipboardAutoCapture,
            ClipboardDedupeConsecutive   = ClipboardDedupeConsecutive,
            AutoPasteOnOverlayClick      = AutoPasteOnOverlayClick,
            EncryptClipboardAtRest       = EncryptClipboardAtRest,
            MousePreviousClip            = MousePreviousClip,
            MouseNextClip                = MouseNextClip,
            MouseScrollCycle             = MouseScrollCycle,
            MouseScrollRequiresModifier  = MouseScrollRequiresModifier,
            HotkeysEnabled  = HotkeysEnabled,
            HotkeyPrevClip  = HotkeyPrevClip,  HotkeyNextClip  = HotkeyNextClip,
            HotkeyPasteClip = HotkeyPasteClip, HotkeyNewNote   = HotkeyNewNote,
            HotkeyNewTask   = HotkeyNewTask,    HotkeySettings  = HotkeySettings,
            HotkeyTheme     = HotkeyTheme,      HotkeyAbout     = HotkeyAbout,
            ShowCompletedTasks   = ShowCompletedTasks,
            AutoArchiveDays      = AutoArchiveDays,
            TimerStudioTimersPath = TimerStudioTimersPath,
            StartWithWindows = StartWithWindows, StartMinimized = StartMinimized,
            ShowClipboardOverlayAtStartup = ShowClipboardOverlayAtStartup,
            ShowTaskOverlayAtStartup      = ShowTaskOverlayAtStartup,
            ShowNotesSidebarAtStartup     = ShowNotesSidebarAtStartup,
            NotesSidebarEdge              = NotesSidebarEdge,
            NotesSidebarPosition          = NotesSidebarPosition,
            NotesSidebarUndocked          = NotesSidebarUndocked,
            NotesSidebarFloatLeft         = NotesSidebarFloatLeft,
            NotesSidebarFloatTop          = NotesSidebarFloatTop,
            NotesSidebarBarOpacity        = NotesSidebarBarOpacity,
            NotesSidebarWidth             = NotesSidebarWidth,
            NotesSidebarShowAddButton     = NotesSidebarShowAddButton,
            MinimizeToTray   = MinimizeToTray,   CloseToTray    = CloseToTray,
            CloseButtonAction = CloseButtonAction,
            HotkeyShowHide   = HotkeyShowHide,
            ClipFontSize   = ClipFontSize,   NoteFontSize   = NoteFontSize,
            TaskFontSize   = TaskFontSize,   FolderFontSize = FolderFontSize,
            FolderAccentBarWidth = FolderAccentBarWidth,
            AppFontFamily  = AppFontFamily,
            Categories     = new List<string>(Categories),
            CategoryColors = new Dictionary<string, string>(CategoryColors, StringComparer.OrdinalIgnoreCase),
            ClipboardFolders = FoldersToDto(ClipboardFolders),
            NoteFolders      = FoldersToDto(NoteFolders),
            TaskFolders      = FoldersToDto(TaskFolders),
            LastNoteBackground = LastNoteBackground,
            LastNoteTextColor  = LastNoteTextColor,
            UndoToastSeconds   = UndoToastSeconds,
            ClipOverlayLeft     = ClipOverlayLeft,
            ClipOverlayTop      = ClipOverlayTop,
            ClipOverlayWidth    = ClipOverlayWidth,
            ClipOverlayHeight   = ClipOverlayHeight,
            ClipOverlayOpacity  = ClipOverlayOpacity,
            ClipOverlayFontSize = ClipOverlayFontSize,
            TaskOverlayLeft     = TaskOverlayLeft,
            TaskOverlayTop      = TaskOverlayTop,
            TaskOverlayWidth    = TaskOverlayWidth,
            TaskOverlayHeight   = TaskOverlayHeight,
            TaskOverlayOpacity  = TaskOverlayOpacity,
            TaskOverlayFontSize = TaskOverlayFontSize,
            MasterPassphraseHash = MasterPassphraseHash,
            ClipFolderPanelWidth  = ClipFolderPanelWidth,
            NoteFolderPanelWidth  = NoteFolderPanelWidth,
            TaskFolderPanelWidth  = TaskFolderPanelWidth,
            ClipExpiryDays        = ClipExpiryDays,
            DensityMode           = DensityMode,
            NoteViewMode          = NoteViewMode,
            HotkeyQuickCapture      = HotkeyQuickCapture,
            HotkeyPasteFromKeyboard = HotkeyPasteFromKeyboard,
            PomodoroMinutes      = PomodoroMinutes,
            PomodoroBreakMinutes = PomodoroBreakMinutes,
            SavedViews           = SavedViewsToDto(SavedViews),
            DeletedRetentionDays = DeletedRetentionDays,
            DialogSizes          = new Dictionary<string, string>(DialogSizes),
        };

        private void FromDto(AppPrefsDto d)
        {
            WindowLeft   = d.WindowLeft;   WindowTop    = d.WindowTop;
            WindowWidth  = d.WindowWidth;  WindowHeight = d.WindowHeight;
            Maximized    = d.Maximized;    SelectedTheme = d.SelectedTheme;
            UiScale      = d.UiScale;      ActiveTab     = d.ActiveTab;
            ClipboardMaxHistory          = d.ClipboardMaxHistory >= 10 && d.ClipboardMaxHistory <= 500 ? d.ClipboardMaxHistory : 50;
            ClipboardAutoCapture         = d.ClipboardAutoCapture;
            ClipboardDedupeConsecutive   = d.ClipboardDedupeConsecutive;
            AutoPasteOnOverlayClick      = d.AutoPasteOnOverlayClick;
            EncryptClipboardAtRest       = d.EncryptClipboardAtRest;
            MousePreviousClip            = d.MousePreviousClip;
            MouseNextClip                = d.MouseNextClip;
            MouseScrollCycle             = d.MouseScrollCycle;
            MouseScrollRequiresModifier  = d.MouseScrollRequiresModifier;
            HotkeysEnabled  = d.HotkeysEnabled;
            HotkeyPrevClip  = d.HotkeyPrevClip;  HotkeyNextClip  = d.HotkeyNextClip;
            HotkeyPasteClip = d.HotkeyPasteClip; HotkeyNewNote   = d.HotkeyNewNote;
            HotkeyNewTask   = d.HotkeyNewTask;    HotkeySettings  = d.HotkeySettings;
            HotkeyTheme     = d.HotkeyTheme;      HotkeyAbout     = d.HotkeyAbout;
            ShowCompletedTasks   = d.ShowCompletedTasks;
            AutoArchiveDays      = d.AutoArchiveDays;
            TimerStudioTimersPath = d.TimerStudioTimersPath;
            StartWithWindows = d.StartWithWindows; StartMinimized = d.StartMinimized;
            ShowClipboardOverlayAtStartup = d.ShowClipboardOverlayAtStartup;
            ShowTaskOverlayAtStartup      = d.ShowTaskOverlayAtStartup;
            ShowNotesSidebarAtStartup     = d.ShowNotesSidebarAtStartup;
            NotesSidebarEdge              = string.IsNullOrEmpty(d.NotesSidebarEdge) ? "Right" : d.NotesSidebarEdge;
            NotesSidebarPosition          = string.IsNullOrEmpty(d.NotesSidebarPosition) ? "Top" : d.NotesSidebarPosition;
            NotesSidebarUndocked          = d.NotesSidebarUndocked;
            NotesSidebarFloatLeft         = d.NotesSidebarFloatLeft;
            NotesSidebarFloatTop          = d.NotesSidebarFloatTop;
            NotesSidebarBarOpacity        = d.NotesSidebarBarOpacity >= 0.0 ? d.NotesSidebarBarOpacity : 0.25;
            NotesSidebarWidth             = d.NotesSidebarWidth >= 44.0 ? d.NotesSidebarWidth : 52.0;
            NotesSidebarShowAddButton     = d.NotesSidebarShowAddButton;
            MinimizeToTray   = d.MinimizeToTray;   CloseToTray    = d.CloseToTray;
            // Migrate the old CloseToTray bool when the new setting hasn't been chosen yet.
            CloseButtonAction = !string.IsNullOrEmpty(d.CloseButtonAction) ? d.CloseButtonAction
                                : (d.CloseToTray ? "Tray" : "Exit");
            HotkeyShowHide   = string.IsNullOrEmpty(d.HotkeyShowHide) ? "Ctrl+Shift+Space" : d.HotkeyShowHide;
            ClipFontSize   = d.ClipFontSize   > 0 ? Math.Min(d.ClipFontSize,   72) : 13;
            NoteFontSize   = d.NoteFontSize   > 0 ? Math.Min(d.NoteFontSize,   72) : 13;
            TaskFontSize   = d.TaskFontSize   > 0 ? Math.Min(d.TaskFontSize,   72) : 13;
            FolderFontSize = d.FolderFontSize > 0 ? Math.Min(d.FolderFontSize, 72) : 12;
            FolderAccentBarWidth = d.FolderAccentBarWidth >= 0 ? d.FolderAccentBarWidth : 3;
            AppFontFamily        = string.IsNullOrWhiteSpace(d.AppFontFamily) ? "Segoe UI" : d.AppFontFamily;
            Categories     = d.Categories  ?? new List<string>();
            CategoryColors = d.CategoryColors != null
                ? new Dictionary<string, string>(d.CategoryColors, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ClipboardFolders = FoldersFromDto(d.ClipboardFolders, "Clipboard");
            NoteFolders      = FoldersFromDto(d.NoteFolders,      "Notes");
            TaskFolders      = FoldersFromDto(d.TaskFolders,      "Tasks");
            LastNoteBackground = string.IsNullOrEmpty(d.LastNoteBackground) ? "#2A2A2A" : d.LastNoteBackground;
            LastNoteTextColor  = string.IsNullOrEmpty(d.LastNoteTextColor)  ? "#FFFFFF" : d.LastNoteTextColor;
            UndoToastSeconds    = d.UndoToastSeconds is >= 0 and <= 600 ? d.UndoToastSeconds : 15;
            ClipOverlayLeft     = d.ClipOverlayLeft;
            ClipOverlayTop      = d.ClipOverlayTop;
            ClipOverlayWidth    = d.ClipOverlayWidth    > 0   ? d.ClipOverlayWidth    : 300;
            ClipOverlayHeight   = d.ClipOverlayHeight   > 0   ? d.ClipOverlayHeight   : 420;
            ClipOverlayOpacity  = d.ClipOverlayOpacity  > 0   ? d.ClipOverlayOpacity  : 0.9;
            ClipOverlayFontSize = d.ClipOverlayFontSize >= 8  ? d.ClipOverlayFontSize : 24;
            TaskOverlayLeft     = d.TaskOverlayLeft;
            TaskOverlayTop      = d.TaskOverlayTop;
            TaskOverlayWidth    = d.TaskOverlayWidth    > 0   ? d.TaskOverlayWidth    : 300;
            TaskOverlayHeight   = d.TaskOverlayHeight   > 0   ? d.TaskOverlayHeight   : 420;
            TaskOverlayOpacity  = d.TaskOverlayOpacity  > 0   ? d.TaskOverlayOpacity  : 0.9;
            TaskOverlayFontSize = d.TaskOverlayFontSize >= 8  ? d.TaskOverlayFontSize : 24;
            MasterPassphraseHash = d.MasterPassphraseHash ?? "";
            ClipFolderPanelWidth  = d.ClipFolderPanelWidth  > 50 ? d.ClipFolderPanelWidth  : 180;
            NoteFolderPanelWidth  = d.NoteFolderPanelWidth  > 50 ? d.NoteFolderPanelWidth  : 180;
            TaskFolderPanelWidth  = d.TaskFolderPanelWidth  > 50 ? d.TaskFolderPanelWidth  : 180;
            ClipExpiryDays        = d.ClipExpiryDays >= 0 ? d.ClipExpiryDays : 0;
            DensityMode           = d.DensityMode >= 0 && d.DensityMode <= 2 ? d.DensityMode : 0;
            NoteViewMode          = d.NoteViewMode >= 0 && d.NoteViewMode <= 1 ? d.NoteViewMode : 0;
            HotkeyQuickCapture      = string.IsNullOrEmpty(d.HotkeyQuickCapture)      ? "Ctrl+Alt+N"    : d.HotkeyQuickCapture;
            HotkeyPasteFromKeyboard = string.IsNullOrEmpty(d.HotkeyPasteFromKeyboard) ? "Ctrl+Shift+V"  : d.HotkeyPasteFromKeyboard;
            PomodoroMinutes      = d.PomodoroMinutes      > 0  ? d.PomodoroMinutes      : 25;
            PomodoroBreakMinutes = d.PomodoroBreakMinutes >= 0 ? d.PomodoroBreakMinutes : 5;
            SavedViews           = SavedViewsFromDto(d.SavedViews);
            DeletedRetentionDays = d.DeletedRetentionDays >= 0 ? d.DeletedRetentionDays : 30;
            DialogSizes          = d.DialogSizes != null
                ? new Dictionary<string, string>(d.DialogSizes)
                : new Dictionary<string, string>();
        }

        // ── Folder helpers ────────────────────────────────────────────────────
        private static List<AppFolderDto> FoldersToDto(List<AppFolder> src)
        {
            var list = new List<AppFolderDto>();
            foreach (var f in src)
                list.Add(new AppFolderDto { Id = f.Id, ParentId = f.ParentId, Name = f.Name, Category = f.Category, SortOrder = f.SortOrder, Tab = f.Tab, DefaultNoteBackground = f.DefaultNoteBackground, Icon = f.Icon, AccentColor = f.AccentColor, IsFavorite = f.IsFavorite });
            return list;
        }

        private static List<AppFolder> FoldersFromDto(List<AppFolderDto> src, string tab)
        {
            var list = new List<AppFolder>();
            if (src == null) return list;
            foreach (var d in src)
                list.Add(new AppFolder { Id = d.Id, ParentId = d.ParentId, Name = d.Name, Category = d.Category, SortOrder = d.SortOrder, Tab = string.IsNullOrEmpty(d.Tab) ? tab : d.Tab, DefaultNoteBackground = d.DefaultNoteBackground ?? "", Icon = d.Icon ?? "", AccentColor = d.AccentColor ?? "", IsFavorite = d.IsFavorite });
            return list;
        }

        private static List<SavedViewDto> SavedViewsToDto(List<SavedView> src)
        {
            var list = new List<SavedViewDto>();
            if (src == null) return list;
            foreach (var v in src)
                list.Add(new SavedViewDto { Id = v.Id, Name = v.Name, FilterExpression = v.FilterExpression, Scope = v.Scope, CreatedAt = v.CreatedAt.ToString("o") });
            return list;
        }

        private static List<SavedView> SavedViewsFromDto(List<SavedViewDto> src)
        {
            var list = new List<SavedView>();
            if (src == null) return list;
            foreach (var d in src)
                list.Add(new SavedView { Id = d.Id, Name = d.Name, FilterExpression = d.FilterExpression ?? "", Scope = string.IsNullOrEmpty(d.Scope) ? "All" : d.Scope, CreatedAt = DateTime.TryParse(d.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.Now });
            return list;
        }
    }
}
