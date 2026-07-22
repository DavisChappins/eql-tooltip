using EqWikiOverlay.Core;

namespace EqWikiOverlay.Tests;

public class ForegroundWindowTests
{
    [Theory]
    [InlineData("eqgame", "eqgame", true)]      // exact match
    [InlineData("EQGAME", "eqgame", true)]      // case-insensitive
    [InlineData("brave", "eqgame", false)]      // a browser tab about EQ must NOT match
    [InlineData("chrome", "eqgame", false)]
    [InlineData("eqgame", "", true)]            // blank wanted disables the gate
    [InlineData("", "eqgame", false)]           // unknown foreground process
    public void Matches_GatesOnProcessName(string foreground, string wanted, bool expected)
    {
        Assert.Equal(expected, ForegroundWindow.Matches(foreground, wanted));
    }
}
