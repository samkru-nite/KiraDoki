using IsleBar.App.Interop;

namespace IsleBar.App.Engine;

/// <summary>Opening a found file, and starting / finding Everything. No UI — each returns whether it worked (the failure is logged).</summary>
internal static class FileActions
{
    /// <summary>Opens the file with its default app, or (<paramref name="revealInFolder"/>) selects it in Explorer.</summary>
    public static bool Open(string path, bool revealInFolder)
    {
        TaskbarHost.Log($"file search: open {(revealInFolder ? "(reveal) " : string.Empty)}{Path.GetExtension(path)}");
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
            TaskbarHost.Log("file search: open failed — " + ex.Message);
            return false;
        }
    }

    /// <summary>Everything's own window with the same words (all results, every column).</summary>
    public static bool SearchInEverything(string exe, string query)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add("-search");
            start.ArgumentList.Add(query);
            System.Diagnostics.Process.Start(start);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            TaskbarHost.Log("file search: opening Everything failed — " + ex.Message);
            return false;
        }
    }

    /// <summary>Starts Everything in the background (no window).</summary>
    public static bool StartEverything(string exe)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-startup") { UseShellExecute = false });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            TaskbarHost.Log("file search: starting Everything failed — " + ex.Message);
            return false;
        }
    }

    /// <summary>Everything isn't installed: its official download page in the browser.</summary>
    public static bool OpenEverythingDownload()
    {
        TaskbarHost.Log("file search: Everything.exe not found — opening the download page");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.voidtools.com/downloads/") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            TaskbarHost.Log("file search: opening the download page failed — " + ex.Message);
            return false;
        }
    }

    /// <summary>The running Everything's program file, else where its installer puts it (64- or 32-bit).</summary>
    public static string? EverythingExe()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("Everything"))
        {
            try
            {
                if (process.MainModule?.FileName is { } running && File.Exists(running))
                {
                    return running;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // running elevated: its path can't be read — fall back to the install folders
            }
            finally
            {
                process.Dispose();
            }
        }

        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Everything", "Everything.exe"))
            .FirstOrDefault(File.Exists);
    }
}
