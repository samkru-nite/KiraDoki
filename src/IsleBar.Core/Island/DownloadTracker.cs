namespace IsleBar.Core.Island;

/// <summary>A browser's partial download file as seen in one scan of the downloads folder.</summary>
/// <param name="Path">Full path of the partial file (".crdownload", ".part", …).</param>
/// <param name="Name">The name it will have when finished.</param>
/// <param name="Size">Bytes on disk now.</param>
/// <param name="LastWrite">When the file was last written.</param>
public sealed record PartialDownload(string Path, string Name, long Size, DateTimeOffset LastWrite);

/// <summary>
/// Turns the partial files of browser downloads into <b>one</b> island item: the summed speed, the name of the earliest download
/// still moving, and how many others there are ("2.4 MB/s  report.pdf  +2"). Speed comes from how much each file grew in the last
/// few seconds — browsers don't say how big the file will be, so nothing here depends on the total.
/// A file that stops growing for <see cref="PauseAfter"/> reads as paused (a paused download and a stalled network look the same
/// from the file). Paused downloads stay until their file goes away; only a file already untouched for
/// <see cref="StaleOnArrival"/> when first seen is skipped, as left over from an earlier session.
/// </summary>
public sealed class DownloadTracker
{
    public static readonly TimeSpan PauseAfter = TimeSpan.FromSeconds(5);   // browsers write in chunks: on a slow line 1–2 s without growth is normal
    public static readonly TimeSpan SpeedWindow = TimeSpan.FromSeconds(3);   // speed averages this long, so the number doesn't jump every second
    public static readonly TimeSpan StaleOnArrival = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan MinSpan = TimeSpan.FromSeconds(1);   // speed is shown once a second of growth has been seen

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Partial files being followed (the rest are leftovers and ignored).</summary>
    public IReadOnlyCollection<string> Tracked => _entries.Keys;

    /// <summary>Takes one scan; returns the item to show, or null when no download is under way.</summary>
    public ActivityState? Update(DateTimeOffset now, IReadOnlyList<PartialDownload> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!_entries.TryGetValue(file.Path, out var entry))
            {
                if (now - file.LastWrite > StaleOnArrival)
                {
                    continue;   // left over from an earlier session; picked up once it grows again (its write time moves)
                }

                entry = new Entry(file.Name, now, file.Size, file.LastWrite < now ? file.LastWrite : now);
                _entries[file.Path] = entry;
            }

            present.Add(file.Path);
            entry.Name = file.Name;
            entry.Observe(now, file.Size);
        }

        foreach (var gone in _entries.Keys.Where(k => !present.Contains(k)).ToList())
        {
            _entries.Remove(gone);
        }

        if (_entries.Count == 0)
        {
            return null;
        }

        var all = _entries.OrderBy(e => e.Value.FirstSeen).ToList();
        var moving = all.Where(e => !e.Value.IsPaused(now)).ToList();
        var shown = moving.Count > 0 ? moving[0] : all[0];
        var speeds = moving.Select(e => e.Value.Speed(now)).Where(s => s is not null).ToList();
        return new ActivityState
        {
            RawKind = ActivityState.KindTransfer,
            Title = "Download → PC",   // ending with "PC" means receiving (↓)
            Name = shown.Value.Name,
            Stage = moving.Count > 0 ? "download" : "paused",
            Done = shown.Value.Size,
            Speed = speeds.Count > 0 ? speeds.Sum() : null,
            MoreCount = all.Count - 1,
            State = "run",
            SourcePath = "download:" + shown.Key,
            UpdatedAt = shown.Value.FirstSeen,
        };
    }

    private sealed class Entry(string name, DateTimeOffset firstSeen, long size, DateTimeOffset lastGrowth)
    {
        private readonly List<(DateTimeOffset At, long Size)> _samples = [];
        private DateTimeOffset _lastGrowth = lastGrowth;

        public string Name { get; set; } = name;

        public DateTimeOffset FirstSeen { get; } = firstSeen;

        public long Size { get; private set; } = size;

        public void Observe(DateTimeOffset now, long size)
        {
            if (size != Size)
            {
                _lastGrowth = now;
            }

            Size = size;
            if (size < (_samples.Count > 0 ? _samples[^1].Size : 0))
            {
                _samples.Clear();   // the browser restarted the file
            }

            _samples.Add((now, size));
            // keep one sample at or before the window start, so the average spans the whole window
            while (_samples.Count > 2 && now - _samples[1].At >= SpeedWindow)
            {
                _samples.RemoveAt(0);
            }
        }

        public bool IsPaused(DateTimeOffset now) => now - _lastGrowth > PauseAfter;

        /// <summary>Bytes a second over the last <see cref="SpeedWindow"/>; null until a second has been seen.</summary>
        public double? Speed(DateTimeOffset now)
        {
            if (_samples.Count < 2)
            {
                return null;
            }

            var (at, size) = _samples[0];
            var span = now - at;
            return span < MinSpan ? null : Math.Max(0, Size - size) / span.TotalSeconds;
        }
    }
}
