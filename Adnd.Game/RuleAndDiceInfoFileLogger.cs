using Adnd.Core.Diagnostics;
using System.Text;

namespace Adnd.Game;

internal sealed class RuleAndDiceInfoFileLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private readonly Action<string> _handler;
    private bool _disposed;

    private RuleAndDiceInfoFileLogger(StreamWriter writer)
    {
        _writer = writer;
        _handler = OnInfoPublished;
        RuleApplicationInfo.InfoPublished += _handler;

        _writer.WriteLine($"=== AD&D Rule and Dice Info log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        _writer.Flush();
    }

    public static RuleAndDiceInfoFileLogger Start()
    {
        var root = ResolveWorkspaceRoot();
        var folder = Path.Combine(root, "AdndRuleAndDiceInfo");
        Directory.CreateDirectory(folder);

        var filePath = Path.Combine(folder, $"AdndRuleAndDiceInfo_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        return new RuleAndDiceInfoFileLogger(writer);
    }

    private static string ResolveWorkspaceRoot()
    {
        var probes = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var probe in probes)
        {
            var dir = new DirectoryInfo(probe);
            while (dir != null)
            {
                var slnxPath = Path.Combine(dir.FullName, "AdndGame.slnx");
                if (File.Exists(slnxPath))
                    return dir.FullName;

                dir = dir.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private void OnInfoPublished(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        lock (_sync)
        {
            if (_disposed)
                return;

            _writer.WriteLine(message);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            RuleApplicationInfo.InfoPublished -= _handler;
            _writer.WriteLine($"=== AD&D Rule and Dice Info log ended {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            _writer.Dispose();
        }
    }
}
