using IsleBar.App.Interop;

namespace IsleBar.App.Engine;

/// <summary>Opening a file, a folder or a settings page from a card's button. No UI — returns whether it worked (the failure is logged).</summary>
internal static class FileActions
{
    /// <summary>Opens the file with its default app, or (<paramref name="revealInFolder"/>) selects it in Explorer.</summary>
    public static bool Open(string path, bool revealInFolder)
    {
        TaskbarHost.Log($"open: {(revealInFolder ? "(reveal) " : string.Empty)}{Path.GetExtension(path)}");
        try
        {
            if (revealInFolder)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            TaskbarHost.Log("open failed — " + ex.Message);
            return false;
        }
    }
}
