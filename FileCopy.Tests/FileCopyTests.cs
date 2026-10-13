using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileCopy;
using Xunit;

namespace FileCopy.Tests
{
    public sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fc_test_" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public string Sub(string name) { var p = System.IO.Path.Combine(Path, name); Directory.CreateDirectory(p); return p; }
        public void Dispose()
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(Path, recursive: true);
            }
            catch { }
        }
    }

    public class PartialFileTests
    {
        [Fact]
        public void Finalize_ReplacesExistingDestination()
        {
            using var d = new TempDir();
            string final = Path.Combine(d.Path, "a.bin");
            File.WriteAllText(final, "old");
            File.WriteAllText(PartialFile.GetTempPath(final), "new");
            File.WriteAllText(PartialFile.GetMetaPath(final), "{}");

            PartialFile.Finalize(final);

            Assert.Equal("new", File.ReadAllText(final));
            Assert.False(File.Exists(PartialFile.GetTempPath(final)));
            Assert.False(File.Exists(PartialFile.GetMetaPath(final)));
        }

        [Fact]
        public void Finalize_ReplacesReadOnlyDestination()
        {
            using var d = new TempDir();
            string final = Path.Combine(d.Path, "ro.bin");
            File.WriteAllText(final, "old");
            File.SetAttributes(final, FileAttributes.ReadOnly);
            File.WriteAllText(PartialFile.GetTempPath(final), "new");

            PartialFile.Finalize(final);

            Assert.Equal("new", File.ReadAllText(final));
        }
    }

    public class CopyEngineTests
    {
        private static async Task<List<FileItem>> RunAsync(string src, string dst, bool verify, bool delete)
        {
            var items = CopyEngine.Enumerate(src, dst, true, "*.*", "", CancellationToken.None);
            CopyEngine.PlanActions(items, ConflictMode.Overwrite);
            var opts = new CopyEngine.CopyOptions
            {
                VerifyCrc = verify, DeleteSource = delete, BufferSizeKb = 64, MaxParallel = 1, DestRoot = dst,
            };
            await CopyEngine.RunAsync(items, opts, new Progress<CopyProgress>(), CancellationToken.None);
            return items;
        }

        [Fact]
        public async Task Move_WithVerifyOff_StillVerifies_ThenDeletesSource()   // FC-01
        {
            using var d = new TempDir();
            string src = d.Sub("src"), dst = d.Sub("dst");
            File.WriteAllText(Path.Combine(src, "a.txt"), "hello world");

            var items = await RunAsync(src, dst, verify: false, delete: true);

            Assert.Single(items);
            Assert.Equal(FileAction.Verified, items[0].Action);
            Assert.False(string.IsNullOrEmpty(items[0].DestCrc));
            Assert.False(File.Exists(Path.Combine(src, "a.txt")));
            Assert.Equal("hello world", File.ReadAllText(Path.Combine(dst, "a.txt")));
        }

        [Fact]
        public async Task Copy_WithVerifyOff_KeepsSource_NoVerify()
        {
            using var d = new TempDir();
            string src = d.Sub("src"), dst = d.Sub("dst");
            File.WriteAllText(Path.Combine(src, "b.txt"), "data");

            var items = await RunAsync(src, dst, verify: false, delete: false);

            Assert.Equal(FileAction.Done, items[0].Action);
            Assert.True(File.Exists(Path.Combine(src, "b.txt")));
        }

        [Fact]
        public async Task Copy_OverwritesExisting_Atomically()   // FC-02 path through engine
        {
            using var d = new TempDir();
            string src = d.Sub("src"), dst = d.Sub("dst");
            File.WriteAllText(Path.Combine(src, "c.txt"), "fresh");
            File.WriteAllText(Path.Combine(dst, "c.txt"), "stale");

            var items = await RunAsync(src, dst, verify: true, delete: false);

            Assert.Equal(FileAction.Verified, items[0].Action);
            Assert.Equal("fresh", File.ReadAllText(Path.Combine(dst, "c.txt")));
            Assert.False(File.Exists(PartialFile.GetTempPath(Path.Combine(dst, "c.txt"))));
        }

        [Fact]
        public void Enumerate_MissingSource_ReturnsEmpty_NoSkips()
        {
            var skipped = new List<string>();
            var items = CopyEngine.Enumerate(@"Z:\does\not\exist\" + Guid.NewGuid(), Path.GetTempPath(), true, "*.*", "",
                                             CancellationToken.None, skipped: skipped);
            Assert.Empty(items);
            Assert.Empty(skipped);
        }

        [Fact]
        public void Enumerate_NestedFolders_RelativePaths()
        {
            using var d = new TempDir();
            string src = d.Sub("src"), dst = d.Sub("dst");
            Directory.CreateDirectory(Path.Combine(src, "x", "y"));
            File.WriteAllText(Path.Combine(src, "x", "y", "z.txt"), "z");
            var items = CopyEngine.Enumerate(src, dst, true, "*.*", "", CancellationToken.None);
            Assert.Single(items);
            Assert.Equal(Path.Combine("x", "y", "z.txt"), items[0].RelativePath);
            Assert.Equal(Path.Combine(dst, "x", "y", "z.txt"), items[0].DestPath);
        }

        [Theory]
        [InlineData(typeof(UnauthorizedAccessException), "access denied")]
        [InlineData(typeof(DirectoryNotFoundException), "folder no longer exists")]
        [InlineData(typeof(PathTooLongException), "path too long")]
        public void SkipReason_MapsExceptions(Type t, string expected)   // FC-10
        {
            var ex = (Exception)Activator.CreateInstance(t)!;
            Assert.Equal(expected, CopyEngine.SkipReason(ex));
        }

        public static TheoryData<ConflictMode, int, FileAction> Conflicts => new()
        {
            { ConflictMode.Overwrite,        -60, FileAction.Overwrite },
            { ConflictMode.OverwriteIfNewer,  60, FileAction.Overwrite },
            { ConflictMode.OverwriteIfNewer, -60, FileAction.Skip },
            { ConflictMode.OverwriteIfNewer,   1, FileAction.Skip },     // within 2 s tolerance
            { ConflictMode.OverwriteIfOlder, -60, FileAction.Overwrite },
            { ConflictMode.SkipIfSameDate,     0, FileAction.Skip },
            { ConflictMode.SkipIfSameDate,    60, FileAction.Overwrite },
            { ConflictMode.SkipAll,           60, FileAction.Skip },
            { ConflictMode.Rename,            60, FileAction.Rename },
        };

        [Theory, MemberData(nameof(Conflicts))]
        public void ResolveConflictAction_Matrix(ConflictMode mode, int srcMinusDstSeconds, FileAction expected)
        {
            var dst = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var src = dst.AddSeconds(srcMinusDstSeconds);
            Assert.Equal(expected, CopyEngine.ResolveConflictAction(src, dst, destExists: true, mode));
        }

        [Fact]
        public void ResolveConflictAction_NoDest_IsCopy() =>
            Assert.Equal(FileAction.Copy, CopyEngine.ResolveConflictAction(DateTime.UtcNow, null, false, ConflictMode.SkipAll));
    }

    public class MiscTests
    {
        [Fact]
        public void SizeFormat_EnUs()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                Assert.Equal("1,023 B", SizeFormat.Format(1023));
                Assert.Equal("1.5 KB", SizeFormat.Format(1536));
                Assert.Equal("2 GB", SizeFormat.Format(2L * 1024 * 1024 * 1024));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        [Fact]
        public void InstanceArgs_JsonRoundTrip()
        {
            var a = new InstanceArgs(@"C:\src ""q""", @"\\nas\dst", true, @"C:\jobs\x.fcjob");
            string json = JsonSerializer.Serialize(a, InstanceArgsContext.Default.InstanceArgs);
            var b = JsonSerializer.Deserialize(json, InstanceArgsContext.Default.InstanceArgs);
            Assert.Equal(a, b);
        }
    }
}
