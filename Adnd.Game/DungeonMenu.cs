using System;
using System.Windows.Forms;
using Adnd.Core.Config;

namespace Adnd.Game;

public class DungeonMenu
{
    public void Show(bool thinWalls = false)
    {
        // Drives the LocationBased monster source option: Proving Grounds = Wizardry monsters, Maze = AD&D.
        GameRulesProvider.Current.InProvingGrounds = thinWalls;
        try
        {
            ShowMaze(thinWalls);
        }
        finally
        {
            GameRulesProvider.Current.InProvingGrounds = false;
        }
    }

    private static void ShowMaze(bool thinWalls)
    {
        using var maze = new MazeForm(thinWalls);

        maze.Shown += (_, _) =>
        {
            maze.TopMost = true;
            maze.Activate();
            maze.BringToFront();
            maze.Focus();
            maze.TopMost = false;
        };

        maze.ShowDialog();
    }
}
