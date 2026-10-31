using Adnd.Core.Dungeon;
using Xunit;

namespace Adnd.Tests;

public class WallGridTests
{
    [Fact]
    public void MaskBitsMapToSides()
    {
        var grid = WallGrid.FromRows(new[] { new[] { 0, 0, 0 }, new[] { 0, 3, 0 }, new[] { 0, 0, 0 } });

        Assert.True(grid.HasWall(1, 1, WallSide.North));
        Assert.True(grid.HasWall(1, 1, WallSide.East));
        Assert.False(grid.HasWall(1, 1, WallSide.South));
        Assert.False(grid.HasWall(1, 1, WallSide.West));
    }

    [Fact]
    public void WallOnEitherSideOfSharedEdgeBlocksMovement()
    {
        // Only the right square records the wall, on its west side.
        var grid = WallGrid.FromRows(new[] { new[] { 0, 8 } });

        Assert.True(grid.HasWall(0, 0, WallSide.East));
        Assert.False(grid.CanMove(0, 0, WallSide.East));
    }

    [Fact]
    public void OpenEdgeAllowsMovementButSolidSquareDoesNot()
    {
        var grid = WallGrid.FromRows(new[] { new[] { 0, 0, 15 } });

        Assert.True(grid.CanMove(0, 0, WallSide.East));
        Assert.False(grid.CanMove(1, 0, WallSide.East));
        Assert.False(grid.CanMove(0, 0, WallSide.West));
    }

    [Fact]
    public void FromFloorsWallsOffNonFloorNeighbours()
    {
        var floors = new[] { "#.#" };
        var grid = WallGrid.FromFloors(3, 1, (x, y) => floors[y][x] == '.');

        Assert.Equal(15, grid.GetMask(0, 0));
        Assert.Equal(1 | 2 | 4 | 8, grid.GetMask(1, 0));
        Assert.False(grid.CanMove(1, 0, WallSide.East));
    }

    [Fact]
    public void RejectsMaskOutsideZeroToFifteen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WallGrid.FromRows(new[] { new[] { 256 } }));
    }

    [Fact]
    public void MapWrapsAround()
    {
        var grid = WallGrid.FromRows(new[] { new[] { 0, 0, 0 } });

        Assert.True(grid.CanMove(2, 0, WallSide.East));
        Assert.True(grid.CanMove(0, 0, WallSide.West));
        Assert.Equal((0, 0), grid.Wrap(3, 0));
        Assert.Equal((2, 0), grid.Wrap(-1, 0));

        var walled = WallGrid.FromRows(new[] { new[] { 8, 0, 0 } });
        Assert.False(walled.CanMove(2, 0, WallSide.East));
    }

    [Fact]
    public void DoorBlocksSightButNotMovement()
    {
        // Door on the east edge of the left square (32), nothing on the right square.
        var grid = WallGrid.FromRows(new[] { new[] { 32, 0 } });

        Assert.True(grid.HasDoor(0, 0, WallSide.East));
        Assert.True(grid.HasDoor(1, 0, WallSide.West));
        Assert.True(grid.BlocksSight(0, 0, WallSide.East));
        Assert.False(grid.HasWall(0, 0, WallSide.East));
        Assert.True(grid.CanMove(0, 0, WallSide.East));
        Assert.False(grid.IsSolid(0, 0));
    }

    [Fact]
    public void OneWayDoorOpensOnlyFromTheDoorSideAndLooksLikeWallFromTheOther()
    {
        // Door on the east edge of the left square (32), wall on the west edge of the right square (8).
        var grid = WallGrid.FromRows(new[] { new[] { 32, 8 } });

        Assert.True(grid.HasDoor(0, 0, WallSide.East));
        Assert.False(grid.HasWall(0, 0, WallSide.East));
        Assert.True(grid.CanMove(0, 0, WallSide.East));

        Assert.True(grid.HasWall(1, 0, WallSide.West));
        Assert.False(grid.HasDoor(1, 0, WallSide.West));
        Assert.False(grid.CanMove(1, 0, WallSide.West));
    }

    [Fact]
    public void SecretDoorLooksLikeWallFromBothSidesButCanBeWalkedThrough()
    {
        var grid = WallGrid.FromRows(new[] { new[] { 32, 128 } });

        grid.HideSecretDoor(0, 0, WallSide.East);

        Assert.True(grid.HasWall(0, 0, WallSide.East));
        Assert.True(grid.HasWall(1, 0, WallSide.West));
        Assert.False(grid.HasDoor(0, 0, WallSide.East));
        Assert.True(grid.CanMove(0, 0, WallSide.East));
        Assert.True(grid.CanMove(1, 0, WallSide.West));
        Assert.Equal(new[] { WallSide.West }, grid.HiddenSecretDoorSides(1, 0));

        Assert.True(grid.RevealSecretDoor(1, 0, WallSide.West));

        Assert.True(grid.HasDoor(0, 0, WallSide.East));
        Assert.True(grid.CanMove(1, 0, WallSide.West));
        Assert.Empty(grid.HiddenSecretDoorSides(0, 0));
    }

    [Fact]
    public void ClosedRoomWithOnlyASecretDoorCanStillBeEntered()
    {
        var grid = WallGrid.FromRows(new[] { new[] { 11, 15 } });

        grid.HideSecretDoor(0, 0, WallSide.East);

        Assert.False(grid.IsSolid(1, 0));
        Assert.True(grid.CanMove(0, 0, WallSide.East));
    }
}
