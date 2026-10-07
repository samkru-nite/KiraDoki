using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>
/// Right-click menu. Uses the standard Windows menu (TrackPopupMenuEx) — it isn't clipped even from a small window
/// attached to the taskbar, and looks like the system menu. Returns the chosen item's id (0 if nothing chosen).
/// </summary>
internal static class ContextMenu
{
    private static bool _themed;

    /// <summary>
    /// Makes the standard menu follow the dark theme (it stayed white on a dark taskbar). Windows exposes this only through
    /// uxtheme ordinals 133/135/136 (AllowDarkModeForWindow / SetPreferredAppMode / FlushMenuThemes) — the same calls
    /// Explorer, Notepad++ and many tray apps use. Missing ordinals (older/newer builds) just leave the menu light.
    /// </summary>
    private static void FollowSystemTheme(IntPtr owner)
    {
        try
        {
            if (!_themed)
            {
                _themed = true;
                _ = SetPreferredAppMode(1);   // AllowDark: dark when the system app theme is dark
            }

            _ = AllowDarkModeForWindow(owner, true);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowDarkModeForWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    private const uint MF_STRING = 0x0000;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_DISABLED = 0x0002;
    private const uint MF_POPUP = 0x0010;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    /// <summary>Shows <paramref name="items"/> at the pointer and returns the chosen id (0 = nothing).</summary>
    public static int Show(IntPtr owner, IReadOnlyList<MenuItem> items)
    {
        FollowSystemTheme(owner);
        var menu = Build(items);
        try
        {
            GetCursorPos(out var point);
            // For clicking outside the menu to close it, the owner window must be in front (a known rule from the Win32 docs)
            SetForegroundWindow(owner);
            return TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_BOTTOMALIGN | TPM_RIGHTBUTTON, point.X, point.Y, owner, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);   // destroys the submenus with it
        }
    }

    private static IntPtr Build(IReadOnlyList<MenuItem> items)
    {
        var menu = CreatePopupMenu();
        foreach (var item in items)
        {
            if (item.Text is null)
            {
                AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            }
            else if (item.Sub is { } sub)
            {
                AppendMenuW(menu, MF_POPUP, (UIntPtr)(ulong)Build(sub), item.Text);
            }
            else
            {
                AppendMenuW(menu, item.Header ? MF_STRING | MF_GRAYED | MF_DISABLED : MF_STRING, (UIntPtr)item.Id, item.Text);
            }
        }

        return menu;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}

/// <summary>
/// One menu line. <see cref="Text"/> null = a separator; <see cref="Header"/> = a grey line that can't be chosen (what the menu is
/// about, e.g. "report.pdf · Downloading"); <see cref="Sub"/> = a submenu.
/// </summary>
internal sealed record MenuItem(int Id, string? Text, bool Header = false, IReadOnlyList<MenuItem>? Sub = null)
{
    public static MenuItem Separator { get; } = new(0, null);
}
