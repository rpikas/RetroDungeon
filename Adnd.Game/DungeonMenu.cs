using System;
using System.Windows.Forms;

namespace Adnd.Game;

public class DungeonMenu
{
    public void Show(bool thinWalls = false)
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
