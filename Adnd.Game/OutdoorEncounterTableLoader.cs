using System.Globalization;
using System.Text;
using Adnd.Core.Monsters;
using ExcelDataReader;

namespace Adnd.Game;

// Reads Assets\Rules\OutdoorEncounter.xls: header row of terrains, then one creature type per row.
public static class OutdoorEncounterTableLoader
{
    public static OutdoorEncounterTable Load(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var terrains = new List<string>();
        var rows = new List<(string, IReadOnlyList<string>)>();
        var first = true;
        while (reader.Read())
        {
            var cells = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
                cells.Add(Convert(reader.GetValue(i)));

            if (first)
            {
                terrains.AddRange(cells.Skip(1));
                first = false;
                continue;
            }

            rows.Add((cells.FirstOrDefault() ?? string.Empty, cells.Skip(1).ToList()));
        }

        return OutdoorEncounterTable.FromRows(terrains, rows);
    }

    private static string Convert(object? value) => value switch
    {
        null => string.Empty,
        double d => d.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
