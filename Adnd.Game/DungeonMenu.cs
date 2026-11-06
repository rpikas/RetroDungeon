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
        var host = TermConsole.Host;

        // The same window as the city: it opens where the city window is, at the same size.
        if (host != null)
        {
            maze.StartPosition = FormStartPosition.Manual;
            maze.Location = host.Location;
            maze.ClientSize = host.ClientSize;
            if (host.WindowState == FormWindowState.Maximized)
                maze.WindowState = FormWindowState.Maximized;
        }

        maze.Shown += (_, _) =>
        {
            maze.TopMost = true;
            maze.Activate();
            maze.BringToFront();
            maze.Focus();
            maze.TopMost = false;
        };

        if (host != null)
            maze.FormClosing += (_, _) => CopyWindowPlace(maze, host);

        maze.ShowDialog(host);
        host?.Activate();
    }

    private static void CopyWindowPlace(MazeForm from, MazeForm to)
    {
        if (from.WindowState != FormWindowState.Normal)
        {
            to.WindowState = from.WindowState;
            return;
        }

        to.WindowState = FormWindowState.Normal;
        to.Location = from.Location;
        to.ClientSize = from.ClientSize;
    }
}
