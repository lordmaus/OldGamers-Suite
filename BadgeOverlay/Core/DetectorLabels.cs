using System;
using System.Collections.Generic;

namespace BadgeOverlay
{
    /// <summary>
    /// Stable, well-known label strings produced by the Win32 classifier for
    /// generic container windows where the real element name lives deeper in
    /// the UI Automation tree. When OverlayWindow sees one of these labels
    /// as the Win32 result, it escalates to UIA to get the real name of
    /// what the user is hovering (a button inside the toolbar, a file in
    /// the list view, a cell in the data grid, etc.).
    ///
    /// Strings are constants so the compiler folds them into a single
    /// interned entry — reference equality is safe for comparisons but we
    /// still use <see cref="IsGenericContainer(string)"/> for clarity.
    /// </summary>
    internal static class DetectorLabels
    {
        // Generic containers whose child holds the real element name
        public const string BrowserControl = "Browser Control";
        public const string ListView       = "List View";
        public const string TreeView       = "Tree View";
        public const string DataGrid       = "Data Grid";
        public const string Toolbar        = "Toolbar";
        public const string Ribbon         = "Ribbon";

        // Category strings — kept as constants to eliminate typos.
        public static class Cat
        {
            public const string Close     = "Close";
            public const string Minimize  = "Minimize";
            public const string Maximize  = "Maximize";
            public const string Action    = "Action";
            public const string Danger    = "Danger";
            public const string Nav       = "Nav";
            public const string Control   = "Control";
            public const string Settings  = "Settings";
            public const string Info      = "Info";
            public const string Account   = "Account";
            public const string Menu      = "Menu";
            public const string Tab       = "Tab";
            public const string Icon      = "Icon";
            public const string Text      = "Text";
            public const string Toolbar   = "Toolbar";
            public const string Chrome    = "Chrome";
            public const string Disabled  = "Disabled";
        }

        private static readonly HashSet<string> _genericContainers =
            new HashSet<string>(StringComparer.Ordinal)
            {
                BrowserControl, ListView, TreeView, DataGrid, Toolbar, Ribbon
            };

        /// <summary>
        /// Canonical destructive/danger keyword list. Single source of truth
        /// for "does this label describe a destructive action" — shared by
        /// <c>ButtonDetector.BT()</c>/<c>Theme()</c> (Win32 button labels) and
        /// <c>UIADetector</c>'s category classifier (UIA element names), which
        /// previously hand-maintained two independent lists that had drifted
        /// apart (see BadgeOverlay_Full_Review_2026-09-07.md, Bug #1 /
        /// Feature Gap #2 — "Discard" was Danger in one and not the other).
        /// All lowercase — callers must match against a lowercased haystack.
        /// </summary>
        public static readonly string[] DestructiveKeywords =
        {
            "delete", "remove", "discard", "clear all", "empty", "uninstall", "revoke",
            "cancel", "abort", "reject", "decline", "deny",
        };

        /// <summary>
        /// True when the Win32 classifier returned a generic container label
        /// whose meaningful child element should be discovered via UIA.
        /// </summary>
        public static bool IsGenericContainer(string label)
            => !string.IsNullOrEmpty(label) && _genericContainers.Contains(label);
    }
}
