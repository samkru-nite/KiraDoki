using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

public class NoticeLengthsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("张三 · 晚上一起吃饭吗")]   // fits: nothing scrolls
    public void A_notification_that_fits_stays_6_s(string? line)
        => Assert.Equal(NoticeLengths.Read, NoticeLengths.ToRead(line));

    [Fact]
    public void A_bit_too_long_stays_for_its_lap()
    {
        // 13 full-width = 182 dip: 1 s + (182 + 40) / 30 = 8.4 s
        Assert.Equal(8.4, NoticeLengths.ToRead(new string('字', 13)).TotalSeconds, 1);
    }

    [Fact]
    public void A_long_one_stays_at_most_10_s()
        => Assert.Equal(NoticeLengths.Attention, NoticeLengths.ToRead(new string('字', 60)));

    [Fact]
    public void Latin_letters_count_half()
    {
        // 23 letters = 172.5 dip: still fits
        Assert.Equal(NoticeLengths.Read, NoticeLengths.ToRead(new string('a', 23)));
    }
}
