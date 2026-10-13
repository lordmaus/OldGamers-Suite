using System;
using System.Runtime.InteropServices;

namespace SuperMagnifier
{
    /// <summary>
    /// Given the current caret position, attempts to find the bounding rect of the
    /// word currently being typed using a pure heuristic (no COM/IAccessible calls —
    /// despite this class's original name, it does not call accLocation; see
    /// TryGetWordFocus for the actual implementation). Falls back gracefully to just
    /// the caret point when the heuristic can't determine a rect.
    ///
    /// The result is a "focus point" (screen px) that MagnifierEngine should centre on.
    /// When the word rect is available the focus is nudged slightly right of centre
    /// so the word sits in the left 2/3 of the lens — comfortable reading position.
    /// </summary>
    internal static class WordTracker
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")]
        static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO gui);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);

        [DllImport("user32.dll")]
        static extern bool GetCaretPos(out POINT pt);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT  { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO
        {
            public int    cbSize;
            public uint   flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture,
                          hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT   rcCaret;
        }

        /// <summary>
        /// Returns a screen-coordinate focus point for the current caret / word.
        /// offsetX: positive = shift right inside the lens (left-biased reading).
        /// </summary>
        public static bool TryGetWordFocus(int caretX, int caretY,
                                           out int focusX, out int focusY)
        {
            focusX = caretX;
            focusY = caretY;

            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;

                uint tid = GetWindowThreadProcessId(fg, out _);
                if (tid == 0) return false;

                var gui = new GUITHREADINFO
                    { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (!GetGUIThreadInfo(tid, ref gui)) return false;
                if (gui.hwndCaret == IntPtr.Zero) return false;

                RECT rc = gui.rcCaret;
                int lineH = Math.Max(1, rc.bottom - rc.top);

                // Estimate word width: typically ~6 chars × char width
                // Use caret height as a proxy for char width (monospace assumption)
                // For proportional fonts, this is a reasonable approximation
                int charW  = Math.Max(6, lineH / 2);
                int wordW  = charW * 7;   // ~7 chars wide

                // Word rect in client coords — convert caret origin to screen
                var ptOrigin = new POINT { X = rc.left, Y = rc.top };
                ClientToScreen(gui.hwndCaret, ref ptOrigin);

                // Estimate word start: go left by ~wordW from the caret, but never
                // left of the caret's own screen X (avoids overshooting on short lines).
                int wordStartX = Math.Max(caretX - wordW, ptOrigin.X - wordW);
                wordStartX = Math.Min(wordStartX, caretX); // clamp: never right of caret

                // Place focus 1/3 into the estimated word — comfortable left-biased reading
                focusX = wordStartX + wordW / 3;
                focusX = Math.Min(focusX, caretX); // final safety clamp to caret
                focusY = caretY;
                return true;
            }
            catch { return false; }
        }
    }
}
