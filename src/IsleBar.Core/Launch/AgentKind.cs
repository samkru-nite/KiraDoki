namespace IsleBar.Core.Launch;

/// <summary>Agent name, as hooks and state files write it.</summary>
public static class AgentKind
{
    public const string Claude = "claude";
    public const string Codex = "codex";

    /// <summary>Every known agent (<c>islebar push --agent</c> checks against it).</summary>
    public static readonly IReadOnlyList<string> All = [Claude, Codex];

    public static bool IsKnown(string? value) => value is Claude or Codex;
}
