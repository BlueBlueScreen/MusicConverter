using System.IO;

namespace MusicConverter.Services;

internal sealed class ToolLocator
{
    public string? Find(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var localCandidates = new[]
        {
            Path.Combine(baseDirectory, "tools", fileName),
            Path.Combine(baseDirectory, fileName)
        };

        foreach (var candidate in localCandidates)
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), fileName);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch { }
        }

        return null;
    }
}
