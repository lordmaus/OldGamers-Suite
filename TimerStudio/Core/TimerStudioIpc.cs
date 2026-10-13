namespace TimerStudio
{
    /// <summary>
    /// Shared IPC contract for NoteStation's "push task to TimerStudio" feature.
    /// TimerStudio and NoteStation are separate processes/assemblies that both read
    /// and write %AppData%\TimerStudio\timers.json, so this constant is intentionally
    /// mirrored in NoteStation's own Core/TimerStudioIpc.cs (only the namespace differs) — a
    /// named Mutex only provides mutual exclusion if every writer uses the identical name.
    /// Added 2026-09-07 (NoteStation Full Review, Feature Gaps: "real IPC contract
    /// (lock + schema version) for the TimerStudio push instead of an out-of-process
    /// raw JSON read-modify-write").
    /// </summary>
    internal static class TimerStudioIpc
    {
        /// <summary>
        /// Mutex name guarding read-modify-write access to timers.json.
        /// "Local\" (per logon session): timers.json is a per-user file, and a Global\ name
        /// let another session's user pre-create/squat the mutex so pushes always timed out.
        /// Both apps must ship this name together (2026-10-12, NS-07).
        /// </summary>
        public const string TimersJsonMutexName = @"Local\NoteStation_TimerStudio_TimersJson";

        /// <summary>
        /// Version of the calendar-event entry shape NoteStation writes via its
        /// TimerPushDialog push (id/schemaVersion/name/mode/targetDateTime/notes/
        /// alertFontSize/alertVolume/snoozeDurationMinutes). Bump this if that shape
        /// changes in a way TimerStudio's reader needs to detect.
        /// </summary>
        public const int PushContractVersion = 1;
    }
}
