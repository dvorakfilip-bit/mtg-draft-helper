using System.Text.Json.Nodes;
using MtgDraftHelper.Core.Logs;

namespace MtgDraftHelper.Core.Tests;

public class LogMessageReaderTests
{
    [Fact]
    public void ReadsRequestOnOneLine()
    {
        var reader = new LogMessageReader();
        var message = reader.ReadLine("""[UnityCrossThreadLogger]==> EventJoin {"id":"1","request":"{\"EventName\":\"PremierDraft_FRA_20260929\"}"}""");

        Assert.NotNull(message);
        Assert.Equal(LogMessageKind.Request, message.Kind);
        Assert.Equal("EventJoin", message.Method);
        Assert.Equal("PremierDraft_FRA_20260929", message.Payload!["request"].GetString("EventName"));
    }

    [Fact]
    public void ReadsResponseWithJsonOnNextLine()
    {
        var reader = new LogMessageReader();
        Assert.Null(reader.ReadLine("[UnityCrossThreadLogger]03.11.2026 13:11:52"));
        Assert.Null(reader.ReadLine("<== EventGetCoursesV2(abc-123)"));
        var message = reader.ReadLine("""{"Courses":[]}""");

        Assert.NotNull(message);
        Assert.Equal(LogMessageKind.Response, message.Kind);
        Assert.Equal("EventGetCourses", message.Method);
        Assert.Equal(new DateTime(2026, 11, 3, 13, 11, 52), message.Timestamp);
    }

    [Fact]
    public void ReadsResponseWithJsonOnSameLine()
    {
        var message = new LogMessageReader().ReadLine("""<== Event_Join(abc) {"Course":{"InternalEventName":"X"}}""");
        Assert.Equal("EventJoin", message!.Method);
        Assert.NotNull(message.Payload!["Course"]);
    }

    [Fact]
    public void ResponseHeaderWithoutJsonDoesNotSwallowNextMessage()
    {
        var reader = new LogMessageReader();
        reader.ReadLine("<== EventJoin(abc)");
        var message = reader.ReadLine("""[UnityCrossThreadLogger]Draft.Notify {"draftId":"d","SelfPick":1,"SelfPack":1,"PackCards":"1,2"}""");
        Assert.Equal(LogMessageKind.Notify, message!.Kind);
    }

    [Theory]
    [InlineData("DETAILED LOGS: ENABLED", true)]
    [InlineData("DETAILED LOGS: DISABLED", false)]
    public void ReadsDetailedLogsFlag(string line, bool enabled)
    {
        var message = new LogMessageReader().ReadLine(line);
        Assert.Equal(LogMessageKind.DetailedLogs, message!.Kind);
        Assert.Equal(enabled, message.DetailedLogsEnabled);
    }

    [Theory]
    [InlineData("Event_Join", "EventJoin")]
    [InlineData("EventGetCoursesV2", "EventGetCourses")]
    [InlineData("EventSetDeckV3", "EventSetDeck")]
    [InlineData("BotDraft_DraftPick", "BotDraftDraftPick")]
    public void NormalizesMethodNames(string raw, string expected) =>
        Assert.Equal(expected, LogMessageReader.NormalizeMethod(raw));

    [Theory]
    [InlineData("03.11.2026 13:11:52")]
    [InlineData("11/3/2026 1:11:52 PM")]
    [InlineData("2026-11-03 13:11:52")]
    public void ParsesTimestampsInSeveralLocales(string text) =>
        Assert.Equal(new DateTime(2026, 11, 3, 13, 11, 52), LogMessageReader.ParseTimestamp(text));
}

public class LogJsonTests
{
    [Fact]
    public void UnwrapsDoubleAndTripleEncodedPayloads()
    {
        var node = LogJson.ParseFrom("""{"id":"1","request":"{\"Type\":600,\"Payload\":\"{\\\"EventName\\\":\\\"QuickDraft_FRA\\\"}\"}"}""");
        Assert.Equal("QuickDraft_FRA", node!["request"]!["Payload"].GetString("EventName"));
    }

    [Fact]
    public void RepairsUnescapedNestedRequest()
    {
        var node = LogJson.ParseFrom("""{"id":"1","request":"{"DraftId":"123","Pack":1}"}""");
        Assert.Equal("123", node!["request"].GetString("DraftId"));
    }

    [Fact]
    public void IgnoresTextAfterJson()
    {
        var node = LogJson.ParseFrom("""prefix {"a":1} trailing text""");
        Assert.Equal(1, node.GetInt("a"));
    }

    [Fact]
    public void ReturnsNullForInvalidJson() =>
        Assert.Null(LogJson.ParseFrom("""{"a":"""));

    [Fact]
    public void ReadsIdsFromArraysStringsAndCsv()
    {
        var node = JsonNode.Parse("""{"a":[1,2],"b":["3","4"],"c":"5,6"}""");
        Assert.Equal([1, 2], node.GetIds("a"));
        Assert.Equal([3, 4], node.GetIds("b"));
        Assert.Equal([5, 6], node.GetIds("c"));
        Assert.Empty(node.GetIds("missing"));
    }
}

public class EventInfoTests
{
    [Theory]
    [InlineData("PremierDraft_FRA_20260929", LimitedFormat.PremierDraft, "FRA", "PremierDraft")]
    [InlineData("TradDraft_FRA_20260929", LimitedFormat.TradDraft, "FRA", "TradDraft")]
    [InlineData("QuickDraft_TDM_20250417", LimitedFormat.QuickDraft, "TDM", "QuickDraft")]
    [InlineData("Y26_FRA_Bot_Draft_20261017", LimitedFormat.QuickDraft, "FRA", "QuickDraft")]
    [InlineData("PickTwoDraft_OM1_20250923", LimitedFormat.PickTwoDraft, "OM1", "PickTwoDraft")]
    [InlineData("Sealed_DSK_20240924", LimitedFormat.Sealed, "DSK", "Sealed")]
    [InlineData("Trad_Sealed_NEO_20220317", LimitedFormat.TradSealed, "NEO", "TradSealed")]
    [InlineData("ArenaDirect_OTJ_Sealed_20240726", LimitedFormat.OtherSealed, "OTJ", "Sealed")]
    [InlineData("ContenderDraft_MSH_20260707", LimitedFormat.OtherDraft, "MSH", "PremierDraft")]
    [InlineData("CubeDraft_Powered_20251028", LimitedFormat.CubeDraft, null, null)]
    public void ParsesLimitedEvents(string name, LimitedFormat format, string? set, string? seventeenLands)
    {
        var info = EventInfo.Parse(name);
        Assert.Equal(format, info.Format);
        Assert.Equal(set, info.SetCode);
        Assert.Equal(seventeenLands, info.SeventeenLandsFormat);
        Assert.True(info.IsLimited);
    }

    [Theory]
    [InlineData("Ladder")]
    [InlineData("Timeless_Play")]
    [InlineData("Jump_In_2024")]
    [InlineData("Traditional_Alchemy_Play")]
    public void ConstructedEventsAreNotLimited(string name) =>
        Assert.False(EventInfo.Parse(name).IsLimited);

    [Fact]
    public void KnownSetCodesWin()
    {
        var info = EventInfo.Parse("ArenaDirect_ABC_OTJ_Sealed", new HashSet<string> { "OTJ" });
        Assert.Equal("OTJ", info.SetCode);
    }
}
