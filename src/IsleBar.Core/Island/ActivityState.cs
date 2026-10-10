using System.Text.Json.Serialization;

namespace IsleBar.Core.Island;

/// <summary>
/// One state JSON. Integration rule = write a file of this shape into the designated folder, or call <c>islebar push</c>.
/// Compatible with the Python version's <c>%TEMP%\tgprog\tgprog_*.json</c> — any key not present in those files is optional.
/// </summary>
public sealed class ActivityState
{
    /// <summary>
    /// The kind of work. Older files lack it, so when absent <see cref="Kind"/> infers it from <see cref="State"/>.
    /// Values: claude-permission · error · transfer · claude-done · timer · music
    /// </summary>
    [JsonPropertyName("kind")]
    public string? RawKind { get; set; }

    /// <summary>
    /// Which agent the work belongs to (<c>claude</c>/<c>codex</c>). Treated as Claude when absent
    /// — older files and already-configured Claude hooks do not have this field.
    /// </summary>
    [JsonPropertyName("agent")]
    public string? Agent { get; set; }

    /// <summary>Title. E.g. "📥 Phone → PC", "📤 PC → Phone".</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>File name, song title, etc. — the name shortened to fit the slot width.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>App identity (AUMID) behind a message notice, so the expanded card can show the app's real icon. Optional.</summary>
    [JsonPropertyName("appid")]
    public string? AppId { get; set; }

    /// <summary>Stage. "upload", "finish", etc. "finish" is shown as "finishing".</summary>
    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    /// <summary>Total bytes. 0 or null means the ratio is unknown.</summary>
    [JsonPropertyName("total")]
    public long? Total { get; set; }

    /// <summary>
    /// Bytes transferred so far. <b>null means the ratio is unknown</b> (server-side processing) →
    /// an indeterminate bar is drawn. Based on the amount actually transferred.
    /// </summary>
    [JsonPropertyName("done")]
    public long? Done { get; set; }

    /// <summary>"run" · "done" · "error".</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>One-line message. If present it can be shown instead of <see cref="Name"/>.</summary>
    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    /// <summary>Path to open when clicked after completion.</summary>
    [JsonPropertyName("open")]
    public string? Open { get; set; }

    /// <summary>Start time (Unix seconds).</summary>
    [JsonPropertyName("t0")]
    public double? T0 { get; set; }

    /// <summary>
    /// Where the agent keeps its session record (the hook's <c>transcript_path</c>). For Codex the bar reads it, because a turn that
    /// fails on the usage limit fires no hook — only the record says so (10-03).
    /// </summary>
    [JsonPropertyName("transcript")]
    public string? Transcript { get; set; }

    /// <summary>When the item ends (Unix seconds): a timer's end, or until when an agent notice (usage limit, error) stays.</summary>
    [JsonPropertyName("due")]
    public double? Due { get; set; }

    /// <summary>
    /// Icon for a short notice (<see cref="ActivityKind.Notice"/>): a Segoe Fluent Icons character code (e.g. "" lightning).
    /// If absent, the default icon for the kind.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("glyph")]
    public string? Glyph { get; set; }

    /// <summary>Process ID of the agent (claude.exe/codex.exe) that sent the notice. Clicking the island jumps to that terminal.</summary>
    [JsonPropertyName("pid")]
    public int? Pid { get; set; }

    /// <summary>Seconds left on a paused timer. If set, the timer is paused (<see cref="Due"/> is cleared).</summary>
    [JsonPropertyName("left")]
    public double? Left { get; set; }

    /// <summary>Transfer speed in bytes a second, when measured (browser downloads). Not serialized to JSON.</summary>
    [JsonIgnore]
    public double? Speed { get; set; }

    /// <summary>The app's own name behind <see cref="AppId"/> ("网易云音乐"), looked up by the App. Not serialized to JSON.</summary>
    [JsonIgnore]
    public string? AppName { get; set; }

    /// <summary>Each download under way (the merged download card, F2b), first come first. Not serialized to JSON.</summary>
    [JsonIgnore]
    public IReadOnlyList<DownloadLine>? Downloads { get; set; }

    /// <summary>Every app behind the item, when there can be several (mic / camera in use). Not serialized to JSON.</summary>
    [JsonIgnore]
    public IReadOnlyList<string>? Apps { get; set; }

    /// <summary>
    /// Came from the person's own act — files dropped on the pill (10-10): it debuts even with the mouse on the pills (they're looking
    /// right at it). Not serialized to JSON.
    /// </summary>
    [JsonIgnore]
    public bool ByHand { get; set; }

    /// <summary>The transit station's files, oldest first (TODO ④, 10-10). Not serialized to JSON.</summary>
    [JsonIgnore]
    public IReadOnlyList<string>? Files { get; set; }

    /// <summary>Those of <see cref="Files"/> no longer where they were ("not found"). Not serialized to JSON.</summary>
    [JsonIgnore]
    public IReadOnlyList<string>? Gone { get; set; }

    /// <summary>How many more items this one stands for ("+2" — other browser downloads). Not serialized to JSON.</summary>
    [JsonIgnore]
    public int MoreCount { get; set; }

    /// <summary>
    /// The state put in words, shown first in the main colour with <see cref="Name"/> grey after it — Explorer's copies (10-10:
    /// "已复制 5 个 跳过 149 个"). Null = the card's usual prefix. Not serialized to JSON.
    /// </summary>
    [JsonIgnore]
    public string? Words { get; set; }

    /// <summary>
    /// A temporary card's time on the big pill, counted from when it goes up (null = <see cref="SystemWatch.NoticeLengths.Read"/>;
    /// <see cref="SystemWatch.NoticeBoard.Flash"/> sets it). Not serialized to JSON.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? ShowFor { get; set; }

    /// <summary>
    /// The messages merged into this one, oldest first (one app's notifications in a row share a card, user 10-10: the pill says
    /// "+N", the lifted card lists them all). Not serialized to JSON.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ActivityState> Earlier { get; set; } = [];

    /// <summary>
    /// What the card is showing now, for a card that keeps its key while it changes looks (the calendar, user 10-10: one card all
    /// day). When set, the card's debuts follow its look instead of its type: a new look debuts if it asks to (<see cref="DebutFor"/>).
    /// Not serialized to JSON.
    /// </summary>
    [JsonIgnore]
    public string? Look { get; set; }

    /// <summary>A new <see cref="Look"/> debuts for this long (null = it goes straight to its cell). Not serialized to JSON.</summary>
    [JsonIgnore]
    public TimeSpan? DebutFor { get; set; }

    /// <summary>Whether this is a paused transfer (a browser download whose file stopped growing).</summary>
    [JsonIgnore]
    public bool IsPausedTransfer => Kind == ActivityKind.Transfer && Stage == "paused";

    /// <summary>Whether this is a paused timer.</summary>
    [JsonIgnore]
    public bool IsPaused => Left is not null;

    /// <summary>Path of the file this state was read from. Not serialized to JSON.</summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    /// <summary>When the file last changed. Used to filter out stale ones. Not serialized to JSON.</summary>
    [JsonIgnore]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Run state. <see cref="ActivityRunState.Running"/> if unknown.</summary>
    [JsonIgnore]
    public ActivityRunState RunState => State switch
    {
        "done" => ActivityRunState.Done,
        "error" => ActivityRunState.Error,
        _ => ActivityRunState.Running,
    };

    /// <summary>
    /// The kind of work. <see cref="RawKind"/> if present; otherwise treated as an old transfer file:
    /// <see cref="ActivityKind.Error"/> on error, else <see cref="ActivityKind.Transfer"/>.
    /// </summary>
    [JsonIgnore]
    public ActivityKind Kind => ParseKind(RawKind) ?? (RunState == ActivityRunState.Error
        ? ActivityKind.Error
        : ActivityKind.Transfer);

    /// <summary>Transfer direction. A title ending in "PC" means incoming.</summary>
    [JsonIgnore]
    public TransferDirection Direction
        => (Title ?? string.Empty).TrimEnd().EndsWith("PC", StringComparison.Ordinal)
            ? TransferDirection.Incoming
            : TransferDirection.Outgoing;

    /// <summary>Progress ratio 0–1. Null if the total is unknown.</summary>
    [JsonIgnore]
    public double? Fraction
        => Total is > 0 && Done is not null
            ? Math.Clamp((double)Done.Value / Total.Value, 0, 1)
            : null;

    /// <summary>Whether the ratio is unknown (indeterminate bar).</summary>
    [JsonIgnore]
    public bool IsIndeterminate => RunState == ActivityRunState.Running && Done is null;

    /// <summary>Agent name shown on the island. Claude if absent.</summary>
    [JsonIgnore]
    public string AgentLabel => string.Equals(Agent, "codex", StringComparison.OrdinalIgnoreCase) ? "Codex" : "Claude";

    /// <summary>Whether this is the "finish" stage — shown as "finishing".</summary>
    [JsonIgnore]
    public bool IsFinishing => Stage == "finish";

    // ---- kind string ↔ ActivityKind ----

    public const string KindAgentPermission = "agent-permission";
    public const string KindAgentDone = "agent-done";

    /// <summary>Legacy name. Still read because already-configured Claude Code hooks use it.</summary>
    public const string KindClaudePermissionLegacy = "claude-permission";

    /// <summary>Legacy name. Still read.</summary>
    public const string KindClaudeDoneLegacy = "claude-done";
    public const string KindError = "error";
    public const string KindTransfer = "transfer";
    public const string KindTimer = "timer";
    public const string KindMusic = "music";
    public const string KindNotice = "notice";
    public const string KindAgentWorking = "agent-working";

    public static ActivityKind? ParseKind(string? raw) => raw switch
    {
        KindAgentPermission or KindClaudePermissionLegacy => ActivityKind.AgentPermission,
        KindError => ActivityKind.Error,
        KindTransfer => ActivityKind.Transfer,
        KindAgentDone or KindClaudeDoneLegacy => ActivityKind.AgentDone,
        KindTimer => ActivityKind.Timer,
        KindMusic => ActivityKind.Music,
        KindNotice => ActivityKind.Notice,
        KindAgentWorking => ActivityKind.AgentWorking,
        _ => null,
    };

    public static string KindToString(ActivityKind kind) => kind switch
    {
        ActivityKind.AgentPermission => KindAgentPermission,
        ActivityKind.Error => KindError,
        ActivityKind.Transfer => KindTransfer,
        ActivityKind.AgentDone => KindAgentDone,
        ActivityKind.Timer => KindTimer,
        ActivityKind.Music => KindMusic,
        ActivityKind.Notice => KindNotice,
        ActivityKind.AgentWorking => KindAgentWorking,
        _ => KindTransfer,
    };

    /// <summary>All kind strings (for CLI help).</summary>
    public static IReadOnlyList<string> AllKinds =>
    [
        KindAgentPermission, KindError, KindTransfer, KindNotice, KindAgentDone, KindTimer, KindAgentWorking, KindMusic,
    ];
}
