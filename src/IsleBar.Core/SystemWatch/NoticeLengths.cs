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

    /// <summary>Something to notice: offline, a busy CPU / memory, usage nearly used up, an update, a pushed error.</summary>
    public static readonly TimeSpan Attention = TimeSpan.FromSeconds(10);
}
