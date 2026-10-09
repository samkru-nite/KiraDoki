namespace IsleBar.Core.SystemWatch;

/// <summary>A single power status reading.</summary>
/// <param name="HasBattery">Whether there is a battery. False on desktops — then no notices are emitted.</param>
/// <param name="PluggedIn">Whether the charger is plugged in.</param>
/// <param name="Percent">Remaining charge 0–100.</param>
public readonly record struct PowerReading(bool HasBattery, bool PluggedIn, int Percent);

/// <summary>What a power change produces.</summary>
public enum PowerChange
{
    /// <summary>Nothing changed (or first reading — nothing is reported at startup).</summary>
    None,

    /// <summary>Charger plugged in → "Charging · 78%" briefly.</summary>
    PluggedIn,

    /// <summary>Charger unplugged → "On battery · 76%" briefly.</summary>
    Unplugged,
}

/// <summary>
/// Rules for power notices. Windows events arrive several times over at the moment of plugging in (status, percent and time remaining change separately),
/// and the state at app startup is not a "change", so we compare with the previous state and report <b>only when plugged/unplugged actually changed</b>.
/// Low battery (user 10-10): a 6-second notice on reaching 20%, another on reaching 10%; under 5% a pinned card that stays until
/// plugged in (shown even if already that low at startup — a human needs to act).
/// </summary>
public sealed class PowerLogic
{
    /// <summary>Low-battery thresholds: 20% and 10% (a brief notice each), and <see cref="Critical"/>.</summary>
    public static readonly int[] LowThresholds = [20, 10, Critical];

    /// <summary>Under 5% (4% or less): the notice stays, pinned, until plugged in (user 10-10).</summary>
    public const int Critical = 4;

    private PowerReading? _last;

    /// <summary>Feeds a newly read state and returns the change to report.</summary>
    public PowerChange Observe(PowerReading reading)
    {
        var last = _last;
        _last = reading;
        if (!reading.HasBattery || last is not { HasBattery: true } previous)
        {
            return PowerChange.None;
        }

        if (previous.PluggedIn == reading.PluggedIn)
        {
            return PowerChange.None;
        }

        return reading.PluggedIn ? PowerChange.PluggedIn : PowerChange.Unplugged;
    }

    /// <summary>
    /// The low-battery threshold currently in effect (20, 10 or <see cref="Critical"/>). Null when charging or with plenty left.
    /// Each new value is a new notice.
    /// </summary>
    public static int? LowLevel(PowerReading reading)
    {
        if (!reading.HasBattery || reading.PluggedIn)
        {
            return null;
        }

        int? level = null;
        foreach (var threshold in LowThresholds)
        {
            if (reading.Percent <= threshold)
            {
                level = threshold;
            }
        }

        return level;
    }
}
