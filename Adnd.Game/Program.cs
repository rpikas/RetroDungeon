/*using Adnd.Game.Engine;

namespace Adnd.Game;

internal static class Program
{
    static void Main(string[] args)
    {
        var game = new GameLoop();
        game.Run();
    }
}

using Adnd.Game;

internal class Program
{
    private static void Main(string[] args)
    {
        var city = new CityMenu();
        city.Show();
    }
}
*/
using Adnd.Game;
using Adnd.Game.Installers;
using System.Threading.Tasks;
using System.Windows.Forms;

internal class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var ruleAndDiceInfoLogger = RuleAndDiceInfoFileLogger.Start();
        RuleApplicationInfoRuntime.StartIfEnabled();

        ItemInstaller.Install();   // ← Kopierar items automatiskt
        SpellInstaller.Install();
        MonsterInstaller.Install();
        TreasureInstaller.Install();

        var main = new Adnd.Game.MainMenu();
        var host = MazeForm.CreateCityHost();
        var finished = false;

        host.Shown += async (_, _) =>
        {
            try
            {
                await main.ShowAsync();
            }
            catch (OperationCanceledException)
            {
                // The window was closed while a menu waited for a key.
            }
            finally
            {
                finished = true;
                RuleApplicationInfoRuntime.Stop();
                if (!host.IsDisposed)
                    host.Close();
            }
        };

        // Closing the window while a menu waits for a key ends the game.
        host.FormClosed += (_, _) =>
        {
            if (!finished)
            {
                RuleApplicationInfoRuntime.Stop();
                Environment.Exit(0);
            }
        };

        Application.Run(host);
    }
}

