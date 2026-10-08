using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using IsleBar.Core.SystemWatch;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// Microphone/camera in-use indicator. <b>Not a regular notice</b> — exposes <see cref="MicInUse"/>, <see cref="CameraInUse"/>, <see cref="AppName"/>
/// so the island shows a status card while it lasts (ActivityFeed.AddPrivacy); it is noticed by that card's debut on the big pill.
/// <para>
/// Source: <c>HKCU\…\CapabilityAccessManager\ConsentStore\{microphone,webcam}</c> — the same values as the taskbar's microphone icon.
/// Store apps are keys directly below, regular apps are keys under <c>NonPackaged</c>, and in use if <c>LastUsedTimeStop</c> is 0
/// (rules in <see cref="PrivacyRules"/>). Waits for registry change notifications (RegNotifyChangeKeyValue, including subkeys) but
/// also reads directly every 2 s (in case notifications are missed). The island itself (IsleBar) is excluded.
/// </para>
/// Runs on a dedicated thread — no UI thread needed. Properties are safe to read from any thread.
/// </summary>
internal sealed class PrivacyWatcher : NoticeWatcher
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private const string Microphone = "microphone";
    private const string Webcam = "webcam";
    private const string NonPackaged = "NonPackaged";
    private const int PollMs = 2000;

    private const uint RegNotifyChangeName = 0x1;
    private const uint RegNotifyChangeLastSet = 0x4;

    private static readonly ConcurrentDictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly ManualResetEvent _stop = new(false);
    private readonly string? _ownExe = Environment.ProcessPath;
    private Thread? _thread;
    private volatile State _state = new([], []);

    public PrivacyWatcher(Func<Core.Localization.LanguageStrings> strings)
        : base(strings)
    {
    }

    /// <summary>Whether any app is using the microphone now.</summary>
    public bool MicInUse => _state.Mic.Count > 0;

    /// <summary>Whether any app is using the camera now.</summary>
    public bool CameraInUse => _state.Camera.Count > 0;

    /// <summary>Name of the app using it (camera takes priority — in a video call both are the same app). Null if none.</summary>
    public string? AppName => Apps.FirstOrDefault();

    /// <summary>Every app using the camera or the microphone, camera ones first, each newest first (the lifted card lists them, F2b).</summary>
    public IReadOnlyList<string> Apps
    {
        get
        {
            var state = _state;
            return [.. state.Camera.Concat(state.Mic).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    public override void Start() => Guard(() =>
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "IsleBar privacy" };
        _thread.Start();
    });

    protected override void Stop()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(3));
        _stop.Dispose();
    }

    private void Loop()
    {
        using var changed = new AutoResetEvent(false);
        RegistryKey? root = null;
        var armed = false;
        try
        {
            while (!IsDisposed)
            {
                try
                {
                    root ??= Registry.CurrentUser.OpenSubKey(ConsentStore);
                    if (root is not null && !armed)
                    {
                        // The async notification fires only "once", so re-arm it — but only after it fired: arming again on every
                        // 2-second timeout queued another request in the kernel each time (review 10-03). The arming thread must
                        // stay alive, hence a dedicated thread.
                        armed = RegNotifyChangeKeyValue(root.Handle, true, RegNotifyChangeName | RegNotifyChangeLastSet,
                            changed.SafeWaitHandle, true) == 0;
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ObjectDisposedException)
                {
                    root?.Dispose();
                    root = null;
                    armed = false;
                }

                Guard(Scan);

                var hit = WaitHandle.WaitAny([changed, _stop], PollMs);
                if (hit == 1)
                {
                    return;
                }

                if (hit == 0)
                {
                    armed = false;   // it fired — arm again next round
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
        finally
        {
            root?.Dispose();
        }
    }

    private void Scan()
    {
        _state = new State(FindUsers(Microphone), FindUsers(Webcam));
        // no notice of its own when use starts any more: the status card's debut on the big pill shows it (user 10-07 — the
        // extra 2 s notice came first and the card then showed a second time)
    }

    /// <summary>Names of the apps using that device, the one that started last first. Empty if none.</summary>
    private List<string> FindUsers(string capability)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ConsentStore + "\\" + capability);
        if (key is null)
        {
            return [];
        }

        var users = new List<(string Name, long Start)>();

        void Consider(RegistryKey parent, string child, bool packaged)
        {
            using var app = parent.OpenSubKey(child);
            if (app is null)
            {
                return;
            }

            var start = app.GetValue("LastUsedTimeStart") as long?;
            var stop = app.GetValue("LastUsedTimeStop") as long?;
            if (!PrivacyRules.IsInUse(start, stop))
            {
                return;
            }

            if (!packaged && PrivacyRules.IsSelf(child, _ownExe))
            {
                return;
            }

            users.Add((packaged ? PrivacyRules.FriendlyName(child, packaged: true) : NonPackagedName(child), start!.Value));
        }

        foreach (var child in key.GetSubKeyNames())
        {
            if (child.Equals(NonPackaged, StringComparison.OrdinalIgnoreCase))
            {
                using var nonPackaged = key.OpenSubKey(child);
                if (nonPackaged is not null)
                {
                    foreach (var exe in nonPackaged.GetSubKeyNames())
                    {
                        Consider(nonPackaged, exe, packaged: false);
                    }
                }

                continue;
            }

            Consider(key, child, packaged: true);
        }

        return [.. users.OrderByDescending(u => u.Start).Select(u => u.Name).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Regular app name: the exe's "File description" (e.g. "Google Chrome") if present, else the exe name. Cached once resolved.</summary>
    private static string NonPackagedName(string keyName)
        => NameCache.GetOrAdd(keyName, static k =>
        {
            var fallback = PrivacyRules.FriendlyName(k, packaged: false);
            try
            {
                var path = PrivacyRules.PathFromNonPackagedKey(k);
                if (File.Exists(path)
                    && FileVersionInfo.GetVersionInfo(path).FileDescription is { } description
                    && !string.IsNullOrWhiteSpace(description)
                    && description.Trim().Length <= 32)
                {
                    return description.Trim();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
            }

            return fallback;
        });

    private sealed record State(IReadOnlyList<string> Mic, IReadOnlyList<string> Camera);

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle key, bool watchSubtree, uint notifyFilter, SafeWaitHandle eventHandle, bool asynchronous);
}
