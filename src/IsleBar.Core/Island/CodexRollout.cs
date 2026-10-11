using System.Globalization;
using System.Text.Json;
using IsleBar.Core.Launch;

namespace IsleBar.Core.Island;

/// <summary>What a Codex session's card should say now.</summary>
public enum CodexLook
{
    None,
    Working,
    Asking,
    Done,
    Failed,
}

/// <summary>
/// One Codex session record (<c>~/.codex/sessions/yyyy/MM/dd/rollout-…-{id}.jsonl</c>) read line by line — the ChatGPT app (the old
/// Codex app) runs no hooks unless the person approves them, so its card comes from the record alone (user 10-11). Seen in real
/// records 10-11:
/// <list type="bullet">
/// <item><c>event_msg</c> <c>task_started</c> = a turn began (working); <c>task_complete</c> = it ended — with <c>error</c> it failed
/// (usage limit, login…), else done; <c>turn_aborted</c> = the person pressed stop (the card goes, like Claude's Esc).</item>
/// <item>Asking for permission: a tool call whose script calls <c>tools.request_permissions(</c>; the answer is that call's output.
/// A long wait first gives an output saying "Script running" (the script yielded) — still waiting, the next finished output ends it.
/// Only asking for longer than <see cref="AskAfter"/> turns the card orange: the app's own reviewer can answer within seconds
/// (user 10-11: 2–3 s late is fine, a false orange is not).</item>
/// <item><c>session_meta</c> (the first line): a <c>parent_thread_id</c> = a sub-agent or the approvals reviewer — no card of its
/// own, the session it works for has one.</item>
/// </list>
/// Types not known here are collected in <see cref="Unknown"/> so the bar can log them (a new version may add one that matters).
/// </summary>
public sealed class CodexRollout
{
    public static readonly TimeSpan AskAfter = TimeSpan.FromSeconds(3);

    private static readonly HashSet<string> KnownTop = new(StringComparer.Ordinal)
    {
        "session_meta", "event_msg", "response_item", "world_state", "turn_context", "token_usage_record", "compacted",
        "inter_agent_communication_metadata",
    };

    private static readonly HashSet<string> KnownEvents = new(StringComparer.Ordinal)
    {
        "task_started", "task_complete", "turn_aborted", "token_count", "item_completed", "thread_settings_applied",
        "agent_message", "user_message", "agent_reasoning", "context_compacted",
    };

    private readonly Dictionary<string, DateTimeOffset> _asking = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unknown = new(StringComparer.Ordinal);

    public string? ParentId { get; private set; }

    public CodexLook Phase { get; private set; } = CodexLook.None;

    /// <summary>When the turn under way (or the last one) began.</summary>
    public DateTimeOffset? TurnStart { get; private set; }

    /// <summary>The first thing the person asked in this record — the card's name until the app names the conversation.</summary>
    public string? Prompt { get; private set; }

    public CodexFailure? Failure { get; private set; }

    /// <summary>The plan's usage windows, last seen (when a hit limit lifts).</summary>
    public IReadOnlyList<UsageWindow>? Windows { get; private set; }

    /// <summary>"event_msg/xyz"-style types met that aren't known here, each once.</summary>
    public IReadOnlyCollection<string> Unknown => _unknown;

    /// <summary>Since when it has been asking for permission, null if it isn't.</summary>
    public DateTimeOffset? AskingSince => _asking.Count == 0 ? null : _asking.Values.Min();

    public void Read(string line)
    {
        if (line.Length < 2 || line[0] != '{')
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            Read(doc.RootElement, line);
        }
        catch (JsonException)
        {
            // a line cut by the first read of a tail, or mid-write
        }
    }

    private void Read(JsonElement root, string line)
    {
        var top = Str(root, "type") ?? "?";
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            if (!KnownTop.Contains(top))
            {
                _unknown.Add(top);
            }

            return;
        }

        var at = Time(root);
        var kind = Str(payload, "type");
        switch (top)
        {
            case "session_meta":
                ParentId = Str(payload, "parent_thread_id");
                break;
            case "event_msg":
                Event(kind, payload, at, line);
                break;
            case "response_item":
                Item(kind, payload, at);
                break;
            default:
                if (!KnownTop.Contains(top))
                {
                    _unknown.Add(top);
                }

                break;
        }
    }

    private void Event(string? kind, JsonElement payload, DateTimeOffset? at, string line)
    {
        switch (kind)
        {
            case "task_started":
                Phase = CodexLook.Working;
                TurnStart = at;
                Failure = null;
                _asking.Clear();
                break;
            case "task_complete":
                Failure = AgentUsage.CodexFailureFrom(line);
                Phase = Failure is null ? CodexLook.Done : CodexLook.Failed;
                _asking.Clear();
                break;
            case "turn_aborted":
                Phase = CodexLook.None;
                _asking.Clear();
                break;
            case "token_count":
                Windows = AgentUsage.FromCodexLine(line) ?? Windows;
                break;
            case "item_completed" when Prompt is null && payload.TryGetProperty("item", out var item)
                                       && Str(item, "type") == "UserMessage":
                Prompt = AgentHook.PromptName(Texts(item, "content", "text"));
                break;
            case "user_message" when Prompt is null:
                Prompt = AgentHook.PromptName(Str(payload, "message"));
                break;
            case null:
                break;
            default:
                if (!KnownEvents.Contains(kind))
                {
                    _unknown.Add("event_msg/" + kind);
                }

                break;
        }
    }

    private void Item(string? kind, JsonElement payload, DateTimeOffset? at)
    {
        switch (kind)
        {
            case "custom_tool_call" when (Str(payload, "input") ?? string.Empty).Contains("tools.request_permissions(", StringComparison.Ordinal):
            case "function_call" when Str(payload, "name") == "request_permissions":
                if (Str(payload, "call_id") is { } call)
                {
                    _asking[call] = at ?? DateTimeOffset.UtcNow;
                }

                break;
            case "custom_tool_call_output" or "function_call_output" when _asking.Count > 0:
                // "Script running …": the script yielded while still waiting — a later finished output ends the wait
                if (!Output(payload).StartsWith("Script running", StringComparison.Ordinal))
                {
                    _asking.Clear();
                }

                break;
        }
    }

    /// <summary>What the card should be at <paramref name="now"/> (asking only after <see cref="AskAfter"/>).</summary>
    public CodexLook LookAt(DateTimeOffset now)
        => Phase == CodexLook.Working && AskingSince is { } since && now - since >= AskAfter ? CodexLook.Asking : Phase;

    /// <summary>
    /// The card for <paramref name="look"/>: <paramref name="name"/> = the conversation's name (else the first prompt),
    /// <paramref name="record"/> = this file (the lifted card reads the answer and the latest step from it).
    /// </summary>
    public ActivityState? Card(CodexLook look, string? name, string record, Localization.LanguageStrings? text, DateTimeOffset now)
    {
        name ??= Prompt;
        return look switch
        {
            CodexLook.Working => new ActivityState
            {
                RawKind = ActivityState.KindAgentWorking, Agent = AgentKind.Codex, Name = name, State = "run",
                T0 = (TurnStart ?? now).ToUnixTimeMilliseconds() / 1000.0, Transcript = record,
            },
            CodexLook.Asking => new ActivityState
            {
                RawKind = ActivityState.KindAgentPermission, Agent = AgentKind.Codex, Name = name, State = "run", Transcript = record,
            },
            CodexLook.Done => new ActivityState
            {
                RawKind = ActivityState.KindAgentDone, Agent = AgentKind.Codex, Name = name, State = "done", Transcript = record,
            },
            CodexLook.Failed when Failure is { } failure => failure.UsageLimit
                ? AgentNotices.UsageLimit(AgentKind.Codex, AgentUsage.ResetOfFullest(Windows), text, now)
                : AgentNotices.Stopped(AgentKind.Codex, failure.Reason, text, now),
            _ => null,
        };
    }

    /// <summary>The state file id: "codex_" + the end of the session id (its start is a timestamp two sessions can share).</summary>
    public static string IdFor(string record)
    {
        var name = Path.GetFileNameWithoutExtension(record);
        var tail = new string(name.Where(char.IsAsciiHexDigit).TakeLast(12).ToArray());
        return $"{AgentKind.Codex}_{(tail.Length > 0 ? tail : "agent")}";
    }

    private static string Output(JsonElement payload)
    {
        if (!payload.TryGetProperty("output", out var output))
        {
            return string.Empty;
        }

        return output.ValueKind switch
        {
            JsonValueKind.String => output.GetString() ?? string.Empty,
            JsonValueKind.Array => output.EnumerateArray().Select(b => Str(b, "text")).FirstOrDefault(t => t is not null) ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static string? Texts(JsonElement e, string list, string key)
        => e.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(b => Str(b, key)).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            : null;

    private static DateTimeOffset? Time(JsonElement root)
        => Str(root, "timestamp") is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;

    private static string? Str(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
