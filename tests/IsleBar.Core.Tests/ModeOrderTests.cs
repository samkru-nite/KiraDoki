using IsleBar.Core.Configuration;
using IsleBar.Core.Localization;
using Xunit;

namespace IsleBar.Core.Tests;

public class ModeOrderTests
{
    private static readonly BarMode[] FilesFirst = [BarMode.Files, BarMode.Claude];

    [Theory]
    [InlineData(BarMode.Files, true, BarMode.Claude)]
    [InlineData(BarMode.Claude, true, BarMode.Files)]
    [InlineData(BarMode.Claude, false, BarMode.Claude)]   // files skipped when file search is off
    public void Cycles_in_configured_order(BarMode now, bool filesOn, BarMode next)
        => Assert.Equal(next, BarModes.Next(now, FilesFirst, filesOn));

    [Fact]
    public void First_is_default_mode()
    {
        Assert.Equal(BarMode.Files, BarModes.First(FilesFirst, filesOn: true));
        Assert.Equal(BarMode.Claude, BarModes.First(FilesFirst, filesOn: false));
    }

    [Theory]
    [InlineData(new[] { "files", "claude" }, "files,claude")]
    [InlineData(new[] { "files" }, "files,claude")]                        // missing ones appended in default order
    [InlineData(new[] { "web", "files", "files", "모름" }, "files,claude")]  // duplicates, unknown names and the removed web mode dropped
    [InlineData(new string[0], "claude,files")]
    public void Normalizes_saved_order(string[] saved, string expected)
        => Assert.Equal(expected, string.Join(",", BarModes.Normalize(saved).Select(BarModes.Key)));

    [Fact]
    public void Is_saved_to_settings_and_read_back()
    {
        Assert.Equal(["claude", "files"], new IsleBarSettings().ModeOrder);
        var json = new System.Text.Json.Nodes.JsonObject();
        SettingsCodec.Apply(new IsleBarSettings { ModeOrder = ["files", "claude"] }, json);
        Assert.Equal(["files", "claude"], SettingsCodec.FromJson(json).ModeOrder);

        json["mode_order"] = new System.Text.Json.Nodes.JsonArray("files", "files");
        Assert.Equal(["files", "claude"], SettingsCodec.FromJson(json).ModeOrder);
    }
}
