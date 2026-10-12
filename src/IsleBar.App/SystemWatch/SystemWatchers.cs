using IsleBar.Core.Island;
using IsleBar.Core.Localization;
using IsleBar.Core.SystemWatch;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// Bundle of system notice watchers. The island (MainWindow) holds only this,
/// merges notices via <c>items.AddRange(_system.Current)</c>, and reads <see cref="MicInUse"/> etc. for the mic/camera indicator.
/// <para>
/// Threads: every watcher runs on its own background thread (thread pool / dedicated thread) — <b>none needs the UI thread.</b>
/// <see cref="Start"/>/<see cref="Apply"/>/<see cref="Dispose"/> may be called from any thread (guarded against each other by a lock),
/// and <see cref="Current"/> and the privacy properties are safe to read from any thread. No method throws.
/// </para>
/// When settings change, call <see cref="Apply"/> again — it starts only newly enabled ones and stops only disabled ones (if the calendar URL changes, only the calendar restarts).
/// </summary>
internal sealed class SystemWatchers : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<LanguageStrings> _strings;
    private PowerWatcher? _power;
    private BluetoothWatcher? _bluetooth;
    private NetworkWatcher? _network;
    private FocusWatcher? _focus;
    private ClipboardWatcher? _clipboard;
    private LoadWatcher? _load;
    private CalendarWatcher? _calendar;
    private PrivacyWatcher? _privacy;
    private ToastWatcher? _toasts;
    private ChatFlashWatcher? _flashes;   // WeChat's messages (it only flashes its button), with the notifications' switch
    private AgentRecordWatcher? _records;   // always on: a Claude turn interrupted with Esc fires no hook (10-03)
    private CodexWatcher? _codex;   // always on: the ChatGPT app's cards come from its session records (10-11)
    private DriveWatcher? _drives;   // always on for now (TODO ④, 10-10: no setting until it proves itself)
    private TransitWatcher? _transit;   // always on (TODO ④ transit station, 10-10)
    private bool? _hidingBanners;   // null until the first Apply (which also cleans up after a crash with the option off)
    private bool _wantBannersHidden;   // the option "only on the bar", as last applied
    private bool _disposed;

    /// <param name="strings">Function that returns strings for the current language (changing the language in settings applies from the next notice).</param>
    public SystemWatchers(Func<LanguageStrings> strings)
    {
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
    }

    /// <summary>Initial start. Same as <see cref="Apply"/>.</summary>
    public void Start(SystemWatchOptions options) => Apply(options);

    /// <summary>Starts and stops watchers per settings. Safe to call repeatedly.</summary>
    public void Apply(SystemWatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Toggle(ref _power, options.Power, () => new PowerWatcher(_strings));
            Toggle(ref _bluetooth, options.Bluetooth, () => new BluetoothWatcher(_strings));
            Toggle(ref _network, options.Network, () => new NetworkWatcher(_strings));
            Toggle(ref _focus, options.Focus, () => new FocusWatcher(_strings));
            Toggle(ref _clipboard, options.Clipboard, () => new ClipboardWatcher(_strings));
            Toggle(ref _load, options.Load, () => new LoadWatcher(_strings));
            Toggle(ref _privacy, options.Privacy, () => new PrivacyWatcher(_strings));
            Toggle(ref _toasts, options.Toasts, () => new ToastWatcher(_strings) { HealthChanged = OnToastHealth });
            Toggle(ref _flashes, options.WeChat, () => new ChatFlashWatcher(_strings));   // its own switch since 10-12 (was the notifications')
            Toggle(ref _records, options.Claude, () => new AgentRecordWatcher(_strings));
            Toggle(ref _codex, options.ChatGpt, () => new CodexWatcher(_strings));
            Toggle(ref _drives, options.Drives, () => new DriveWatcher(_strings));
            Toggle(ref _transit, options.Transit, () => new TransitWatcher(_strings));
            if (_toasts is not null)
            {
                _toasts.HideBanners = options.HideBanners;
            }

            _wantBannersHidden = options.HideBanners;
            ApplyBanners(BannersHidden(), wait: false);

            // The calendar must refetch when its addresses change, so stop and restart it
            if (_calendar is not null && (!options.Calendar || _calendar.Urls != string.Join('\n', CalendarWatcher.Normalize(options.Calendars))))
            {
                _calendar.Dispose();
                _calendar = null;
            }

            Toggle(ref _calendar, options.Calendar, () => new CalendarWatcher(_strings, options.Calendars));
        }
    }

    /// <summary>The calendar at this address: how its last fetch went (settings page); null while it is still being read.</summary>
    public CalendarStatus? CalendarStatus(string url)
    {
        lock (_gate)
        {
            return _calendar?.StatusOf(url);
        }
    }

    /// <summary>
    /// A card closed by hand that its watcher keeps track of itself (the calendar's one card: closing hides only its look, 10-10).
    /// True when handled — then the island must not hide the card's key.
    /// </summary>
    public bool Close(ActivityState card)
    {
        ArgumentNullException.ThrowIfNull(card);
        CalendarWatcher? calendar;
        TransitWatcher? transit;
        lock (_gate)
        {
            calendar = _calendar;
            transit = _transit;
        }

        if (TransitWatcher.Owns(card) && transit is not null)
        {
            transit.Remove(null);   // closing the transit station = empty it (it only forgets where the files are)
            return true;
        }

        return calendar?.Close(card) == true;
    }

    /// <summary>The copy watcher (the copy card's "加入中转站" reads what was copied). Null when copy cards are off.</summary>
    public ClipboardWatcher? Clipboard
    {
        get
        {
            lock (_gate)
            {
                return _clipboard;
            }
        }
    }

    /// <summary>The transit station (TODO ④, 10-10): files dropped on the pill, dragged out again. Null only after quitting.</summary>
    public TransitWatcher? Transit
    {
        get
        {
            lock (_gate)
            {
                return _transit;
            }
        }
    }

    /// <summary>The lifted USB card's "Eject" (TODO ④, 10-10). Does nothing for another card.</summary>
    public void Eject(ActivityState card)
    {
        ArgumentNullException.ThrowIfNull(card);
        DriveWatcher? drives;
        lock (_gate)
        {
            drives = _drives;
        }

        drives?.Eject(card);
    }

    /// <summary>
    /// Only hide Windows' pop-ups when the bar can actually read the notifications — without the database (code review 10-01), or
    /// while reading it keeps failing (TODO ② 10-10), the person would see no notifications at all. Call under the lock.
    /// </summary>
    private bool BannersHidden()
        => _wantBannersHidden && _toasts is { Healthy: true } && File.Exists(ToastWatcher.DatabasePath);

    /// <summary>The toast watcher can't read the notifications any more, or can again: banners back / hidden again.</summary>
    private void OnToastHealth(bool healthy)
    {
        // off the watcher's thread: Dispose holds the lock while it stops that watcher
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    ApplyBanners(BannersHidden(), wait: false);
                }
            }
        });
    }

    /// <summary>All notices from enabled watchers concatenated. Safe to read from any thread.</summary>
    public IReadOnlyList<ActivityState> Current
    {
        get
        {
            NoticeWatcher?[] watchers;
            lock (_gate)
            {
                watchers = [_power, _bluetooth, _network, _focus, _clipboard, _load, _calendar, _privacy, _toasts, _flashes, _records, _codex, _drives, _transit];
            }

            List<ActivityState>? all = null;
            foreach (var watcher in watchers)
            {
                if (watcher?.Current is { Count: > 0 } items)
                {
                    (all ??= []).AddRange(items);
                }
            }

            return all is null ? [] : all;
        }
    }

    /// <summary>Microphone in use (always false if privacy watching is off).</summary>
    public bool MicInUse => _privacy?.MicInUse ?? false;

    /// <summary>Camera in use (always false if privacy watching is off).</summary>
    public bool CameraInUse => _privacy?.CameraInUse ?? false;

    /// <summary>Name of the app using the mic/camera. Null if none.</summary>
    public string? PrivacyApp => _privacy?.AppName;

    /// <summary>Every app using the camera / microphone now (the lifted card lists them).</summary>
    public IReadOnlyList<string> PrivacyApps => _privacy?.Apps ?? [];

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Toggle(ref _power, false, null!);
            Toggle(ref _bluetooth, false, null!);
            Toggle(ref _network, false, null!);
            Toggle(ref _focus, false, null!);
            Toggle(ref _clipboard, false, null!);
            Toggle(ref _load, false, null!);
            Toggle(ref _calendar, false, null!);
            Toggle(ref _privacy, false, null!);
            Toggle(ref _toasts, false, null!);
            Toggle(ref _flashes, false, null!);
            Toggle(ref _records, false, null!);
            Toggle(ref _codex, false, null!);
            Toggle(ref _drives, false, null!);
            Toggle(ref _transit, false, null!);
            ApplyBanners(false, wait: true);   // quitting the bar gives Windows its pop-ups back (nothing would show the notifications otherwise)
        }
    }

    /// <summary>Windows' own pop-ups off while the bar shows notifications (option "only on the bar"), back on otherwise.</summary>
    /// <param name="wait">Do it now (on quit — a queued job would die with the process) instead of off the calling thread.</param>
    private void ApplyBanners(bool hide, bool wait)
    {
        if (_hidingBanners == hide)
        {
            return;
        }

        _hidingBanners = hide;
        ToastBanners.Want(hide);
        Action work = hide ? ToastBanners.Hide : ToastBanners.Restore;
        if (wait)
        {
            work();
        }
        else
        {
            ThreadPool.QueueUserWorkItem(_ => work());
        }
    }

    private static void Toggle<T>(ref T? watcher, bool on, Func<T> create)
        where T : NoticeWatcher
    {
        if (on && watcher is null)
        {
            watcher = create();
            watcher.Start();
        }
        else if (!on && watcher is not null)
        {
            watcher.Dispose();
            watcher = null;
        }
    }
}
