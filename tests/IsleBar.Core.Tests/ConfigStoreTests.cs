using System.Text.Json.Nodes;
using IsleBar.Core.Configuration;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class ConfigStoreTests
{
    [Fact]
    public void Missing_file_gives_defaults()
    {
        using var dir = new TempDir();
        var settings = new ConfigStore(dir.File("islebar.json")).Load();

        Assert.Equal("auto", settings.Lang);
        Assert.True(settings.NotifyPower);
        Assert.False(settings.NotifyToasts);
        Assert.Equal("", settings.CalendarIcs);
        Assert.Equal([5, 25], settings.QuickTimers);
    }

    [Theory]
    [InlineData("{ 이건 JSON이 아니다")]
    [InlineData("")]
    [InlineData("[1, 2, 3]")]          // JSON but not an object
    [InlineData("\"그냥 문자열\"")]
    [InlineData("null")]
    public void Broken_JSON_gives_defaults_and_keeps_the_file(string content)
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        File.WriteAllText(path, content);

        var settings = new ConfigStore(path).Load();

        Assert.Equal("auto", settings.Lang);
        Assert.True(settings.NotifyPower);
        Assert.True(File.Exists(path));                     // never delete the user's file
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void Saving_over_a_broken_file_keeps_a_copy_of_it_first()
    {
        // A truncated settings file used to be replaced by defaults on the bar's first save — history and all gone (10-01).
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        const string broken = "{ \"drop_command\": \"pythonw send.py\", \"history\": [\"안녕";
        File.WriteAllText(path, broken);
        var store = new ConfigStore(path);

        Assert.True(store.Update(s => { s.Lang = "ko"; return true; }));

        var copy = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "islebar.json.broken-*"));
        Assert.Equal(broken, File.ReadAllText(copy));
        Assert.Equal("ko", store.Load().Lang);

        // Once the file is valid again, later saves don't make more copies
        Assert.True(store.Update(s => { s.Lang = "en"; return true; }));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "islebar.json.broken-*"));
    }

    [Fact]
    public void Only_keys_with_wrong_type_fall_back_and_the_rest_survive()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        File.WriteAllText(path, """
        {
          "lang": 42,
          "calendar_ics": "https://example.com/cal.ics",
          "notify_power": "yes",
          "download_folders": "문자열인데 목록이어야 함",
          "manual_offset": "어제",
          "pomo_focus": 50
        }
        """);

        var settings = new ConfigStore(path).Load();

        Assert.Equal("auto", settings.Lang);                 // number → default
        Assert.Equal("https://example.com/cal.ics", settings.CalendarIcs);   // survives
        Assert.True(settings.NotifyPower);                   // "yes" isn't a bool → default
        Assert.Empty(settings.DownloadFolders);              // not a list → default
        Assert.Equal(12, settings.ManualOffset);             // not a number → default
        Assert.Equal(50, settings.PomodoroFocus);            // survives
    }

    [Fact]
    public void Value_not_in_choices_gives_default()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        File.WriteAllText(path, """
        { "lang": "sv", "position": "nowhere", "ball_side": "up" }
        """);

        var settings = new ConfigStore(path).Load();

        Assert.Equal("auto", settings.Lang);
        Assert.Equal("left", settings.Position);
        Assert.Equal("right", settings.BallSide);
    }

    [Fact]
    public void Known_language_code_is_kept()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        File.WriteAllText(path, """{ "lang": "zht" }""");

        Assert.Equal("zht", new ConfigStore(path).Load().Lang);
    }

    [Fact]
    public void Unknown_keys_are_preserved_on_save()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        File.WriteAllText(path, """
        { "lang": "ko", "내가_나중에_추가할_설정": { "깊은": [1, 2, 3] }, "theme": "dark" }
        """);

        var store = new ConfigStore(path);
        var settings = store.Load();
        settings.CalendarIcs = "/tmp/새폴더";
        Assert.True(store.Save(settings));

        var raw = store.LoadRaw();
        Assert.Equal("dark", (string?)raw["theme"]);
        Assert.Equal(3, raw["내가_나중에_추가할_설정"]!["깊은"]!.AsArray().Count);
        Assert.Equal("/tmp/새폴더", (string?)raw["calendar_ics"]);
        Assert.Equal("ko", (string?)raw["lang"]);
    }

    [Fact]
    public void Saved_values_read_back_unchanged()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        var store = new ConfigStore(path);

        var written = new IsleBarSettings
        {
            Lang = "ja",
            NotifyPower = false,
            DownloadFolders = [@"D:\다운로드", @"E:\받은 파일"],
            QuickTimers = [10, 45],
            BallSide = "left",
        };
        Assert.True(store.Save(written));

        var read = store.Load();
        Assert.Equal("ja", read.Lang);
        Assert.False(read.NotifyPower);
        Assert.Equal([@"D:\다운로드", @"E:\받은 파일"], read.DownloadFolders);
        Assert.Equal([10, 45], read.QuickTimers);
        Assert.Equal("left", read.BallSide);
    }

    [Fact]
    public void Korean_is_written_unescaped()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        var store = new ConfigStore(path);
        var settings = store.Load();
        settings.DownloadFolders = ["작업표시줄 검색창 만들어 줘"];
        Assert.True(store.Save(settings));

        Assert.Contains("작업표시줄 검색창 만들어 줘", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_of_removed_features_are_dropped_on_save()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        File.WriteAllText(path, """{ "files": true, "history": ["질문"], "mode_order": ["claude"], "values": { "rc": true }, "flags": [], "theme": "dark" }""");

        var store = new ConfigStore(path);
        Assert.True(store.Save(store.Load()));

        var raw = store.LoadRaw();
        Assert.Null(raw["files"]);
        Assert.Null(raw["history"]);
        Assert.Null(raw["mode_order"]);
        Assert.Null(raw["values"]);
        Assert.Null(raw["flags"]);
        Assert.Equal("dark", (string?)raw["theme"]);   // unknown keys still survive
    }

    [Fact]
    public void Concurrent_saves_from_many_threads_do_not_corrupt_the_file()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        var store = new ConfigStore(path);

        // Each thread adds a key with its own name — all must survive at the end (meaning read-modify-write never overlapped)
        const int threads = 8;
        const int rounds = 25;
        var failures = 0;
        Parallel.For(0, threads, t =>
        {
            for (var r = 0; r < rounds; r++)
            {
                var ok = store.Update(raw =>
                {
                    raw[$"thread{t}"] = r;
                    return true;
                });
                if (!ok)
                {
                    Interlocked.Increment(ref failures);
                }
            }
        });

        Assert.Equal(0, failures);
        var raw = store.LoadRaw();
        for (var t = 0; t < threads; t++)
        {
            Assert.Equal(rounds - 1, (int?)raw[$"thread{t}"]);
        }
    }

    [Fact]
    public async Task Concurrent_save_and_load_never_sees_broken_settings()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        var store = new ConfigStore(path);
        var seed = store.Load();
        seed.Lang = "ko";
        Assert.True(store.Save(seed));

        var stop = false;
        var writer = Task.Run(() =>
        {
            var n = 0;
            while (!Volatile.Read(ref stop))
            {
                store.Update(s =>
                {
                    s.CalendarIcs = $"질문 {n++}";
                    return true;
                });
            }
        });

        // Keep reading while writing. Thanks to the atomic swap "ko" must always be visible (falling back to the default "auto" means a half-written file was read).
        var reads = 0;
        for (var i = 0; i < 400; i++)
        {
            Assert.Equal("ko", store.Load().Lang);
            reads++;
        }

        Volatile.Write(ref stop, true);
        await writer.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(400, reads);
    }

    [Fact]
    public void No_temp_files_remain_after_saving()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        var store = new ConfigStore(path);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(store.Save(store.Load()));
        }

        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp-*"));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.lock"));   // DeleteOnClose
    }

    [Fact]
    public void Update_returning_false_does_not_write()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        var store = new ConfigStore(path);

        Assert.True(store.Update((IsleBarSettings s) =>
        {
            s.Lang = "de";
            return false;
        }));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Creates_missing_folder_when_saving()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "깊은", "폴더", "islebar.json");
        var store = new ConfigStore(path);
        Assert.True(store.Save(store.Load()));
        Assert.True(File.Exists(path));
    }
}
