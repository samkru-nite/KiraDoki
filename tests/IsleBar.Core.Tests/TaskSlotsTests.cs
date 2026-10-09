using IsleBar.Core.Island;
using IsleBar.Core.Ui;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class TaskSlotsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static Card C(string key, CardType type = CardType.Status, ActivityState? state = null)
        => new(key, type, type != CardType.Status, state ?? new ActivityState { RawKind = ActivityState.KindTransfer, SourcePath = key });

    private static string[] Keys(TaskSlots slots) => slots.Cells.Select(c => c.Key).ToArray();

    [Fact]
    public void Every_card_but_the_shown_one_and_temporary_ones_gets_a_cell_in_stack_order()
    {
        var slots = new TaskSlots();
        slots.Update([C("copied", CardType.Temporary), C("done", CardType.Inform), C("a"), C("b")], "copied", hovering: false);
        Assert.Equal(["done", "a", "b"], Keys(slots));
    }

    [Fact]
    public void A_new_card_takes_its_stack_place_but_goes_last_while_hovering()
    {
        var slots = new TaskSlots();
        slots.Update([C("top"), C("a"), C("c")], "top", hovering: false);
        slots.Update([C("top"), C("a"), C("b"), C("c")], "top", hovering: false);
        Assert.Equal(["a", "b", "c"], Keys(slots));

        var held = new TaskSlots();
        held.Update([C("top"), C("a"), C("c")], "top", hovering: true);
        held.Update([C("top"), C("a"), C("b"), C("c")], "top", hovering: true);
        Assert.Equal(["a", "c", "b"], Keys(held));
    }

    [Fact]
    public void A_swap_puts_the_shown_card_in_the_clicked_cell_and_leaves_the_others()
    {
        var slots = new TaskSlots();
        var cards = new[] { C("top"), C("a"), C("b"), C("c") };
        slots.Update(cards, "top", hovering: false);
        slots.Swap("b", "top", shownIsTemporary: false);
        slots.Update(cards, "b", hovering: true);
        Assert.Equal(["a", "top", "c"], Keys(slots));
    }

    [Fact]
    public void A_temporary_card_swapped_away_leaves_no_cell()
    {
        var slots = new TaskSlots();
        var cards = new[] { C("copied", CardType.Temporary), C("a"), C("b") };
        slots.Update(cards, "copied", hovering: false);
        slots.Swap("a", "copied", shownIsTemporary: true);
        slots.Update(cards, "a", hovering: true);
        Assert.Equal(["b"], Keys(slots));
    }

    [Fact]
    public void Icons_colour_only_what_needs_you()
    {
        static TaskIcon For(ActivityKind kind, string state, CardType type)
            => TaskIcons.For(new Card("k", type, true, new ActivityState { RawKind = ActivityState.KindToString(kind), State = state }), false, T0);

        Assert.Equal(IconTone.Plain, For(ActivityKind.AgentWorking, "run", CardType.Status).Tone);
        Assert.Equal(IconTone.Waiting, For(ActivityKind.AgentPermission, "run", CardType.Pending).Tone);
        Assert.Equal(IconTone.Done, For(ActivityKind.AgentDone, "done", CardType.Inform).Tone);
        Assert.Equal(IconTone.Error, For(ActivityKind.Error, "error", CardType.Inform).Tone);
        Assert.Equal(IconTone.Error, For(ActivityKind.Transfer, "error", CardType.Inform).Tone);
    }

    [Fact]
    public void Paused_music_and_downloads_get_the_bars()
    {
        var music = new Card("m", CardType.Status, false, new ActivityState { RawKind = ActivityState.KindMusic, Stage = "paused" });
        var icon = TaskIcons.For(music, hasCover: true, T0);
        Assert.True(icon.Paused);
        Assert.True(icon.Cover);

        var download = new Card("d", CardType.Status, false, new ActivityState { RawKind = ActivityState.KindTransfer, State = "run", Stage = "paused" });
        Assert.True(TaskIcons.For(download, false, T0).Paused);
    }

    [Fact]
    public void Every_agent_stage_asks_for_the_makers_logo()
    {
        static string? Logo(ActivityKind kind, string? agent = null)
            => TaskIcons.For(new Card("k", CardType.Status, false, new ActivityState { RawKind = ActivityState.KindToString(kind), State = "run", Agent = agent }), false, T0).Logo;

        Assert.Equal("claude", Logo(ActivityKind.AgentWorking));
        Assert.Equal("claude", Logo(ActivityKind.AgentPermission));
        Assert.Equal("codex", Logo(ActivityKind.AgentDone, "codex"));
        Assert.Equal("claude", Logo(ActivityKind.Notice, "claude"));   // interrupted / stopped
        Assert.Null(Logo(ActivityKind.Notice));                        // a system notice
        Assert.Null(Logo(ActivityKind.Transfer));
    }
}
