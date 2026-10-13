using System;
using System.Security.Cryptography;
using System.Text;

namespace NoteStation
{
    /// <summary>
    /// Salted PBKDF2 hashing for the "Hide note" PIN / master passphrase (2026-09-07
    /// review, Bug #6: the previous scheme was unsalted single-round SHA-256, trivially
    /// brute-forceable offline for a numeric PIN). Hide note is a privacy screen, not
    /// encryption — note content stays cleartext on disk (decision 2026-10-12, NS-02 option C)
    /// — but the hash itself resists offline rainbow-table/brute-force attack.
    ///
    /// Format: "v2:{iterations}:{saltBase64}:{hashBase64}" for new hashes. A stored
    /// value that doesn't start with "v2:" is a legacy 64-char lowercase-hex unsalted
    /// SHA-256 hash (pre-2026-09-07) — Verify() still accepts it so existing PINs and
    /// the master passphrase keep working, and reports needsRehash so the caller can
    /// transparently upgrade it to v2 on the next successful unlock.
    /// </summary>
    internal static class PinHasher
    {
        // OWASP-recommended minimum iteration count for PBKDF2-HMAC-SHA256 (2023 guidance).
        private const int Iterations = 210_000;
        // Stored iteration count is attacker-controllable (prefs/note JSON): reject values that
        // would downgrade the hash (v2:1:…) or freeze the UI (v2:2147483647:…).
        internal const int MinIterations = 100_000;
        internal const int MaxIterations = 1_000_000;
        private const int SaltBytes  = 16;
        private const int HashBytes  = 32;

        public static string CreateHash(string input)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(input), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
            return $"v2:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// True if <paramref name="input"/> matches <paramref name="storedHash"/>.
        /// <paramref name="needsRehash"/> comes back true when the match was against a
        /// legacy unsalted-SHA-256 hash — the caller should persist
        /// <c>PinHasher.CreateHash(input)</c> in place of the old value to upgrade it.
        /// </summary>
        public static bool Verify(string input, string storedHash, out bool needsRehash)
        {
            needsRehash = false;
            if (string.IsNullOrEmpty(storedHash)) return false;

            if (storedHash.StartsWith("v2:", StringComparison.Ordinal))
            {
                string[] parts = storedHash.Split(':');
                if (parts.Length != 4) return false;
                if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int iterations)) return false;
                if (iterations < MinIterations || iterations > MaxIterations) return false;

                byte[] salt, expected;
                try
                {
                    salt     = Convert.FromBase64String(parts[2]);
                    expected = Convert.FromBase64String(parts[3]);
                }
                catch (FormatException) { return false; }
                if (salt.Length < 8 || expected.Length < 16 || expected.Length > 64) return false;

                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(input), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }

            // Legacy: unsalted single-round SHA-256 hex (pre-2026-09-07). Accepted once so the
            // caller can upgrade it (needsRehash) — remove after 2026.11 release.
            if (storedHash.Length != 64) return false;
            byte[] legacyHash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            string legacyHex  = Convert.ToHexString(legacyHash).ToLowerInvariant();
            bool matched      = string.Equals(legacyHex, storedHash, StringComparison.Ordinal);
            if (matched) needsRehash = true;
            return matched;
        }
    }
}
