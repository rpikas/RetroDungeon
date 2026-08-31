using Adnd.Core.Config;

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

        var nl = Environment.NewLine;
        string message =
            $"Source: {source}, Page: {page}, Context: {context}.{nl} Rule: {rule}.{nl}" +
            $"Roll: {numberOfDices}d{sidesOnDices} => Result: {resultOfRoll}.{nl}" +
            $"Consequence: {consequenceOfRoll}";

        InfoPublished?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
