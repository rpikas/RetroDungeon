using Adnd.Core.Config;
using System.IO;

namespace Adnd.Core.Diagnostics;

public static class RuleApplicationInfo
{
    public static event Action<string>? InfoPublished;

    public static void Publish(string message)
    {
        if (!GameRulesProvider.Current.ShowDiceRollAndRuleApplicationInfo)
            return;

        if (string.IsNullOrWhiteSpace(message))
            return;

        InfoPublished?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
    public static void Publish(string source, string page, string context, string rule, string numberOfDices, 
        string sidesOnDices, string resultOfRoll, string consequenceOfRoll)
    {
        if (!GameRulesProvider.Current.ShowDiceRollAndRuleApplicationInfo)
            return;

        var sourcePageText = $"{source}, {page}";
        if (TryResolveRuleImagePath(source, page, out var imagePath))
            sourcePageText = $"[[RULEIMG|{sourcePageText}|{imagePath}|{DateTime.Now.Ticks}]]";

        string message =
            $"{sourcePageText}, {context}:" +
            $"{numberOfDices}d{sidesOnDices}({resultOfRoll}) -> " +
            $"{consequenceOfRoll}";

        InfoPublished?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private static bool TryResolveRuleImagePath(string source, string page, out string imagePath)
    {
        imagePath = string.Empty;

        var sourceToken = (source ?? string.Empty).Trim().Replace(" ", string.Empty);
        var pageToken = (page ?? string.Empty).Trim().Replace(" ", string.Empty);
        var fileName = $"{sourceToken}{pageToken}.png";
        wwif (string.IsNullOrWhiteSpace(sourceToken) || string.IsNullOrWhiteSpace(pageToken))
            return false;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Monsters","MM1", fileName),
            Path.Combine(AppContext.BaseDirectory,  "..", "..", "..", "Assets", "Monsters","MM1", fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Adnd.Game", "Assets", "Monsters","MM1", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Monsters","MM1", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Adnd.Game", "Assets",  "Monsters","MM1", fileName),

            Path.Combine(AppContext.BaseDirectory, "Assets", "Rules", fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Rules", fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Adnd.Game", "Assets", "Rules", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Rules", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Adnd.Game", "Assets", "Rules", fileName)
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (!File.Exists(full))
                continue;

            imagePath = full;
            return true;
        }

        return false;
    }
}
