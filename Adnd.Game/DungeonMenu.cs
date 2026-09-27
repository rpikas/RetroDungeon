using System;
using System.Windows.Forms;

namespace Adnd.Game;

public class DungeonMenu
{
    public void Show()
    {
        using var maze = new MazeForm();

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
