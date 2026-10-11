using System.Runtime.InteropServices;
using System.Text;
using Interop.UIAutomationClient;

namespace IsleBar.App.Interop;

/// <summary>
/// Where something dragged out of the transit station was let go (TODO ④, user 10-10: say whether it was copied or moved): the window
/// under the mouse, and when it is File Explorer or the desktop, the folder it shows (Windows' own list of shell windows — the
/// folder's real path, not words meant for people) and the item right under the mouse (a subfolder's icon takes the drop itself).
/// Step 1: only logged ("dropspot: …"), to see what can be read in each case.
/// </summary>
internal static class DropSpot
{
    public sealed record Spot(string WindowClass, string? Folder, string? Item, string? ItemType);

    /// <summary>Looks at the screen point on its own STA thread (the shell's windows are COM); null when nothing could be read.</summary>
    public static Task<Spot?> FindAsync(int x, int y)
    {
        var done = new TaskCompletionSource<Spot?>();
        var thread = new Thread(() =>
        {
            try
            {
                done.TrySetResult(Find(x, y));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException
                                           or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                TaskbarHost.Log("dropspot: failed — " + ex.Message);
                done.TrySetResult(null);
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static Spot Find(int x, int y)
    {
        var point = new NativeMethods.POINT { X = x, Y = y };
        var hwnd = WindowFromPoint(point);
        var root = hwnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var cls = ClassOf(root);
        string? folder = cls switch
        {
            "CabinetWClass" or "ExploreWClass" => FolderOf(root),
            "Progman" or "WorkerW" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            _ => null,
        };
        var (item, type) = ItemAt(x, y);
        return new Spot(cls, folder, item, type);
    }

    /// <summary>The folders open in Explorer (every window and tab) and the desktop, read on an STA thread; empty when unreadable.</summary>
    public static Task<IReadOnlyList<string>> OpenFoldersAsync()
    {
        var done = new TaskCompletionSource<IReadOnlyList<string>>();
        var thread = new Thread(() =>
        {
            var folders = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType is not null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    try
                    {
                        dynamic windows = shell.Windows();
                        var count = (int)windows.Count;
                        for (var i = 0; i < count; i++)
                        {
                            dynamic? w = windows.Item(i);
                            if (w is null)
                            {
                                continue;
                            }

                            try
                            {
                                if ((string?)w.Document?.Folder?.Self?.Path is { Length: > 0 } path && Directory.Exists(path))
                                {
                                    folders.Add(path);
                                }
                            }
                            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                            {
                                // a window that isn't a folder (a search, Control Panel)
                            }
                            finally
                            {
                                Marshal.FinalReleaseComObject(w);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(shell);
                    }
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                TaskbarHost.Log("dropspot: open folders unreadable — " + ex.Message);
            }

            done.TrySetResult([.. folders.Distinct(StringComparer.OrdinalIgnoreCase)]);
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    /// <summary>The folder an Explorer window shows, from the shell's window list (matched by its handle).</summary>
    private static string? FolderOf(IntPtr window)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return null;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic windows = shell.Windows();
            var count = (int)windows.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic? w = windows.Item(i);
                if (w is null)
                {
                    continue;
                }

                try
                {
                    if (new IntPtr((long)w.HWND) == window)
                    {
                        return (string)w.Document.Folder.Self.Path;
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(w);
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return null;
    }

    /// <summary>The item right under the mouse (its name and kind: a list item = a file or a subfolder's icon).</summary>
    private static (string? Name, string? Type) ItemAt(int x, int y)
    {
        var automation = (IUIAutomation)new CUIAutomation();
        IUIAutomationElement? element = null;
        try
        {
            element = automation.ElementFromPoint(new tagPOINT { x = x, y = y });
            return element is null ? (null, null) : (element.CurrentName, element.CurrentLocalizedControlType);
        }
        catch (COMException)
        {
            return (null, null);
        }
        finally
        {
            if (element is not null)
            {
                Marshal.ReleaseComObject(element);
            }

            Marshal.ReleaseComObject(automation);
        }
    }

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(256);
        return window != IntPtr.Zero && GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "-";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativeMethods.POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
}
