using IsleBar.Core.Island;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>迅雷 / 夸克 / browsers: names and the folders their settings name (measured on the user's PC, 10-11).</summary>
public class DownloadersTests
{
    [Fact]
    public void Quark_finishes_without_its_id()
    {
        Assert.Equal("XMind26(64bit).zip", Downloaders.QuarkFinalName("XMind26(64bit)-c4d5541d929b.zip.qkdownloading"));
        Assert.Equal("notes", Downloaders.QuarkFinalName("notes-c4d5541d929b.qkdownloading"));   // no extension of its own
        Assert.Equal("a-b.zip", Downloaders.QuarkFinalName("a-b.zip.qkdownloading"));   // a dash that's no id stays
    }

    [Fact]
    public void Thunder_saves_to_its_task_default()
    {
        const string json = """
            {
              "TaskDefaultSettings": { "InitPath": "C:\\迅雷下载", "DefaultPath": "D:\\Downloads" },
              "ConfigNormalSession": { "defaultDownloadPath": "C:\\Users\\Kasumi\\Desktop" }
            }
            """;
        Assert.Equal(@"D:\Downloads", Downloaders.ThunderFolder(json));
    }

    [Fact]
    public void Quark_saves_where_its_preference_says()
        => Assert.Equal(@"C:\Users\Kasumi\Downloads",
            Downloaders.QuarkFolder("""{"downloadPosition":{"enable":false,"lastSavePath":"","where":"C:\\Users\\Kasumi\\Downloads"}}"""));

    [Fact]
    public void A_browser_without_its_own_folder_is_null_not_its_save_as_folder()
    {
        // the user's Edge, 10-11: "Save as…" last went to the desktop; downloads go to the Downloads folder (not set)
        const string json = """{"download":{"directory_upgrade":true},"savefile":{"default_directory":"C:\\Users\\Kasumi\\Desktop","type":1}}""";
        Assert.Null(Downloaders.ChromiumFolder(json));
        Assert.Equal(@"E:\Down", Downloaders.ChromiumFolder("""{"download":{"default_directory":"E:\\Down"}}"""));
    }

    [Fact]
    public void Unreadable_settings_are_null()
    {
        Assert.Null(Downloaders.ThunderFolder(string.Empty));
        Assert.Null(Downloaders.QuarkFolder("{ not json"));
        Assert.Null(Downloaders.ChromiumFolder("[]"));
    }

    [Fact]
    public void Their_downloads_are_named_by_program()
    {
        Assert.Equal("迅雷", DownloadSources.Of(@"C:\Users\Kasumi\Desktop\Topaz.7z.xltd", []));
        Assert.Equal("夸克网盘", DownloadSources.Of(@"C:\d\XMind-c4d5541d929b.zip.qkdownloading", []));
    }

    [Fact]
    public void Thunder_held_open_is_not_paused_however_slow()
    {
        var tracker = new DownloadTracker();
        var t0 = DateTimeOffset.UtcNow;
        PartialDownload Seen(bool open) => new(@"C:\d\a.zip.xltd", "a.zip", 0, t0, App: "thunder.exe", Moving: open);
        tracker.Update(t0, [Seen(true)]);
        var card = tracker.Update(t0 + TimeSpan.FromSeconds(30), [Seen(true)]);
        Assert.Equal("download", card?.Stage);   // nothing counted (too slow to tell from its writes), still under way
        Assert.Null(card?.Speed);   // no "0 KB/s"

        card = tracker.Update(t0 + TimeSpan.FromSeconds(40), [Seen(false)]);
        Assert.Equal("paused", card?.Stage);   // closed: paused
    }

    [Fact]
    public void One_program_s_downloads_carry_its_icon()
    {
        var tracker = new DownloadTracker();
        var now = DateTimeOffset.UtcNow;
        var card = tracker.Update(now, [new PartialDownload(@"C:\d\a.zip.xltd", "a.zip", 0, now, App: "thunder.exe")]);
        Assert.Equal("thunder.exe", card?.AppId);

        card = tracker.Update(now, [new PartialDownload(@"C:\d\a.zip.xltd", "a.zip", 0, now, App: "thunder.exe"),
            new PartialDownload(@"C:\d\b.zip.crdownload", "b.zip", 10, now)]);
        Assert.Null(card?.AppId);   // mixed: the plain arrow
    }
}
