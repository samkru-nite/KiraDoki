using IsleBar.Core.Configuration;
using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

/// <summary>Keep the AI working with the lid closed (user 10-12).</summary>
public sealed class LidRulesTests
{
    private static ActivityState Card(ActivityKind kind) => new() { Agent = "claude", RawKind = ActivityState.KindToString(kind) };

    [Theory]
    [InlineData(ActivityKind.AgentWorking, true)]
    [InlineData(ActivityKind.AgentPermission, true)]   // waiting for the user counts too (user 10-12)
    [InlineData(ActivityKind.AgentDone, false)]
    [InlineData(ActivityKind.Music, false)]
    public void Working_or_waiting_is_a_task_under_way(ActivityKind kind, bool busy)
        => Assert.Equal(busy, LidRules.AgentBusy([Card(kind)]));

    [Fact]
    public void Nothing_shown_is_nothing_under_way() => Assert.False(LidRules.AgentBusy([]));

    [Theory]
    [InlineData(true, true, 50, false, true)]
    [InlineData(false, true, 50, false, false)]   // the setting off
    [InlineData(true, false, 50, false, false)]   // nothing under way
    [InlineData(true, true, 4, false, false)]     // under 5 % on battery: back at once
    [InlineData(true, true, 4, true, true)]       // under 5 % but plugged in
    [InlineData(true, true, 5, false, true)]
    public void Kept_running_only_with_the_setting_a_task_and_battery_to_spare(bool on, bool busy, int battery, bool charging, bool kept)
        => Assert.Equal(kept, LidRules.KeepAwake(on, busy, battery, charging));

    [Fact]
    public void An_unknown_battery_never_stops_it() => Assert.True(LidRules.KeepAwake(true, true, null, false));

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]   // lid open
    [InlineData(true, false, true, false)]   // sub-switch off
    [InlineData(false, true, true, false)]   // not held
    public void Best_efficiency_only_with_the_lid_closed(bool held, bool on, bool closed, bool efficient)
        => Assert.Equal(efficient, LidRules.Efficient(held, on, closed));

    [Fact]
    public void A_saved_record_reads_back()
    {
        var saved = new LidSaved(Guid.NewGuid(), 1, 2, LidRules.BestEfficiency);
        Assert.Equal(saved, LidSaved.Parse(saved.Format()));
        var plain = saved with { Overlay = null };
        Assert.Equal(plain, LidSaved.Parse(plain.Format()));
        Assert.Equal(saved with { Overlay = Guid.Empty }, LidSaved.Parse((saved with { Overlay = Guid.Empty }).Format()));   // "balanced"
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a guid\n1\n1")]
    [InlineData("381b4222-f694-41f0-9685-ff5bb260df2e\n1")]      // cut off
    [InlineData("381b4222-f694-41f0-9685-ff5bb260df2e\n9\n1")]   // not a lid action
    public void A_broken_record_is_nothing(string? text) => Assert.Null(LidSaved.Parse(text));

    [Fact]
    public void The_switches_keep_their_own_defaults_and_save()
    {
        Assert.False(new IsleBarSettings().LidAwake);   // it changes a Windows setting: the user turns it on
        Assert.True(new IsleBarSettings().LidEfficiency);
        Assert.False(SettingsCodec.FromJson(new System.Text.Json.Nodes.JsonObject()).LidAwake);

        var raw = new System.Text.Json.Nodes.JsonObject();
        SettingsCodec.Apply(new IsleBarSettings { LidAwake = true, LidEfficiency = false }, raw);
        var back = SettingsCodec.FromJson(raw);
        Assert.True(back.LidAwake);
        Assert.False(back.LidEfficiency);
        Assert.True(back.Clone().LidAwake);
    }
}
