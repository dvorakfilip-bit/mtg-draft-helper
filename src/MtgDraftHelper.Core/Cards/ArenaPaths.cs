using System.Text.RegularExpressions;

namespace MtgDraftHelper.Core.Cards;

/// <summary>Hledání instalace Areny a jejích souborů.</summary>
public static partial class ArenaPaths
{
    public static string PlayerLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Wizards Of The Coast", "MTGA", "Player.log");

    private static readonly string[] DefaultInstallDirs =
    [
        @"C:\Program Files\Wizards of the Coast\MTGA",
        @"C:\Program Files (x86)\Wizards of the Coast\MTGA",
        @"C:\Program Files\Epic Games\MagicTheGathering",
        @"C:\Program Files (x86)\Steam\steamapps\common\MTGA",
    ];

    [GeneratedRegex(@"Mono path\[0\] = '(?<path>[^']+)/MTGA_Data/Managed'")]
    private static partial Regex MonoPathRegex();

    /// <summary>Složka MTGA_Data – nejdřív podle logu, pak výchozí cesty instalace.</summary>
    public static string? FindDataDir(string? playerLogPath = null)
    {
        var fromLog = FindInstallDirFromLog(playerLogPath ?? PlayerLogPath);
        var candidates = fromLog is null ? DefaultInstallDirs : [fromLog, .. DefaultInstallDirs];
        return candidates
            .Select(dir => Path.Combine(dir, "MTGA_Data"))
            .FirstOrDefault(Directory.Exists);
    }

    /// <summary>Soubory Raw_CardDatabase_*.mtga, nejnovější první.</summary>
    public static IReadOnlyList<string> FindCardDatabases(string? dataDir = null)
    {
        dataDir ??= FindDataDir();
        if (dataDir is null)
            return [];

        var rawDir = Path.Combine(dataDir, "Downloads", "Raw");
        if (!Directory.Exists(rawDir))
            return [];

        return new DirectoryInfo(rawDir)
            .GetFiles("Raw_CardDatabase_*.mtga")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .ToList();
    }

    private static string? FindInstallDirFromLog(string playerLogPath)
    {
        if (!File.Exists(playerLogPath))
            return null;

        try
        {
            // Arena drží log otevřený, proto FileShare.ReadWrite. Cesta je na začátku souboru.
            using var stream = new FileStream(playerLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            for (var i = 0; i < 200 && reader.ReadLine() is { } line; i++)
            {
                var match = MonoPathRegex().Match(line);
                if (match.Success)
                    return match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar);
            }
        }
        catch (IOException)
        {
        }
        return null;
    }
}
