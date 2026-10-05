using IsleBar.Core.Island;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class CardStackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static ActivityState Item(ActivityKind kind, string source, int updatedSec = 0, string state = "run")
        => new()
        {
            RawKind = ActivityState.KindToString(kind),
            State = state,
            SourcePath = source,
            UpdatedAt = T0.AddSeconds(updatedSec),
        };

    private static ActivityState Timer(string source, double dueInSec, DateTimeOffset now)
        => new()
        {
            RawKind = ActivityState.KindTimer,
            State = "run",
            SourcePath = source,
            Due = now.ToUnixTimeMilliseconds() / 1000.0 + dueInSec,
            UpdatedAt = now,
        };

    private static string[] Keys(CardStack stack) => stack.Cards.Select(c => c.Key).ToArray();

    // ---------------- Types ----------------

    [Theory]
    [InlineData(ActivityKind.AgentPermission, "run", CardType.Pending)]
    [InlineData(ActivityKind.AgentDone, "done", CardType.Inform)]
    [InlineData(ActivityKind.Error, "error", CardType.Inform)]
    [InlineData(ActivityKind.AgentWorking, "run", CardType.Status)]
    [InlineData(ActivityKind.Music, "run", CardType.Status)]
    [InlineData(ActivityKind.Notice, "run", CardType.Temporary)]
    [InlineData(ActivityKind.Notice, "error", CardType.Temporary)]   // the system's warnings (battery low, offline)
    [InlineData(ActivityKind.Transfer, "run", CardType.Status)]
    [InlineData(ActivityKind.Transfer, "done", CardType.Temporary)]
    public void Each_kind_has_a_type(ActivityKind kind, string state, CardType expected)
        => Assert.Equal(expected, CardStack.TypeOf(Item(kind, "a", state: state), T0));

    [Fact]
    public void An_agents_own_failure_is_an_inform_card()
    {
        var limit = AgentNotices.UsageLimit("claude", T0.AddHours(2), null, T0);
        Assert.Equal(CardType.Inform, CardStack.TypeOf(limit, T0));
    }

    [Fact]
    public void A_timer_is_status_while_running_and_inform_once_it_reaches_zero()
    {
        Assert.Equal(CardType.Status, CardStack.TypeOf(Timer("t", 60, T0), T0));
        Assert.Equal(CardType.Inform, CardStack.TypeOf(Timer("t", -1, T0), T0));
        var paused = Timer("t", -1, T0);
        paused.Left = 30;
        Assert.Equal(CardType.Status, CardStack.TypeOf(paused, T0));
    }

    [Fact]
    public void Inform_cards_stay_until_closed_and_temporary_ones_go_by_themselves()
    {
        var hourLater = T0.AddHours(1);
        Assert.True(ActivityStore.IsAlive(Timer("t", -1, T0), T0, hourLater));                   // time's up: stays
        Assert.True(ActivityStore.IsAlive(Item(ActivityKind.Error, "e", state: "error"), T0, hourLater));
        var launch = Item(ActivityKind.Notice, "islebar_launch", state: "error");
        launch.Agent = "claude";
        Assert.True(ActivityStore.IsAlive(launch, T0, hourLater));
        Assert.False(ActivityStore.IsAlive(Item(ActivityKind.Notice, "n", state: "error"), T0, T0.AddMinutes(1)));
    }

    [Fact]
    public void All_music_is_one_card_and_the_merged_download_keeps_its_key()
    {
        Assert.Equal(CardStack.KeyOf(Item(ActivityKind.Music, "media:spotify")), CardStack.KeyOf(Item(ActivityKind.Music, "media:cloudmusic")));
        Assert.Equal(CardStack.KeyOf(Item(ActivityKind.Transfer, "download:a.zip")), CardStack.KeyOf(Item(ActivityKind.Transfer, "download:b.zip")));
        Assert.NotEqual(CardStack.KeyOf(Item(ActivityKind.Transfer, "download:a.zip")), CardStack.KeyOf(Item(ActivityKind.Transfer, "download-done:a.zip")));
    }

    // ---------------- Order ----------------

    [Fact]
    public void Nothing_going_on_means_no_card()
    {
        var stack = new CardStack();
        stack.Update([], T0);
        Assert.Null(stack.Shown);
        Assert.Empty(stack.Cards);
    }

    [Fact]
    public void Pinned_cards_go_temporary_then_pending_then_inform_and_before_all_status_cards()
    {
        var stack = new CardStack();
        stack.Update(
        [
            Item(ActivityKind.Music, "m"),
            Item(ActivityKind.AgentDone, "done", state: "done"),
            Item(ActivityKind.AgentPermission, "ask"),
            Item(ActivityKind.Notice, "copied"),
        ], T0);

        Assert.Equal(["copied", "ask", "done", "music"], Keys(stack));
        Assert.Equal("copied", stack.Shown!.Key);
    }

    [Fact]
    public void Within_a_pinned_type_the_newest_is_on_top()
    {
        var stack = new CardStack();
        stack.Update([Item(ActivityKind.AgentDone, "old", 1, "done"), Item(ActivityKind.AgentDone, "new", 5, "done")], T0);
        Assert.Equal(["new", "old"], Keys(stack));
    }

    [Fact]
    public void A_new_status_card_waits_at_the_back()
    {
        var stack = new CardStack();
        stack.Update([Item(ActivityKind.Music, "m")], T0);
        stack.Update([Item(ActivityKind.Music, "m"), Item(ActivityKind.Transfer, "download:a")], T0.AddSeconds(1));

        Assert.Equal(["music", "download"], Keys(stack));
        Assert.Equal("music", stack.Shown!.Key);
    }

    [Fact]
    public void Working_turning_done_comes_to_the_front_and_working_again_goes_to_the_back()
    {
        var stack = new CardStack();
        stack.Update([Item(ActivityKind.AgentWorking, "s")], T0);
        stack.Update([Item(ActivityKind.AgentWorking, "s"), Item(ActivityKind.Music, "m")], T0.AddSeconds(1));
        Assert.Equal(["s", "music"], Keys(stack));

        stack.Update([Item(ActivityKind.AgentDone, "s", 2, "done"), Item(ActivityKind.Music, "m")], T0.AddSeconds(2));
        Assert.True(stack.Cards[0].Pinned);

        stack.Update([Item(ActivityKind.AgentWorking, "s", 4), Item(ActivityKind.Music, "m")], T0.AddSeconds(4));
        Assert.Equal(["music", "s"], Keys(stack));
    }

    [Fact]
    public void A_kind_set_to_never_pin_waits_with_the_status_cards()
    {
        var stack = new CardStack();
        stack.Update([Item(ActivityKind.Music, "m")], T0);
        stack.Update([Item(ActivityKind.Music, "m"), Item(ActivityKind.AgentDone, "d", 1, "done")], T0.AddSeconds(1),
            new HashSet<ActivityKind> { ActivityKind.AgentDone });

        Assert.Equal(["music", "d"], Keys(stack));
        Assert.False(stack.Cards[1].Pinned);
    }

    // ---------------- Scrolling ----------------

    private static readonly ActivityState[] Three =
        [Item(ActivityKind.Music, "m"), Item(ActivityKind.AgentWorking, "w"), Item(ActivityKind.Transfer, "download:x")];

    /// <summary>music · w · download, arrived in that order.</summary>
    private static CardStack ThreeStatusCards()
    {
        var stack = new CardStack();
        stack.Update(Three[..1], T0);
        stack.Update(Three[..2], T0.AddSeconds(1));
        stack.Update(Three, T0.AddSeconds(2));
        return stack;
    }

    [Fact]
    public void Scrolling_moves_through_the_stack_and_wraps_round_at_both_ends()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0);
        stack.Scroll(1, T0);
        Assert.Equal("w", stack.Shown!.Key);
        stack.Scroll(2, T0);
        Assert.Equal("music", stack.Shown!.Key);   // past the bottom → the top
        stack.Scroll(-1, T0);
        Assert.Equal("download", stack.Shown!.Key);   // past the top → the bottom
        Assert.Equal(3, stack.Cards.Count);   // scrolling never removes anything
    }

    [Fact]
    public void The_chosen_card_stays_after_the_mouse_leaves_when_nothing_is_pinned()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Scroll(1, T0.AddSeconds(3));
        stack.SetHover(false, T0.AddSeconds(4));

        stack.Update(Three, T0.AddMinutes(10));
        Assert.Equal("w", stack.Shown!.Key);
        Assert.Equal("w", stack.ChosenKey);
    }

    [Fact]
    public void While_the_mouse_is_on_the_pill_a_new_pinned_card_waits_until_three_seconds_after_it_leaves()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Scroll(1, T0.AddSeconds(3));
        ActivityState[] withAsk = [.. Three, Item(ActivityKind.AgentPermission, "ask", 5)];

        stack.Update(withAsk, T0.AddSeconds(5));
        Assert.Equal("w", stack.Shown!.Key);   // still scrolling: the pill doesn't change under the hand
        Assert.Equal("ask", stack.Cards[0].Key);   // but it is already first in the stack

        var left = T0.AddMinutes(1);
        stack.SetHover(false, left);
        stack.Update(withAsk, left.AddSeconds(2.9));
        Assert.Equal("w", stack.Shown!.Key);
        stack.Update(withAsk, left.AddSeconds(3));
        Assert.Equal("ask", stack.Shown!.Key);
        Assert.Equal(["ask", "music", "w", "download"], Keys(stack));   // the order is unchanged

        stack.Update(Three, left.AddSeconds(10));   // answered
        Assert.Equal("w", stack.Shown!.Key);   // back to the chosen card
    }

    [Fact]
    public void Away_from_the_pill_a_new_pinned_card_shows_at_once()
    {
        var stack = ThreeStatusCards();
        stack.Update([.. Three, Item(ActivityKind.Notice, "copied", 9)], T0.AddSeconds(9));
        Assert.Equal("copied", stack.Shown!.Key);
    }

    [Fact]
    public void Scrolling_onto_a_pinned_card_doesnt_change_the_chosen_status_card()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Scroll(2, T0.AddSeconds(3));   // download
        stack.Update([.. Three, Item(ActivityKind.AgentDone, "d", 4, "done")], T0.AddSeconds(4));   // d is on top now; download still shown
        stack.Scroll(1, T0.AddSeconds(5));   // past the bottom → d (pinned)
        Assert.Equal("d", stack.Shown!.Key);
        Assert.Equal("download", stack.ChosenKey);
    }

    [Fact]
    public void When_the_chosen_card_ends_the_top_card_shows()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Scroll(2, T0.AddSeconds(3));   // download
        stack.SetHover(false, T0.AddSeconds(4));

        stack.Update(Three[..2], T0.AddMinutes(1));   // the download finished and went away
        Assert.Equal("music", stack.Shown!.Key);
        Assert.Null(stack.ChosenKey);
    }

    [Fact]
    public void While_holding_the_shown_card_follows_its_place_in_the_stack()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Scroll(2, T0.AddSeconds(3));   // download, place 2

        stack.Update(Three[1..], T0.AddSeconds(5));   // the music stopped
        Assert.Equal("download", stack.Shown!.Key);
        Assert.Equal(1, stack.ShownIndex);
    }

    // ---------------- Close ----------------

    [Fact]
    public void A_hidden_card_stays_hidden_until_it_ends_and_shows_again_when_it_comes_back()
    {
        var stack = ThreeStatusCards();
        stack.Hide("music", T0.AddSeconds(3));
        Assert.Equal(["w", "download"], Keys(stack));

        stack.Update(Three, T0.AddSeconds(4));   // a new song is still the music card — still hidden
        Assert.DoesNotContain("music", Keys(stack));

        stack.Update(Three[1..], T0.AddSeconds(5));   // music stopped
        stack.Update(Three, T0.AddSeconds(6));
        Assert.Equal(["w", "download", "music"], Keys(stack));
    }

    // ---------------- Wheel ----------------

    [Fact]
    public void One_wheel_notch_is_one_card()
    {
        var wheel = new WheelSteps();
        Assert.Equal(1, wheel.Add(-120));
        Assert.Equal(-1, wheel.Add(120));
        Assert.Equal(2, wheel.Add(-240));
    }

    [Fact]
    public void Touchpad_deltas_add_up_to_a_notch()
    {
        var wheel = new WheelSteps();
        var steps = 0;
        for (var i = 0; i < 9; i++)
        {
            steps += wheel.Add(-13);   // 117: not yet
        }

        Assert.Equal(0, steps);
        Assert.Equal(1, wheel.Add(-13));
    }

    [Fact]
    public void Reversing_direction_drops_what_was_collected()
    {
        var wheel = new WheelSteps();
        wheel.Add(-100);
        Assert.Equal(0, wheel.Add(30));
        Assert.Equal(-1, wheel.Add(90));
    }
}
