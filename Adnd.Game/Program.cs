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
    private static async Task Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        ItemInstaller.Install();   // ← Kopierar items automatiskt
        SpellInstaller.Install();
        MonsterInstaller.Install();
        TreasureInstaller.Install();

        var main = new Adnd.Game.MainMenu();
        await main.ShowAsync();
    }
}

