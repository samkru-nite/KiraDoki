using System.Text.Json;
using System.Text.RegularExpressions;

namespace IsleBar.Core.Island;

/// <summary>
/// Download programs other than browsers (TODO 四, measured 10-11): their temporary files, the names they finish under, and where
/// their settings say they save. Where a download is saved is mostly asked of Everything (by the temporary file's extension, so
/// a folder picked for one download is found too — 10-11: both saved to the desktop, not to their configured folders); the
/// folders read here are for when Everything isn't there.
/// </summary>
public static partial class Downloaders
{
    /// <summary>迅雷: reserves the whole file up front ("X.7z.xltd" at its full size, a ".xltd.cfg" beside it), holds it open while
    /// downloading and closes it when paused (its write time moves only then); done = renamed to "X.7z" (not seen yet: expected).</summary>
    public const string Thunder = ".xltd";

    /// <summary>夸克网盘: "X-c4d5541d929b.zip.qkdownloading" grows as it comes; done = renamed to "X.zip" (seen 10-11).</summary>
    public const string Quark = ".qkdownloading";

    /// <summary>
    /// In a recycle bin: never a download. Deleted, "X.7z.xltd" becomes "$R….xltd" (the file, its old write time) and "$I….xltd"
    /// (where it came from — written that moment, so it read as a download just begun: "下载中 $ILST22V", 10-11). Only the last
    /// extension stays, so 夸克's and the browsers' are hit too; Baidu's ".downloading" happens not to be.
    /// </summary>
    public static bool InRecycleBin(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The name a Quark download finishes under: its extension and the id before the file's own extension go.</summary>
    public static string QuarkFinalName(string partialName)
    {
        ArgumentNullException.ThrowIfNull(partialName);
        var name = partialName.EndsWith(Quark, StringComparison.OrdinalIgnoreCase) ? partialName[..^Quark.Length] : partialName;
        return QuarkId().Replace(name, string.Empty, 1);
    }

    /// <summary>迅雷's Profiles\config.json: the folder new tasks save to ("TaskDefaultSettings" → "DefaultPath").</summary>
    public static string? ThunderFolder(string json)
        => Read(json, root => root.TryGetProperty("TaskDefaultSettings", out var task) ? Text(task, "DefaultPath") : null);

    /// <summary>Quark's preference.json: "downloadPosition" → "where".</summary>
    public static string? QuarkFolder(string json)
        => Read(json, root => root.TryGetProperty("downloadPosition", out var position) ? Text(position, "where") : null);

    /// <summary>
    /// A Chromium browser's profile Preferences: "download" → "default_directory"; null when not set (= the system Downloads folder).
    /// Not "savefile" → "default_directory": that's where "Save as…" last saved (10-11, the user's Edge: the desktop).
    /// </summary>
    public static string? ChromiumFolder(string json)
        => Read(json, root => root.TryGetProperty("download", out var download) ? Text(download, "default_directory") : null);

    private static string? Read(string json, Func<JsonElement, string?> pick)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return doc.RootElement.ValueKind == JsonValueKind.Object && pick(doc.RootElement) is { Length: > 0 } folder ? folder : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>"-c4d5541d929b" just before the file's extension (or at its end).</summary>
    [GeneratedRegex(@"-[0-9a-f]{12}(?=\.[^.]+$|$)", RegexOptions.IgnoreCase)]
    private static partial Regex QuarkId();
}
