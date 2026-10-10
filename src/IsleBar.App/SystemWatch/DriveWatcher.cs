using IsleBar.App.Interop;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// The USB drive card (TODO ④, user 10-10; rules in <see cref="DriveRules"/>). Looks at the drives every second
/// (<see cref="RemovableDrives.List"/>; a drive letter list and a few small calls). One status card per drive, called the way File
/// Explorer does — "KINGSTON (E:)", "U盘 (E:)":
/// <list type="bullet">
/// <item>plugged in → debut 6 s (it was recognised): the name white, "(E:)" grey, ↗ = open it; then in its cell. Drives already
/// there at start don't debut.</item>
/// <item>pulled out → the same card rises "Removed · KINGSTON (E:)" for 2 s, no colour (Windows 10 1809 on removes quickly by
/// default: pulling is normal). Its free space still moving in the last 5 s → "Not safely removed" orange for 6 s, the lifted card
/// says files just copied may be incomplete.</item>
/// <item>"Eject" in the lifted card → Windows' own safe removal; once the drive is really gone, "Ejected" green 2 s. Refused (a
/// program holds a file), or still there 3 s later → "Eject failed" red, debut 6 s, the lifted card names the program
/// (<see cref="EjectBlocker"/>); back to the plain card after 10 s.</item>
/// </list>
/// Windows' own "AutoPlay" notification is dropped meanwhile (<c>ActivityFeed.DropDriveToasts</c>): one thing, one card.
/// Log "drive: …" (letters only, never file names).
/// </summary>
internal sealed class DriveWatcher : NoticeWatcher
{
    private const string KeyPrefix = "drive:";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FailedLength = NoticeLengths.Attention;
    private static readonly TimeSpan GoneWait = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly Dictionary<string, Plug> _plugged = new(StringComparer.OrdinalIgnoreCase);   // by key
    private readonly HashSet<string> _ejecting = new(StringComparer.OrdinalIgnoreCase);   // asked to go: going away says "Ejected"
    private readonly Dictionary<string, (DateTimeOffset Until, string Why)> _failed = new(StringComparer.OrdinalIgnoreCase);
    private bool _first = true;
    private Timer? _poll;

    private sealed record Plug(PluggedDrive Drive, DateTimeOffset Since, DateTimeOffset? Written);

    public DriveWatcher(Func<Core.Localization.LanguageStrings> strings)
        : base(strings)
    {
    }

    public override void Start() => Guard(() => _poll ??= new Timer(_ => Guard(Scan), null, TimeSpan.Zero, PollInterval));

    protected override void Stop() => _poll?.Dispose();

    /// <summary>Whether a card is a USB drive's.</summary>
    public static bool Owns(Core.Island.ActivityState card)
        => card.SourcePath?.StartsWith(SystemNotice.SourcePrefix + KeyPrefix, StringComparison.Ordinal) == true;

    private void Scan()
    {
        var drives = RemovableDrives.List();
        var now = DateTimeOffset.UtcNow;
        var s = Text;
        lock (_gate)
        {
            var (came, went) = DriveRules.Diff([.. _plugged.Values.Select(p => p.Drive)], drives);
            foreach (var drive in went)
            {
                var key = DriveRules.KeyOf(drive);
                var written = _plugged.TryGetValue(key, out var was) ? was.Written : null;
                _plugged.Remove(key);
                _failed.Remove(key);
                if (_ejecting.Remove(key))
                {
                    AppLog.Write($"drive: {drive.Letter} ejected and gone");
                    Board.Flash(key, Brief(s.NoticeDriveSafe, drive, s, now, state: "done"), NoticeLengths.Glance, now);
                }
                else if (DriveRules.WasWriting(written, now))
                {
                    AppLog.Write($"drive: {drive.Letter} pulled out while being written to");
                    var card = Brief(s.NoticeDriveUnsafe, drive, s, now, state: SystemNotice.WaitState);
                    card.Name = s.CardDriveUnsafe;
                    Board.Flash(key, card, NoticeLengths.Read, now);
                }
                else
                {
                    AppLog.Write($"drive: {drive.Letter} pulled out");
                    Board.Flash(key, Brief(s.NoticeDriveRemoved, drive, s, now), NoticeLengths.Glance, now);
                }
            }

            foreach (var drive in came)
            {
                _plugged[DriveRules.KeyOf(drive)] = new Plug(drive, now, null);
                AppLog.Write($"drive: {drive.Letter} plugged in ({drive.Total / 1e9:0.#} GB){(_first ? ", already there at start" : string.Empty)}");
            }

            foreach (var drive in drives)
            {
                var key = DriveRules.KeyOf(drive);
                if (!_plugged.TryGetValue(key, out var p))
                {
                    continue;
                }

                var written = drive.Free != p.Drive.Free ? now : p.Written;   // free space moved: something is writing (or deleting)
                _plugged[key] = p with { Drive = drive, Written = written };
                if (_ejecting.Contains(key))
                {
                    continue;   // its card says nothing new until it's gone or refused
                }

                if (_failed.TryGetValue(key, out var failed) && now >= failed.Until)
                {
                    _failed.Remove(key);
                }

                Board.Hold(key, _failed.TryGetValue(key, out var f)
                    ? Failed(drive, s, p.Since, f.Why)
                    : Plugged(drive, s, p.Since, debut: !_first && came.Contains(drive)));   // back from "failed": no second debut
            }

            _first = false;
        }
    }

    /// <summary>The lifted card's "Eject": Windows' safe removal, off the UI thread. Does nothing for another card.</summary>
    public void Eject(Core.Island.ActivityState card)
    {
        if (!Owns(card))
        {
            return;
        }

        var key = card.SourcePath![SystemNotice.SourcePrefix.Length..];
        PluggedDrive? drive;
        lock (_gate)
        {
            drive = _plugged.TryGetValue(key, out var p) && _ejecting.Add(key) ? p.Drive : null;
        }

        if (drive is null)
        {
            return;   // gone, or already being ejected
        }

        _ = Task.Run(() => Guard(() =>
        {
            var (why, device) = RemovableDrives.Eject(drive.Root);
            if (why is null)
            {
                // Windows said yes: "Ejected" only once the drive is really gone (Scan says it) — still there after 3 s = failed
                var until = DateTime.UtcNow + GoneWait;
                while (DateTime.UtcNow < until && Directory.Exists(drive.Root))
                {
                    Thread.Sleep(200);
                }

                if (!Directory.Exists(drive.Root))
                {
                    AppLog.Write($"drive: {drive.Letter} ejected");
                    return;
                }

                why = "still there after the eject";
            }

            AppLog.Write($"drive: {drive.Letter} not ejected — {why}");
            var s = Text;
            var who = why.StartsWith("veto", StringComparison.Ordinal) ? EjectBlocker.Find(device) : null;
            var detail = who is null ? s.CardDriveBusy : s.CardDriveBusyBy.Replace("{a}", who, StringComparison.Ordinal);
            lock (_gate)
            {
                _ejecting.Remove(key);
                if (_plugged.TryGetValue(key, out var p))
                {
                    _failed[key] = (DateTimeOffset.UtcNow + FailedLength, detail);
                    Board.Hold(key, Failed(p.Drive, s, p.Since, detail));
                }
            }
        }));
    }

    /// <summary>Plugged in: the name, then "(E:)" in grey; ↗ opens it; the lifted card shows the space used.</summary>
    private static Core.Island.ActivityState Plugged(PluggedDrive drive, Core.Localization.LanguageStrings s, DateTimeOffset since, bool debut)
    {
        var card = SystemNotice.Make(DriveRules.Name(drive, s.NoticeDrive), NoticeGlyphs.Drive, since, msg: DriveRules.Letter(drive),
            done: drive.Total - drive.Free, total: drive.Total, open: drive.Root);
        card.Look = "plugged";
        card.DebutFor = debut ? DriveRules.DebutLength : null;
        return card;
    }

    /// <summary>Refused: "Eject failed", "KINGSTON (E:)" grey, red; it debuts again; the lifted card says why (<paramref name="why"/>).</summary>
    private static Core.Island.ActivityState Failed(PluggedDrive drive, Core.Localization.LanguageStrings s, DateTimeOffset since, string why)
    {
        var card = SystemNotice.Make(s.NoticeDriveBusy, NoticeGlyphs.Drive, since, msg: DriveRules.FullName(drive, s.NoticeDrive),
            urgent: true, done: drive.Total - drive.Free, total: drive.Total, open: drive.Root);
        card.Name = why;
        card.Look = "failed";
        card.DebutFor = DriveRules.DebutLength;
        return card;
    }

    /// <summary>A brief card: "Removed" / "Ejected" / "Not safely removed", then "KINGSTON (E:)" in grey.</summary>
    private static Core.Island.ActivityState Brief(string words, PluggedDrive drive, Core.Localization.LanguageStrings s, DateTimeOffset now, string? state = null)
    {
        var card = SystemNotice.Make(words, NoticeGlyphs.Drive, now, msg: DriveRules.FullName(drive, s.NoticeDrive));
        if (state is not null)
        {
            card.State = state;
        }

        return card;
    }
}
