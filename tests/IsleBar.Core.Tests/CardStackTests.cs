using IsleBar.Core.Island;
using IsleBar.Core.SystemWatch;
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
    public void Lasting_notices_are_status_cards_and_a_critical_battery_is_pending()
    {
        var board = new NoticeBoard();
        board.Hold("focus", SystemNotice.Make("Focus", "x", T0));
        var critical = SystemNotice.Make("Battery 3%", "x", T0, urgent: true);
        critical.Stage = SystemNotice.CriticalStage;
        board.Hold("low", critical);
        var items = board.Snapshot(T0);
        Assert.Equal(CardType.Status, CardStack.TypeOf(items[0], T0));
        Assert.Equal(CardType.Pending, CardStack.TypeOf(items[1], T0));
    }

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
    public void Each_player_is_its_own_card_and_the_merged_download_keeps_its_key()
    {
        Assert.NotEqual(CardStack.KeyOf(Item(ActivityKind.Music, "media:spotify")), CardStack.KeyOf(Item(ActivityKind.Music, "media:cloudmusic")));
        Assert.Equal(CardStack.KeyOf(Item(ActivityKind.Transfer, "download:a.zip")), CardStack.KeyOf(Item(ActivityKind.Transfer, "download:b.zip")));
        // finished downloads are told on the same card (user 10-07)
        Assert.Equal(CardStack.KeyOf(Item(ActivityKind.Transfer, "download:a.zip")), CardStack.KeyOf(Item(ActivityKind.Transfer, "download:b.zip", 0, "done")));
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

        Assert.Equal(["copied", "ask", "done", "m"], Keys(stack));
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

        Assert.Equal(["m", "download"], Keys(stack));
        Assert.Equal("m", stack.Shown!.Key);
    }

    [Fact]
    public void Working_turning_done_comes_to_the_front_and_working_again_goes_to_the_back()
    {
        var stack = new CardStack();
        stack.Update([Item(ActivityKind.AgentWorking, "s")], T0);
        stack.Update([Item(ActivityKind.AgentWorking, "s"), Item(ActivityKind.Music, "m")], T0.AddSeconds(1));
        Assert.Equal(["s", "m"], Keys(stack));

        stack.Update([Item(ActivityKind.AgentDone, "s", 2, "done"), Item(ActivityKind.Music, "m")], T0.AddSeconds(2));
        Assert.True(stack.Cards[0].Pinned);

        stack.Update([Item(ActivityKind.AgentWorking, "s", 4), Item(ActivityKind.Music, "m")], T0.AddSeconds(4));
        Assert.Equal(["m", "s"], Keys(stack));
    }

    [Fact]
    public void A_kind_set_to_never_pin_waits_with_the_status_cards()
    {
        var stack = new CardStack();
        stack.Update([Item(ActivityKind.Music, "m")], T0);
        stack.Update([Item(ActivityKind.Music, "m"), Item(ActivityKind.AgentDone, "d", 1, "done")], T0.AddSeconds(1),
            new HashSet<ActivityKind> { ActivityKind.AgentDone });

        Assert.Equal(["m", "d"], Keys(stack));
        Assert.False(stack.Cards[1].Pinned);
    }

    // ---------------- Choosing ----------------

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
    public void Choosing_shows_that_card_and_removes_nothing()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0);
        stack.Choose("w", T0);
        Assert.Equal("w", stack.Shown!.Key);
        stack.Choose("download", T0);
        Assert.Equal("download", stack.Shown!.Key);
        stack.Choose("gone", T0);
        Assert.Equal("download", stack.Shown!.Key);   // an unknown key changes nothing
        Assert.Equal(3, stack.Cards.Count);
    }

    [Fact]
    public void The_chosen_card_stays_after_the_mouse_leaves_when_nothing_is_pinned()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Choose("w", T0.AddSeconds(3));
        stack.SetHover(false, T0.AddSeconds(4));

        stack.Update(Three, T0.AddMinutes(10));
        Assert.Equal("w", stack.Shown!.Key);
        Assert.Equal("w", stack.ChosenKey);
    }

    [Fact]
    public void While_the_mouse_is_on_the_pill_a_new_pinned_card_waits_until_two_seconds_after_it_leaves()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Choose("w", T0.AddSeconds(3));
        ActivityState[] withAsk = [.. Three, Item(ActivityKind.AgentPermission, "ask", 5)];

        stack.Update(withAsk, T0.AddSeconds(5));
        Assert.Equal("w", stack.Shown!.Key);   // mouse on the pill: it doesn't change under the hand
        Assert.Equal("ask", stack.Cards[0].Key);   // but it is already first in the stack

        var left = T0.AddMinutes(1);
        stack.SetHover(false, left);
        stack.Update(withAsk, left.AddSeconds(1.9));
        Assert.Equal("w", stack.Shown!.Key);
        stack.Update(withAsk, left.AddSeconds(2));
        Assert.Equal("ask", stack.Shown!.Key);
        Assert.Equal(["ask", "m", "w", "download"], Keys(stack));   // the order is unchanged

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
    public void Choosing_a_pinned_card_doesnt_change_the_chosen_status_card()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Choose("download", T0.AddSeconds(3));
        stack.Update([.. Three, Item(ActivityKind.AgentDone, "d", 4, "done")], T0.AddSeconds(4));   // d is on top now; download still shown
        stack.Choose("d", T0.AddSeconds(5));
        Assert.Equal("d", stack.Shown!.Key);
        Assert.Equal("download", stack.ChosenKey);
    }

    [Fact]
    public void When_the_chosen_card_ends_the_top_card_shows()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Choose("download", T0.AddSeconds(3));
        stack.SetHover(false, T0.AddSeconds(4));

        stack.Update(Three[..2], T0.AddMinutes(1));   // the download finished and went away
        Assert.Equal("m", stack.Shown!.Key);
        Assert.Null(stack.ChosenKey);
    }

    [Fact]
    public void While_holding_the_shown_card_follows_its_place_in_the_stack()
    {
        var stack = ThreeStatusCards();
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Choose("download", T0.AddSeconds(3));   // place 2

        stack.Update(Three[1..], T0.AddSeconds(5));   // the music stopped
        Assert.Equal("download", stack.Shown!.Key);
        Assert.Equal(1, stack.ShownIndex);
    }

    // ---------------- Close ----------------

    [Fact]
    public void A_hidden_card_stays_hidden_until_it_ends_and_shows_again_when_it_comes_back()
    {
        var stack = ThreeStatusCards();
        stack.Hide("m", T0.AddSeconds(3));
        Assert.Equal(["w", "download"], Keys(stack));

        stack.Update(Three, T0.AddSeconds(4));   // a new song is still the music card — still hidden
        Assert.DoesNotContain("m", Keys(stack));

        stack.Update(Three[1..], T0.AddSeconds(5));   // music stopped
        stack.Update(Three, T0.AddSeconds(6));
        Assert.Equal(["w", "download", "m"], Keys(stack));
    }

    [Fact]
    public void Mic_or_camera_in_use_waits_as_a_status_card()
        => Assert.Equal(CardType.Status, CardStack.TypeOf(new ActivityState { RawKind = ActivityState.KindNotice, Stage = "privacy", State = "run" }, T0));

    // ---- debut (user 10-07): a new status card shows on the big pill, then goes down into its cell ----

    private static CardStack Started(params ActivityState[] items)
    {
        var stack = new CardStack();
        stack.Update(items, T0);
        stack.Update(items, T0.AddSeconds(5));   // past the start's quiet seconds
        return stack;
    }

    private static string? ShownKey(CardStack stack) => stack.Shown?.Key;

    [Fact]
    public void A_new_status_card_shows_for_two_seconds_then_goes_back()
    {
        var music = Item(ActivityKind.Music, "m");
        var stack = Started(music);
        var work = Item(ActivityKind.AgentWorking, "w", 6);
        stack.Update([music, work], T0.AddSeconds(6));
        Assert.Equal("w", ShownKey(stack));
        stack.Update([music, work], T0.AddSeconds(7.9));
        Assert.Equal("w", ShownKey(stack));
        stack.Update([music, work], T0.AddSeconds(8));
        Assert.Equal("m", ShownKey(stack));
    }

    [Fact]
    public void Cards_there_at_start_do_not_debut()
    {
        var stack = new CardStack();
        stack.Update([Item(ActivityKind.Music, "m")], T0);
        stack.Update([Item(ActivityKind.Music, "m"), Item(ActivityKind.AgentWorking, "w", 1)], T0.AddSeconds(1));
        Assert.Equal("m", ShownKey(stack));
    }

    [Fact]
    public void Crowded_debuts_take_turns_of_one_and_a_half_seconds()
    {
        var music = Item(ActivityKind.Music, "m");
        var stack = Started(music);
        var a = Item(ActivityKind.Transfer, "a", 6);
        var b = Item(ActivityKind.Transfer, "b", 6);
        stack.Update([music, a, b], T0.AddSeconds(6));
        Assert.Equal("a", ShownKey(stack));
        stack.Update([music, a, b], T0.AddSeconds(7.5));
        Assert.Equal("b", ShownKey(stack));   // a had 1.5 s while b waited
        stack.Update([music, a, b], T0.AddSeconds(9.4));
        Assert.Equal("b", ShownKey(stack));   // nobody waits: b gets its 2 s
        stack.Update([music, a, b], T0.AddSeconds(9.5));
        Assert.Equal("m", ShownKey(stack));
    }

    [Fact]
    public void Debut_over_a_done_card_then_back_but_not_under_the_mouse()
    {
        var done = Item(ActivityKind.AgentDone, "d", 0, "done");
        var stack = Started(done);
        stack.Update([done, Item(ActivityKind.Music, "m", 6)], T0.AddSeconds(6));
        Assert.Equal("m", ShownKey(stack));
        stack.Update([done, Item(ActivityKind.Music, "m", 6)], T0.AddSeconds(9));
        Assert.Equal("d", ShownKey(stack));

        var music = Item(ActivityKind.Music, "m");
        var other = Started(music);
        other.SetHover(true, T0.AddSeconds(6));
        other.Update([music, Item(ActivityKind.AgentWorking, "w", 6)], T0.AddSeconds(6));
        other.SetHover(false, T0.AddSeconds(7));
        other.Update([music, Item(ActivityKind.AgentWorking, "w", 6)], T0.AddSeconds(11));
        Assert.Equal("m", ShownKey(other));
    }

    [Fact]
    public void Back_to_work_after_a_question_debuts_again()
    {
        var music = Item(ActivityKind.Music, "m");
        var ask = Item(ActivityKind.AgentPermission, "s", 6);
        var stack = Started(music);
        stack.Update([music, ask], T0.AddSeconds(6));
        Assert.Equal("s", ShownKey(stack));
        var work = Item(ActivityKind.AgentWorking, "s", 7);
        stack.Update([music, work], T0.AddSeconds(7));
        Assert.Equal("s", ShownKey(stack));
        stack.Update([music, work], T0.AddSeconds(12.9));
        Assert.Equal("s", ShownKey(stack));   // working again in the same chat: 6 s (user 10-10)
        stack.Update([music, work], T0.AddSeconds(13));
        Assert.Equal("m", ShownKey(stack));
    }

    [Fact]
    public void Back_to_work_after_done_debuts_for_six_seconds()
    {
        var music = Item(ActivityKind.Music, "m");
        var done = Item(ActivityKind.AgentDone, "s", 0, "done");
        var stack = Started(music, done);
        var work = Item(ActivityKind.AgentWorking, "s", 6);
        stack.Update([music, work], T0.AddSeconds(6));
        Assert.Equal("s", ShownKey(stack));
        stack.Update([music, work], T0.AddSeconds(11.9));
        Assert.Equal("s", ShownKey(stack));
        stack.Update([music, work], T0.AddSeconds(12));
        Assert.Equal("m", ShownKey(stack));
    }

    [Fact]
    public void A_temporary_card_goes_first_and_the_debut_waits()
    {
        var music = Item(ActivityKind.Music, "m");
        var stack = Started(music);
        var copied = Item(ActivityKind.Notice, "copied", 6);
        var work = Item(ActivityKind.AgentWorking, "w", 6);
        stack.Update([music, copied, work], T0.AddSeconds(6));
        Assert.Equal("copied", ShownKey(stack));
        stack.Update([music, work], T0.AddSeconds(8));
        Assert.Equal("w", ShownKey(stack));
        stack.Update([music, work], T0.AddSeconds(11));
        Assert.Equal("m", ShownKey(stack));
    }

    // ---------------- Keep in the small pill (甲⑤) ----------------

    [Fact]
    public void A_tucked_card_leaves_the_big_pill_and_does_not_come_back_when_it_turns_done()
    {
        var stack = new CardStack();
        var music = Item(ActivityKind.Music, "m");
        var work = Item(ActivityKind.AgentWorking, "w");
        stack.Update([music, work], T0);
        Assert.Equal("m", ShownKey(stack));

        stack.Tuck("m", T0.AddSeconds(1));
        Assert.Equal("w", ShownKey(stack));

        stack.Update([music, work], T0.AddSeconds(10));
        Assert.Equal("w", ShownKey(stack));
        Assert.Contains("m", Keys(stack));   // still a card, on the small pill

        var done = Item(ActivityKind.AgentDone, "w", 20, "done");
        stack.Tuck("w", T0.AddSeconds(11));
        stack.Update([music, done], T0.AddSeconds(20));
        Assert.Null(ShownKey(stack));   // all tucked: only the small pill
    }

    [Fact]
    public void A_clicked_tucked_card_shows_while_looked_at_then_goes_back_down()
    {
        var stack = new CardStack();
        var music = Item(ActivityKind.Music, "m");
        var work = Item(ActivityKind.AgentWorking, "w");
        stack.Update([music, work], T0);
        stack.Tuck("w", T0);

        stack.SetHover(true, T0.AddSeconds(1));
        stack.Choose("w", T0.AddSeconds(1));
        stack.Update([music, work], T0.AddSeconds(2));
        Assert.Equal("w", ShownKey(stack));

        stack.SetHover(false, T0.AddSeconds(3));
        stack.Update([music, work], T0.AddSeconds(3 + CardStack.HoldAfterLeave.TotalSeconds + 1));
        Assert.Equal("m", ShownKey(stack));
    }

    [Fact]
    public void A_tucked_card_that_ends_is_forgotten()
    {
        var stack = new CardStack();
        var work = Item(ActivityKind.AgentWorking, "w");
        stack.Update([work], T0);
        stack.Tuck("w", T0);
        stack.Update([], T0.AddSeconds(5));
        Assert.False(stack.IsTucked("w"));
        stack.Update([work], T0.AddSeconds(6));
        Assert.Equal("w", ShownKey(stack));
    }

    [Fact]
    public void An_unpinned_card_follows_the_rules_again()
    {
        var stack = new CardStack();
        var music = Item(ActivityKind.Music, "m");
        var done = Item(ActivityKind.AgentDone, "w", state: "done");
        stack.Update([music, done], T0);
        stack.Tuck("w", T0);
        Assert.Equal("m", ShownKey(stack));

        stack.Untuck("w", T0.AddSeconds(1));
        Assert.False(stack.IsTucked("w"));
        Assert.Equal("w", ShownKey(stack));   // a done card comes up by itself again
    }
}
