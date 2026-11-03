using System.Text;
using System.Text.RegularExpressions;

namespace MtgDraftHelper.Core.Cards;

/// <summary>
/// Převod mana cost z formátu Areny (<c>o3oRoRoGoG</c>, <c>o1o(B/P)</c>) na běžný zápis (<c>{3}{R}{R}{G}{G}</c>).
/// </summary>
public static partial class ManaCost
{
    [GeneratedRegex(@"o(\([^)]*\)|[^o(]+)")]
    private static partial Regex SymbolRegex();

    public static IReadOnlyList<string> ParseArenaSymbols(string? arenaText)
    {
        if (string.IsNullOrEmpty(arenaText))
            return [];

        return SymbolRegex().Matches(arenaText)
            .Select(m => m.Groups[1].Value.Trim('(', ')'))
            .ToList();
    }

    public static string FromArena(string? arenaText)
    {
        var sb = new StringBuilder();
        foreach (var symbol in ParseArenaSymbols(arenaText))
            sb.Append('{').Append(symbol).Append('}');
        return sb.ToString();
    }

    public static int ManaValue(string? arenaText)
    {
        var total = 0;
        foreach (var symbol in ParseArenaSymbols(arenaText))
        {
            if (int.TryParse(symbol, out var generic))
                total += generic;
            else if (symbol == "X")
                continue;
            else if (symbol.StartsWith("2/"))
                total += 2; // monohybrid {2/W}
            else
                total += 1; // barva, hybrid, phyrexian, {C}
        }
        return total;
    }

    /// <summary>Barvy, které se v mana cost vyskytují (včetně hybridů).</summary>
    public static ManaColors Colors(string? arenaText)
    {
        var colors = ManaColors.None;
        foreach (var symbol in ParseArenaSymbols(arenaText))
            foreach (var c in symbol)
                colors |= FromLetter(c);
        return colors;
    }

    public static ManaColors FromLetter(char c) => c switch
    {
        'W' => ManaColors.W,
        'U' => ManaColors.U,
        'B' => ManaColors.B,
        'R' => ManaColors.R,
        'G' => ManaColors.G,
        _ => ManaColors.None,
    };

    public static ManaColors ParseColors(string? letters)
    {
        var colors = ManaColors.None;
        foreach (var c in letters ?? "")
            colors |= FromLetter(c);
        return colors;
    }

    /// <summary>Zápis barev v pořadí WUBRG, např. "WU".</summary>
    public static string ToLetters(ManaColors colors)
    {
        var sb = new StringBuilder(5);
        if (colors.HasFlag(ManaColors.W)) sb.Append('W');
        if (colors.HasFlag(ManaColors.U)) sb.Append('U');
        if (colors.HasFlag(ManaColors.B)) sb.Append('B');
        if (colors.HasFlag(ManaColors.R)) sb.Append('R');
        if (colors.HasFlag(ManaColors.G)) sb.Append('G');
        return sb.ToString();
    }
}
