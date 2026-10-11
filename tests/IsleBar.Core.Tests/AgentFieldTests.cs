using IsleBar.Core.Island;
using IsleBar.Core.Launch;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>The island's agent field (owner's requests, 2026-09-29).</summary>
public sealed class AgentFieldTests
{
    [Fact]
    public void Island_shows_which_agent()
    {
        Assert.Equal("ChatGPT", new ActivityState { Agent = "codex" }.AgentLabel);
        Assert.Equal("ChatGPT", new ActivityState { Agent = "CODEX" }.AgentLabel);
        Assert.Equal("Claude", new ActivityState { Agent = "claude" }.AgentLabel);
        Assert.Equal("Claude", new ActivityState().AgentLabel);   // no field → Claude
    }

    [Theory]
    [InlineData("agent-done", ActivityKind.AgentDone)]
    [InlineData("agent-permission", ActivityKind.AgentPermission)]
    [InlineData("claude-done", ActivityKind.AgentDone)]              // hooks already configured
    [InlineData("claude-permission", ActivityKind.AgentPermission)]  // hooks already configured
    public void Legacy_names_are_still_read(string raw, ActivityKind expected)
        => Assert.Equal(expected, ActivityState.ParseKind(raw));

    [Fact]
    public void Agent_field_is_written_to_the_file_too()
    {
        using var dir = new TempDir();
        var store = new ActivityStore(dir.Path);
        store.Write("코덱스작업", new ActivityState
        {
            RawKind = ActivityState.KindAgentDone,
            Agent = AgentKind.Codex,
            Title = "Codex",
            Name = "고치기",
            State = "done",
        });

        var back = store.Read(DateTimeOffset.UtcNow).Single();
        Assert.Equal(AgentKind.Codex, back.Agent);
        Assert.Equal("ChatGPT", back.AgentLabel);
        Assert.Equal(ActivityKind.AgentDone, back.Kind);
    }
}
