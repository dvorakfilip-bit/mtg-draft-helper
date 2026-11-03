namespace MtgDraftHelper.Core;

/// <summary>Lokální složky aplikace v %LOCALAPPDATA%\MtgDraftHelper.</summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MtgDraftHelper");

    public static string StatsDatabase => Path.Combine(DataDir, "stats.db");

    public static string DatasetCacheDir => Path.Combine(DataDir, "datasets");
}
