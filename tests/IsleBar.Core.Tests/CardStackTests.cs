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
    [InlineData(ActivityKind.AgentWorking, "run", CardType.Status)]
    [InlineData(ActivityKind.Music, "run", CardType.Status)]
    [InlineData(ActivityKind.Notice, "run", CardType.Temporary)]
    [InlineData(ActivityKind.Error, "error", CardType.Temporary)]
    [InlineData(ActivityKind.Transfer, "run", CardType.Status)]
    [InlineData(ActivityKind.Transfer, "done", CardType.Temporary)]
    public void Each_kind_has_a_type(ActivityKind kind, string state, CardType expected)
        => Assert.Equal(expected, CardStack.TypeOf(Item(kind, "a", state: state), T0));

    [Fact]
    public void A_timer_is_status_while_running_and_temporary_once_it_reaches_zero()
    {
        Assert.Equal(CardType.Status, CardStack.TypeOf(Timer("t", 60, T0), T0));
        Assert.Equal(CardType.Temporary, CardStack.TypeOf(Timer("t", -1, T0), T0));
        var paused = Timer("t", -1, T0);
        paused.Left = 30;
        Assert.Equal(CardType.Status, CardStack.TypeOf(paused, T0));
    }

    [Fact]
    public void All_music_is_one_card_and_the_merged_download_keeps_its_key()
    {
        var a = Item(ActivityKind.Music, "media:spotify");
        var b = Item(ActivityKind.Music, "media:cloudmusic");
        Assert.Equal(CardStack.KeyOf(a), CardStack.KeyOf(b));
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
        Assert.Equal(["s", "music"], Keys(stack));
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

    // ---------------- Scrolling (peek) ----------------

    private static CardStack ThreeStatusCards()
    {
        var stack = new CardStack();
        stack.Update([Item(ActivityKind.Music, "m")], T0);
        stack.Update([Item(ActivityKind.Music, "m"), Item(ActivityKind.AgentWorking, "w")], T0.AddSeconds(1));
        stack.Update([Item(ActivityKind.Music, "m"), Item(ActivityKind.AgentWorking, "w"), Item(ActivityKind.Transfer, "download:x")], T0.AddSeconds(2));
        return stack;
    }

    [Fact]
    public void Scrolling_moves_through_the_stack_and_stops_at_both_ends()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0);
        stack.Scroll(1, T0);
        Assert.Equal("w", stack.Shown!.Key);
        stack.Scroll(5, T0);
        Assert.Equal("download", stack.Shown!.Key);
        stack.Scroll(-9, T0);
        Assert.Equal("music", stack.Shown!.Key);
        Assert.False(stack.Peeking);
        Assert.Equal(3, stack.Cards.Count);   // scrolling never removes anything
    }

    [Fact]
    public void The_peek_holds_while_hovering_and_returns_to_the_top_three_seconds_after_leaving()
    {
        var stack = ThreeStatusCards();
        var items = stack.Cards.Select(c => c.State).ToList();
        stack.SetHover(true, T0);
        stack.Scroll(1, T0);

        stack.Update(items, T0.AddMinutes(5));   // still on the pill
        Assert.Equal("w", stack.Shown!.Key);

        var left = T0.AddMinutes(5);
        stack.SetHover(false, left);
        stack.Update(items, left.AddSeconds(2.9));
        Assert.Equal("w", stack.Shown!.Key);
        stack.Update(items, left.AddSeconds(3));
        Assert.Equal("music", stack.Shown!.Key);
        Assert.False(stack.Peeking);
    }

    [Fact]
    public void A_new_pinned_card_ends_the_peek()
    {
        var stack = ThreeStatusCards();
        var items = stack.Cards.Select(c => c.State).ToList();
        stack.SetHover(true, T0);
        stack.Scroll(2, T0);

        stack.Update([.. items, Item(ActivityKind.AgentPermission, "ask", 9)], T0.AddSeconds(9));
        Assert.Equal("ask", stack.Shown!.Key);
        Assert.Equal(0, stack.ShownIndex);
    }

    [Fact]
    public void The_peek_follows_its_card_and_ends_when_the_card_does()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0);
        stack.Scroll(2, T0);   // the download

        // the music stops: the download moves up one place, still shown
        stack.Update([Item(ActivityKind.AgentWorking, "w"), Item(ActivityKind.Transfer, "download:x")], T0.AddSeconds(5));
        Assert.Equal("download", stack.Shown!.Key);
        Assert.Equal(1, stack.ShownIndex);

        // the download finishes and goes away
        stack.Update([Item(ActivityKind.AgentWorking, "w")], T0.AddSeconds(6));
        Assert.Equal("w", stack.Shown!.Key);
        Assert.False(stack.Peeking);
    }

    // ---------------- Keep showing · close ----------------

    [Fact]
    public void A_fixed_card_leads_the_status_cards_but_pinned_ones_still_come_first()
    {
        var stack = ThreeStatusCards();
        var items = stack.Cards.Select(c => c.State).ToList();
        stack.Fix("download", T0.AddSeconds(3));
        Assert.Equal(["download", "music", "w"], Keys(stack));

        stack.Update([.. items, Item(ActivityKind.Notice, "copied", 4)], T0.AddSeconds(4));
        Assert.Equal(["copied", "download", "music", "w"], Keys(stack));

        stack.Update([Item(ActivityKind.Music, "m"), Item(ActivityKind.AgentWorking, "w")], T0.AddSeconds(5));
        Assert.Null(stack.FixedKey);   // it ended — a later download isn't fixed
    }

    [Fact]
    public void A_hidden_card_stays_hidden_until_it_ends_and_shows_again_when_it_comes_back()
    {
        var stack = ThreeStatusCards();
        var items = stack.Cards.Select(c => c.State).ToList();
        stack.Hide("music", T0.AddSeconds(3));
        Assert.Equal(["w", "download"], Keys(stack));

        stack.Update(items, T0.AddSeconds(4));
        Assert.DoesNotContain("music", Keys(stack));

        stack.Update([Item(ActivityKind.AgentWorking, "w")], T0.AddSeconds(5));   // music stopped
        stack.Update([Item(ActivityKind.AgentWorking, "w"), Item(ActivityKind.Music, "m")], T0.AddSeconds(6));
        Assert.Equal(["w", "music"], Keys(stack));
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
