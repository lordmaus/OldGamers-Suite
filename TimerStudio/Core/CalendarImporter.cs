using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TimerStudio
{
    /// <summary>
    /// Represents a single calendar event parsed from an ICS file or Outlook.
    /// </summary>
    public class CalendarEvent
    {
        public string   Summary      { get; set; }
        public string   Description  { get; set; }
        public string   Location     { get; set; }
        public string   CalendarName { get; set; }
        public int      AlarmMinutes { get; set; } = -1;
        public DateTime StartUtc     { get; set; }
        public bool     IsAllDay     { get; set; }

        public DateTime StartLocal => StartUtc.ToLocalTime();

        public override string ToString() =>
            StartLocal.ToString("ddd MMM d yyyy  h:mm tt") + "  —  " + Summary;
    }

    /// <summary>
    /// Calendar folder metadata returned by <see cref="CalendarImporter.GetOutlookCalendarFolders"/>.
    /// Does NOT hold any live COM objects — safe to cache indefinitely.
    /// </summary>
    public class OutlookCalendarFolder
    {
        public string Name     { get; set; }
        public string FullPath { get; set; }
        // FolderRef intentionally removed: holding live COM objects caused
        // "Only one version of Outlook can run" errors by keeping the background
        // COM instance alive after enumeration.
    }

    /// <summary>
    /// Parses ICS (iCalendar) files and reads Outlook calendar events via COM.
    ///
    /// COM-lifetime contract (all Outlook methods):
    ///   • Connect to a running Outlook process when possible (no new process).
    ///   • When we start Outlook ourselves, always call Quit() + ReleaseComObject
    ///     before returning so the background process terminates.
    ///   • Every nested COM object (namespace, stores, folders, items) is released
    ///     before the Application object is released.
    ///   • GC.Collect() + WaitForPendingFinalizers() flush any unreachable RCW wrappers.
    /// </summary>
    public static class CalendarImporter
    {
        // ── Outlook COM acquisition ───────────────────────────────────────────

        /// <summary>
        /// Set by <see cref="AcquireOutlook"/> when all connection strategies fail.
        /// Contains a short user-facing explanation (e.g. elevation mismatch).
        /// Cleared to null on each successful acquire.
        /// </summary>
        public static string LastAcquireHint { get; private set; }

        // ── P/Invoke declarations ─────────────────────────────────────────────

        // oleaut32: attach to a running COM server via the Running Object Table (ROT).
        [DllImport("oleaut32.dll")]
        private static extern int GetActiveObject(
            ref Guid rclsid, IntPtr pvReserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        // ole32: resolve ProgID → CLSID from the registry.
        // More reliable than Type.GetTypeFromProgID().GUID on .NET 10.
        [DllImport("ole32.dll")]
        private static extern int CLSIDFromProgID(
            [MarshalAs(UnmanagedType.LPWStr)] string lpszProgID,
            out Guid pclsid);

        // user32: enumerate all top-level windows.
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        // oleacc: get the native OM object for a window (bypasses ROT).
        // OBJID_NATIVEOM (0xFFFFFFF0) returns the Outlook Explorer IDispatch.
        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(
            IntPtr hwnd, uint dwId, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

        /// <summary>
        /// Strategy 1: attach via Running Object Table (fastest path).
        /// Fails silently if Outlook is not in the ROT (new Microsoft 365 Outlook,
        /// or ROT entry expired).
        /// </summary>
        private static object TryGetRunningOutlook()
        {
            try
            {
                int clsidHr = CLSIDFromProgID("Outlook.Application", out Guid clsid);
                if (clsidHr != 0)
                {
                    AppLog.Info($"CalendarImporter.TryGetRunningOutlook  CLSIDFromProgID hr=0x{clsidHr:X8} — ProgID not in registry.");
                    return null;
                }
                int hr = GetActiveObject(ref clsid, IntPtr.Zero, out object punk);
                AppLog.Info($"CalendarImporter.TryGetRunningOutlook  CLSID={clsid}  GetActiveObject hr=0x{hr:X8}" +
                              (hr == 0 ? " → attached via ROT." : " → not in ROT."));
                return hr == 0 ? punk : null;
            }
            catch (Exception ex) { AppLog.Error("CalendarImporter.TryGetRunningOutlook", ex); return null; }
        }

        // user32: get window title text.
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // advapi32: check whether this process token has elevated privileges.
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
            ref int TokenInformation, int TokenInformationLength, out int ReturnLength);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                const uint TOKEN_QUERY = 0x0008;
                const int TokenElevation = 20;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out IntPtr token)) return false;
                int elevation = 0;
                bool ok = GetTokenInformation(token, TokenElevation, ref elevation,
                    sizeof(int), out _);
                CloseHandle(token);
                return ok && elevation != 0;
            }
            catch { return false; }
        }

        // TokenElevationType: 1=Default (UAC off/no split token), 2=Full, 3=Limited.
        // When type=Default, every process runs at the same level — no mismatch possible.
        private static bool IsElevationMismatchPossible()
        {
            try
            {
                const uint TOKEN_QUERY = 0x0008;
                const int TokenElevationType = 18;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out IntPtr token)) return true;
                int elevType = 0;
                bool ok = GetTokenInformation(token, TokenElevationType, ref elevType,
                    sizeof(int), out _);
                CloseHandle(token);
                // Type 1 = TokenElevationTypeDefault: UAC disabled or built-in admin account.
                // All processes share the same token — no cross-process elevation mismatch.
                return !(ok && elevType == 1);
            }
            catch { return true; }
        }

        /// <summary>
        /// Strategy 2: find an Outlook top-level window and pull its native OM object via
        /// <c>AccessibleObjectFromWindow(OBJID_NATIVEOM)</c>.
        /// Logs every visible top-level window whose class or title contains "outlook" so
        /// we can see exactly what class name the running version uses.
        /// Classic Outlook: class = "rctrl_renwnd32".
        /// New Outlook (Monarch/web-shell, 2024+): no COM OM — OBJID_NATIVEOM not supported.
        /// </summary>
        private static object TryGetOutlookViaHwnd()
        {
            const uint OBJID_NATIVEOM = 0xFFFFFFF0;
            var iidDispatch = new Guid("00020400-0000-0000-C000-000000000046");
            object found = null;
            var candidates = new List<string>();

            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;

                var cls  = new StringBuilder(128);
                var title = new StringBuilder(256);
                GetClassName(hwnd, cls, cls.Capacity);
                GetWindowText(hwnd, title, title.Capacity);

                string c = cls.ToString();
                string t = title.ToString();

                // Log anything that looks Outlook-related for diagnostics
                bool looksLikeOutlook =
                    c.IndexOf("outlook", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("outlook", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    c == "rctrl_renwnd32";

                if (looksLikeOutlook)
                    candidates.Add($"class='{c}' title='{(t.Length > 60 ? t.Substring(0,60) + "…" : t)}'");

                // Classic Outlook window class
                if (c != "rctrl_renwnd32") return true;

                var g  = iidDispatch;
                int hr = AccessibleObjectFromWindow(hwnd, OBJID_NATIVEOM, ref g, out object obj);
                AppLog.Info($"CalendarImporter.TryGetOutlookViaHwnd  rctrl_renwnd32 found, OBJID_NATIVEOM hr=0x{hr:X8}");

                if (hr == 0 && obj != null)
                {
                    try
                    {
                        object app = ((dynamic)obj).Application;
                        if (!ReferenceEquals(app, obj)) SafeRelease(obj);
                        found = app;
                    }
                    catch { found = obj; }
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (candidates.Count > 0)
                AppLog.Info("CalendarImporter.TryGetOutlookViaHwnd  Outlook-related windows: " +
                    string.Join(" | ", candidates));
            else
                AppLog.Info("CalendarImporter.TryGetOutlookViaHwnd  No Outlook-related windows found in EnumWindows.");

            return found;
        }

        /// <summary>
        /// Acquires an Outlook.Application COM object using three progressive strategies.
        ///
        /// S1 — GetActiveObject via ROT: fastest; works when Outlook registers itself there
        ///      (classic Outlook 2016/2019, some M365 installs).
        ///
        /// S2 — AccessibleObjectFromWindow(OBJID_NATIVEOM): bypasses ROT; works with new
        ///      Microsoft 365 "new Outlook" which no longer registers in the ROT.
        ///      Fails if TimerStudio and Outlook run at different Windows integrity levels.
        ///
        /// S3 — Activator.CreateInstance: starts a background Outlook process when Outlook
        ///      is NOT already running.  Skipped (with a clear elevation-mismatch log) when
        ///      Outlook IS running but S1+S2 both failed, because CreateInstance would also
        ///      fail with CO_E_SERVER_EXEC_FAILURE in that scenario.
        ///
        /// Elevation mismatch fix: run TimerStudio and Outlook at the same privilege level
        /// (both as normal user — do NOT run TimerStudio as Administrator).
        /// </summary>
        private static (object app, bool weStarted) AcquireOutlook()
        {
            LastAcquireHint = null; // clear from previous attempt

            bool elevated = IsCurrentProcessElevated();
            AppLog.Info($"CalendarImporter.AcquireOutlook  TimerStudio elevated={elevated}");

            // S1: ROT
            object existing = TryGetRunningOutlook();
            if (existing != null) return (existing, false);

            // S2: HWND / OBJID_NATIVEOM
            AppLog.Info("CalendarImporter.AcquireOutlook  S1 failed. Trying HWND/OBJID_NATIVEOM...");
            object hwndApp = TryGetOutlookViaHwnd();
            if (hwndApp != null)
            {
                AppLog.Info("CalendarImporter.AcquireOutlook  HWND approach succeeded.");
                return (hwndApp, false);
            }
            AppLog.Info("CalendarImporter.AcquireOutlook  HWND approach returned null.");

            bool outlookRunning = Process.GetProcessesByName("OUTLOOK").Length > 0;
            if (outlookRunning)
            {
                // Diagnose why S1+S2 failed when Outlook IS running.
                // Only blame elevation mismatch when UAC is enabled (split token exists).
                // When UAC is disabled (TokenElevationTypeDefault), all processes share the
                // same token — no mismatch is possible; the real cause is something else
                // (new Outlook not supporting COM OM, or a thread-apartment issue).
                string diagnosis;
                if (elevated && IsElevationMismatchPossible())
                {
                    diagnosis =
                        "⚠ Cannot connect to Outlook — TimerStudio is running as Administrator.\n" +
                        "Outlook runs as a normal user and blocks elevated COM clients.\n\n" +
                        "Fix: Right-click TimerStudio → Properties → Compatibility →\n" +
                        "uncheck 'Run this program as an administrator', then restart.";
                }
                else
                {
                    diagnosis =
                        "⚠ Cannot connect to Outlook.\n" +
                        "If you are using the new Outlook (2024+), COM automation is not supported.\n" +
                        "Switch to Classic Outlook via the toggle in Outlook → Help → 'Go back to classic Outlook'.\n\n" +
                        "If you use classic Outlook, try restarting Outlook, then click Refresh.";
                }
                LastAcquireHint = diagnosis;
                AppLog.Info("CalendarImporter.AcquireOutlook  " + diagnosis.Replace("\n", " "));

                // S3-fallback: even with Outlook running, CreateInstance often routes to
                // the existing process (COM singleton behaviour). Try it before giving up.
                // COM objects are properly released after each call so no "only one version"
                // errors from leaked RCW handles.
                AppLog.Info("CalendarImporter.AcquireOutlook  S3-fallback: trying CreateInstance while Outlook already running...");
                try
                {
                    var type2 = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
                    if (type2 != null)
                    {
                        var app2 = Activator.CreateInstance(type2);
                        if (app2 != null)
                        {
                            AppLog.Info("CalendarImporter.AcquireOutlook  S3-fallback succeeded.");
                            LastAcquireHint = null;
                            return (app2, false); // don't Quit() — we didn't start it
                        }
                    }
                }
                catch (Exception ex2)
                {
                    AppLog.Error("CalendarImporter.AcquireOutlook  S3-fallback failed", ex2);
                }
                return (null, false);
            }

            // S3: Outlook not running at all — start a background instance
            try
            {
                var type = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
                if (type == null)
                {
                    AppLog.Info("CalendarImporter.AcquireOutlook  Outlook not installed (ProgID absent).");
                    return (null, false);
                }
                AppLog.Info("CalendarImporter.AcquireOutlook  S3: Outlook not running. Calling CreateInstance...");
                var app = Activator.CreateInstance(type);
                if (app != null)
                {
                    AppLog.Info("CalendarImporter.AcquireOutlook  CreateInstance succeeded (new Outlook process).");
                    return (app, true); // weStarted=true → Quit() on exit
                }
                AppLog.Info("CalendarImporter.AcquireOutlook  CreateInstance returned null.");
            }
            catch (Exception ex)
            {
                AppLog.Error("CalendarImporter.AcquireOutlook  CreateInstance failed", ex);
            }
            return (null, false);
        }

        /// <summary>
        /// Releases the Outlook.Application COM object.
        /// Calls Quit() first if weStarted=true (we launched the process).
        /// </summary>
        private static void ReleaseOutlook(object app, bool weStarted)
        {
            if (app == null) return;
            try { if (weStarted) ((dynamic)app).Quit(); } catch { }
            try { Marshal.ReleaseComObject(app); } catch { }
            // Flush any unreachable RCW wrappers so the process handle drops.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private static void SafeRelease(object o)
        {
            if (o != null) try { Marshal.ReleaseComObject(o); } catch { }
        }

        // ── Public: elevation helpers ─────────────────────────────────────────

        /// <summary>
        /// Returns true if the AppCompat registry layer for the running EXE contains
        /// "RUNASADMIN" — the flag set by right-click → Properties → Compatibility →
        /// "Run this program as an administrator".
        /// Checks HKCU first, then HKLM.
        /// </summary>
        public static bool HasRunAsAdminFlag()
        {
            string exePath = GetExePath();
            if (string.IsNullOrEmpty(exePath)) return false;
            foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser,
                                         Microsoft.Win32.Registry.LocalMachine })
            {
                try
                {
                    using var key = root.OpenSubKey(
                        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                    if (key == null) continue;
                    string val = key.GetValue(exePath) as string;
                    if (val != null &&
                        val.IndexOf("RUNASADMIN", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Removes "RUNASADMIN" from the AppCompat Layers value for the running EXE.
        /// If the remaining value is empty/whitespace the whole registry value is deleted.
        /// Returns true if at least one entry was cleared.
        /// </summary>
        public static bool ClearRunAsAdminFlag()
        {
            string exePath = GetExePath();
            if (string.IsNullOrEmpty(exePath)) return false;
            bool cleared = false;
            foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser,
                                         Microsoft.Win32.Registry.LocalMachine })
            {
                try
                {
                    using var key = root.OpenSubKey(
                        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
                        writable: true);
                    if (key == null) continue;
                    string val = key.GetValue(exePath) as string;
                    if (val == null) continue;

                    // Strip the RUNASADMIN token (may appear alone or alongside others)
                    string newVal = System.Text.RegularExpressions.Regex
                        .Replace(val, @"\bRUNASADMIN\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        .Trim();

                    if (string.IsNullOrWhiteSpace(newVal))
                        key.DeleteValue(exePath, throwOnMissingValue: false);
                    else
                        key.SetValue(exePath, newVal);

                    AppLog.Info($"CalendarImporter.ClearRunAsAdminFlag  Cleared from {root.Name}: old='{val}' new='{newVal}'");
                    cleared = true;
                }
                catch (Exception ex)
                {
                    AppLog.Error("CalendarImporter.ClearRunAsAdminFlag", ex);
                }
            }
            return cleared;
        }

        private static string GetExePath()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? ""; }
            catch { return ""; }
        }

        // ── Public: availability check ────────────────────────────────────────

        public static bool IsOutlookAvailable()
        {
            try { return Type.GetTypeFromProgID("Outlook.Application") != null; }
            catch { return false; }
        }

        // ── Public: enumerate calendar folder metadata ────────────────────────

        /// <summary>
        /// Returns the Name and FullPath of every discoverable Outlook calendar folder.
        /// Opens an Outlook session, enumerates, then fully releases all COM resources.
        /// The returned list contains NO live COM objects.
        /// </summary>
        public static List<OutlookCalendarFolder> GetOutlookCalendarFolders()
        {
            var result = new List<OutlookCalendarFolder>();
            var (app, weStarted) = AcquireOutlook();
            if (app == null) { AppLog.Info("CalendarImporter.GetOutlookCalendarFolders  AcquireOutlook returned null — cannot enumerate folders."); return result; }

            object ns = null;
            try
            {
                ns = ((dynamic)app).GetNamespace("MAPI");
                CollectCalendarFolderMetadata((dynamic)ns, result);
            }
            catch (Exception ex)
            {
                AppLog.Error("CalendarImporter.GetOutlookCalendarFolders", ex);
            }
            finally
            {
                SafeRelease(ns);
                ReleaseOutlook(app, weStarted);
            }
            return result;
        }

        /// <summary>
        /// Enumerates all calendar folders visible in the Outlook navigation pane,
        /// adding metadata to <paramref name="result"/>.
        ///
        /// Uses <c>Namespace.Folders</c> (not <c>Namespace.Stores</c>) as the entry point
        /// because <c>Namespace.Stores</c> omits Internet Calendar stores — the mechanism
        /// Outlook uses to surface Google Calendar / subscribed ICS feeds.
        /// <c>Namespace.Folders</c> returns the same root-folder view that the
        /// navigation pane uses, so every calendar visible in Outlook is reachable here.
        ///
        /// A full recursive walk finds deeply-nested calendars such as
        /// <c>Calendar\Other Calendars\matthewlpittman@gmail.com</c>.
        /// All COM objects created here are released before returning.
        /// </summary>
        private static void CollectCalendarFolderMetadata(dynamic ns,
            List<OutlookCalendarFolder> result)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object topFolders = null;
            try
            {
                topFolders = ns.Folders;
                int count = (int)((dynamic)topFolders).Count;
                for (int i = 1; i <= count; i++)
                {
                    object root = null;
                    try
                    {
                        root = ((dynamic)topFolders)[i];
                        dynamic dynRoot = (dynamic)root;

                        // Skip archive / public-folder roots by name
                        string rname = TryGetString(dynRoot, "Name") ?? "";
                        if (rname.IndexOf("Archive",       StringComparison.OrdinalIgnoreCase) >= 0 ||
                            rname.IndexOf("Public Folder", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        WalkFolderForCalendars(dynRoot, seen, result);
                    }
                    catch { }
                    finally { SafeRelease(root); }
                }
            }
            catch { }
            finally { SafeRelease(topFolders); }
        }

        /// <summary>
        /// Recursively walks <paramref name="folder"/> and all its descendants,
        /// adding an <see cref="OutlookCalendarFolder"/> entry for every folder whose
        /// <c>DefaultItemType == 1</c> (appointment/calendar items).
        /// Caller owns <paramref name="folder"/> and must release it separately.
        /// </summary>
        private static void WalkFolderForCalendars(dynamic folder,
            HashSet<string> seen, List<OutlookCalendarFolder> result)
        {
            // Register this folder if it holds calendar items
            string cpath = GetFolderPath(folder);
            string cname = GetFolderName(folder);
            if (string.IsNullOrEmpty(cname)) cname = "Calendar";
            // Fallback key when FolderPath is unavailable (e.g. local OST calendars)
            if (string.IsNullOrEmpty(cpath) && !string.IsNullOrEmpty(cname))
                cpath = "\\\\" + cname;
            bool isCal = false;
            try { isCal = (int)folder.DefaultItemType == 1; } catch { isCal = true; }
            if (isCal && !string.IsNullOrEmpty(cpath) && seen.Add(cpath))
                result.Add(new OutlookCalendarFolder { Name = cname, FullPath = cpath });

            // Recurse into sub-folders regardless (they may contain calendar sub-folders)
            object subs = null;
            try
            {
                subs = folder.Folders;
                int cnt = (int)((dynamic)subs).Count;
                for (int i = 1; i <= cnt; i++)
                {
                    object sub = null;
                    try
                    {
                        sub = ((dynamic)subs)[i];
                        WalkFolderForCalendars((dynamic)sub, seen, result);
                    }
                    catch { }
                    finally { SafeRelease(sub); }
                }
            }
            catch { }
            finally { SafeRelease(subs); }
        }

        /// <summary>
        /// Recursively walks <paramref name="folder"/> and all its descendants,
        /// reading events from every calendar-type folder not in <paramref name="hiddenSet"/>.
        /// Caller owns <paramref name="folder"/> and must release it separately.
        /// </summary>
        private static void WalkFolderForEvents(dynamic folder,
            HashSet<string> seen, HashSet<string> hiddenSet,
            int maxItems, int lookaheadDays, List<CalendarEvent> all)
        {
            string cpath = GetFolderPath(folder);
            string cname = GetFolderName(folder);
            if (string.IsNullOrEmpty(cname)) cname = "Calendar";
            // Fallback key when FolderPath is unavailable (e.g. local OST calendars)
            if (string.IsNullOrEmpty(cpath) && !string.IsNullOrEmpty(cname))
                cpath = "\\\\" + cname;
            bool isCal = false;
            try { isCal = (int)folder.DefaultItemType == 1; } catch { isCal = true; }

            if (isCal && !string.IsNullOrEmpty(cpath) &&
                seen.Add(cpath) && !hiddenSet.Contains(cpath))
            {
                var ev = ReadEventsFromFolder(folder, cname, maxItems, lookaheadDays);
                all.AddRange(ev);
            }

            // Recurse regardless — sub-folders may be calendar type even if parent is not
            object subs = null;
            try
            {
                subs = folder.Folders;
                int cnt = (int)((dynamic)subs).Count;
                for (int i = 1; i <= cnt; i++)
                {
                    object sub = null;
                    try
                    {
                        sub = ((dynamic)subs)[i];
                        WalkFolderForEvents((dynamic)sub, seen, hiddenSet, maxItems, lookaheadDays, all);
                    }
                    catch { }
                    finally { SafeRelease(sub); }
                }
            }
            catch { }
            finally { SafeRelease(subs); }
        }

        // ── Public: read events from a specific folder (by path) ─────────────

        /// <summary>
        /// Reads calendar events from the folder identified by <paramref name="calFolder"/>.FullPath.
        /// Opens a fresh Outlook session, navigates to the folder, reads events, releases all COM.
        /// </summary>
        public static List<CalendarEvent> ReadOutlookFolder(OutlookCalendarFolder calFolder,
            int maxItems = 300)
        {
            if (calFolder == null || string.IsNullOrEmpty(calFolder.FullPath))
                return new List<CalendarEvent>();
            return ReadByPath(calFolder.FullPath, calFolder.Name ?? "Calendar", maxItems, lookaheadDays: 90);
        }

        private static List<CalendarEvent> ReadByPath(string folderPath, string calName,
            int maxItems, int lookaheadDays)
        {
            var events = new List<CalendarEvent>();
            var (app, weStarted) = AcquireOutlook();
            if (app == null) { AppLog.Info("CalendarImporter.ReadByPath  AcquireOutlook returned null — cannot read events from: " + folderPath); return events; }

            object ns     = null;
            object folder = null;
            try
            {
                ns     = ((dynamic)app).GetNamespace("MAPI");
                folder = FindFolderByPath((dynamic)ns, folderPath);
                if (folder != null)
                    events = ReadEventsFromFolder((dynamic)folder, calName, maxItems, lookaheadDays);
            }
            catch (Exception ex)
            {
                AppLog.Error("CalendarImporter.ReadByPath path=" + folderPath, ex);
            }
            finally
            {
                SafeRelease(folder);
                SafeRelease(ns);
                ReleaseOutlook(app, weStarted);
            }
            return events;
        }

        // ── Public: combined one-shot for auto-pull ───────────────────────────

        /// <summary>
        /// Opens one Outlook session, reads all visible calendars, releases everything, returns events.
        /// Designed for auto-pull: one COM session = one Windows process = no "already running" error.
        /// </summary>
        /// <param name="hiddenPaths">Calendar FullPaths to skip (from AppPrefs).</param>
        /// <param name="maxItems">Max events per calendar folder.</param>
        /// <param name="lookaheadDays">Only read events starting within this many days.</param>
        public static List<CalendarEvent> ReadAllCalendarsOneShot(
            List<string> hiddenPaths  = null,
            int          maxItems     = 600,
            int          lookaheadDays = 365)
        {
            var all        = new List<CalendarEvent>();
            var (app, weStarted) = AcquireOutlook();
            if (app == null) { AppLog.Info("CalendarImporter.ReadAllCalendarsOneShot  AcquireOutlook returned null — cannot read any calendars."); return all; }

            object ns = null;
            try
            {
                ns = ((dynamic)app).GetNamespace("MAPI");

                var seen      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hiddenSet = hiddenPaths != null
                    ? new HashSet<string>(hiddenPaths, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>();

                // Use Namespace.Folders (not Namespace.Stores) — Stores omits Internet
                // Calendar stores (Google Calendar, subscribed ICS feeds).
                object topFolders = null;
                try { topFolders = ((dynamic)ns).Folders; } catch { }
                if (topFolders != null)
                {
                    int count = 0;
                    try { count = (int)((dynamic)topFolders).Count; } catch { }
                    for (int i = 1; i <= count; i++)
                    {
                        object root = null;
                        try
                        {
                            root = ((dynamic)topFolders)[i];
                            dynamic dynRoot = (dynamic)root;

                            string rname = TryGetString(dynRoot, "Name") ?? "";
                            if (rname.IndexOf("Archive",       StringComparison.OrdinalIgnoreCase) >= 0 ||
                                rname.IndexOf("Public Folder", StringComparison.OrdinalIgnoreCase) >= 0)
                                continue;

                            WalkFolderForEvents(dynRoot, seen, hiddenSet, maxItems, lookaheadDays, all);
                        }
                        catch { }
                        finally { SafeRelease(root); }
                    }
                }
                SafeRelease(topFolders);
            }
            catch (Exception ex)
            {
                AppLog.Error("CalendarImporter.ReadAllCalendarsOneShot", ex);
            }
            finally
            {
                SafeRelease(ns);
                ReleaseOutlook(app, weStarted);
            }

            all.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            return all;
        }

        // ── Private COM helpers ───────────────────────────────────────────────

        /// <summary>
        /// Finds a folder in <paramref name="ns"/> whose FolderPath matches <paramref name="targetPath"/>.
        /// Returns a live COM object — caller MUST release it via <see cref="SafeRelease"/>.
        /// All other COM objects created internally are released before returning.
        /// </summary>
        private static object FindFolderByPath(dynamic ns, string targetPath)
        {
            object result     = null;
            object topFolders = null;
            try
            {
                // Use Namespace.Folders (not Namespace.Stores) to cover Internet Calendar
                // stores (Google Calendar, subscribed ICS feeds) that Stores omits.
                topFolders = ns.Folders;
                int count = (int)((dynamic)topFolders).Count;

                for (int i = 1; i <= count && result == null; i++)
                {
                    object root = null;
                    try
                    {
                        root = ((dynamic)topFolders)[i];
                        object found = SearchFolderRecursive((dynamic)root, targetPath);
                        if (found != null)
                        {
                            result = found;
                            // If root IS the match, prevent finally from releasing it.
                            if (object.ReferenceEquals(found, root)) root = null;
                        }
                    }
                    catch { }
                    finally { SafeRelease(root); }
                }
            }
            catch { }
            finally { SafeRelease(topFolders); }
            return result;
        }

        /// <summary>
        /// Recursively searches <paramref name="folder"/> and its descendants for a folder
        /// whose FolderPath equals <paramref name="targetPath"/>.
        /// Returns a live COM object the caller must release via <see cref="SafeRelease"/>, or null.
        /// <paramref name="folder"/> itself is NOT released by this method — caller owns it.
        ///
        /// COM ownership rule: each index access (subs[i]) creates one COM reference.
        /// When a sub-folder IS the match we null it out before the finally so the
        /// reference is not prematurely released. When a descendant is the match, the
        /// intermediate sub-folder IS released (COM ref-counts are per-object; releasing
        /// a parent never invalidates a child reference).
        /// </summary>
        private static object SearchFolderRecursive(dynamic folder, string targetPath)
        {
            string cpath = GetFolderPath(folder);
            if (string.IsNullOrEmpty(cpath)) { string n = GetFolderName(folder); if (!string.IsNullOrEmpty(n)) cpath = "\\\\" + n; }
            if (string.Equals(cpath, targetPath, StringComparison.OrdinalIgnoreCase))
                return folder; // caller owns this; do NOT release here

            object subs = null;
            try
            {
                subs = folder.Folders;
                int cnt = (int)((dynamic)subs).Count;
                for (int i = 1; i <= cnt; i++)
                {
                    object sub = null;
                    try
                    {
                        sub = ((dynamic)subs)[i];
                        object found = SearchFolderRecursive((dynamic)sub, targetPath);
                        if (found != null)
                        {
                            // If sub IS the match, null it so the finally doesn't release it.
                            // If found is a deeper descendant, releasing sub is safe —
                            // COM objects are independently ref-counted.
                            if (object.ReferenceEquals(found, sub)) sub = null;
                            return found;
                        }
                    }
                    catch { }
                    finally { SafeRelease(sub); }
                }
            }
            catch { }
            finally { SafeRelease(subs); }
            return null;
        }

        /// <summary>
        /// Reads appointment items from <paramref name="folder"/> within the next
        /// <paramref name="lookaheadDays"/> days.  Releases all internal COM objects before returning.
        /// The <paramref name="folder"/> object itself is NOT released — caller is responsible.
        /// </summary>
        private static List<CalendarEvent> ReadEventsFromFolder(dynamic folder, string calName,
            int maxItems, int lookaheadDays)
        {
            var events = new List<CalendarEvent>();
            object items      = null;
            object restricted = null;
            try
            {
                items = folder.Items;
                dynamic dynItems = (dynamic)items;
                // IncludeRecurrences MUST be set before Sort
                dynItems.IncludeRecurrences = true;
                dynItems.Sort("[Start]", false);

                DateTime rangeStart = DateTime.Now;
                DateTime rangeEnd   = DateTime.Now.AddDays(Math.Max(1, lookaheadDays));
                string   filter     = string.Format(
                    "[Start] >= \"{0}\" AND [Start] <= \"{1}\"",
                    rangeStart.ToString("MM/dd/yyyy HH:mm"),
                    rangeEnd.ToString("MM/dd/yyyy HH:mm"));

                restricted = dynItems.Restrict(filter);
                int count  = 0;

                // Use GetEnumerator so we can release each item individually.
                var enumerator = ((System.Collections.IEnumerable)restricted).GetEnumerator();
                while (count < maxItems && enumerator.MoveNext())
                {
                    object itemObj = enumerator.Current;
                    try
                    {
                        dynamic item = (dynamic)itemObj;
                        int cls = 0;
                        try { cls = (int)item.Class; } catch { cls = 26; }
                        if (cls != 26) continue; // not an AppointmentItem

                        string   subject = item.Subject ?? "(No title)";
                        DateTime startDt = (DateTime)item.Start;
                        bool     allDay  = (bool)item.AllDayEvent;

                        events.Add(new CalendarEvent
                        {
                            Summary      = subject,
                            CalendarName = calName,
                            Description  = TryGetString(item, "Body"),
                            Location     = TryGetString(item, "Location"),
                            StartUtc     = allDay ? startDt.Date.ToUniversalTime()
                                                  : startDt.ToUniversalTime(),
                            IsAllDay     = allDay
                        });
                        count++;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("CalendarImporter.ReadEventsFromFolder item", ex);
                    }
                    finally { SafeRelease(itemObj); }
                }

                // Drain remaining items so COM enumerator state is clean
                while (enumerator.MoveNext())
                    SafeRelease(enumerator.Current);

                if (enumerator is IDisposable d) try { d.Dispose(); } catch { }
                try { Marshal.ReleaseComObject(enumerator); } catch { }

                events.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            }
            catch (Exception ex)
            {
                AppLog.Error("CalendarImporter.ReadEventsFromFolder calendar=" + calName, ex);
            }
            finally
            {
                SafeRelease(restricted);
                SafeRelease(items);
            }
            return events;
        }

        // ── Option 1: ICS / iCalendar file import (unchanged) ─────────────────

        public static List<CalendarEvent> ParseIcs(string filePath)
        {
            var events = new List<CalendarEvent>();
            if (!File.Exists(filePath)) return events;
            try
            {
                string raw = File.ReadAllText(filePath, Encoding.UTF8);
                raw = raw.Replace("\r\n ", "").Replace("\r\n\t", "")
                         .Replace("\n ", "").Replace("\n\t", "");
                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                CalendarEvent current = null;
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (line == "BEGIN:VEVENT")
                    {
                        current = new CalendarEvent();
                    }
                    else if (line == "END:VEVENT" && current != null)
                    {
                        // H-10: skip events whose date failed to parse (StartUtc == MinValue)
                        if (current.StartUtc != DateTime.MinValue &&
                            current.StartUtc > DateTime.UtcNow.AddDays(-1))
                            events.Add(current);
                        current = null;
                    }
                    else if (current != null)
                    {
                        if (line.StartsWith("DTSTART", StringComparison.OrdinalIgnoreCase))
                        {
                            int colon = line.IndexOf(':');
                            if (colon >= 0)
                            {
                                string prop  = line.Substring(0, colon).ToUpperInvariant();
                                string value = line.Substring(colon + 1).Trim();
                                current.IsAllDay = prop.Contains("VALUE=DATE") && !prop.Contains("DATE-TIME");
                                current.StartUtc = ParseIcsDateTime(value, current.IsAllDay);
                            }
                        }
                        else if (line.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase))
                            current.Summary = UnescapeIcs(line.Substring(8));
                        else if (line.StartsWith("DESCRIPTION:", StringComparison.OrdinalIgnoreCase))
                            current.Description = UnescapeIcs(line.Substring(12));
                        else if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                            current.Location = UnescapeIcs(line.Substring(9));
                        else if (line.StartsWith("TRIGGER:", StringComparison.OrdinalIgnoreCase))
                            current.AlarmMinutes = ParseTriggerMinutes(line.Substring(8));
                        else if (line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
                        {
                            var extras = ExpandRRule(current, line.Substring(6));
                            foreach (var ex in extras)
                                if (ex.StartUtc > DateTime.UtcNow.AddDays(-1))
                                    events.Add(ex);
                        }
                    }
                }
                events.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            }
            catch (Exception ex)
            {
                AppLog.Error("CalendarImporter.ParseIcs path=" + filePath, ex);
            }
            return events;
        }

        private static DateTime ParseIcsDateTime(string value, bool allDay)
        {
            // H-10: Use TryParse — int.Parse throws on malformed input and the outer
            // ParseIcs catch would abort the entire file. Return MinValue on bad data
            // so the caller can skip just that event.
            if (string.IsNullOrEmpty(value)) return DateTime.MinValue;
            bool isUtc = value.EndsWith("Z");
            value = value.TrimEnd('Z');
            if (allDay && value.Length == 8)
            {
                if (!int.TryParse(value.Substring(0, 4), out int y)  ||
                    !int.TryParse(value.Substring(4, 2), out int mo) ||
                    !int.TryParse(value.Substring(6, 2), out int d))
                    return DateTime.MinValue;
                try { return new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc); }
                catch { return DateTime.MinValue; }
            }
            if (value.Length >= 15)
            {
                if (!int.TryParse(value.Substring(0, 4), out int y)  ||
                    !int.TryParse(value.Substring(4, 2), out int mo) ||
                    !int.TryParse(value.Substring(6, 2), out int d)  ||
                    !int.TryParse(value.Substring(9, 2), out int h)  ||
                    !int.TryParse(value.Substring(11, 2), out int mi) ||
                    !int.TryParse(value.Substring(13, 2), out int s))
                    return DateTime.MinValue;
                try
                {
                    var kind = isUtc ? DateTimeKind.Utc : DateTimeKind.Local;
                    var dt   = new DateTime(y, mo, d, h, mi, s, kind);
                    return kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt;
                }
                catch { return DateTime.MinValue; }
            }
            return DateTime.MinValue;
        }

        private static int ParseTriggerMinutes(string trigger)
        {
            trigger = trigger.TrimStart('-');
            int minutes = 0;
            var m = Regex.Match(trigger, @"P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?");
            if (!m.Success) return -1;
            if (m.Groups[1].Success) minutes += int.Parse(m.Groups[1].Value) * 1440;
            if (m.Groups[2].Success) minutes += int.Parse(m.Groups[2].Value) * 60;
            if (m.Groups[3].Success) minutes += int.Parse(m.Groups[3].Value);
            return minutes;
        }

        private static List<CalendarEvent> ExpandRRule(CalendarEvent baseEvent, string rrule)
        {
            var results = new List<CalendarEvent>();
            try
            {
                string freq     = GetRRulePart(rrule, "FREQ");
                int interval    = int.TryParse(GetRRulePart(rrule, "INTERVAL"), out int iv) ? iv : 1;
                // L-4: default 200 when RRULE has no COUNT/UNTIL (was 52 — silently dropped
                // recurring events beyond ~1 year for weekly, ~7 weeks for daily).
                int count       = int.TryParse(GetRRulePart(rrule, "COUNT"),    out int ct) ? ct : 200;
                string untilStr = GetRRulePart(rrule, "UNTIL");
                DateTime until  = DateTime.UtcNow.AddDays(365);
                if (!string.IsNullOrEmpty(untilStr))
                    until = ParseIcsDateTime(untilStr.TrimEnd('Z'), false);

                // Hard safety cap: never expand more than 200 occurrences regardless of COUNT.
                int maxOcc = Math.Min(count, 200);
                DateTime cur = baseEvent.StartUtc;
                for (int i = 1; i < maxOcc; i++)
                {
                    switch (freq?.ToUpperInvariant())
                    {
                        case "DAILY":   cur = cur.AddDays(interval);   break;
                        case "WEEKLY":  cur = cur.AddDays(interval*7); break;
                        case "MONTHLY": cur = cur.AddMonths(interval); break;
                        case "YEARLY":  cur = cur.AddYears(interval);  break;
                        default: return results;
                    }
                    if (cur > until || cur > DateTime.UtcNow.AddDays(365)) break;
                    results.Add(new CalendarEvent
                    {
                        Summary     = baseEvent.Summary,
                        Description = baseEvent.Description,
                        Location    = baseEvent.Location,
                        AlarmMinutes= baseEvent.AlarmMinutes,
                        StartUtc    = cur,
                        IsAllDay    = baseEvent.IsAllDay
                    });
                }
            }
            catch (Exception ex) { Debug.WriteLine("[TimerStudio] RRULE: " + ex.GetType().Name); }
            return results;
        }

        private static string GetRRulePart(string rrule, string key)
        {
            foreach (var part in rrule.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && string.Equals(part.Substring(0, eq).Trim(), key,
                    StringComparison.OrdinalIgnoreCase))
                    return part.Substring(eq + 1).Trim();
            }
            return null;
        }

        private static string UnescapeIcs(string s)
            => s.Replace("\\n", "\n").Replace("\\N", "\n")
                .Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

        private static string TryGetString(dynamic obj, string prop)
        {
            // Direct IDispatch dispatch via dynamic is more reliable for COM objects
            // than reflection InvokeMember, which can silently fail on non-standard
            // store types (e.g. local OST "Calendar (This computer only)" folders).
            try
            {
                return (string)obj.GetType().InvokeMember(prop,
                    System.Reflection.BindingFlags.GetProperty |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance,
                    null, obj, null) ?? "";
            }
            catch { return ""; }
        }

        /// <summary>Gets a string property directly via COM IDispatch (dynamic). Fallback to TryGetString.</summary>
        private static string GetFolderPath(dynamic folder)
        {
            try { return (string)folder.FolderPath ?? ""; } catch { }
            return TryGetString(folder, "FolderPath");
        }

        private static string GetFolderName(dynamic folder)
        {
            try { return (string)folder.Name ?? ""; } catch { }
            return TryGetString(folder, "Name");
        }
    }
}
