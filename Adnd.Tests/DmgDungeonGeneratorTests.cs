using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Dungeon;
using Xunit;

namespace Adnd.Tests;

public class DmgDungeonGeneratorTests
{
    private static readonly WallSide[] Sides = { WallSide.North, WallSide.East, WallSide.South, WallSide.West };

    private static HashSet<(int X, int Y)> Reachable(WallGrid grid, (int X, int Y)? from = null)
    {
        var seen = new HashSet<(int, int)> { from ?? DmgDungeonGenerator.Start };
        var queue = new Queue<(int X, int Y)>(seen);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var side in Sides)
            {
                if (!grid.CanMove(x, y, side))
                    continue;
                var next = WallGrid.Step(x, y, side);
                if (seen.Add(next))
                    queue.Enqueue(next);
            }
        }

        return seen;
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(1)]
    [InlineData(99)]
    public void Generated_level_is_consistent_connected_and_large_enough(int seed)
    {
        var level = DmgDungeonGenerator.GenerateLevel(seed);
        var grid = level.Walls;

        Assert.Equal(level.OneWayDoors.Count, grid.FindInconsistentEdges().Count());
        Assert.InRange(level.EncounterSquares.Count, 5, 15);
        Assert.NotEmpty(level.Traps);
        Assert.NotEmpty(level.SecretDoors);
        Assert.NotEmpty(level.OneWayDoors);
        foreach (var (x, y, side) in level.SecretDoors)
            Assert.True(grid.IsSecretDoor(x, y, side));
        foreach (var (x, y) in level.EncounterSquares)
            Assert.False(grid.IsSolid(x, y));
        var open = Enumerable.Range(0, grid.Width).SelectMany(x => Enumerable.Range(0, grid.Height).Select(y => (x, y)))
            .Where(c => !grid.IsSolid(c.x, c.y)).ToList();
        Assert.True(open.Count >= 100);
        Assert.Equal(open.Count, Reachable(grid).Count);

        // One-way doors never trap the party: every square can still get back to the elevator.
        foreach (var c in open)
            Assert.Contains(DmgDungeonGenerator.Start, Reachable(grid, c));
        Assert.Contains(open, c => grid.HasDoor(c.x, c.y, WallSide.North) || grid.HasDoor(c.x, c.y, WallSide.East)
            || grid.HasDoor(c.x, c.y, WallSide.South) || grid.HasDoor(c.x, c.y, WallSide.West));
    }

    [Fact]
    public void Start_rooms_differ_between_floors()
    {
        var starts = Enumerable.Range(6, 11).Select(seed =>
        {
            var grid = DmgDungeonGenerator.Generate(seed);
            return string.Join(",", Enumerable.Range(0, 5).SelectMany(y => Enumerable.Range(0, 5).Select(x => grid.GetMask(x, y))));
        }).ToList();

        Assert.Equal(starts.Count, starts.Distinct().Count());
    }

    [Fact]
    public void Floor_one_start_room_covers_the_stairs_up_and_the_elevator()
    {
        var level = DmgDungeonGenerator.GenerateLevel(1, corridorsOnly: true, startAtOrigin: true);

        Assert.False(level.Walls.IsSolid(0, 0));
        Assert.Contains(DmgDungeonGenerator.Start, Reachable(level.Walls, (0, 0)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public void Corridors_only_floor_still_has_all_features_and_stays_connected(int seed)
    {
        var level = DmgDungeonGenerator.GenerateLevel(seed, corridorsOnly: true);
        var grid = level.Walls;
        var open = Enumerable.Range(0, grid.Width).SelectMany(x => Enumerable.Range(0, grid.Height).Select(y => (x, y)))
            .Where(c => !grid.IsSolid(c.x, c.y)).ToList();

        Assert.True(open.Count >= 60);
        Assert.Equal(open.Count, Reachable(grid).Count);
        Assert.NotEmpty(level.Traps);
        Assert.NotEmpty(level.SecretDoors);
        Assert.NotEmpty(level.OneWayDoors);
        Assert.InRange(level.EncounterSquares.Count, 5, 15);
    }

    [Fact]
    public void Same_seed_gives_the_same_level()
    {
        var a = DmgDungeonGenerator.Generate(6);
        var b = DmgDungeonGenerator.Generate(6);
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
                Assert.Equal(a.GetMask(x, y), b.GetMask(x, y));
    }
}
