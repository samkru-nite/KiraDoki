using System.Globalization;
using IsleBar.Core.Island;

namespace IsleBar.Core.SystemWatch;

/// <summary>
/// "Keep the AI working with the lid closed" (settings, user 10-12): while Claude or the ChatGPT app is working or waiting for
/// the user, the lid's action is set to "do nothing" for a while, and back to the user's own once every task is through — or at
/// once when the battery runs under 5 %. With the lid really closed, the power mode may go to "best power efficiency" too (the
/// AI thinks in the cloud; here it mostly waits on the network). The app side is <c>SystemWatch/LidGuard</c>.
/// </summary>
public static class LidRules
{
    /// <summary>Under this, on battery, everything goes back at once (Windows' own critical-battery action still holds too).</summary>
    public const int LowBattery = 5;

    /// <summary>The lid's "do nothing" (LIDACTION: 0 nothing, 1 sleep, 2 hibernate, 3 shut down — the same on every PC).</summary>
    public const uint DoNothing = 0;

    /// <summary>Windows 11's "best power efficiency" power mode (the overlay scheme).</summary>
    public static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    /// <summary>
    /// An agent task under way: a Claude / ChatGPT card working or waiting for the user (user 10-12: waiting counts — back from a
    /// break, it goes on once answered). Cards from hooks set up before the "agent" field are Claude's.
    /// </summary>
    public static bool AgentBusy(IEnumerable<ActivityState> items)
        => items.Any(s => s.Kind is ActivityKind.AgentWorking or ActivityKind.AgentPermission);

    /// <param name="on">The setting.</param>
    /// <param name="busy"><see cref="AgentBusy"/>.</param>
    /// <param name="battery">Percent left; null when there's no battery or it isn't known.</param>
    /// <param name="charging">On mains power.</param>
    public static bool KeepAwake(bool on, bool busy, int? battery, bool charging)
        => on && busy && !(battery < LowBattery && !charging);

    /// <summary>Best power efficiency: only while kept awake and the lid really is closed.</summary>
    public static bool Efficient(bool keepAwake, bool efficiencyOn, bool lidClosed) => keepAwake && efficiencyOn && lidClosed;
}

/// <summary>
/// What was changed, kept in a file before changing it — so a crash, a forced quit or a power cut can be undone at the next start.
/// Restored only where the value is still what we wrote (the user may have changed it meanwhile).
/// </summary>
/// <param name="Scheme">The power plan whose lid action was changed.</param>
/// <param name="Ac">The user's lid action on mains power.</param>
/// <param name="Dc">The user's lid action on battery.</param>
/// <param name="Overlay">The user's own power mode while we had set best efficiency; null when we hadn't.</param>
public sealed record LidSaved(Guid Scheme, uint Ac, uint Dc, Guid? Overlay = null)
{
    public string Format()
        => string.Join('\n', Scheme.ToString(), Ac.ToString(CultureInfo.InvariantCulture), Dc.ToString(CultureInfo.InvariantCulture),
            Overlay?.ToString() ?? "-");

    /// <summary>Null for anything that isn't a whole record (an empty or cut-off file).</summary>
    public static LidSaved? Parse(string? text)
    {
        var lines = (text ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries);
        if (lines.Length < 3
            || !Guid.TryParse(lines[0], out var scheme)
            || !uint.TryParse(lines[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ac)
            || !uint.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out var dc)
            || ac > 3 || dc > 3)
        {
            return null;
        }

        Guid? overlay = lines.Length > 3 && Guid.TryParse(lines[3], out var o) ? o : null;
        return new LidSaved(scheme, ac, dc, overlay);
    }
}
