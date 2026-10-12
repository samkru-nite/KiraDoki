using IsleBar.App.Interop;
using IsleBar.Core.Configuration;
using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// "Keep the AI working with the lid closed" (settings, user 10-12; rules in <see cref="LidRules"/>). While an agent works or waits:
/// the system kept from idle sleep, the power plan's lid action set to "do nothing"; with the lid really closed, best power
/// efficiency too (sub-switch). All back to the user's own when the tasks are through, under 5 % battery, or on quitting.
/// <para>
/// Every change is written down first (<c>lid-saved.txt</c>) and undone at the next start if the app didn't get to (crash, forced
/// quit, power cut). A value is put back only while it is still what we wrote — the user may have changed it meanwhile. If the plan
/// in use is switched (makers' control centres do), the old one is put back and the new one held. Log "lid: …".
/// </para>
/// <para>
/// Threads (10-12, the performance session's logs): writing a power setting and re-activating the plan takes 230–320 ms, and it
/// happens just as a card turns working → done, so the swap froze. Now <see cref="Update"/> (UI thread, every tick) only decides —
/// and keeps the system running, which Windows holds per thread — and one background thread of its own does every read and write
/// of the power settings, in order.
/// </para>
/// </summary>
internal sealed class LidGuard : IDisposable
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SchemeEvery = TimeSpan.FromSeconds(5);

    // the worker's own (only touched on its thread)
    private LidSaved? _saved;   // non-null while the lid action is ours
    private bool _efficient;
    private DateTimeOffset _retryAt;
    private DateTimeOffset _schemeAt;

    // the UI thread's
    private bool _awake;

    // what the UI thread wants, read by the worker
    private volatile bool _wantHeld;
    private volatile bool _wantEfficient;
    private volatile bool _stopping;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread? _worker;

    /// <summary>The last try to change the lid action failed (locked by policy, unreadable) — the settings page says so.</summary>
    public static bool LidLocked { get; private set; }

    /// <summary>The power mode couldn't be switched (not the Balanced plan, or Windows didn't take it).</summary>
    public static bool ModesMissing { get; private set; }

    private static string SavedPath => Path.Combine(AppPaths.DataDirectory, "lid-saved.txt");

    public LidGuard()
    {
        if (!PowerSettings.HasLid)
        {
            return;
        }

        _worker = new Thread(Work) { IsBackground = true, Name = "lid" };
        _worker.Start();
    }

    /// <summary>The worker: last run's changes undone, then each wish carried out as it comes (and the plan looked at every 5 s).</summary>
    private void Work()
    {
        try
        {
            Recover();
            PowerSettings.WatchLid();
            while (true)
            {
                _wake.WaitOne(SchemeEvery);
                var stopping = _stopping;
                Carry(stopping ? false : _wantHeld, !stopping && _wantEfficient, DateTimeOffset.UtcNow);
                if (stopping)
                {
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Write("lid: worker stopped: " + ex.Message);
        }
    }

    /// <summary>
    /// UI thread, every tick: what should be (no power setting is touched here — the worker does that). Keeping the system running
    /// is set here, since Windows holds it for the thread that asked.
    /// </summary>
    public void Update(IReadOnlyList<ActivityState> items, IsleBarSettings settings, DateTimeOffset now)
    {
        if (!PowerSettings.HasLid)
        {
            return;
        }

        var (battery, charging) = PowerSettings.Battery();
        var keep = LidRules.KeepAwake(settings.LidAwake, LidRules.AgentBusy(items), battery, charging);
        if (keep != _awake)
        {
            _awake = keep;
            PowerSettings.KeepSystemRunning(keep);
            AppLog.Write($"lid: {(keep ? "an agent is working — keeping the PC running" : "no agent working (or the battery is low) — back to normal")}"
                + $" (battery {battery?.ToString() ?? "-"}%, {(charging ? "plugged in" : "on battery")})");
        }

        var efficient = settings.LidEfficiency && PowerSettings.LidClosed;
        if (keep != _wantHeld || efficient != _wantEfficient)
        {
            _wantHeld = keep;
            _wantEfficient = efficient;
            _wake.Set();
        }
    }

    /// <summary>Worker: the power settings brought in line with what is wanted.</summary>
    private void Carry(bool keep, bool wantEfficient, DateTimeOffset now)
    {
        if (keep && _saved is null && now >= _retryAt)
        {
            Hold(now);
        }
        else if (!keep && _saved is not null)
        {
            Release();
        }
        else if (_saved is { } held && now - _schemeAt >= SchemeEvery)
        {
            _schemeAt = now;
            if (PowerSettings.ActiveScheme() is { } scheme && scheme != held.Scheme)
            {
                AppLog.Write($"lid: the power plan changed to {scheme} — putting the old one back, holding the new one");
                Release();
                Hold(now);
            }
        }

        var efficient = LidRules.Efficient(_saved is not null, wantEfficient, true);
        if (efficient != _efficient)
        {
            SetEfficient(efficient);
        }
    }

    /// <summary>The lid action to "do nothing" in the plan in use, the user's own written down first.</summary>
    private void Hold(DateTimeOffset now)
    {
        _schemeAt = now;
        if (PowerSettings.ActiveScheme() is not { } scheme || PowerSettings.ReadLid(scheme) is not { } lid)
        {
            Fail(now, "can't read the lid action");
            return;
        }

        var (ac, dc) = lid;

        var saved = new LidSaved(scheme, ac, dc);
        Write(saved);
        if (!PowerSettings.WriteLid(scheme, LidRules.DoNothing, LidRules.DoNothing)
            || PowerSettings.ReadLid(scheme) != (LidRules.DoNothing, LidRules.DoNothing))
        {
            PowerSettings.WriteLid(scheme, ac, dc);   // whatever got through, back
            Delete();
            Fail(now, $"the lid action didn't change (still {PowerSettings.ReadLid(scheme)}) — locked?");
            return;
        }

        _saved = saved;
        LidLocked = false;
        AppLog.Write($"lid: closing the lid does nothing now (was {ac} plugged in, {dc} on battery; plan {scheme})");
    }

    private void Fail(DateTimeOffset now, string why)
    {
        LidLocked = true;
        _retryAt = now + RetryAfter;
        AppLog.Write("lid: " + why);
    }

    /// <summary>The user's lid action back (where it's still ours), the power mode too.</summary>
    private void Release()
    {
        if (_efficient)
        {
            SetEfficient(false);
        }

        if (_saved is { } saved)
        {
            PutBack(saved);
            Delete();
            _saved = null;
        }
    }

    private static void PutBack(LidSaved saved)
    {
        if (saved.Overlay is { } mode && PowerSettings.ChosenMode() == LidRules.BestEfficiency)
        {
            PowerSettings.SetMode(mode);
            AppLog.Write($"lid: power mode back to {mode}");
        }

        if (PowerSettings.ReadLid(saved.Scheme) is { } now)
        {
            var (ac, dc) = now;
            // only what is still "do nothing" (= ours); a value the user set meanwhile stays
            var back = (ac == LidRules.DoNothing ? saved.Ac : ac, dc == LidRules.DoNothing ? saved.Dc : dc);
            if (back != (ac, dc))
            {
                PowerSettings.WriteLid(saved.Scheme, back.Item1, back.Item2);
            }

            AppLog.Write($"lid: lid action back to {back.Item1} plugged in, {back.Item2} on battery (was {ac}, {dc})");
        }
    }

    /// <summary>Best power efficiency on (the user's mode written down first) or the user's mode back.</summary>
    private void SetEfficient(bool on)
    {
        _efficient = on;
        if (_saved is not { } saved)
        {
            return;
        }

        if (!on)
        {
            if (saved.Overlay is { } mode)
            {
                if (PowerSettings.ChosenMode() == LidRules.BestEfficiency)
                {
                    PowerSettings.SetMode(mode);
                }

                _saved = saved with { Overlay = null };
                Write(_saved);
                AppLog.Write($"lid: lid open (or the switch off) — power mode back to {mode}");
            }

            return;
        }

        var chosen = PowerSettings.ChosenMode();
        if (chosen is not { } own || PowerSettings.ActiveScheme() != PowerSettings.Balanced)
        {
            ModesMissing = true;
            AppLog.Write("lid: lid closed, but the power mode can't be switched here (not the Balanced plan)");
            return;
        }

        if (own == LidRules.BestEfficiency)
        {
            return;   // already the user's own: nothing to put back later
        }

        _saved = saved with { Overlay = own };
        Write(_saved);
        if (!PowerSettings.SetMode(LidRules.BestEfficiency))
        {
            ModesMissing = true;
            _saved = saved;
            Write(_saved);
            AppLog.Write("lid: lid closed, but Windows didn't take best power efficiency");
            return;
        }

        ModesMissing = false;
        AppLog.Write($"lid: lid closed — best power efficiency (was {own})");
    }

    /// <summary>Last run's changes still written down: it ended before putting them back.</summary>
    private static void Recover()
    {
        string? text;
        try
        {
            text = File.Exists(SavedPath) ? File.ReadAllText(SavedPath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (text is null)
        {
            return;
        }

        if (LidSaved.Parse(text) is { } saved)
        {
            AppLog.Write("lid: last run didn't put the lid back — doing it now");
            PutBack(saved);
        }

        Delete();
    }

    private static void Write(LidSaved saved)
    {
        try
        {
            File.WriteAllText(SavedPath, saved.Format());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("lid: can't write " + SavedPath + ": " + ex.Message);
        }
    }

    private static void Delete()
    {
        try
        {
            File.Delete(SavedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Quitting: the worker puts everything back (waited for — the process ends right after; a forced end leaves it to the next start).</summary>
    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        _worker?.Join(TimeSpan.FromSeconds(2));
        if (_awake)
        {
            _awake = false;
            PowerSettings.KeepSystemRunning(false);
        }
    }
}
