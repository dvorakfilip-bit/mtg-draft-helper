namespace MtgDraftHelper.Core.Cards;

[Flags]
public enum ManaColors
{
    None = 0,
    W = 1,
    U = 2,
    B = 4,
    R = 8,
    G = 16,
}

public enum Rarity
{
    Unknown = 0,
    Basic = 1,
    Common = 2,
    Uncommon = 3,
    Rare = 4,
    Mythic = 5,
}

/// <summary>Karta z databáze Areny. Jedna instance na GrpId.</summary>
public sealed record CardInfo(
    int GrpId,
    string Name,
    string SetCode,
    Rarity Rarity,
    ManaColors Colors,
    string ManaCost,
    int ManaValue,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Subtypes,
    IReadOnlyList<string> Supertypes,
    string? Power,
    string? Toughness,
    string RulesText,
    bool IsPrimaryFace,
    IReadOnlyList<int> LinkedFaceGrpIds)
{
    public bool IsCreature => Types.Contains("Creature");
    public bool IsLand => Types.Contains("Land");

    public string TypeLine => Subtypes.Count == 0
        ? string.Join(' ', Supertypes.Concat(Types))
        : $"{string.Join(' ', Supertypes.Concat(Types))} - {string.Join(' ', Subtypes)}";
}
