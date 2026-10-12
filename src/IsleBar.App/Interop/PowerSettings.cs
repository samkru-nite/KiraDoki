using System.Runtime.InteropServices;

namespace IsleBar.App.Interop;

/// <summary>
/// Windows' power settings for "keep the AI working with the lid closed" (user 10-12, <see cref="SystemWatch.LidGuard"/>): the lid's
/// action in a power plan, the power mode (overlay), keeping the system running, the lid's open / closed state. All read and written
/// through powrprof — it sees settings a maker has hidden (HONOR hides the lid action; <c>powercfg /q</c> doesn't show it, 10-12).
/// </summary>
internal static class PowerSettings
{
    private static Guid s_buttons = new("4f971e89-eebd-4455-a8de-9e59040e7347");   // SUB_BUTTONS
    private static Guid s_lidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");   // LIDACTION
    private static Guid s_lidSwitch = new("ba3e0f4d-b817-4094-a2d1-d56379e6a0f3");   // GUID_LIDSWITCH_STATE_CHANGE

    /// <summary>"Balanced": the only plan Windows offers the power mode slider on.</summary>
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    private static bool? s_hasLid;

    /// <summary>The PC has a lid (a laptop); a desktop doesn't get the setting at all.</summary>
    public static bool HasLid
    {
        get
        {
            if (s_hasLid is { } known)
            {
                return known;
            }

            var caps = new byte[256];   // SYSTEM_POWER_CAPABILITIES: PowerButtonPresent, SleepButtonPresent, LidPresent, …
            s_hasLid = GetPwrCapabilities(caps) && caps[2] != 0;
            return s_hasLid.Value;
        }
    }

    /// <summary>The power plan in use; null if it can't be read.</summary>
    public static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    /// <summary>A plan's lid action on mains power and on battery; null if it can't be read.</summary>
    public static (uint Ac, uint Dc)? ReadLid(Guid scheme)
        => PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref s_buttons, ref s_lidAction, out var ac) == 0
           && PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref s_buttons, ref s_lidAction, out var dc) == 0
            ? (ac, dc) : null;

    /// <summary>Writes a plan's lid actions and makes the plan take them (re-activates it). True when both were written.</summary>
    public static bool WriteLid(Guid scheme, uint ac, uint dc)
    {
        var ok = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref s_buttons, ref s_lidAction, ac) == 0
                 & PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref s_buttons, ref s_lidAction, dc) == 0;
        if (ActiveScheme() == scheme)
        {
            PowerSetActiveScheme(IntPtr.Zero, ref scheme);   // a written value only takes effect once the plan is set again
        }

        return ok;
    }

    /// <summary>
    /// The power mode the user chose (Guid.Empty = balanced). Not the one in effect: Energy Saver forces best efficiency under 20 %
    /// (10-12: the effective one read best efficiency at 18 % with "balanced" chosen) — saving that would never give "balanced" back.
    /// </summary>
    public static Guid? ChosenMode() => PowerGetActualOverlayScheme(out var mode) == 0 ? mode : null;

    /// <summary>Sets the power mode; true when Windows took it.</summary>
    public static bool SetMode(Guid mode) => PowerSetActiveOverlayScheme(mode) == 0 && ChosenMode() == mode;

    /// <summary>
    /// Keeps the system from sleeping for being idle (on battery it sleeps after its idle time even with the lid set to do nothing —
    /// an agent working isn't the user's input). Per thread, and dropped by Windows when the process ends: nothing to undo after a crash.
    /// Call on a thread that lives as long as the app (the UI thread).
    /// </summary>
    public static void KeepSystemRunning(bool on) => SetThreadExecutionState(on ? EsContinuous | EsSystemRequired : EsContinuous);

    /// <summary>Battery percent (null: no battery / unknown) and whether on mains power.</summary>
    public static (int? Battery, bool Charging) Battery()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return (null, true);
        }

        var percent = status.BatteryFlag == 128 || status.BatteryLifePercent > 100 ? (int?)null : status.BatteryLifePercent;
        return (percent, status.ACLineStatus != 0);   // 1 on mains, 255 unknown (a desktop): neither counts as running down
    }

    private static DeviceNotifyCallback? s_lidCallback;   // held so it isn't collected
    private static IntPtr s_lidRegistration;

    private static volatile bool s_lidClosed;

    /// <summary>The lid is closed now (as last told by Windows; false until told).</summary>
    public static bool LidClosed => s_lidClosed;

    /// <summary>Starts listening for the lid (once; Windows tells the current state right away).</summary>
    public static void WatchLid()
    {
        if (s_lidRegistration != IntPtr.Zero || !HasLid)
        {
            return;
        }

        s_lidCallback = OnPowerSetting;
        var recipient = new DeviceNotifySubscribeParameters { Callback = Marshal.GetFunctionPointerForDelegate(s_lidCallback) };
        var result = PowerSettingRegisterNotification(ref s_lidSwitch, DeviceNotifyCallbackFlag, ref recipient, out s_lidRegistration);
        if (result != 0)
        {
            AppLog.Write($"lid: can't listen for the lid ({result})");
            s_lidRegistration = new IntPtr(-1);   // don't try again
        }
    }

    private static uint OnPowerSetting(IntPtr context, uint type, IntPtr setting)
    {
        if (type == PbtPowerSettingChange && setting != IntPtr.Zero && Marshal.PtrToStructure<Guid>(setting) == s_lidSwitch)
        {
            var closed = Marshal.ReadInt32(setting, 20) == 0;   // POWERBROADCAST_SETTING: Guid, DataLength, Data (0 closed, 1 open)
            if (closed != s_lidClosed)
            {
                s_lidClosed = closed;
                AppLog.Write($"lid: {(closed ? "closed" : "opened")}");
            }
        }

        return 0;
    }

    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint DeviceNotifyCallbackFlag = 2;
    private const uint PbtPowerSettingChange = 0x8013;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint DeviceNotifyCallback(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public IntPtr Callback;
        public IntPtr Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetPwrCapabilities([Out] byte[] capabilities);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActualOverlayScheme(out Guid overlay);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlay);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingRegisterNotification(ref Guid setting, uint flags, ref DeviceNotifySubscribeParameters recipient, out IntPtr registration);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
