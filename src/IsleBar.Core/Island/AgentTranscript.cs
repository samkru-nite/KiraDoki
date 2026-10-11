using System.Text.Json;
using System.Text.RegularExpressions;
using IsleBar.Core.Launch;

namespace IsleBar.Core.Island;

/// <summary>
/// What a session record's end says: the agent's last answer, its latest step (a tool call), and what it last said <b>in the
/// turn under way</b> (<see cref="Said"/>: null until it says something after the latest prompt — never the turn before's answer).
/// Any may be missing.
/// </summary>
public sealed record AgentTail(string? Answer, string? Step, string? Said = null);

/// <summary>
/// Reads an agent's last answer and latest step from the end of its session record (JSONL) — the lifted card's body
/// (TODO 7.9 F2b): done = the last answer, working / needs you = the latest step. Only text is parsed here; the App reads
/// the file's tail (<see cref="TailBytes"/>), so the first line may be cut and broken lines are skipped.
/// <list type="bullet">
/// <item>Claude Code: one content block per line — <c>{"type":"assistant","message":{"id":…,"content":[{"type":"text"…}|{"type":"tool_use"…}]}}</c>.
/// Sub-agent lines (<c>isSidechain</c>) don't count. Text blocks of one message written on several lines are joined.</item>
/// <item>Codex: <c>{"type":"response_item","payload":{"type":"message","role":"assistant",…}}</c> or a tool call
/// (<c>function_call</c> / <c>custom_tool_call</c> / <c>local_shell_call</c>); <c>task_complete</c> carries the final answer too.</item>
/// </list>
/// </summary>
public static class AgentTranscript
{
    public const int TailBytes = 256 * 1024;

    /// <summary>A step longer than this is cut (it is one line on the card).</summary>
    public const int StepLength = 120;

    public static AgentTail FromTail(string? text, string? agent)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new AgentTail(null, null);
        }

        string? answer = null, answerId = null, step = null, said = null;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length < 2 || line[0] != '{')
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var before = answer;
                if (agent == AgentKind.Codex ? CodexPrompt(root) : ClaudePrompt(root))
                {
                    said = null;   // a new turn: nothing said in it yet
                    answerId = null;
                    continue;
                }

                if (agent == AgentKind.Codex)
                {
                    Codex(root, ref answer, ref step);
                }
                else
                {
                    Claude(root, ref answer, ref answerId, ref step);
                }

                if (!ReferenceEquals(before, answer))
                {
                    said = answer;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new AgentTail(Clean(answer), step, Clean(said));
    }

    private static void Claude(JsonElement root, ref string? answer, ref string? answerId, ref string? step)
    {
        if (Str(root, "type") != "assistant" || (root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True)
            || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var id = Str(message, "id");
        foreach (var block in content.EnumerateArray())
        {
            switch (Str(block, "type"))
            {
                case "text" when Str(block, "text") is { } said && !string.IsNullOrWhiteSpace(said):
                    answer = id is not null && id == answerId && answer is not null ? answer + "\n\n" + said : said;
                    answerId = id;
                    break;
                case "tool_use":
                    step = ClaudeStep(block) ?? step;
                    answerId = null;   // text after a tool call starts a new answer
                    break;
            }
        }
    }

    /// <summary>A prompt the person typed: a user line with words (a tool's result comes back as a user line too — not a prompt).</summary>
    private static bool ClaudePrompt(JsonElement root)
    {
        if (Str(root, "type") != "user" || IsTrue(root, "isSidechain") || IsTrue(root, "isMeta")
            || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content))
        {
            return false;
        }

        return content.ValueKind == JsonValueKind.String
               || (content.ValueKind == JsonValueKind.Array
                   && content.EnumerateArray().Any(b => Str(b, "type") == "text")
                   && !content.EnumerateArray().Any(b => Str(b, "type") == "tool_result"));
    }

    private static bool CodexPrompt(JsonElement root)
        => Str(root, "type") == "response_item" && root.TryGetProperty("payload", out var p)
           && Str(p, "type") == "message" && Str(p, "role") == "user";

    private static bool IsTrue(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>"Edit · CardSheet.cs", or a shell command's own description ("Build the app").</summary>
    private static string? ClaudeStep(JsonElement block)
    {
        if (Str(block, "name") is not { } name)
        {
            return null;
        }

        if (!block.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object)
        {
            return name;
        }

        var detail = Str(input, "description")
                     ?? FileName(Str(input, "file_path") ?? Str(input, "notebook_path"))
                     ?? Str(input, "pattern") ?? Str(input, "command") ?? Str(input, "url") ?? Str(input, "query");
        return Cut(detail is null ? name : Ui.TextGap.Join(name, FirstLine(detail)));   // "Bash  ls -la": a gap, no " · "
    }

    private static void Codex(JsonElement root, ref string? answer, ref string? step)
    {
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        switch (Str(root, "type"), Str(payload, "type"))
        {
            case ("event_msg", "task_complete") when Str(payload, "last_agent_message") is { } last && !string.IsNullOrWhiteSpace(last):
                answer = last;
                break;
            case ("response_item", "message") when Str(payload, "role") == "assistant"
                                                  && payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array:
                var said = string.Join("\n\n", content.EnumerateArray()
                    .Where(b => Str(b, "type") == "output_text").Select(b => Str(b, "text")).Where(t => !string.IsNullOrWhiteSpace(t)));
                if (said.Length > 0)
                {
                    answer = said;
                }

                break;
            case ("response_item", "function_call" or "custom_tool_call" or "local_shell_call"):
                step = CodexStep(payload) ?? step;
                break;
        }
    }

    private static readonly Regex CmdInScript = new(@"cmd:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant);

    // the ChatGPT app asking for permission: tools.request_permissions({permissions:{…},"reason":"…"}) — the reason is the step (10-11)
    private static readonly Regex ReasonInScript = new(@"request_permissions\(.*?""?reason""?\s*:\s*""((?:[^""\\]|\\.)*)""",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>The command a Codex tool call runs (its first line), else the tool's name.</summary>
    private static string? CodexStep(JsonElement payload)
    {
        var name = Str(payload, "name") ?? "shell";
        string? command = null;
        if (Str(payload, "arguments") is { } args)
        {
            try
            {
                using var doc = JsonDocument.Parse(args);
                command = Command(doc.RootElement);
            }
            catch (JsonException)
            {
            }
        }
        else if (Str(payload, "input") is { } script
                 && (ReasonInScript.Match(script) is { Success: true } r ? r : CmdInScript.Match(script)) is { Success: true } m)
        {
            try
            {
                command = JsonSerializer.Deserialize<string>("\"" + m.Groups[1].Value + "\"");
            }
            catch (JsonException)
            {
            }
        }
        else if (payload.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.Object)
        {
            command = Command(action);
        }

        return Cut(command is null ? name : FirstLine(command));
    }

    private static string? Command(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in (string[])["cmd", "command"])
        {
            if (!e.TryGetProperty(key, out var v))
            {
                continue;
            }

            if (v.ValueKind == JsonValueKind.String)
            {
                return v.GetString();
            }

            if (v.ValueKind == JsonValueKind.Array)
            {
                var parts = v.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!).ToList();
                // ["powershell.exe", "-Command", "git status"] → the script is what says what it does
                return parts.Count > 0 ? parts[^1] : null;
            }
        }

        return null;
    }

    private static string? FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var cut = path.TrimEnd('/', '\\').LastIndexOfAny(['/', '\\']);
        return cut >= 0 ? path.TrimEnd('/', '\\')[(cut + 1)..] : path;
    }

    private static string FirstLine(string s)
    {
        var t = s.Trim();
        var cut = t.IndexOf('\n');
        return (cut >= 0 ? t[..cut] : t).Trim();
    }

    private static string? Cut(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        s = s.Trim();
        if (s.Length <= StepLength)
        {
            return s;
        }

        var cut = char.IsHighSurrogate(s[StepLength - 2]) ? StepLength - 2 : StepLength - 1;
        return s[..cut].TrimEnd() + "…";
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Str(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
