using System;
using System.IO;

#nullable enable


namespace FileCopy
{
    public static class DriveAffinity
    {
        /// <summary>
        /// Returns a stable key identifying the volume backing <paramref name="path"/>.
        /// Falls back to drive letter for UNC + unrecognized paths.
        /// </summary>
        public static string GetVolumeKey(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root)) return "";
                return root.ToUpperInvariant();
            }
            catch { return path.Substring(0, Math.Min(3, path.Length)).ToUpperInvariant(); }
        }

        /// <summary>
        /// Same volume copy = serial (HDD seek-thrash).
        /// Different volumes = parallel-friendly.
        /// </summary>
        public static bool SameVolume(string srcPath, string dstPath)
            => string.Equals(GetVolumeKey(srcPath), GetVolumeKey(dstPath), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Returns true when the path is a UNC share (\\server\share), a mapped
        /// network drive, or a cloud-synced folder (OneDrive, SharePoint).
        /// Used to warn users and auto-lower parallelism on slow connections.
        /// </summary>
        public static bool IsNetworkOrCloudPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string full = Path.GetFullPath(path);

                // UNC path \\server\share
                if (full.StartsWith(@"\\", StringComparison.Ordinal)) return true;

                // Mapped network drive
                string? root = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root) && root.Length >= 2)
                {
                    var di = new DriveInfo(root);
                    if (di.DriveType == DriveType.Network) return true;
                }

                // Cloud-synced folders (OneDrive, SharePoint, Dropbox, Google Drive)
                string lower = full.ToLowerInvariant();
                if (lower.Contains("onedrive") ||
                    lower.Contains("sharepoint") ||
                    lower.Contains("dropbox") ||
                    lower.Contains("google drive") ||
                    lower.Contains("googledrive") ||
                    lower.Contains("box sync") ||
                    lower.Contains("icloud drive"))
                    return true;

                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Recommended max parallel streams for this src→dst pair.
        /// Network/cloud paths are capped at 1 to avoid hammering slow links.
        /// </summary>
        public static int RecommendedParallel(string srcPath, string dstPath, int userMax)
        {
            if (IsNetworkOrCloudPath(srcPath) || IsNetworkOrCloudPath(dstPath))
                return 1;
            if (SameVolume(srcPath, dstPath))
                return 1;
            return Math.Max(2, userMax);
        }
    }
}
