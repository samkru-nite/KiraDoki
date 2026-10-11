using IsleBar.Core.SystemWatch;
using Windows.System.Power;

namespace IsleBar.App.SystemWatch;

/// <summary>
/// Charger plugged/unplugged and low battery (<see cref="PowerManager"/>). Plugging in shows "Charging · 78%" for 2.5 s, unplugging "Battery · 76%" for 2 s,
/// and without a charger "Battery 20% · about 45 min" for 6 s at 20% and again at 10%, and at 5% or less until plugged in (a pinned
/// orange card, user 10-10).
/// On desktops without a battery nothing is shown. Rules are in <see cref="PowerLogic"/>.
/// PowerManager events come from the thread pool — no UI thread needed.
/// Even if events are missed (right after waking from sleep, etc.), re-reads every 30 s to keep the low warning and remaining time right.
/// </summary>
internal sealed class PowerWatcher : NoticeWatcher
{
    private const string PluggedKey = "power";
    private const string LowKey = "power-low";
    private const string SettingsUri = "ms-settings:batterysaver";
    private static readonly TimeSpan PluggedShow = NoticeLengths.Glance;
    private static readonly TimeSpan UnpluggedShow = NoticeLengths.Glance;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LowShow = NoticeLengths.Read;

    private readonly object _gate = new();
    private readonly PowerLogic _logic = new();
    private Timer? _poll;
    private int? _lowLevel;
    private DateTimeOffset _lowSince;
    private bool _started;

    public PowerWatcher(Func<Core.Localization.LanguageStrings> strings)
        : base(strings)
    {
    }

    public override void Start() => Guard(() =>
    {
        if (_started)
        {
            return;
        }

        _started = true;
        PowerManager.BatteryStatusChanged += OnChanged;
        PowerManager.PowerSupplyStatusChanged += OnChanged;
        PowerManager.RemainingChargePercentChanged += OnChanged;
        PowerManager.RemainingDischargeTimeChanged += OnChanged;
        _poll = new Timer(_ => Guard(Refresh), null, TimeSpan.Zero, PollInterval);
    });

    protected override void Stop()
    {
        if (!_started)
        {
            return;
        }

        PowerManager.BatteryStatusChanged -= OnChanged;
        PowerManager.PowerSupplyStatusChanged -= OnChanged;
        PowerManager.RemainingChargePercentChanged -= OnChanged;
        PowerManager.RemainingDischargeTimeChanged -= OnChanged;
        _poll?.Dispose();
    }

    private void OnChanged(object? sender, object e) => Guard(Refresh);

    private void Refresh()
    {
        var reading = Read(out var remaining);
        var now = DateTimeOffset.UtcNow;
        var s = Text;
        lock (_gate)
        {
            switch (_logic.Observe(reading))
            {
                case PowerChange.PluggedIn:
                    Board.Flash(PluggedKey, Lifted(SystemNotice.Make(
                        NoticeText.Charging(s, reading.Percent), NoticeGlyphs.Charging, now,
                        open: SettingsUri), NoticeText.Head(NoticeText.Charging(s, reading.Percent)), reading.Percent, null), PluggedShow, now);   // no gauge — the percent is in the text (user 10-01)
                    break;
                case PowerChange.Unplugged:
                    Board.Flash(PluggedKey, Lifted(SystemNotice.Make(
                        NoticeText.OnBattery(s, reading.Percent), NoticeGlyphs.Battery(reading.Percent), now,
                        open: SettingsUri), NoticeText.Head(NoticeText.OnBattery(s, reading.Percent)), reading.Percent, NoticeText.Remaining(s, remaining)), UnpluggedShow, now);
                    break;
            }

            var level = PowerLogic.LowLevel(reading);
            if (level is null)
            {
                _lowLevel = null;
                Board.Clear(LowKey);
                return;
            }

            var fresh = level != _lowLevel;
            if (fresh)
            {
                _lowLevel = level;
                _lowSince = now;   // 20 → 10 → 5 is a new notice each time (a new timestamp)
            }

            // no gauge — the percent is already in the text (user 10-01)
            var notice = Lifted(SystemNotice.Make(
                NoticeText.BatteryLow(s, reading.Percent, remaining), NoticeGlyphs.Battery(reading.Percent), _lowSince,
                urgent: true, open: SettingsUri), s.CardLowBattery, reading.Percent, NoticeText.Remaining(s, remaining));
            if (level == PowerLogic.Critical)
            {
                // stays, pinned; percent and time left are rewritten each time, the timestamp stays at when it reached 5%
                notice.Stage = SystemNotice.CriticalStage;
                Board.Hold(LowKey, notice);
            }
            else if (fresh)
            {
                Board.Flash(LowKey, notice, LowShow, now);   // once at 20%, once at 10%: not again every 30 s
            }
        }
    }

    /// <summary>
    /// The lifted card's words (TODO ⑤ 掀起卡片适配, user 10-11): "电源", the state, the percent as the big number, time left beside it.
    /// </summary>
    private IsleBar.Core.Island.ActivityState Lifted(IsleBar.Core.Island.ActivityState notice, string words, int percent, string? left)
    {
        notice.AppName = Text.CardPower;
        notice.Words = words;
        notice.Level = percent;
        notice.Note = left;
        return notice;
    }

    private static PowerReading Read(out TimeSpan? remaining)
    {
        remaining = null;
        var hasBattery = PowerManager.BatteryStatus != BatteryStatus.NotPresent;
        var plugged = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
        var percent = PowerManager.RemainingChargePercent;
        if (hasBattery && !plugged)
        {
            remaining = PowerManager.RemainingDischargeTime;   // a huge value if unknown → NoticeText discards it
        }

        return new PowerReading(hasBattery, plugged, percent);
    }
}
