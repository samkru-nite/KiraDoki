using System.Globalization;
using IsleBar.Core.Launch;
using IsleBar.Core.Localization;

namespace IsleBar.Core.Island;

/// <summary>
/// The pill's messages about an agent's plan and failures (user 10-03): the usage limit was hit (red, until it resets), the turn
/// stopped on another error (red, until closed or the next prompt — 10-10). (The "usage at 90%" warning went 10-10.)
/// Built the same way for Claude Code (its hooks and status line) and Codex (its session record), so both read alike.
/// </summary>
public static class AgentNotices
{
    private const string Clock = "";      // Segoe Fluent Icons: clock ("wait until")
    private const string Warning = "";    // Segoe Fluent Icons: warning

    public static string Name(string agent) => agent == AgentKind.Codex ? "Codex" : "Claude";

    /// <summary>The plan's usage limit stopped the turn. Stays until <paramref name="resetsAt"/> (an hour if unknown).</summary>
    public static ActivityState UsageLimit(string agent, DateTimeOffset? resetsAt, LanguageStrings? text, DateTimeOffset now)
    {
        text ??= LanguageCatalog.For(LanguageCatalog.Fallback);
        return new ActivityState
        {
            RawKind = ActivityState.KindNotice,
            Agent = agent,
            Glyph = Clock,
            Title = text.UsageLimit.Replace("{a}", Name(agent), StringComparison.Ordinal),
            Msg = resetsAt is { } r ? text.ResetsAt.Replace("{}", Time(r), StringComparison.Ordinal) : null,
            State = "error",
            T0 = Seconds(now),
            Due = Seconds(resetsAt ?? now.AddHours(1)),
        };
    }

    /// <summary>The turn ended on an error other than the usage limit. <paramref name="reason"/>: login, billing, busy or anything else.</summary>
    public static ActivityState Stopped(string agent, StopReason reason, LanguageStrings? text, DateTimeOffset now)
    {
        text ??= LanguageCatalog.For(LanguageCatalog.Fallback);
        return new ActivityState
        {
            RawKind = ActivityState.KindNotice,
            Agent = agent,
            Glyph = Warning,
            Title = text.AgentStopped.Replace("{a}", Name(agent), StringComparison.Ordinal),
            Msg = reason switch
            {
                StopReason.Login => text.StopLogin,
                StopReason.Billing => text.StopBilling,
                StopReason.Busy => text.StopBusy,
                _ => text.StopError,
            },
            State = "error",
            T0 = Seconds(now),
            // no end (user 10-10: was 30 min): an inform card, it waits to be closed like an interrupted one — away for an hour you
            // still learn it stopped. The next prompt replaces it (same state file); 12 h is the backstop (ActivityStore)
        };
    }

    /// <summary>
    /// The session ended (terminal closed, app quit, SessionEnd) while the agent was still working or waiting for an answer: an inform
    /// card that stays until closed by hand (TODO 7.9 D, user 10-06: say so instead of silently dropping the card). Stage
    /// <c>interrupted</c> marks it for the lifted card's longer text; the name is the session's.
    /// </summary>
    public static ActivityState Interrupted(string agent, string? name, LanguageStrings? text, DateTimeOffset now)
    {
        text ??= LanguageCatalog.For(LanguageCatalog.Fallback);
        return new ActivityState
        {
            RawKind = ActivityState.KindNotice,
            Agent = agent,
            Glyph = Warning,
            Title = text.AgentInterrupted.Replace("{a}", Name(agent), StringComparison.Ordinal),
            Name = name,
            Stage = "interrupted",
            State = "error",
            T0 = Seconds(now),
        };
    }

    /// <summary>Claude Code's StopFailure error types (hooks reference) → why it stopped. Null for the usage limit (its own notice).</summary>
    public static StopReason? ClaudeReason(string? errorType) => errorType switch
    {
        "rate_limit" => null,
        "authentication_failed" or "oauth_org_not_allowed" or "cloud_credential_error" => StopReason.Login,
        "billing_error" or "account_on_hold" => StopReason.Billing,
        "overloaded" or "server_error" => StopReason.Busy,
        _ => StopReason.Other,
    };

    /// <summary>Local clock time, "HH:mm" — with the date in front when it isn't today ("10-04 05:42").</summary>
    public static string Time(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static double Seconds(DateTimeOffset at) => at.ToUnixTimeMilliseconds() / 1000.0;
}

public enum StopReason
{
    Login,
    Billing,
    Busy,
    Other,
}
