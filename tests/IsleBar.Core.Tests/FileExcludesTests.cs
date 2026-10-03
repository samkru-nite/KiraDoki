using IsleBar.Core.Configuration;
using IsleBar.Core.Search;
using Xunit;

namespace IsleBar.Core.Tests;

public class FileExcludesTests
{
    private static readonly IReadOnlyList<FileExcludes.Rule> Defaults =
        FileExcludes.Parse(FileExcludes.Defaults, line => line.Replace("%SystemRoot%", @"C:\Windows"));

    [Theory]
    [InlineData(@"C:\Windows\Prefetch\DEEPSEEK HARNESS.EXE-250A3CF3.pf")]
    [InlineData(@"C:\System Volume Information\K4W-21-26\klobjdb.dat")]
    [InlineData(@"C:\Users\Kasumi\AppData\Local\Temp\x.tmp")]
    [InlineData(@"C:\Users\Kasumi\AppData\Local\Microsoft\Windows\INetCache\IE\x.json")]
    [InlineData(@"C:\Users\Kasumi\AppData\Local\Microsoft\Edge\User Data\Default\Cache\Cache_Data\f_000001")]
    [InlineData(@"C:\$WINDOWS.~BT\Sources\setup.exe")]
    public void Hidden_by_default(string path)
        => Assert.True(FileExcludes.IsHidden(path, Defaults));

    [Theory]
    [InlineData(@"D:\code\tool\node_modules\react\index.js")]
    [InlineData(@"D:\code\tool\node_modules")]                     // the folder itself too
    [InlineData(@"D:\code\tool\.git\HEAD")]
    [InlineData(@"E:\Photos\Lightroom Catalog Previews.lrdata\a\b.lrprev")]   // *.lrdata
    [InlineData(@"E:\Photos\Lightroom Catalog Smart Previews.lrdata")]
    public void Hidden_by_rules_a_person_adds(string path)
    {
        Assert.False(FileExcludes.IsHidden(path, Defaults));   // not in the small built-in list…
        Assert.True(FileExcludes.IsHidden(path, FileExcludes.Parse([.. FileExcludes.Defaults, "node_modules", ".git", "*.lrdata"], s => s)));
    }

    [Theory]
    [InlineData(@"C:\Users\Kasumi\AppData\Local\Programs\DeepSeek Harness\DeepSeek Harness.exe")]
    [InlineData(@"C:\ProgramData\Autodesk\RVT 2025\Libraries\Chinese\门.rfa")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\WeChat.lnk")]
    [InlineData(@"C:\Users\Kasumi\AppData\Roaming\Adobe\CameraRaw\Settings\胶片.xmp")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\$R1.dwg")]
    [InlineData(@"C:\WindowsApps\thing.txt")]
    [InlineData(@"D:\code\tool\bin\Release\tool.exe")]
    [InlineData(@"D:\项目\CacheStudy.rvt")]                         // a file merely named like a hidden folder
    [InlineData(@"D:\code\objects\model.obj")]
    [InlineData(@"E:\Photos\Lightroom Catalog.lrcat")]                     // the catalog itself stays
    [InlineData(@"E:\Photos\2026\DSC_0001.NEF")]
    public void Shown(string path)
        => Assert.False(FileExcludes.IsHidden(path, Defaults));

    [Fact]
    public void A_star_matches_within_one_folder_name_only()
    {
        var rules = FileExcludes.Parse(["*.lrdata", @"D:\Sim*"], s => s);
        Assert.True(FileExcludes.IsHidden(@"E:\x\A Previews.lrdata\1.lrprev", rules));
        Assert.True(FileExcludes.IsHidden(@"D:\Simulation\run1\out.rst", rules));
        Assert.False(FileExcludes.IsHidden(@"E:\Sim\out.rst", rules));           // rooted at D: only
        Assert.False(FileExcludes.IsHidden(@"E:\x\notes.txt", rules));
    }

    [Fact]
    public void Lines_are_cleaned_up()
    {
        var rules = FileExcludes.Parse(["  # a note", "", @"D:\Scratch\", "node_modules", "/build/", "NODE_MODULES"], s => s);
        Assert.Equal(
            [new FileExcludes.Rule(@"D:\Scratch\", true), new FileExcludes.Rule(@"\node_modules\", false), new FileExcludes.Rule(@"\build\", false)],
            rules);
    }

    [Fact]
    public void Unset_means_the_defaults_and_an_edited_list_is_saved()
    {
        Assert.Null(new IsleBarSettings().FileExclude);
        Assert.Equal(FileExcludes.Defaults, new IsleBarSettings().FileExcludeLines);

        var raw = new System.Text.Json.Nodes.JsonObject();
        SettingsCodec.Apply(new IsleBarSettings(), raw);
        Assert.False(raw.ContainsKey("file_exclude"));      // untouched: later default changes still reach this person

        SettingsCodec.Apply(new IsleBarSettings { FileExclude = ["node_modules", @"D:\Scratch"] }, raw);
        var back = SettingsCodec.FromJson(raw);
        Assert.Equal(["node_modules", @"D:\Scratch"], back.FileExclude);
        Assert.Equal(["node_modules", @"D:\Scratch"], back.Clone().FileExclude);
    }
}
