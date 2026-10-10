using IsleBar.Core.Island;
using Xunit;

namespace IsleBar.Core.Tests;

public class AgentUsageTests
{
    [Fact]
    public void Codex_record_gives_usage_and_skips_premium_snapshots()
    {
        var w = AgentUsage.FromCodexLine("""
            {"type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":97.0,"window_minutes":300,"resets_at":1790973732},"secondary":{"used_percent":17.0,"window_minutes":10080,"resets_at":1791418185}}}}
            """)!;
        Assert.Equal(97.0, w.Single(x => x.Key == "5h").Percent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790973732), AgentUsage.ResetOfFullest(w));
        Assert.Null(AgentUsage.FromCodexLine("""{"type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"premium","primary":null,"secondary":null}}}"""));
        Assert.Null(AgentUsage.FromCodexLine("""{"type":"response_item","payload":{"type":"message"}}"""));
    }

    [Fact]
    public void Codex_failed_turns_are_told_apart()
    {
        var limit = AgentUsage.CodexFailureFrom("""
            {"type":"event_msg","payload":{"type":"task_complete","error":{"message":"You've hit your usage limit.","codex_error_info":"usage_limit_exceeded"}}}
            """)!;
        Assert.True(limit.UsageLimit);
        var auth = AgentUsage.CodexFailureFrom("""{"type":"event_msg","payload":{"type":"task_complete","error":{"message":"x","codex_error_info":"unauthorized"}}}""")!;
        Assert.False(auth.UsageLimit);
        Assert.Equal(StopReason.Login, auth.Reason);
        Assert.Null(AgentUsage.CodexFailureFrom("""{"type":"event_msg","payload":{"type":"task_complete","last_agent_message":"done"}}"""));
    }

    [Fact]
    public void Claude_stop_reasons_map_from_error_types()
    {
        Assert.Null(AgentNotices.ClaudeReason("rate_limit"));
        Assert.Equal(StopReason.Login, AgentNotices.ClaudeReason("authentication_failed"));
        Assert.Equal(StopReason.Billing, AgentNotices.ClaudeReason("billing_error"));
        Assert.Equal(StopReason.Busy, AgentNotices.ClaudeReason("overloaded"));
        Assert.Equal(StopReason.Other, AgentNotices.ClaudeReason("max_output_tokens"));
        var stopped = AgentHook.FromClaude("""{"hook_event_name":"StopFailure","session_id":"s","error_type":"authentication_failed"}""")!.Write!;
        Assert.Equal(ActivityRunState.Error, stopped.RunState);
        Assert.Equal("Claude stopped", stopped.Title);
        Assert.Equal("sign in again", stopped.Msg);
    }
}
