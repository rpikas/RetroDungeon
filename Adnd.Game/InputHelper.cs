using System;

namespace Adnd.Game;

public static class InputHelper
{
    // Read a selection number using keypress input.
    // Auto-submits after the specified number of typed characters,
    // or when Enter is pressed.
    public static int? ReadNumber(int min, int max, int autoSubmitAfterCharacters = 1)
    {
        if (autoSubmitAfterCharacters < 1)
            autoSubmitAfterCharacters = 1;

        string combined = string.Empty;
        while (true)
        {
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Enter)
                break;

            if (key.KeyChar == '\0')
                continue;

            combined += key.KeyChar;

            if (combined.Length >= autoSubmitAfterCharacters)
                break;
        }

        if (int.TryParse(combined.Trim(), out int val))
        {
            if (val >= min && val <= max)
                return val;
        }

        return null;
    }

    // Read a single letter key (A..Z) and return its zero-based index (A=0).
    // Returns null for Enter or invalid input.
    public static int? ReadLetterIndex(int count)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Enter)
            return null;

        if (key.KeyChar == '\0')
            return null;

        var ch = char.ToUpperInvariant(key.KeyChar);
        if (ch < 'A' || ch > 'Z')
            return null;

        int idx = ch - 'A';
        if (idx < 0 || idx >= count)
            return null;

        return idx;
    }
}
