using System.IO;

namespace Adnd.Data;

internal static class FolderSignature
{
    // Cheap change detector (file count + newest write time) so parsed static data can be cached
    // and still refresh when a JSON file is edited or added.
    public static (int Count, long LatestTicks) Compute(string folder)
    {
        var count = 0;
        long latest = 0;

        if (!Directory.Exists(folder))
            return (0, 0);

        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            count++;
            var ticks = File.GetLastWriteTimeUtc(file).Ticks;
            if (ticks > latest)
                latest = ticks;
        }

        return (count, latest);
    }
}
