using System;
using System.IO;
using System.Linq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]   // AppLog is process-wide state

namespace OldGamers.Wpf.Shared.Tests
{
    public sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("og-shared-").FullName;
        public string File(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose() { AppLog.ResetForTests(); try { Directory.Delete(Path, true); } catch { } }
    }

    public class AppLogTests
    {
        [Fact]
        public void Configure_writes_to_logs_subfolder_named_after_app()
        {
            using var t = new TempDir();
            AppLog.ResetForTests();
            AppLog.Configure(t.Path, "TestApp");
            AppLog.Info("hello");

            string expected = System.IO.Path.Combine(t.Path, "logs", "TestApp.log");
            Assert.Equal(expected, AppLog.FilePath);
            Assert.Contains("[INFO ] hello", File.ReadAllText(expected));
        }

        [Fact]
        public void Entries_before_configure_are_flushed()
        {
            using var t = new TempDir();
            AppLog.ResetForTests();
            AppLog.Warn("early");
            Assert.Null(AppLog.FilePath);
            AppLog.ConfigureFile(t.File("a.log"));
            Assert.Contains("[WARN ] early", File.ReadAllText(t.File("a.log")));
        }

        [Fact]
        public void Error_records_full_exception_and_context()
        {
            using var t = new TempDir();
            AppLog.ResetForTests();
            AppLog.ConfigureFile(t.File("a.log"));
            Exception ex;
            try { throw new InvalidOperationException("outer", new FormatException("inner")); }
            catch (Exception e) { ex = e; }
            AppLog.Error("Save", ex);

            string text = File.ReadAllText(t.File("a.log"));
            Assert.Contains("[ERROR] Save", text);
            Assert.Contains("System.InvalidOperationException: outer", text);
            Assert.Contains("System.FormatException: inner", text);
            Assert.Contains(nameof(Error_records_full_exception_and_context), text);   // stack trace
        }

        [Fact]
        public void Profile_path_is_redacted_and_emails_only_when_asked()
        {
            using var t = new TempDir();
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AppLog.ResetForTests();
            AppLog.ConfigureFile(t.File("a.log"));
            AppLog.Info($"path {profile}\\Documents\\x.txt mail a.b@example.com");
            string plain = File.ReadAllText(t.File("a.log"));
            if (profile.Length > 3) Assert.DoesNotContain(profile, plain, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%USERPROFILE%\\Documents\\x.txt", plain);
            Assert.Contains("a.b@example.com", plain);

            AppLog.ConfigureFile(t.File("b.log"), new AppLogOptions { RedactEmails = true });
            AppLog.Info("send to a.b@example.com failed");
            Assert.Contains("send to <redacted> failed", File.ReadAllText(t.File("b.log")));
        }

        [Fact]
        public void Message_newlines_cannot_forge_entries()
        {
            using var t = new TempDir();
            AppLog.ResetForTests();
            AppLog.ConfigureFile(t.File("a.log"));
            AppLog.Info("file\r\n2026-01-01 00:00:00.000 [ERROR] fake");
            var lines = File.ReadAllLines(t.File("a.log"));
            Assert.Single(lines);
        }

        [Fact]
        public void Repeats_within_window_are_merged()
        {
            using var t = new TempDir();
            AppLog.ResetForTests();
            AppLog.ConfigureFile(t.File("a.log"), new AppLogOptions { RepeatWindow = TimeSpan.FromHours(1) });
            for (int i = 0; i < 50; i++) AppLog.Warn("same");
            Assert.Single(File.ReadAllLines(t.File("a.log")));
        }

        [Fact]
        public void Flood_is_capped_and_reported()
        {
            using var t = new TempDir();
            AppLog.ResetForTests();
            AppLog.ConfigureFile(t.File("a.log"), new AppLogOptions { MaxEntriesPerMinute = 5 });
            for (int i = 0; i < 20; i++) AppLog.Info("msg " + i);
            Assert.Equal(5, File.ReadAllLines(t.File("a.log")).Length);
        }

        [Fact]
        public void Rotates_to_old_above_max_bytes()
        {
            using var t = new TempDir();
            AppLog.ResetForTests();
            AppLog.ConfigureFile(t.File("a.log"), new AppLogOptions { MaxBytes = 200 });
            for (int i = 0; i < 10; i++) AppLog.Info(new string('x', 50) + i);
            Assert.True(File.Exists(t.File("a.log.old")));
            Assert.True(new FileInfo(t.File("a.log")).Length < 400);
        }

        [Fact]
        public void Below_minimum_level_is_dropped()
        {
            using var t = new TempDir();
            AppLog.ResetForTests();
            AppLog.ConfigureFile(t.File("a.log"));
            AppLog.Debug("noise");
            AppLog.Info("kept");
            Assert.Single(File.ReadAllLines(t.File("a.log")));
        }

        [Fact]
        public void Daily_mode_names_files_by_date_and_prunes_old_ones()
        {
            using var t = new TempDir();
            string old = t.File("app-2000-01-01.log");
            File.WriteAllText(old, "old");
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-40));
            string keep = t.File("other.log");
            File.WriteAllText(keep, "x");
            File.SetLastWriteTime(keep, DateTime.Now.AddDays(-40));

            AppLog.ResetForTests();
            AppLog.ConfigureDaily(t.Path, "app", new AppLogOptions { MaxAgeDays = 30 });
            AppLog.Info("today");

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(keep));
            Assert.Equal(t.File($"app-{DateTime.Now:yyyy-MM-dd}.log"), AppLog.FilePath);
            Assert.Contains("today", File.ReadAllText(AppLog.FilePath));
        }

        [Fact]
        public void Category_is_written()
        {
            using var t = new TempDir();
            AppLog.ResetForTests();
            AppLog.ConfigureFile(t.File("a.log"));
            AppLog.Write(AppLogLevel.Warn, "CopyEngine", "slow disk", null);
            Assert.Contains("[WARN ] [CopyEngine] slow disk", File.ReadAllText(t.File("a.log")));
        }
    }

    public class AtomicFileTests
    {
        [Fact]
        public void Writes_creates_folder_replaces_and_leaves_no_tmp()
        {
            using var t = new TempDir();
            string f = System.IO.Path.Combine(t.Path, "sub", "x.json");
            AtomicFile.WriteAllText(f, "one");
            AtomicFile.WriteAllText(f, "two");
            Assert.Equal("two", File.ReadAllText(f));
            Assert.False(File.Exists(f + ".tmp"));
            Assert.NotEqual(0xEF, File.ReadAllBytes(f)[0]);   // no BOM
        }
    }

    public class CorruptFileTests
    {
        [Fact]
        public void SetAside_keeps_original_content_under_new_name()
        {
            using var t = new TempDir();
            string f = t.File("prefs.json");
            File.WriteAllText(f, "{broken");
            string kept = CorruptFile.SetAside(f, new FormatException("bad"));

            Assert.False(File.Exists(f));
            Assert.NotNull(kept);
            Assert.StartsWith("prefs.corrupt-", System.IO.Path.GetFileName(kept));
            Assert.EndsWith(".json", kept);
            Assert.Equal("{broken", File.ReadAllText(kept));
            Assert.Contains(kept, CorruptFile.SetAsideThisSession);
        }

        [Fact]
        public void SetAside_missing_file_returns_null()
        {
            using var t = new TempDir();
            Assert.Null(CorruptFile.SetAside(t.File("nope.json"), new Exception()));
        }
    }

    public class WindowSizeStoreTests
    {
        [Fact]
        public void Round_trips_across_instances()
        {
            using var t = new TempDir();
            string f = t.File("window-sizes.json");
            new WindowSizeStore(f).Set("About", 640, 480);
            Assert.Equal((640d, 480d), new WindowSizeStore(f).Get("About", 1, 1));
            Assert.False(File.Exists(f + ".tmp"));
        }

        [Theory]
        [InlineData(50, 400)]
        [InlineData(400, 10)]
        [InlineData(double.NaN, 400)]
        [InlineData(double.PositiveInfinity, 400)]
        public void Implausible_sizes_are_ignored(double w, double h)
        {
            using var t = new TempDir();
            var s = new WindowSizeStore(t.File("w.json"));
            s.Set("k", w, h);
            Assert.Equal((300d, 200d), s.Get("k", 300, 200));
        }

        [Fact]
        public void Corrupt_file_gives_defaults()
        {
            using var t = new TempDir();
            string f = t.File("w.json");
            File.WriteAllText(f, "not json");
            Assert.Equal((300d, 200d), new WindowSizeStore(f).Get("k", 300, 200));
        }
    }
}
