using IsleBar.Core.Island;
using Xunit;

namespace IsleBar.Core.Tests;

public class ChatRootTests
{
    private const string First = """{"parentUuid":null,"isSidechain":false,"type":"user","message":{"role":"user","content":"hi \"uuid\":\"11111111-1111-1111-1111-111111111111\""},"uuid":"9d50755a-1a8f-49df-a078-e5b91775b62d","sessionId":"ef8a56e3"}""";

    [Fact]
    public void First_message_names_the_chat()
        => Assert.Equal("9d50755a-1a8f-49df-a078-e5b91775b62d", ChatRoot.FromLine(First));

    [Fact]
    public void Other_lines_name_nothing()
    {
        Assert.Null(ChatRoot.FromLine("""{"type":"custom-title","customTitle":"x","sessionId":"ef8a56e3"}"""));
        Assert.Null(ChatRoot.FromLine("""{"parentUuid":"9d50755a-1a8f-49df-a078-e5b91775b62d","type":"assistant","uuid":"aaaaaaaa-1a8f-49df-a078-e5b91775b62d"}"""));
    }

    [Fact]
    public void A_continued_chat_has_the_same_root_in_both_sessions()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var old = Path.Combine(dir.FullName, "9a7d9157.jsonl");
            var next = Path.Combine(dir.FullName, "ef8a56e3.jsonl");
            File.WriteAllLines(old, ["""{"type":"mode"}""", First]);
            File.WriteAllLines(next, ["""{"type":"custom-title","customTitle":"x"}""", """{"type":"file-history-snapshot"}""", First, """{"parentUuid":"9d50755a-1a8f-49df-a078-e5b91775b62d","uuid":"bbbbbbbb-1a8f-49df-a078-e5b91775b62d"}"""]);
            Assert.Equal(ChatRoot.Read(old), ChatRoot.Read(next));
            Assert.NotNull(ChatRoot.Read(old));
            Assert.Null(ChatRoot.Read(Path.Combine(dir.FullName, "missing.jsonl")));
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
