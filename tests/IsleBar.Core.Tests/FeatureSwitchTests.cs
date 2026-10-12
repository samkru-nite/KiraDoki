using System.Text.Json.Nodes;
using IsleBar.Core.Configuration;
using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>One switch per feature (settings, user 10-12): all on by default, each saved and read back on its own.</summary>
public sealed class FeatureSwitchTests
{
    private static readonly (string Key, Func<IsleBarSettings, bool> Get, Action<IsleBarSettings, bool> Set)[] All =
    [
        ("notify_wechat", s => s.NotifyWeChat, (s, v) => s.NotifyWeChat = v),
        ("show_browser_downloads", s => s.ShowBrowserDownloads, (s, v) => s.ShowBrowserDownloads = v),
        ("show_downloaders", s => s.ShowDownloaders, (s, v) => s.ShowDownloaders = v),
        ("show_steam", s => s.ShowSteam, (s, v) => s.ShowSteam = v),
        ("show_file_ops", s => s.ShowFileOps, (s, v) => s.ShowFileOps = v),
        ("show_archivers", s => s.ShowArchivers, (s, v) => s.ShowArchivers = v),
        ("show_exports", s => s.ShowExports, (s, v) => s.ShowExports = v),
        ("show_printer", s => s.ShowPrinter, (s, v) => s.ShowPrinter = v),
        ("show_drives", s => s.ShowDrives, (s, v) => s.ShowDrives = v),
        ("show_transit", s => s.ShowTransit, (s, v) => s.ShowTransit = v),
        ("show_claude", s => s.ShowClaude, (s, v) => s.ShowClaude = v),
        ("show_chatgpt", s => s.ShowChatGpt, (s, v) => s.ShowChatGpt = v),
    ];

    [Fact]
    public void Every_switch_is_on_by_default_and_in_an_old_file()
    {
        var fresh = new IsleBarSettings();
        var old = SettingsCodec.FromJson(new JsonObject { ["lang"] = "zh" });   // a file from before the switches
        foreach (var (key, get, _) in All)
        {
            Assert.True(get(fresh), key);
            Assert.True(get(old), key);
        }
    }

    [Fact]
    public void Each_switch_is_saved_and_read_back_alone()
    {
        foreach (var (key, get, set) in All)
        {
            var settings = new IsleBarSettings();
            set(settings, false);
            var raw = new JsonObject();
            SettingsCodec.Apply(settings, raw);
            Assert.Equal(false, raw[key]?.GetValue<bool>());

            var back = SettingsCodec.FromJson(raw);
            Assert.False(get(back), key);
            Assert.Equal(All.Length - 1, All.Count(other => other.Get(back)));   // the others stay on
            Assert.False(get(back.Clone()), key);
        }
    }

    [Fact]
    public void Watcher_switches_follow_the_settings()
    {
        var settings = new IsleBarSettings { NotifyWeChat = false, ShowDrives = false, ShowTransit = false, ShowClaude = false, ShowChatGpt = false };
        var options = SystemWatchOptions.From(settings);
        Assert.False(options.WeChat);
        Assert.False(options.Drives);
        Assert.False(options.Transit);
        Assert.False(options.Claude);
        Assert.False(options.ChatGpt);
        Assert.True(SystemWatchOptions.From(new IsleBarSettings()) is { WeChat: true, Drives: true, Transit: true, Claude: true, ChatGpt: true });
    }
}
