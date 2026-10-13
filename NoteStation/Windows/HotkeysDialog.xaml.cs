using System.Windows;

namespace NoteStation
{
    public partial class HotkeysDialog : Window
    {
        private static readonly string[] _mouseNames = { "None", "XButton1 (Back)", "XButton2 (Forward)" };

        public HotkeysDialog()
        {
            InitializeComponent();
            WindowSizeMemory.Attach(this, "HotkeysDialog");
            var p = AppPrefs.Instance;

            HkNewTask.Text  = string.IsNullOrEmpty(p.HotkeyNewTask)  ? "(not set)" : p.HotkeyNewTask;
            HkNewNote.Text  = string.IsNullOrEmpty(p.HotkeyNewNote)  ? "(not set)" : p.HotkeyNewNote;
            HkSettings.Text = string.IsNullOrEmpty(p.HotkeySettings) ? "(not set)" : p.HotkeySettings;
            HkTheme.Text    = string.IsNullOrEmpty(p.HotkeyTheme)    ? "(not set)" : p.HotkeyTheme;

            int pi = p.MousePreviousClip;
            int ni = p.MouseNextClip;
            MousePrev.Text   = pi >= 0 && pi < _mouseNames.Length ? _mouseNames[pi] : "None";
            MouseNext.Text   = ni >= 0 && ni < _mouseNames.Length ? _mouseNames[ni] : "None";
            MouseScroll.Text = p.MouseScrollCycle
                ? (p.MouseScrollRequiresModifier ? "Ctrl+Alt+Scroll" : "Scroll wheel")
                : "Disabled";
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
