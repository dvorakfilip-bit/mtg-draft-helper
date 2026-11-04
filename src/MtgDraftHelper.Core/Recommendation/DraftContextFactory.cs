using MtgDraftHelper.Core.Cards;
using MtgDraftHelper.Core.Logs;

namespace MtgDraftHelper.Core.Recommendation;

/// <summary>Převod stavu z logu (<see cref="DraftSnapshot"/>) na vstup doporučování.</summary>
public static class DraftContextFactory
{
    public const int DefaultPicksPerPack = 14;

    public static DraftContext Create(DraftSnapshot snapshot, ArenaCardDatabase cards, bool signalDataFromSameFormat)
    {
        IReadOnlyList<CardInfo> Resolve(IEnumerable<int> ids) => ids.Select(cards.Get).OfType<CardInfo>().ToList();

        var current = snapshot.CurrentPack;
        return new DraftContext(
            Pool: Resolve(snapshot.Pool),
            SeenPacks: snapshot.Picks
                .Where(p => p.Offered.Count > 0)
                .Select(p => new SeenPack(p.Pack, p.Pick, Resolve(p.Offered)))
                .ToList(),
            Pack: current?.Pack ?? snapshot.Picks.LastOrDefault()?.Pack ?? 1,
            Pick: current?.Pick ?? (snapshot.Picks.LastOrDefault()?.Pick + 1) ?? 1,
            PackSize: PicksPerPack(snapshot),
            IsBotDraft: snapshot.Event?.Format == LimitedFormat.QuickDraft,
            SignalDataFromSameFormat: signalDataFromSameFormat);
    }

    /// <summary>Počet picků na balíček podle velikosti prvního balíčku (Pick-Two: polovina).</summary>
    public static int PicksPerPack(DraftSnapshot snapshot)
    {
        var first = snapshot.Picks.FirstOrDefault(p => p.Pick == 1 && p.Offered.Count > 0);
        int cardsInPack, cardsToPick;
        if (first is not null)
        {
            cardsInPack = first.Offered.Count;
            cardsToPick = Math.Max(1, first.Picked.Count);
        }
        else if (snapshot.CurrentPack is { Pick: 1 } pack)
        {
            cardsInPack = pack.Cards.Count;
            cardsToPick = Math.Max(1, pack.CardsToPick);
        }
        else
        {
            return DefaultPicksPerPack;
        }
        return Math.Max(1, (int)Math.Ceiling((double)cardsInPack / cardsToPick));
    }
}
