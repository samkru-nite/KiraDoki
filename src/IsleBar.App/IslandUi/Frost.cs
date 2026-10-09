using System.Runtime.InteropServices;

namespace IsleBar.App.IslandUi;

/// <summary>The lifted card's glass (TODO 7.9 丙④): whether to show it.</summary>
internal static class Frost
{
    /// <summary>
    /// Windows' effective power mode, kept up to date by Windows itself (it calls back once with the current mode, then on every
    /// change). Windows 11's energy saver shows up only here: plugged in it reads "better battery" (1), on battery presumably
    /// "battery saver" (0); the older energy-saver status and the power status's saver flag both stayed off with it on (user
    /// 10-10, measured). "Best power efficiency" in the power mode setting also reads 1 — saving power too, so solid as well.
    /// </summary>
    private static volatile int s_mode = -1;
    private static IntPtr s_watch;
    private static readonly PowerModeChanged s_changed = (mode, _) => s_mode = mode;   // held here so it isn't collected

    private const int BatterySaver = 0, BetterBattery = 1;

    /// <summary>
    /// Glass only with Windows' transparency effects on and energy saver off (it's off there in Windows' own menus too; user 10-10:
    /// energy saver → solid). Read at each lift, so it follows the switch without a restart.
    /// </summary>
    public static bool Allowed()
    {
        try
        {
            Watch();
            return new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled
                && s_mode is not (BatterySaver or BetterBattery)
                && !SaverFlag()
                && Windows.System.Power.PowerManager.EnergySaverStatus != Windows.System.Power.EnergySaverStatus.On;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>For the log: what Windows says about transparency and energy saver.</summary>
    public static string Why()
    {
        try
        {
            Watch();
            return $"transparency effects {new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled}, power mode {s_mode} (0 = saver, 1 = better battery), saver flag {SaverFlag()}, energy saver {Windows.System.Power.PowerManager.EnergySaverStatus}";
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name} {ex.Message}";
        }
    }

    private static void Watch()
    {
        if (s_watch != IntPtr.Zero)
        {
            return;
        }

        var hr = PowerRegisterForEffectivePowerModeNotifications(EffectivePowerModeV2, s_changed, IntPtr.Zero, out s_watch);
        if (hr != 0)
        {
            Interop.TaskbarHost.Log($"lift: power mode watch failed hr={hr:X}");
            s_watch = new IntPtr(-1);   // don't try again every lift
        }
    }

    /// <summary>The power status's "battery saver on" flag (SYSTEM_POWER_STATUS.SystemStatusFlag).</summary>
    private static bool SaverFlag() => GetSystemPowerStatus(out var status) && status.SystemStatusFlag == 1;

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

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);

    private const uint EffectivePowerModeV2 = 2;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PowerModeChanged(int mode, IntPtr context);

    [DllImport("powrprof.dll")]
    private static extern int PowerRegisterForEffectivePowerModeNotifications(uint version, PowerModeChanged callback, IntPtr context, out IntPtr handle);
}
