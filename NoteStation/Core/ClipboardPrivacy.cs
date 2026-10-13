using System;
using System.IO;
using System.Windows;

namespace NoteStation.Core
{
    /// <summary>
    /// Clipboard formats that copiers (password managers, Windows credential UI) set to say
    /// "don't record this". Honoured by Windows Clipboard History; any one present → skip capture.
    /// https://learn.microsoft.com/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats
    /// </summary>
    internal static class ClipboardPrivacy
    {
        internal const string ExcludeFromMonitor       = "ExcludeClipboardContentFromMonitorProcessing";
        internal const string CanIncludeInHistory      = "CanIncludeInClipboardHistory";
        internal const string ClipboardViewerIgnore    = "Clipboard Viewer Ignore";

        /// <summary>Reads the live clipboard. Fails open (false) on a busy/locked clipboard.</summary>
        public static bool IsCurrentClipboardExcluded()
        {
            try { return IsExcluded(Clipboard.GetDataObject()); }
            catch { return false; }
        }

        public static bool IsExcluded(IDataObject data)
        {
            if (data == null) return false;
            if (data.GetDataPresent(ExcludeFromMonitor, false)) return true;
            if (data.GetDataPresent(ClipboardViewerIgnore, false)) return true;
            if (data.GetDataPresent(CanIncludeInHistory, false))
            {
                // DWORD: 0 = must not be added to history, 1 = allowed.
                if (ReadDword(data.GetData(CanIncludeInHistory, false)) == 0) return true;
            }
            return false;
        }

        private static int? ReadDword(object raw)
        {
            switch (raw)
            {
                case MemoryStream ms when ms.Length >= 4:
                    var buf = new byte[4];
                    ms.Position = 0;
                    ms.ReadExactly(buf, 0, 4);
                    return BitConverter.ToInt32(buf, 0);
                case byte[] b when b.Length >= 4:
                    return BitConverter.ToInt32(b, 0);
                case int i:
                    return i;
                default:
                    return null;
            }
        }
    }
}
