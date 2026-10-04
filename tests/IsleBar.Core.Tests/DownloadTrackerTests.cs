using IsleBar.Core.Island;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class DownloadTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static PartialDownload File(string name, long size, DateTimeOffset written)
        => new($@"C:\Downloads\{name}.crdownload", name, size, written);

    [Fact]
    public void Nothing_under_way_shows_nothing()
    {
        Assert.Null(new DownloadTracker().Update(T0, []));
    }

    [Fact]
    public void Speed_is_the_growth_over_the_last_seconds()
    {
        var tracker = new DownloadTracker();
        var first = tracker.Update(T0, [File("a.zip", 0, T0)])!;
        Assert.Null(first.Speed);   // nothing to compare with yet
        Assert.Equal("download", first.Stage);

        ActivityState? item = null;
        for (var s = 1; s <= 6; s++)
        {
            item = tracker.Update(T0.AddSeconds(s), [File("a.zip", s * 1_000_000L, T0.AddSeconds(s))]);
        }

        Assert.Equal(1_000_000, item!.Speed!.Value, 1);
        Assert.Equal("a.zip", item.Name);
        Assert.Equal(0, item.MoreCount);
    }

    [Fact]
    public void Several_downloads_become_one_item_with_summed_speed_and_the_earliest_name()
    {
        var tracker = new DownloadTracker();
        tracker.Update(T0, [File("first.zip", 0, T0)]);
        tracker.Update(T0.AddSeconds(1), [File("first.zip", 100, T0), File("second.iso", 0, T0)]);
        var item = tracker.Update(T0.AddSeconds(2), [File("first.zip", 200, T0), File("second.iso", 300, T0)])!;
        Assert.Equal("first.zip", item.Name);
        Assert.Equal(1, item.MoreCount);
        Assert.Equal(100 + 300, item.Speed!.Value, 1);
    }

    [Fact]
    public void A_file_that_stops_growing_reads_as_paused_and_stays()
    {
        var tracker = new DownloadTracker();
        tracker.Update(T0, [File("a.zip", 100, T0)]);
        Assert.Equal("download", tracker.Update(T0.AddSeconds(4), [File("a.zip", 100, T0)])!.Stage);

        var paused = tracker.Update(T0.AddSeconds(6), [File("a.zip", 100, T0)])!;
        Assert.True(paused.IsPausedTransfer);
        Assert.NotNull(tracker.Update(T0.AddHours(3), [File("a.zip", 100, T0)]));   // stays until its file goes away

        var resumed = tracker.Update(T0.AddHours(3).AddSeconds(1), [File("a.zip", 200, T0.AddHours(3))])!;
        Assert.Equal("download", resumed.Stage);
    }

    [Fact]
    public void A_paused_download_gives_its_name_to_one_still_moving()
    {
        var tracker = new DownloadTracker();
        tracker.Update(T0, [File("old.zip", 100, T0), File("new.zip", 0, T0)]);
        var item = tracker.Update(T0.AddSeconds(10), [File("old.zip", 100, T0), File("new.zip", 5000, T0.AddSeconds(10))])!;
        Assert.Equal("new.zip", item.Name);
        Assert.Equal(1, item.MoreCount);
        Assert.Equal("download", item.Stage);
    }

    [Fact]
    public void A_leftover_partial_file_from_earlier_is_ignored_and_gone_files_are_dropped()
    {
        var tracker = new DownloadTracker();
        Assert.Null(tracker.Update(T0, [File("leftover.zip", 100, T0.AddDays(-2))]));

        tracker.Update(T0, [File("a.zip", 100, T0)]);
        Assert.Single(tracker.Tracked);
        Assert.Null(tracker.Update(T0.AddSeconds(1), []));
        Assert.Empty(tracker.Tracked);
    }

    [Fact]
    public void Known_totals_give_one_fraction_for_all_and_one_unknown_keeps_the_bar_flowing()
    {
        var tracker = new DownloadTracker();
        var baidu = File("a.mp4", 250, T0) with { Total = 1000 };   // reserved at full size; received counted elsewhere
        var other = File("b.mp4", 250, T0) with { Total = 1000 };
        var item = tracker.Update(T0, [baidu, other])!;
        Assert.Equal(0.25, item.Fraction!.Value, 3);
        Assert.Equal("download", item.Stage);   // the text still shows the speed

        var edge = File("c.zip", 10, T0);
        Assert.Null(tracker.Update(T0.AddSeconds(1), [baidu, other, edge])!.Fraction);
    }

    [Fact]
    public void A_download_without_a_name_yet_keeps_none()
    {
        var item = new DownloadTracker().Update(T0, [new PartialDownload(@"C:\Downloads\Unconfirmed 1.crdownload", null, 5, T0)])!;
        Assert.Null(item.Name);   // the pill says "Downloading" instead of "Unconfirmed 1"
    }

    [Fact]
    public void Download_folders_are_saved_and_empty_means_the_system_folder()
    {
        Assert.Empty(new IsleBar.Core.Configuration.IsleBarSettings().DownloadFolders);
        var raw = new System.Text.Json.Nodes.JsonObject();
        IsleBar.Core.Configuration.SettingsCodec.Apply(new IsleBar.Core.Configuration.IsleBarSettings(), raw);
        Assert.False(raw.ContainsKey("download_folders"));

        var settings = new IsleBar.Core.Configuration.IsleBarSettings { DownloadFolders = [@"D:\Downloads", @"E:\BaiduNetdiskDownload"] };
        IsleBar.Core.Configuration.SettingsCodec.Apply(settings, raw);
        var back = IsleBar.Core.Configuration.SettingsCodec.FromJson(raw);
        Assert.Equal([@"D:\Downloads", @"E:\BaiduNetdiskDownload"], back.DownloadFolders);
        Assert.Equal([@"D:\Downloads", @"E:\BaiduNetdiskDownload"], back.Clone().DownloadFolders);
    }

    [Fact]
    public void A_paused_download_waits_behind_music_but_a_moving_one_does_not()
    {
        var music = new ActivityState { RawKind = ActivityState.KindMusic, UpdatedAt = T0 };
        var paused = new ActivityState { RawKind = ActivityState.KindTransfer, Stage = "paused", State = "run", UpdatedAt = T0 };
        var moving = new ActivityState { RawKind = ActivityState.KindTransfer, Stage = "download", State = "run", UpdatedAt = T0 };
        var done = new ActivityState { RawKind = ActivityState.KindAgentDone, UpdatedAt = T0 };
        Assert.Same(music, IslandSelector.Select([paused, music]).Primary);
        Assert.Same(paused, IslandSelector.Select([done, paused]).Primary);
        Assert.Same(moving, IslandSelector.Select([music, moving]).Primary);
    }
}
