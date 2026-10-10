using IsleBar.Core.Island;

namespace IsleBar.Core.Ui;

/// <summary>The only colours a small icon may have (TODO 7.9 E, user 10-07): does it need you now?</summary>
public enum IconTone
{
    /// <summary>Nothing to do — the normal icon colour.</summary>
    Plain,

    /// <summary>Orange: someone is waiting for you (an agent asking for permission).</summary>
    Waiting,

    /// <summary>Green: finished, waiting for you to look.</summary>
    Done,

    /// <summary>Red: something went wrong.</summary>
    Error,
}

/// <summary>A small icon: a Segoe Fluent Icons glyph (or the album cover / an agent's logo), its colour, and the pause bars.</summary>
/// <param name="Cover">Music with album art: draw the cover instead of <paramref name="Glyph"/> (the glyph is the fallback).</param>
/// <param name="Logo">An agent's card: draw that agent's logo ("claude" / "codex") when this PC has it; the glyph is the fallback.</param>
public sealed record TaskIcon(string Glyph, IconTone Tone, bool Paused, bool Cover, string? Logo = null);

/// <summary>
/// <b>The one table of small icons</b> (TODO 7.9 E "small-icon principles", user 10-07). A small icon answers only "what is it" and
/// "do I need to act now"; the details are on the big pill.
/// <list type="bullet">
/// <item>Shape = what it is + its stage. A stage change swaps the glyph, nothing is layered on top.</item>
/// <item>Colour = do you need to act: <see cref="IconTone"/>, only three colours.</item>
/// <item>The only extra mark: two pause bars (music, a paused download or timer).</item>
/// <item>No breathing, no fading, no progress rings.</item>
/// <item>Agents: every stage shows the maker's logo (user 10-07, replacing "one-colour line glyphs"); the glyphs below are only
/// the fallback when the logo can't be found on this PC.</item>
/// </list>
/// Glyphs are stand-ins from Segoe Fluent Icons until the user picks the final set (10-07): change them here only.
/// </summary>
public static class TaskIcons
{
    public const string Chat = "";
    public const string Bell = "";
    public const string Completed = "";
    public const string ErrorBadge = "";
    public const string Download = "";
    public const string Upload = "";
    public const string Copy = "";
    public const string Stopwatch = "";
    public const string Note = "";
    public const string Camera = "";
    public const string Microphone = "";
    public const string Info = "";

    public static TaskIcon For(Card card, bool hasCover, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);
        var s = card.State;
        TaskIcon icon = s.Kind switch
        {
            ActivityKind.AgentWorking => new(Chat, IconTone.Plain, false, false),
            ActivityKind.AgentPermission => new(Bell, IconTone.Waiting, false, false),
            ActivityKind.AgentDone => new(Completed, IconTone.Done, false, false),
            ActivityKind.Error => new(ErrorBadge, IconTone.Error, false, false),
            ActivityKind.Music => new(Note, IconTone.Plain, s.Stage == "paused", hasCover),
            ActivityKind.Timer => card.Type == CardType.Inform
                ? new(Stopwatch, IconTone.Done, false, false)
                : new(Stopwatch, IconTone.Plain, s.IsPaused, false),
            ActivityKind.Transfer => s.RunState switch
            {
                ActivityRunState.Error => new(ErrorBadge, IconTone.Error, false, false),
                ActivityRunState.Done => new(Completed, IconTone.Done, false, false),                _ => new(
                    s.Stage == "copy" ? s.Glyph ?? Copy : s.Direction == TransferDirection.Incoming ? Download : Upload,
                    IconTone.Plain,
                    s.IsPausedTransfer,
                    false),
            },
            ActivityKind.Notice => s.RunState == ActivityRunState.Error
                ? new(ErrorBadge, IconTone.Error, false, false)
                // a calendar reminder / start is orange, an end / gap green (10-10)
                : new(s.Glyph ?? Info, s.State == SystemWatch.SystemNotice.WaitState ? IconTone.Waiting
                    : s.RunState == ActivityRunState.Done ? IconTone.Done : IconTone.Plain, false, false),
            _ => new(s.Glyph ?? Info, IconTone.Plain, false, false),
        };
        var agent = s.Kind is ActivityKind.AgentWorking or ActivityKind.AgentPermission or ActivityKind.AgentDone
                    || (s.Kind == ActivityKind.Notice && s.Agent is not null);
        return agent ? icon with { Logo = s.AgentLabel == "Codex" ? "codex" : "claude" } : icon;
    }
}
