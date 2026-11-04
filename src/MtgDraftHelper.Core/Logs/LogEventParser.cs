using System.Text.Json.Nodes;

namespace MtgDraftHelper.Core.Logs;

/// <summary>
/// Převod zpráv z logu na události. Podporuje draft s lidmi (Premier, Traditional, Pick-Two, Cube),
/// Quick Draft s boty a Sealed. Formát zpráv viz docs/M0-pruzkum.md, kap. 3.
/// </summary>
public static class LogEventParser
{
    public static IEnumerable<LogEvent> Parse(LogMessage message)
    {
        if (message.Kind == LogMessageKind.DetailedLogs)
        {
            yield return new DetailedLogsStatus(message.DetailedLogsEnabled);
            yield break;
        }

        var payload = message.Payload;
        if (payload is null)
            yield break;

        switch (message.Kind, message.Method)
        {
            case (LogMessageKind.Request, "EventJoin"):
                if (Request(payload).GetString("EventName") is { } joined)
                    yield return new EventJoined(joined);
                break;

            case (LogMessageKind.Response, "EventJoin"):
            case (LogMessageKind.Response, "EventSetDeck"):
                if (ParseCourse(payload["Course"] ?? payload) is { } course)
                    yield return new CoursesReceived([course], IsFullList: false);
                break;

            case (LogMessageKind.Response, "EventGetCourses"):
                if (payload["Courses"] is JsonArray courses)
                    yield return new CoursesReceived(courses.Select(ParseCourse).OfType<CourseInfo>().ToList(), IsFullList: true);
                break;

            case (LogMessageKind.Notify, "DraftNotify"):
                yield return new PackShown(
                    DraftId: payload.GetString("draftId") ?? payload.GetString("DraftId"),
                    EventName: null,
                    Pack: payload.GetInt("SelfPack") ?? 0,
                    Pick: payload.GetInt("SelfPick") ?? 0,
                    Cards: payload.GetIds("PackCards"),
                    CardsToPick: 1);
                break;

            case (LogMessageKind.Request, "EventPlayerDraftMakePick"):
            {
                var request = Request(payload);
                var cards = request.GetIds("GrpIds").Where(id => id > 0).ToList();
                if (cards.Count == 0 && request.GetInt("GrpId") is > 0 and var single)
                    cards.Add(single);
                if (cards.Count > 0)
                    yield return new CardsPicked(request.GetString("DraftId"), null,
                        request.GetInt("Pack") ?? 0, request.GetInt("Pick") ?? 0, cards);
                break;
            }

            case (LogMessageKind.Response, "BotDraftDraftStatus"):
            case (LogMessageKind.Response, "BotDraftDraftPick"):
                foreach (var e in ParseBotDraftStatus(payload["Payload"] ?? payload))
                    yield return e;
                break;

            case (LogMessageKind.Request, "BotDraftDraftPick"):
            {
                var request = Request(payload);
                var info = request["PickInfo"];
                var cards = info.GetIds("CardIds");
                if (cards.Count == 0)
                    cards = info.GetIds("CardId");
                if (cards.Count > 0)
                    yield return new CardsPicked(null, info.GetString("EventName") ?? request.GetString("EventName"),
                        (info.GetInt("PackNumber") ?? 0) + 1, (info.GetInt("PickNumber") ?? 0) + 1, cards);
                break;
            }

            case (LogMessageKind.Response, "DraftCompleteDraft"):
                yield return new DraftCompleted(payload.GetString("InternalEventName"), payload.GetString("DraftId"), payload.GetIds("CardPool"));
                break;

            case (LogMessageKind.Request, "EventSetDeck"):
            {
                var request = Request(payload);
                if (request.GetString("EventName") is { } eventName && request["Deck"] is JsonObject deck)
                    yield return new DeckSubmitted(eventName, ParseDeckCards(deck["MainDeck"]), ParseDeckCards(deck["Sideboard"]));
                break;
            }
        }
    }

    /// <summary>Data požadavku – v novém formátu přímo v "request", ve starém ještě v "Payload".</summary>
    private static JsonNode Request(JsonNode payload)
    {
        var request = payload["request"] ?? payload;
        return request["Payload"] is JsonObject inner ? inner : request;
    }

    private static CourseInfo? ParseCourse(JsonNode? node)
    {
        if (node?.GetString("InternalEventName") is not { } name)
            return null;
        return new CourseInfo(name, node.GetString("CurrentModule"), node.GetIds("CardPool"));
    }

    private static IEnumerable<LogEvent> ParseBotDraftStatus(JsonNode status)
    {
        var eventName = status.GetString("EventName");
        if (status["PickedCards"] is JsonArray)
            yield return new PoolSnapshot(eventName, status.GetIds("PickedCards"));

        var pack = status.GetIds("DraftPack");
        var draftStatus = status.GetString("DraftStatus");
        if (string.Equals(draftStatus, "PickNext", StringComparison.OrdinalIgnoreCase) && pack.Count > 0)
        {
            yield return new PackShown(null, eventName,
                (status.GetInt("PackNumber") ?? 0) + 1, (status.GetInt("PickNumber") ?? 0) + 1,
                pack, status.GetInt("NumCardsToPick") ?? 1);
        }
        else if (draftStatus is not null && !string.Equals(draftStatus, "PickNext", StringComparison.OrdinalIgnoreCase))
        {
            // Neověřeno na skutečném logu: jiný stav než PickNext bereme jako konec draftu.
            yield return new DraftCompleted(eventName, null, status.GetIds("PickedCards"));
        }
    }

    private static List<DeckCard> ParseDeckCards(JsonNode? node) =>
        node is JsonArray array
            ? array.Select(c => new DeckCard(c.GetInt("cardId") ?? 0, c.GetInt("quantity") ?? 1)).Where(c => c.GrpId > 0).ToList()
            : [];
}
