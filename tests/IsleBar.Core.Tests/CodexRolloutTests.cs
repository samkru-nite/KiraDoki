using IsleBar.Core.Island;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>The ChatGPT app's cards from its session record (10-11) — lines shaped like the real records seen that day.</summary>
public class CodexRolloutTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 11, 15, 5, 16, TimeSpan.Zero);

    private static string At(int seconds) => T.AddSeconds(seconds).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    private static string Started(int s) => $$$"""{"timestamp":"{{{At(s)}}}","type":"event_msg","payload":{"type":"task_started","turn_id":"t1"}}""";

    private static string Prompt(int s, string text)
        => $$$"""{"timestamp":"{{{At(s)}}}","type":"event_msg","payload":{"type":"item_completed","item":{"type":"UserMessage","content":[{"type":"text","text":"{{{text}}}"}]""" + "}}}";

    private static string Complete(int s)
        => $$$"""{"timestamp":"{{{At(s)}}}","type":"event_msg","payload":{"type":"task_complete","turn_id":"t1","last_agent_message":"Done it."}}""";

    private static string Ask(int s, string call)
        => $$$"""{"timestamp":"{{{At(s)}}}","type":"response_item","payload":{"type":"custom_tool_call","call_id":"{{{call}}}","name":"exec","input":"const r = await tools.request_permissions({permissions:{network:{enabled:true}},\"reason\":\"Download the SDK\"});"}}""";

    private static string Output(int s, string call, string text)
        => $$$"""{"timestamp":"{{{At(s)}}}","type":"response_item","payload":{"type":"custom_tool_call_output","call_id":"{{{call}}}","output":[{"type":"input_text","text":"{{{text}}}"}]}}""";

    private static CodexRollout Read(params string[] lines)
    {
        var rollout = new CodexRollout();
        foreach (var line in lines)
        {
            rollout.Read(line);
        }

        return rollout;
    }

    [Fact]
    public void A_turn_works_then_is_done_named_after_the_first_prompt()
    {
        var r = Read(Started(0), Prompt(0, "在桌面随便新建一个文件夹"));
        Assert.Equal(CodexLook.Working, r.LookAt(T.AddSeconds(5)));
        Assert.Equal("在桌面随便新建一个文件夹", r.Prompt);

        r.Read(Complete(40));
        Assert.Equal(CodexLook.Done, r.LookAt(T.AddSeconds(41)));
        var card = r.Card(CodexLook.Done, null, @"C:\x\rollout-a.jsonl", null, T);
        Assert.Equal(ActivityKind.AgentDone, card!.Kind);
        Assert.Equal("ChatGPT", card.AgentLabel);
        Assert.Equal("在桌面随便新建一个文件夹", card.Name);
        Assert.Equal(@"C:\x\rollout-a.jsonl", card.Transcript);
    }

    [Fact]
    public void Stopped_by_the_person_means_no_card()
    {
        var r = Read(Started(0), """{"type":"event_msg","payload":{"type":"turn_aborted","reason":"interrupted"}}""");
        Assert.Equal(CodexLook.None, r.LookAt(T.AddSeconds(20)));
        Assert.Null(r.Card(CodexLook.None, "x", "r", null, T));
    }

    [Fact]
    public void Asking_turns_orange_only_after_three_seconds_and_ends_with_the_answer()
    {
        var r = Read(Started(0), Ask(10, "c1"));
        Assert.Equal(CodexLook.Working, r.LookAt(T.AddSeconds(12)));   // the app's own reviewer may answer within seconds
        Assert.Equal(CodexLook.Asking, r.LookAt(T.AddSeconds(13)));
        Assert.Equal(ActivityKind.AgentPermission, r.Card(CodexLook.Asking, "n", "r", null, T)!.Kind);

        r.Read(Output(40, "c1", "Script running with cell ID 15\\nWall time 31.0 seconds"));
        Assert.Equal(CodexLook.Asking, r.LookAt(T.AddSeconds(45)));   // yielded while still waiting

        r.Read(Output(50, "c2", "Script completed\\nWall time 5.0 seconds"));
        Assert.Equal(CodexLook.Working, r.LookAt(T.AddSeconds(55)));
    }

    [Fact]
    public void An_answer_within_three_seconds_never_shows_orange()
    {
        var r = Read(Started(0), Ask(10, "c1"), Output(12, "c1", "Script completed"));
        Assert.Equal(CodexLook.Working, r.LookAt(T.AddSeconds(30)));
    }

    [Fact]
    public void A_failed_turn_becomes_the_usage_limit_notice()
    {
        var r = Read(Started(0),
            """{"type":"event_msg","payload":{"type":"task_complete","error":{"codex_error_info":"usage_limit_exceeded"}}}""");
        Assert.Equal(CodexLook.Failed, r.LookAt(T));
        var card = r.Card(CodexLook.Failed, "n", "r", null, T);
        Assert.Equal(ActivityKind.Notice, card!.Kind);
        Assert.Equal("error", card.State);
    }

    [Fact]
    public void Sub_agents_and_the_reviewer_are_marked_and_unknown_types_collected()
    {
        var r = Read("""{"type":"session_meta","payload":{"id":"x","parent_thread_id":"01a0a52b","thread_source":"guardian_review"}}""",
            """{"type":"event_msg","payload":{"type":"exec_approval_request"}}""",
            """{"type":"brand_new","payload":{}}""");
        Assert.Equal("01a0a52b", r.ParentId);
        Assert.Contains("event_msg/exec_approval_request", r.Unknown);
        Assert.Contains("brand_new", r.Unknown);
    }

    [Fact]
    public void The_state_id_is_the_end_of_the_session_id()
    {
        // two sessions started within a minute share the start of their ids (it is a timestamp)
        Assert.Equal("codex_6d5e129c4f3b",
            CodexRollout.IdFor(@"C:\s\rollout-2026-10-11T19-35-34-01a12abf-2bd6-7032-adde-6d5e129c4f3b.jsonl"));
        Assert.NotEqual(CodexRollout.IdFor("rollout-01a099c7-552a-72f3-a89e-7db0bc6064b0.jsonl"),
            CodexRollout.IdFor("rollout-01a099c7-6f96-7890-b601-ccb553eba697.jsonl"));
    }

    [Fact]
    public void Asking_for_permission_shows_its_reason_as_the_step()
    {
        var tail = AgentTranscript.FromTail(Ask(0, "c1"), Core.Launch.AgentKind.Codex);
        Assert.Equal("Download the SDK", tail.Step);
    }
}
