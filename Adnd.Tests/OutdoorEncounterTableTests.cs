using Xunit;
using Adnd.Core.Monsters;

namespace Adnd.Tests;

public class OutdoorEncounterTableTests
{
    [Theory]
    [InlineData("2.0", 2, 2)]
    [InlineData("02-03", 2, 3)]
    [InlineData("00", 100, 100)]
    [InlineData("98-00", 98, 100)]
    public void ParsesRanges(string text, int low, int high)
    {
        Assert.True(OutdoorEncounterTable.TryParseRange(text, out var l, out var h));
        Assert.Equal((low, high), (l, h));
    }

    [Theory]
    [InlineData("")]
    [InlineData("–")]
    [InlineData(null)]
    public void BlankCellsHaveNoEntry(string? text) =>
        Assert.False(OutdoorEncounterTable.TryParseRange(text, out _, out _));

    [Fact]
    public void LooksUpCreatureByTerrainAndRoll()
    {
        var table = OutdoorEncounterTable.FromRows(
            new[] { "Plain", "Forest" },
            new (string, IReadOnlyList<string>)[]
            {
                ("Wolf", new[] { "01-10", "–" }),
                ("Bear", new[] { "11-20", "5.0" })
            });

        Assert.Equal("Wolf", table.Lookup("Plain", 10));
        Assert.Equal("Bear", table.Lookup("plain", 11));
        Assert.Equal("Bear", table.Lookup("Forest", 5));
        Assert.Null(table.Lookup("Forest", 10));
        Assert.Null(table.Lookup("Desert", 10));
    }
}
