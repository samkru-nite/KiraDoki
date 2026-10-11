using IsleBar.Core.Island;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>Tests of one program's downloads: its one card, or null when none is under way.</summary>
internal static class DownloadTrackerOne
{
    public static ActivityState? One(this DownloadTracker tracker, DateTimeOffset now, IReadOnlyList<PartialDownload> files)
        => tracker.Update(now, files) is { Count: > 0 } items ? Assert.Single(items) : null;
}
