using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>Steam's manifests and libraries in the public format (EXPERIMENTAL: not yet seen on the user's PC, 10-11).</summary>
public class SteamTests
{
    private static string Manifest(long flags, long toDownload = 0, long downloaded = 0, long toStage = 0, long staged = 0) => $$"""
        "AppState"
        {
        	"appid"		"620"
        	"universe"		"1"
        	"name"		"Portal 2"
        	"StateFlags"		"{{flags}}"
        	"installdir"		"Portal 2"
        	"SizeOnDisk"		"12345"
        	"BytesToDownload"		"{{toDownload}}"
        	"BytesDownloaded"		"{{downloaded}}"
        	"BytesToStage"		"{{toStage}}"
        	"BytesStaged"		"{{staged}}"
        	"InstalledDepots"
        	{
        		"621"
        		{
        			"manifest"		"1"
        			"size"		"999"
        		}
        	}
        }
        """;

    [Fact]
    public void A_manifest_gives_the_name_the_flags_and_how_far()
    {
        var m = SteamManifest.Parse(Manifest(1026, toDownload: 4000, downloaded: 1000));

        Assert.NotNull(m);
        Assert.Equal(620, m.AppId);
        Assert.Equal("Portal 2", m.Name);
        Assert.Equal(1026, m.Flags);
        Assert.Equal((1000L, 4000L), m.Progress);
        Assert.False(m.Installed);
    }

    [Fact]
    public void Nothing_to_download_falls_back_to_staging_and_then_to_no_bar()
    {
        Assert.Equal((50L, 200L), SteamManifest.Parse(Manifest(1030, toStage: 200, staged: 50))!.Progress);
        Assert.Null(SteamManifest.Parse(Manifest(1030))!.Progress);
    }

    [Fact]
    public void Not_a_manifest_is_null()
        => Assert.Null(SteamManifest.Parse("\"AppState\" { \"name\" \"x\" }"));

    [Fact]
    public void Libraries_are_every_path_unescaped()
    {
        const string text = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"apps" { "228980" "1" }
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            	}
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamVdf.Libraries(text));
    }

    [Fact]
    public void A_new_game_downloads_then_is_installed()
    {
        var tally = new SteamTally();
        Assert.Equal(SteamPhase.Downloading, tally.See(SteamManifest.Parse(Manifest(1026, 100, 10)), updating: true));
        Assert.Equal(SteamPhase.Installed, tally.See(SteamManifest.Parse(Manifest(4)), updating: false));
        Assert.Equal(SteamPhase.None, tally.See(SteamManifest.Parse(Manifest(4)), updating: false));   // said once
    }

    [Fact]
    public void An_installed_game_is_updated()
    {
        var tally = new SteamTally();
        Assert.Equal(SteamPhase.Downloading, tally.See(SteamManifest.Parse(Manifest(1030, 100, 10)), updating: true));
        Assert.Equal(SteamPhase.Updated, tally.See(SteamManifest.Parse(Manifest(4)), updating: false));
    }

    [Fact]
    public void Paused_then_resumed()
    {
        var tally = new SteamTally();
        tally.See(SteamManifest.Parse(Manifest(1026, 100, 10)), updating: true);
        Assert.Equal(SteamPhase.Paused, tally.See(SteamManifest.Parse(Manifest(1538, 100, 40)), updating: true));
        Assert.Equal(SteamPhase.Paused, tally.See(SteamManifest.Parse(Manifest(1538, 100, 40)), updating: false));
        Assert.Equal(SteamPhase.Downloading, tally.See(SteamManifest.Parse(Manifest(1026, 100, 40)), updating: true));
    }

    [Fact]
    public void A_paused_download_from_before_is_not_shown()
        => Assert.Equal(SteamPhase.None, new SteamTally().See(SteamManifest.Parse(Manifest(1538, 100, 40)), updating: false));

    [Fact]
    public void Uninstalled_while_downloading_says_nothing()
    {
        var tally = new SteamTally();
        tally.See(SteamManifest.Parse(Manifest(1026, 100, 10)), updating: true);
        Assert.Equal(SteamPhase.None, tally.See(null, updating: false));
        Assert.Equal(SteamPhase.None, tally.See(SteamManifest.Parse(Manifest(4)), updating: false));
    }
}
