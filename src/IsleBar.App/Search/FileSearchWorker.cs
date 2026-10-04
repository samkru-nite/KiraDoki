using System.Collections.Concurrent;

namespace IsleBar.App.Search;

/// <summary>
/// Calls the Everything SDK only from <b>one dedicated thread</b> (SDK constraint).
/// Of queued requests only the last is processed, and results for stale input are filtered out by number —
/// this stops late results from overwriting newer ones when typing fast.
///
/// <b>This file has never been built. Needs phase-0 verification on PC.</b>
/// </summary>
internal sealed class FileSearchWorker : IDisposable
{
    private readonly BlockingCollection<(long Seq, string Query, int Max, string Sort, IReadOnlyList<IsleBar.Core.Search.FileExcludes.Rule> Hidden)> _requests =
        new(new ConcurrentQueue<(long, string, int, string, IReadOnlyList<IsleBar.Core.Search.FileExcludes.Rule>)>());
    private readonly Action<long, IReadOnlyList<FileHit>, SearchProblem> _onResult;
    private readonly Thread _thread;
    private long _seq;

    /// <param name="onResult">Delivers results. <b>Marshalling to the UI thread is the caller's responsibility</b>.</param>
    public FileSearchWorker(Action<long, IReadOnlyList<FileHit>, SearchProblem> onResult)
    {
        _onResult = onResult;
        _thread = new Thread(Loop) { IsBackground = true, Name = "IsleBar Everything" };
        _thread.SetApartmentState(ApartmentState.STA);   // Windows' recent list is read through the shell's COM shortcut object
        _thread.Start();
    }

    /// <summary>Requests a search — an empty query asks for recent files. Returns a request number (used to match results).</summary>
    public long Request(string query, int max, string sort, IReadOnlyList<IsleBar.Core.Search.FileExcludes.Rule> hidden)
    {
        var seq = Interlocked.Increment(ref _seq);
        _requests.Add((seq, query, max, sort, hidden));
        return seq;
    }

    /// <summary>Most recently requested number.</summary>
    public long LatestSeq => Interlocked.Read(ref _seq);

    private void Loop()
    {
        using var sdk = EverythingSdk.TryLoad();
        foreach (var item in _requests.GetConsumingEnumerable())
        {
            var current = item;

            // Drop queued requests and handle only the last one
            while (_requests.TryTake(out var newer))
            {
                current = newer;
            }

            // nothing typed yet: Windows' recently opened files when it keeps them, else files changed this week (user 10-04)
            if (current.Query.Trim().Length == 0 && RecentFiles.WindowsKeepsThem()
                && ReadWindowsRecent(current) is { Count: > 0 } recent)
            {
                _onResult(current.Seq, recent, SearchProblem.None);
                continue;
            }

            if (sdk is null)
            {
                _onResult(current.Seq, [], SearchProblem.SdkMissing);
                continue;
            }

            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                SearchProblem problem;
                var hits = current.Query.Trim().Length == 0
                    ? sdk.Search(IsleBar.Core.Search.FileSearchQuery.RecentQuery, current.Max, current.Sort, current.Hidden, out problem)
                    : sdk.Search(current.Query, current.Max, current.Sort, current.Hidden, out problem);
                if (clock.ElapsedMilliseconds >= 30)
                {
                    Interop.TaskbarHost.Log($"file search: Everything took {clock.ElapsedMilliseconds} ms for {hits.Count} rows");
                }

                _onResult(current.Seq, hits, problem);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _onResult(current.Seq, [], SearchProblem.SdkMissing);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.SEHException or InvalidOperationException)
            {
                _onResult(current.Seq, [], SearchProblem.Failed);
            }
        }
    }

    private static List<FileHit> ReadWindowsRecent((long Seq, string Query, int Max, string Sort, IReadOnlyList<IsleBar.Core.Search.FileExcludes.Rule> Hidden) request)
    {
        try
        {
            var rows = RecentFiles.Read(request.Max, path => IsleBar.Core.Search.FileExcludes.IsHidden(path, request.Hidden));
            return IsleBar.Core.Search.FileSearchQuery.Order(rows, request.Sort, h => h.Name, h => h.Modified, h => h.Size, h => h.IsFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];   // fall back to files changed this week
        }
    }

    public void Dispose()
    {
        _requests.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(2));
        _requests.Dispose();
    }
}
