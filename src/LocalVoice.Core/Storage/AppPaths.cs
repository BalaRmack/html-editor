namespace LocalVoice.Core;

public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalVoice");
        SettingsFile = Path.Combine(Root, "settings.json");
        HistoryFile = Path.Combine(Root, "history.json");
        StatisticsFile = Path.Combine(Root, "statistics.json");
        ModelsDirectory = Path.Combine(Root, "models");
        LogsDirectory = Path.Combine(Root, "logs");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
    public string Root { get; }
    public string SettingsFile { get; }
    public string HistoryFile { get; }
    public string StatisticsFile { get; }
    public string ModelsDirectory { get; }
    public string LogsDirectory { get; }
}
