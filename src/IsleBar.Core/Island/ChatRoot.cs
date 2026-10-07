using System.Text.RegularExpressions;

namespace IsleBar.Core.Island;

/// <summary>
/// Which chat a Claude Code session belongs to. A chat can go on in a new session (the desktop app does this — seen 10-07: chat
/// "CLAUDE.md 动画和图标问题" went from session 9a7d9157 to ef8a56e3); the new transcript copies the history, and its first
/// message keeps the same uuid. So the first message's uuid names the chat, and a new turn in it can clear the old session's
/// "done" / "interrupted" card (user 10-07).
/// </summary>
public static partial class ChatRoot
{
    /// <summary>The chat's id from one transcript line: the uuid of the first message (the one with no parent); null for other lines.</summary>
    public static string? FromLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!line.Contains("\"parentUuid\":null", StringComparison.Ordinal))
        {
            return null;
        }

        var match = Uuid().Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The chat's id from a transcript file (read up to its first message); null when there is none yet or it can't be read.</summary>
    public static string? Read(string transcriptPath)
    {
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            for (var i = 0; i < 200 && reader.ReadLine() is { } line; i++)
            {
                if (FromLine(line) is { } root)
                {
                    return root;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // gone or locked: not known
        }

        return null;
    }

    /// <summary>The top-level "uuid" (not "parentUuid", not one inside the message).</summary>
    [GeneratedRegex("(?<![A-Za-z])\"uuid\":\"([0-9a-fA-F-]{36})\"")]
    private static partial Regex Uuid();
}
