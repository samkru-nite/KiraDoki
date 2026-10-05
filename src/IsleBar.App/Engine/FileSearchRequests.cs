using IsleBar.App.Search;
using IsleBar.Core.Configuration;
using IsleBar.Core.Search;

namespace IsleBar.App.Engine;

/// <summary>
/// Sends file searches to the background worker with the settings' count, sort and hidden folders, and remembers the latest
/// query sent (so Enter right after editing can tell the shown results are for older text).
/// </summary>
internal sealed class FileSearchRequests : IDisposable
{
    private readonly FileSearchWorker _worker;
    private string? _hiddenSource;
    private IReadOnlyList<FileExcludes.Rule> _hiddenRules = [];

    /// <param name="onResult">Called on the search thread — marshal to the UI thread to draw.</param>
    public FileSearchRequests(Action<long, IReadOnlyList<FileHit>, SearchProblem> onResult) => _worker = new FileSearchWorker(onResult);

    /// <summary>The text of the latest search sent.</summary>
    public string RequestedQuery { get; private set; } = string.Empty;

    /// <summary>Sequence number of the latest search — results with an older one are stale.</summary>
    public long LatestSeq => _worker.LatestSeq;

    public void Request(string query, IsleBarSettings settings)
    {
        RequestedQuery = query;
        _worker.Request(query, settings.FileResults, settings.FileSort, HiddenFolders(settings));
    }

    /// <summary>The settings' hidden-folder lines as rules — parsed again only when the lines change.</summary>
    private IReadOnlyList<FileExcludes.Rule> HiddenFolders(IsleBarSettings settings)
    {
        var source = string.Join('\n', settings.FileExcludeLines);
        if (source != _hiddenSource)
        {
            _hiddenRules = FileExcludes.Parse(settings.FileExcludeLines);
            _hiddenSource = source;
        }

        return _hiddenRules;
    }

    public void Dispose() => _worker.Dispose();
}
