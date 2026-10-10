namespace IsleBar.Core.SystemWatch;

/// <summary>A drive the USB card is about: its root ("E:\"), volume label, size and free space, and its volume serial.</summary>
public sealed record PluggedDrive(string Root, string? Label, long Total, long Free, uint Serial = 0)
{
    /// <summary>"E:".</summary>
    public string Letter => Root.Length >= 2 ? Root[..2].ToUpperInvariant() : Root;
}

/// <summary>
/// The USB drive card (TODO ④, user 10-10): one card per drive, a status card — plugged in → debut 6 s with ↗ open, then in its cell;
/// pulled out → the same card rises "Removed" 2 s; ejected from the lifted card → "Safe to remove" 2 s; can't eject → it says so.
/// This part: which drives came and went between two looks, and what the card is called.
/// </summary>
public static class DriveRules
{
    /// <summary>A plugged-in card's debut — long enough to see it was recognised (user 10-10: loose USB ports are common).</summary>
    public static readonly TimeSpan DebutLength = TimeSpan.FromSeconds(6);

    /// <summary>The card's key under the notice board: one per drive letter.</summary>
    public static string KeyOf(PluggedDrive drive) => "drive:" + drive.Letter.TrimEnd(':');

    /// <summary>The name on the card: the volume label, or the word for a USB drive ("U盘") when it has none.</summary>
    public static string Name(PluggedDrive drive, string fallback)
        => string.IsNullOrWhiteSpace(drive.Label) ? fallback : drive.Label.Trim();

    /// <summary>The letter as File Explorer writes it after a name: "(E:)".</summary>
    public static string Letter(PluggedDrive drive) => $"({drive.Letter})";

    /// <summary>
    /// The drive as every card calls it, the way File Explorer does (user 10-10): "KINGSTON (E:)", "U盘 (E:)" — the same words from
    /// plugged in to pulled out, so you see it is the same drive; and two unnamed drives stay apart.
    /// </summary>
    public static string FullName(PluggedDrive drive, string fallback) => $"{Name(drive, fallback)} {Letter(drive)}";

    /// <summary>How recently the free space must have moved for a pulled-out drive to count as being written to.</summary>
    public static readonly TimeSpan WritingWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Pulled out while being written to (user 10-10: then orange "Not safely removed"): its free space moved within the last
    /// <see cref="WritingWindow"/> (looked at every second). It catches a copy cut off halfway; it can't see a file merely held
    /// open, nor a write too small to move the free space — no colour doesn't promise it was safe, it only says nothing was seen.
    /// </summary>
    public static bool WasWriting(DateTimeOffset? lastChange, DateTimeOffset now)
        => lastChange is { } at && now - at <= WritingWindow;

    /// <summary>
    /// The drives that came and went since <paramref name="before"/>. The same letter with another volume (another serial, or another
    /// label when serials are unknown) counts as one going and another coming — a stick swapped between two looks.
    /// </summary>
    public static (IReadOnlyList<PluggedDrive> Came, IReadOnlyList<PluggedDrive> Went) Diff(
        IReadOnlyCollection<PluggedDrive> before, IReadOnlyCollection<PluggedDrive> now)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(now);
        static bool Same(PluggedDrive a, PluggedDrive b)
            => string.Equals(a.Letter, b.Letter, StringComparison.OrdinalIgnoreCase)
               && (a.Serial != 0 && b.Serial != 0 ? a.Serial == b.Serial : string.Equals(a.Label, b.Label, StringComparison.Ordinal));

        var came = now.Where(d => !before.Any(b => Same(b, d))).ToList();
        var went = before.Where(b => !now.Any(d => Same(b, d))).ToList();
        return (came, went);
    }
}
