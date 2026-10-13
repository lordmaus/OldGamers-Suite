using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace NoteStation.Core
{
    /// <summary>
    /// Optional at-rest encryption for clipboard history (pref EncryptClipboardAtRest, opt-in).
    /// DPAPI CurrentUser scope: files are readable only by this Windows account on this PC.
    /// Encrypted files carry an ".enc" suffix ({id}.json.enc, {id}.png.enc); readers accept both forms.
    /// </summary>
    internal static class ClipVault
    {
        public const string EncSuffix = ".enc";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NoteStation.clips.v1");

        public static bool IsEncryptedPath(string path) =>
            path != null && path.EndsWith(EncSuffix, StringComparison.OrdinalIgnoreCase);

        public static byte[] Protect(byte[] plain) =>
            ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

        public static byte[] Unprotect(byte[] cipher) =>
            ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);

        /// <summary>Reads a clip JSON file, decrypting when it is an .enc file.</summary>
        public static string ReadText(string path)
        {
            if (!IsEncryptedPath(path)) return File.ReadAllText(path, Encoding.UTF8);
            byte[] plain = Unprotect(File.ReadAllBytes(path));
            return Encoding.UTF8.GetString(plain);
        }

        /// <summary>Raw (decrypted) bytes of a clip image file.</summary>
        public static byte[] ReadImageBytes(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            return IsEncryptedPath(path) ? Unprotect(raw) : raw;
        }

        /// <summary>Frozen bitmap from a plain or encrypted clip image. Never locks the file.</summary>
        public static BitmapImage LoadImage(string path)
        {
            using var ms = new MemoryStream(ReadImageBytes(path));
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption  = BitmapCacheOption.OnLoad;
            b.StreamSource = ms;
            b.EndInit();
            b.Freeze();
            return b;
        }

        /// <summary>Writes PNG bytes for a new clip image and returns the path actually written.</summary>
        public static string SaveImage(string plainPngPath, byte[] pngBytes, bool encrypt)
        {
            string path = encrypt ? plainPngPath + EncSuffix : plainPngPath;
            File.WriteAllBytes(path, encrypt ? Protect(pngBytes) : pngBytes);
            return path;
        }

        /// <summary>Converts an existing image file to the requested form; returns the new path (or the same path).</summary>
        public static string ConvertImage(string path, bool encrypt)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || IsEncryptedPath(path) == encrypt) return path;
            byte[] plain = ReadImageBytes(path);
            string target = encrypt ? path + EncSuffix : path[..^EncSuffix.Length];
            File.WriteAllBytes(target, encrypt ? Protect(plain) : plain);
            File.Delete(path);
            return target;
        }
    }
}
