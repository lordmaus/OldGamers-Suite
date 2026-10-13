using System;
using System.IO;

namespace ReticleOverlay
{
    /// <summary>
    /// HIGH (2026-09-14 review): a custom-reticle image path sourced from an imported profile is
    /// attacker-controlled. A UNC path reaching File.Exists()/BitmapImage triggers an outbound SMB
    /// connection and an NTLM authentication attempt — a known credential-leak technique.
    /// RO-01 (2026-10-13): allowlist instead of denylist. Only "X:\…" on a non-network drive passes;
    /// \\host, \\?\UNC, \??\UNC, \\.\device, file:// / http(s) URIs, relative and drive-relative
    /// ("C:foo") paths and NTFS stream names ("C:\a.png:x") are rejected before touching the disk.
    /// </summary>
    internal static class ImagePathValidator
    {
        public static bool IsSafeLocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0) return false;
            if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
                (path[2] != '\\' && path[2] != '/'))
                return false;
            if (path.IndexOf(':', 2) >= 0) return false;            // alternate data stream / odd forms
            if (!Path.IsPathFullyQualified(path)) return false;
            try
            {
                var drive = new DriveInfo(path.Substring(0, 1));
                if (drive.DriveType == DriveType.Network) return false;   // mapped network drive → SMB
            }
            catch { return false; }
            return true;
        }
    }
}
