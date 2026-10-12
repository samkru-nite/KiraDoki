using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using IsleBar.App.Interop;
using IsleBar.Core.Ui;
using static IsleBar.App.Interop.NativeMethods;

namespace IsleBar.App.Tracking;

/// <summary>Where the bar goes on the taskbar (physical screen pixels). <paramref name="Visible"/> false = no room there, hidden.</summary>
internal sealed record BarPlacement(bool Visible, int X, int Y)
{
    public static readonly BarPlacement Hidden = new(false, 0, 0);
}

/// <summary>
/// Reads the taskbar's layout — its edges, the icon group (Start, search, apps), the Widgets button and the tray — and works out
/// the bar's spot with <see cref="BarSlot"/> for the position chosen in settings (TODO 7.1, 10-05). Replaces following the real
/// search box: the bar no longer sits on it (the user keeps it off), so nothing here touches the search box.
/// <para>
/// <b>Always runs on a background MTA thread.</b> Querying Explorer via UIA from the UI thread makes both wait on each other and
/// Explorer hangs (AppHangXProcB1, 09-29). The UI thread only reads <see cref="Current"/>.
/// UI Automation ids measured 10-05 on Windows 11: StartButton, SearchButton, WidgetsButton, app buttons with the class
/// <c>Taskbar.TaskListButtonAutomationPeer</c>; the tray is the Win32 child window TrayNotifyWnd.
/// </para>
/// </summary>
internal sealed class TaskbarTracker : IDisposable
{
    private const string StartButtonId = "StartButton";
    private const string SearchButtonId = "SearchButton";
    private const string WidgetsButtonId = "WidgetsButton";
    private const string AppButtonClass = "Taskbar.TaskListButtonAutomationPeer";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(30);      // while the taskbar is reflowing, track closely
    private static readonly TimeSpan FastWindow = TimeSpan.FromMilliseconds(650);   // ...for this long after the last change

    private readonly Func<int> _barWidth;
    private readonly int _barHeight;
    private readonly Func<(BarPosition Position, int ManualOffsetPx)> _settings;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;
    private DateTime _lastChangeUtc = DateTime.MinValue;
    private bool? _loggedVisible;
    private BarPosition? _loggedPosition;

    public TaskbarTracker(Func<int> barWidth, int barHeight, Func<(BarPosition Position, int ManualOffsetPx)> settings)
    {
        _barWidth = barWidth;
        _barHeight = barHeight;
        _settings = settings;
    }

    /// <summary>Latest spot found by the background thread. The UI thread reads only this (reference assignment is atomic).</summary>
    public BarPlacement Current { get; private set; } = BarPlacement.Hidden;

    /// <summary>The taskbar as last read (for the diagnostics page). Null until read once.</summary>
    public TaskbarLayout? Layout { get; private set; }

    /// <summary>
    /// The spot for a bar <paramref name="width"/> wide on the taskbar as last read. The UI thread calls this the moment the pills
    /// change width (a card came or went), instead of waiting up to half a second for the next pass. Pure arithmetic, no UIA.
    /// </summary>
    public BarPlacement PlaceNow(int width)
    {
        if (Layout is not { } layout)
        {
            return Current;
        }

        var (position, manualOffset) = _settings();
        var spot = BarSlot.Place(position, layout, width, _barHeight, DpiSetup.Px(12), manualOffset, topEdge: DpiSetup.Px(1), iconGap: DpiSetup.Px(8));
        return spot.Fits ? new BarPlacement(true, spot.X, spot.Y) : BarPlacement.Hidden;
    }

    /// <summary>Fired (on the tracker thread) the moment the spot changes, so the UI moves the bar without waiting for its next tick.</summary>
    public Action? OnChanged { get; set; }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "IsleBar taskbar tracker" };
        _thread.SetApartmentState(ApartmentState.MTA);   // not the UI thread's STA — they would wait on each other
        _thread.Start();
    }

    private void Loop()
    {
        // an unexpected error used to end tracking for good (review 10-03) — start over instead
        while (true)
        {
            try
            {
                LoopCore();
                return;   // stopped
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                TaskbarHost.Log($"tracker error, starting over: {ex.GetType().Name} {ex.Message}");
                try
                {
                    if (_stop.IsCancellationRequested || _stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                    {
                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }
    }

    private void LoopCore()
    {
        var automation = (IUIAutomation)new CUIAutomation();
        var elements = new Elements();
        var lookedUp = DateTime.MinValue;
        while (!_stop.IsCancellationRequested)
        {
            var delay = DateTime.UtcNow - _lastChangeUtc < FastWindow ? FastPoll : PollInterval;
            var started = Perf.Now;   // DIAGNOSTIC (performance, 10-12): each pass's time in the perf summary
            try
            {
                var tray = TaskbarHost.FindTaskbar();
                if (tray == IntPtr.Zero || !GetWindowRect(tray, out var bar))
                {
                    elements = new Elements();   // Explorer is restarting
                }
                else
                {
                    var (position, manualOffset) = _settings();

                    // finding elements walks the taskbar's tree (keeps Explorer busy) — only now and then; reading a found
                    // element's rectangle is cheap and done every pass. Apps come and go, so in the modes that sit beside them
                    // the app buttons are looked up more often.
                    var lookupEvery = position is BarPosition.Center ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(3);
                    if (DateTime.UtcNow - lookedUp > lookupEvery)
                    {
                        elements = Elements.Find(automation, tray);
                        lookedUp = DateTime.UtcNow;
                    }

                    var layout = Read(elements, tray, bar);
                    Layout = layout;
                    var spot = BarSlot.Place(position, layout, _barWidth(), _barHeight, DpiSetup.Px(12), manualOffset, topEdge: DpiSetup.Px(1), iconGap: DpiSetup.Px(8));
                    SetCurrent(spot.Fits ? new BarPlacement(true, spot.X, spot.Y) : BarPlacement.Hidden);
                    if (spot.Fits != _loggedVisible || position != _loggedPosition)
                    {
                        (_loggedVisible, _loggedPosition) = (spot.Fits, position);
                        TaskbarHost.Log($"placement: {position} fits={spot.Fits} x={spot.X} y={spot.Y} · taskbar {layout.Left}..{layout.Right} "
                            + $"icons {layout.IconsLeft}..{layout.IconsRight} tray {layout.TrayLeft} widgets {layout.WidgetsRight}");
                    }
                }
            }
            catch (COMException ex)
            {
                TaskbarHost.Log($"UIA error: {ex.Message}");
                elements = new Elements();     // Explorer restarted → find them again on the next pass
                lookedUp = DateTime.MinValue;
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                Perf.Failed("taskbar tracker", ex);
                elements = new Elements();
                lookedUp = DateTime.MinValue;
            }

            Perf.Work("taskbar tracker", started);
            if (_stop.Token.WaitHandle.WaitOne(delay))
            {
                return;
            }
        }
    }

    private void SetCurrent(BarPlacement placement)
    {
        if (placement != Current)
        {
            Current = placement;
            _lastChangeUtc = DateTime.UtcNow;   // a short fast-poll burst so a taskbar reflow is tracked smoothly
            OnChanged?.Invoke();
        }
    }

    private static TaskbarLayout Read(Elements found, IntPtr tray, RECT bar)
    {
        int? iconsLeft = null, iconsRight = null;
        foreach (var element in found.Icons)
        {
            var r = element.CurrentBoundingRectangle;
            if (r.right > r.left)
            {
                iconsLeft = Math.Min(iconsLeft ?? r.left, r.left);
                iconsRight = Math.Max(iconsRight ?? r.right, r.right);
            }
        }

        int? widgetsRight = null;
        if (found.Widgets?.CurrentBoundingRectangle is { } w && w.right > w.left && (iconsLeft is null || w.right <= iconsLeft))
        {
            widgetsRight = w.right;   // only when it sits left of the icons (its default place with centred icons)
        }

        int? trayLeft = null;
        var notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && GetWindowRect(notify, out var t) && t.Right > t.Left)
        {
            trayLeft = t.Left;
        }

        return new TaskbarLayout(bar.Left, bar.Top, bar.Right, bar.Bottom, iconsLeft, iconsRight, trayLeft, widgetsRight);
    }

    /// <summary>The taskbar elements found by the last lookup.</summary>
    private sealed class Elements
    {
        public List<IUIAutomationElement> Icons { get; } = [];

        public IUIAutomationElement? Widgets { get; private set; }

        public static Elements Find(IUIAutomation automation, IntPtr trayWindow)
        {
            var found = new Elements();
            var root = automation.ElementFromHandle(trayWindow);
            if (root is null)
            {
                return found;
            }

            foreach (var id in new[] { StartButtonId, SearchButtonId })
            {
                if (FindFirst(automation, root, UIA_PropertyIds.UIA_AutomationIdPropertyId, id) is { } element)
                {
                    found.Icons.Add(element);
                }
            }

            found.Widgets = FindFirst(automation, root, UIA_PropertyIds.UIA_AutomationIdPropertyId, WidgetsButtonId);
            var condition = automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ClassNamePropertyId, AppButtonClass);
            try
            {
                var apps = root.FindAll(TreeScope.TreeScope_Descendants, condition);
                for (var i = 0; apps is not null && i < apps.Length; i++)
                {
                    found.Icons.Add(apps.GetElement(i));
                }
            }
            finally
            {
                Release(condition);   // piled-up COM references make Explorer sluggish
            }

            return found;
        }

        private static IUIAutomationElement? FindFirst(IUIAutomation automation, IUIAutomationElement root, int property, string value)
        {
            var condition = automation.CreatePropertyCondition(property, value);
            try
            {
                return root.FindFirst(TreeScope.TreeScope_Descendants, condition);
            }
            finally
            {
                Release(condition);
            }
        }

        private static void Release(object? com)
        {
            if (com is not null && Marshal.IsComObject(com))
            {
                Marshal.ReleaseComObject(com);
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }
}
