using System.Text.Json.Nodes;

namespace MtgDraftHelper.Core.Logs;

public enum LogMessageKind
{
    /// <summary>Požadavek klienta: <c>[UnityCrossThreadLogger]==&gt; Metoda {...}</c>.</summary>
    Request,

    /// <summary>Odpověď serveru: řádek <c>&lt;== Metoda(id)</c> a JSON na dalším řádku.</summary>
    Response,

    /// <summary>Notifikace bez páru požadavek/odpověď, např. <c>Draft.Notify {...}</c>.</summary>
    Notify,

    /// <summary>Řádek <c>DETAILED LOGS: ENABLED|DISABLED</c>.</summary>
    DetailedLogs,
}

/// <summary>
/// Jedna zpráva z Player.log. <see cref="Method"/> je normalizovaný název (bez podtržítek
/// a bez přípony verze, např. <c>EventGetCoursesV2</c> → <c>EventGetCourses</c>).
/// <see cref="Payload"/> má rozbalené vnořené JSON řetězce (<c>request</c>, <c>Payload</c>).
/// </summary>
public sealed record LogMessage(
    LogMessageKind Kind,
    string Method,
    JsonNode? Payload,
    DateTime? Timestamp,
    bool DetailedLogsEnabled = false);
