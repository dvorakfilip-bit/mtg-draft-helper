using System.Text.RegularExpressions;

namespace MtgDraftHelper.Core.Cards;

[Flags]
public enum CardRole
{
    None = 0,
    Creature = 1,
    Removal = 2,
    Counterspell = 4,
    CombatTrick = 8,
    CardDraw = 16,
    ManaFixing = 32,
    Evasion = 64,
}

/// <summary>
/// Heuristické určení rolí karty z typu a textu schopností. Odhad – ruční opravy jsou až
/// v pozdější verzi (PRD, mimo rozsah MVP).
/// </summary>
public static partial class CardRoles
{
    [GeneratedRegex(
        @"(destroy|exile) target (?!card)[\w ,-]*?(creature|permanent|planeswalker)" +
        @"|deals? (\d+|x|that much) damage to (any target|target [\w ,-]*?(creature|planeswalker)|each creature)" +
        @"|target creature[\w ,-]*? gets -\d+/-\d+|gets -x/-x" +
        @"|deals damage equal to its power to (another )?target creature" +
        @"|fights? (another )?target creature|fights up to one target creature" +
        @"|(each opponent|target player|target opponent) sacrifices a (creature|nonland permanent)" +
        @"|enchanted (creature|permanent) can't attack or block",
        RegexOptions.IgnoreCase)]
    private static partial Regex RemovalRegex();

    [GeneratedRegex(@"counter target [\w ]*spell", RegexOptions.IgnoreCase)]
    private static partial Regex CounterRegex();

    [GeneratedRegex(@"target creature( you control)? gets \+\d+/\+\d+|target creature( you control)? gains (indestructible|hexproof|first strike|double strike|protection)",
        RegexOptions.IgnoreCase)]
    private static partial Regex TrickRegex();

    [GeneratedRegex(@"\bdraws? (a card|two cards|three cards|\w+ cards|cards equal)", RegexOptions.IgnoreCase)]
    private static partial Regex DrawRegex();

    [GeneratedRegex(@"add one mana of any color|search your library for (a|up to two) basic land|add \{o?[WUBRG]\} or \{o?[WUBRG]\}|mana of any (one )?color",
        RegexOptions.IgnoreCase)]
    private static partial Regex FixingRegex();

    [GeneratedRegex(@"(^|\n)\s*(flying|menace|shadow)\b|can't be blocked", RegexOptions.IgnoreCase)]
    private static partial Regex EvasionRegex();

    public static CardRole Of(CardInfo card)
    {
        var roles = CardRole.None;
        var text = card.RulesText;

        if (card.IsCreature)
            roles |= CardRole.Creature;
        if (RemovalRegex().IsMatch(text))
            roles |= CardRole.Removal;
        if (CounterRegex().IsMatch(text))
            roles |= CardRole.Counterspell;
        if (card.Types.Contains("Instant") && TrickRegex().IsMatch(text) && !roles.HasFlag(CardRole.Removal))
            roles |= CardRole.CombatTrick;
        if (DrawRegex().IsMatch(text))
            roles |= CardRole.CardDraw;
        if (FixingRegex().IsMatch(text) && !(card.IsLand && card.Supertypes.Contains("Basic")))
            roles |= CardRole.ManaFixing;
        if (card.IsCreature && EvasionRegex().IsMatch(text))
            roles |= CardRole.Evasion;

        return roles;
    }
}
