using IsleBar.Core.Island;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class MediaCardsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static MediaReading Play(string source, string title, bool playing = true)
        => new(source, playing, new ActivityState { RawKind = ActivityState.KindMusic, Name = title, State = "run", Stage = playing ? "playing" : "paused" });

    [Fact]
    public void Each_player_gets_its_own_card_once_it_plays()
    {
        var cards = new MediaCards();
        cards.Update([Play("spotify", "A"), Play("edge", "B", playing: false)], T0);
        var card = Assert.Single(cards.Cards);
        Assert.Equal("media:spotify#1", card.SourcePath);
        Assert.Equal("spotify", MediaCards.SourceIn(card.SourcePath));

        cards.Update([Play("spotify", "A"), Play("edge", "B")], T0.AddSeconds(1));
        Assert.Equal(["media:spotify#1", "media:edge#1"], cards.Cards.Select(c => c.SourcePath));
    }

    [Fact]
    public void A_new_title_shows_after_half_a_second_on_the_same_card()
    {
        var cards = new MediaCards();
        cards.Update([Play("edge", "A")], T0);
        Assert.True(cards.Update([Play("edge", "stale")], T0.AddSeconds(0.1)));
        Assert.True(cards.Update([Play("edge", "B")], T0.AddSeconds(0.2)));
        Assert.Equal("A", Assert.Single(cards.Cards).Name);
        cards.Update([Play("edge", "B")], T0.AddSeconds(0.8));
        var card = Assert.Single(cards.Cards);
        Assert.Equal("B", card.Name);
        Assert.Equal("media:edge#1", card.SourcePath);
    }

    [Fact]
    public void Paused_five_minutes_drops_the_card_and_playing_again_is_a_new_one()
    {
        var cards = new MediaCards();
        cards.Update([Play("spotify", "A")], T0);
        cards.Update([Play("spotify", "A", playing: false)], T0.AddSeconds(1));
        Assert.Equal("paused", Assert.Single(cards.Cards).Stage);
        cards.Update([Play("spotify", "A", playing: false)], T0.AddMinutes(4));
        Assert.Single(cards.Cards);
        cards.Update([Play("spotify", "A", playing: false)], T0.AddSeconds(1).AddMinutes(5));
        Assert.Empty(cards.Cards);

        cards.Update([Play("spotify", "A")], T0.AddMinutes(6));
        Assert.Equal("media:spotify#2", Assert.Single(cards.Cards).SourcePath);
    }

    [Fact]
    public void Closing_the_player_drops_its_card_at_once()
    {
        var cards = new MediaCards();
        cards.Update([Play("spotify", "A"), Play("edge", "B")], T0);
        cards.Update([Play("edge", "B")], T0.AddSeconds(1));
        Assert.Equal("media:edge#1", Assert.Single(cards.Cards).SourcePath);
    }
}
