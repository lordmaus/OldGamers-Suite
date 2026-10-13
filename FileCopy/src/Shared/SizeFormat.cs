using System;
using System.Globalization;

#nullable enable


namespace FileCopy
{
    public static class SizeFormat
    {
        private static readonly string[] Units = { "KB", "MB", "GB", "TB" };

        public static string Format(long bytes)
        {
            if (bytes < 1024) return bytes.ToString("N0") + " B";
            double d = bytes;
            int i = -1;
            do { d /= 1024; i++; } while (d >= 1024 && i < Units.Length - 1);
            return d.ToString("0.##", CultureInfo.CurrentCulture) + " " + Units[i];
        }
    }
}
