namespace IsleBar.Core.SystemWatch;

/// <summary>
/// How long a temporary card stays (user 10-07: the old 1.5 / 2 / 2.5 / 3 / 6 / 10 / 12 / 15 s were each guessed on their own).
/// Three steps, by what you need to do with it.
/// </summary>
public static class NoticeLengths
{
    /// <summary>A glance: copied, Bluetooth on / off, power plugged / unplugged, back online.</summary>
    public static readonly TimeSpan Glance = TimeSpan.FromSeconds(2);

    /// <summary>Something to read: a system notification, a download / copy finished.</summary>
    public static readonly TimeSpan Read = TimeSpan.FromSeconds(6);

    /// <summary>Something to notice: offline, a busy CPU / memory, an update, a pushed error.</summary>
    public static readonly TimeSpan Attention = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A system notification: long enough for its line to scroll through once — at least <see cref="Read"/>, at most
    /// <see cref="Attention"/> (user 10-10: a fixed 6 s ended long ones halfway through the lap). The width is estimated from the
    /// characters (the watcher can't measure): 14 a full-width one (Chinese, Japanese, Korean, full-width marks), 7.5 any other,
    /// at the pill's 14 size. The lap is PillView's: 1 s still (a notice's hold), then 30 dip/s over the line and its 40 gap.
    /// </summary>
    public static TimeSpan ToRead(string? line)
    {
        double width = 0;
        foreach (var c in line ?? string.Empty)
        {
            width += c >= 0x1100 && !char.IsLowSurrogate(c) ? 14 : char.IsLowSurrogate(c) ? 0 : 7.5;
        }

        if (width <= TextWidth)
        {
            return Read;   // fits: nothing scrolls
        }

        var lap = TimeSpan.FromSeconds(1 + ((width + 40) / 30));
        return lap < Read ? Read : lap > Attention ? Attention : lap;
    }

    /// <summary>The text's width on a card with nothing on its right (PillView: 32 to 208).</summary>
    private const double TextWidth = 176;
}
