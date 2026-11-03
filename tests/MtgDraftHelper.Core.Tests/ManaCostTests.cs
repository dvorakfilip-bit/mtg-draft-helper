using MtgDraftHelper.Core.Cards;

namespace MtgDraftHelper.Core.Tests;

public class ManaCostTests
{
    [Theory]
    [InlineData("o3oRoRoGoG", "{3}{R}{R}{G}{G}", 7)]
    [InlineData("o1oW", "{1}{W}", 2)]
    [InlineData("oXoW", "{X}{W}", 1)]
    [InlineData("o10", "{10}", 10)]
    [InlineData("o1o(B/P)o(B/P)", "{1}{B/P}{B/P}", 3)]
    [InlineData("o(2/R)", "{2/R}", 2)]
    [InlineData("o2o(U/B)o(U/B)", "{2}{U/B}{U/B}", 4)]
    [InlineData("", "", 0)]
    public void ConvertsArenaManaText(string arena, string expected, int manaValue)
    {
        Assert.Equal(expected, ManaCost.FromArena(arena));
        Assert.Equal(manaValue, ManaCost.ManaValue(arena));
    }

    [Fact]
    public void ColorsIncludeHybridSymbols()
    {
        Assert.Equal(ManaColors.U | ManaColors.B, ManaCost.Colors("o2o(U/B)"));
        Assert.Equal(ManaColors.None, ManaCost.Colors("o4"));
    }

    [Fact]
    public void ColorLettersRoundTripInWubrgOrder()
    {
        Assert.Equal("WUG", ManaCost.ToLetters(ManaCost.ParseColors("GWU")));
    }
}
