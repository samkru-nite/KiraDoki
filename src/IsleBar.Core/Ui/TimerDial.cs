using IsleBar.Core.Island;

namespace IsleBar.Core.Ui;

/// <summary>
/// Where a timer's small icon points its hand (user 10-07): a countdown / pomodoro goes round once from start to end, a stopwatch
/// once a minute. <see cref="Angle"/> = now, in degrees clockwise from 12 o'clock; the hand then turns <see cref="Turn"/> degrees
/// over <see cref="Over"/> (once), or keeps going round when <see cref="Loop"/> — the compositor runs it, nothing is redrawn.
/// </summary>
public sealed record TimerDial(double Angle, double Turn, TimeSpan Over, bool Loop)
{
    /// <summary>A stopwatch's hand goes round in a minute.</summary>
    public static readonly TimeSpan StopwatchLap = TimeSpan.FromSeconds(60);

    /// <summary>The dial for a timer card; null when it isn't a timer or its length is unknown (then the plain glyph).</summary>
    public static TimerDial? Of(ActivityState state, bool finished, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Kind != ActivityKind.Timer)
        {
            return null;
        }

        if (TimerParser.Elapsed(state, now) is { } went)
        {
            var angle = went.TotalSeconds % StopwatchLap.TotalSeconds * (360 / StopwatchLap.TotalSeconds);
            return state.IsPaused ? Still(angle) : new TimerDial(angle, 360, StopwatchLap, Loop: true);
        }

        if (finished)
        {
            return Still(360);   // round once: back at 12
        }

        if (TimerParser.Fraction(state, now) is not { } done || TimerParser.Remaining(state, now) is not { } left)
        {
            return null;
        }

        var at = done * 360;
        return state.IsPaused || left <= TimeSpan.Zero ? Still(at) : new TimerDial(at, 360 - at, left, Loop: false);
    }

    private static TimerDial Still(double angle) => new(angle, 0, TimeSpan.Zero, false);
}
