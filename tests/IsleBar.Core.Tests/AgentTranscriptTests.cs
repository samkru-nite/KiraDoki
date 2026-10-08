using IsleBar.Core.Island;
using IsleBar.Core.Launch;
using Xunit;

namespace IsleBar.Core.Tests;

public class AgentTranscriptTests
{
    private static string Claude(string id, string block, bool side = false)
        => $$"""{"parentUuid":"x","isSidechain":{{(side ? "true" : "false")}},"message":{"id":"{{id}}","role":"assistant","content":[{{block}}]},"type":"assistant"}""";

    [Fact]
    public void Claude_the_last_answer_is_the_text_after_the_last_tool_call()
    {
        var text = string.Join('\n',
            "ken line from the middle\"}",
            Claude("m1", """{"type":"text","text":"先看看代码"}"""),
            Claude("m1", """{"type":"tool_use","name":"Edit","input":{"file_path":"C:\\src\\CardSheet.cs","old_string":"a"}}"""),
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","content":"ok"}]}}""",
            Claude("m2", """{"type":"text","text":"改好了。"}"""),
            Claude("m2", """{"type":"text","text":"构建通过。"}"""),
            """{"type":"system","subtype":"stop_hook_summary"}""",
            "");

        var tail = AgentTranscript.FromTail(text, AgentKind.Claude);

        Assert.Equal("改好了。\n\n构建通过。", tail.Answer);
        Assert.Equal("Edit · CardSheet.cs", tail.Step);
    }

    [Fact]
    public void Claude_a_shell_step_uses_its_description_and_sub_agents_do_not_count()
    {
        var text = string.Join('\n',
            Claude("m1", """{"type":"tool_use","name":"PowerShell","input":{"command":"dotnet build","description":"Build the app"}}"""),
            Claude("s1", """{"type":"tool_use","name":"Grep","input":{"pattern":"foo"}}""", side: true),
            Claude("s1", """{"type":"text","text":"sub-agent talk"}""", side: true));

        var tail = AgentTranscript.FromTail(text, AgentKind.Claude);

        Assert.Equal("PowerShell · Build the app", tail.Step);
        Assert.Null(tail.Answer);
    }

    [Fact]
    public void Codex_reads_output_text_and_the_command_of_a_tool_call()
    {
        var text = string.Join('\n',
            """{"type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"powershell\",\"-Command\",\"git status\\nGet-Date\"]}"}}""",
            """{"type":"response_item","payload":{"type":"custom_tool_call","name":"exec","input":"const r = await tools.exec_command({\n  cmd: \"dotnet test -v q\",\n})"}}""",
            """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"测试都过了。"}]}}""");

        var tail = AgentTranscript.FromTail(text, AgentKind.Codex);

        Assert.Equal("测试都过了。", tail.Answer);
        Assert.Equal("dotnet test -v q", tail.Step);
    }

    [Fact]
    public void Claude_what_was_said_counts_only_from_the_latest_prompt()
    {
        var before = string.Join('\n',
            Claude("m1", """{"type":"text","text":"上一轮的回答"}"""),
            """{"type":"user","isSidechain":false,"message":{"role":"user","content":"继续"}}""",
            Claude("m2", """{"type":"tool_use","name":"Read","input":{"file_path":"C:\\a\\TODO.md"}}"""),
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","content":"..."}]}}""");

        var quiet = AgentTranscript.FromTail(before, AgentKind.Claude);
        Assert.Null(quiet.Said);   // a tool result is no prompt, but the answer before "继续" isn't this turn's
        Assert.Equal("上一轮的回答", quiet.Answer);

        var talking = AgentTranscript.FromTail(before + "\n" + Claude("m3", """{"type":"text","text":"找到了对应关系。"}"""), AgentKind.Claude);
        Assert.Equal("找到了对应关系。", talking.Said);
    }

    [Fact]
    public void Nothing_readable_gives_nothing()
    {
        Assert.Equal(new AgentTail(null, null), AgentTranscript.FromTail("not json\n{broken", AgentKind.Claude));
        Assert.Equal(new AgentTail(null, null), AgentTranscript.FromTail(null, AgentKind.Codex));
    }
}
