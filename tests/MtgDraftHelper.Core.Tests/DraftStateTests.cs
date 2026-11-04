using MtgDraftHelper.Core.Logs;

namespace MtgDraftHelper.Core.Tests;

public class FixtureLogTests
{
    private static DraftState Replay(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "logs", fixture);
        var reader = new LogMessageReader();
        var state = new DraftState();
        foreach (var line in File.ReadLines(path))
            if (reader.ReadLine(line) is { } message)
                foreach (var e in LogEventParser.Parse(message))
                    state.Apply(e);
        return state;
    }

    [Fact]
    public void PremierDraft()
    {
        var state = Replay("premier_draft.log");

        Assert.Equal(LimitedFormat.PremierDraft, state.Event!.Format);
        Assert.Equal("FRA", state.Event.SetCode);
        Assert.Equal("00000000-0000-0000-0000-0000000000d1", state.DraftId);
        Assert.True(state.DetailedLogsEnabled);

        Assert.Equal(3, state.Picks.Count);
        Assert.Equal(new[] { 106230 }, state.Picks[0].Picked);
        Assert.Equal(14, state.Picks[0].Offered.Count);
        Assert.Equal((1, 3), (state.Picks[2].Pack, state.Picks[2].Pick));
        Assert.Equal([106230, 106249, 106264], state.Pool);

        // Po odeslání balíčku a odpovědi CreateMatch se hraje.
        Assert.Equal(DraftPhase.Playing, state.Phase);
        Assert.Null(state.CurrentPack);
        var snapshot = state.ToSnapshot();
        Assert.Equal(2, snapshot.Deck.Count);
        Assert.Single(snapshot.Sideboard);
    }

    [Fact]
    public void QuickDraft()
    {
        var state = Replay("quick_draft.log");

        Assert.Equal(LimitedFormat.QuickDraft, state.Event!.Format);
        Assert.Equal(DraftPhase.Drafting, state.Phase);
        Assert.Equal(2, state.Picks.Count);
        Assert.Equal((1, 1), (state.Picks[0].Pack, state.Picks[0].Pick));
        Assert.Equal(14, state.Picks[0].Offered.Count);
        Assert.Equal([106238, 106242], state.Pool);

        // Další balíček čeká na pick: P1p3, 12 karet.
        Assert.NotNull(state.CurrentPack);
        Assert.Equal((1, 3), (state.CurrentPack.Pack, state.CurrentPack.Pick));
        Assert.Equal(12, state.CurrentPack.Cards.Count);
    }

    [Fact]
    public void Sealed()
    {
        var state = Replay("sealed_join.log");

        Assert.Equal(LimitedFormat.Sealed, state.Event!.Format);
        Assert.Equal(DraftPhase.DeckBuilding, state.Phase);
        Assert.Equal(45, state.Pool.Count);
        Assert.Empty(state.Picks);
    }
}

public class DraftStateTests
{
    private static PackShown Pack(string draftId, int pack, int pick, params int[] cards) =>
        new(draftId, null, pack, pick, cards, 1);

    [Fact]
    public void ConstructedJoinKeepsLimitedEvent()
    {
        var state = new DraftState();
        state.Apply(new EventJoined("PremierDraft_FRA_20260929"));
        state.Apply(Pack("d1", 1, 1, 1, 2, 3));

        Assert.False(state.Apply(new EventJoined("Ladder")));
        Assert.Equal("PremierDraft_FRA_20260929", state.Event!.EventName);
        Assert.NotNull(state.CurrentPack);
    }

    [Fact]
    public void NewDraftIdStartsNewPool()
    {
        var state = new DraftState();
        state.Apply(new EventJoined("PremierDraft_FRA_20260929"));
        state.Apply(Pack("d1", 1, 1, 1, 2));
        state.Apply(new CardsPicked("d1", null, 1, 1, [1]));
        state.Apply(Pack("d2", 1, 1, 5, 6));

        Assert.Equal("d2", state.DraftId);
        Assert.Empty(state.Pool);
        Assert.Empty(state.Picks);
    }

    [Fact]
    public void DuplicatePickIsIgnored()
    {
        var state = new DraftState();
        state.Apply(Pack("d1", 1, 1, 1, 2));
        state.Apply(new CardsPicked("d1", null, 1, 1, [1]));
        Assert.False(state.Apply(new CardsPicked("d1", null, 1, 1, [1])));
        Assert.Equal([1], state.Pool);
    }

    [Fact]
    public void PickTwoAddsBothCards()
    {
        var state = new DraftState();
        state.Apply(new PackShown("d1", null, 1, 1, [1, 2, 3], 2));
        state.Apply(new CardsPicked("d1", null, 1, 1, [1, 3]));
        Assert.Equal([1, 3], state.Pool);
    }

    [Fact]
    public void RestoresDraftInProgressFromCourses()
    {
        var state = new DraftState();
        state.Apply(new CoursesReceived(
        [
            new CourseInfo("Ladder", "Complete", []),
            new CourseInfo("Sealed_DSK_20240924", "Complete", [1, 2, 3]),
            new CourseInfo("PremierDraft_FRA_20260929", "Draft", [10, 11]),
        ], IsFullList: true));

        Assert.Equal("PremierDraft_FRA_20260929", state.Event!.EventName);
        Assert.Equal(DraftPhase.Drafting, state.Phase);
        Assert.Equal([10, 11], state.Pool);
    }

    [Fact]
    public void EventMissingFromFullCourseListIsComplete()
    {
        var state = new DraftState();
        state.Apply(new EventJoined("PremierDraft_FRA_20260929"));
        state.Apply(new CoursesReceived([new CourseInfo("Ladder", "CreateMatch", [])], IsFullList: true));

        Assert.Equal(DraftPhase.Complete, state.Phase);
    }

    [Fact]
    public void NoActiveLimitedCourseLeavesStateIdle()
    {
        var state = new DraftState();
        Assert.False(state.Apply(new CoursesReceived([new CourseInfo("Jump_In_2024", "CreateMatch", [1, 2])], IsFullList: true)));
        Assert.Equal(DraftPhase.Idle, state.Phase);
        Assert.Null(state.Event);
    }

    [Fact]
    public void DraftCompletedSwitchesToDeckBuilding()
    {
        var state = new DraftState();
        state.Apply(new EventJoined("QuickDraft_FRA_20261017"));
        state.Apply(new DraftCompleted("QuickDraft_FRA_20261017", null, [1, 2, 3]));

        Assert.Equal(DraftPhase.DeckBuilding, state.Phase);
        Assert.Equal([1, 2, 3], state.Pool);
    }
}

public sealed class LogTailerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "MtgDraftHelperTests", Guid.NewGuid().ToString("N"), "Player.log");

    public LogTailerTests() => Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ReturnsOnlyCompletedLinesAndContinues()
    {
        File.WriteAllText(_path, "a\r\nb\npart");
        var tailer = new LogTailer(_path);

        Assert.Equal(["a", "b"], tailer.ReadNewLines());
        Assert.Empty(tailer.ReadNewLines());

        File.AppendAllText(_path, "ial\nnext\n");
        Assert.Equal(["partial", "next"], tailer.ReadNewLines());
    }

    [Fact]
    public void DetectsRewrittenFile()
    {
        File.WriteAllText(_path, "old run line 1\nold run line 2\n");
        var tailer = new LogTailer(_path);
        var rewritten = 0;
        tailer.Rewritten += () => rewritten++;
        tailer.ReadNewLines();

        File.WriteAllText(_path, "new\n");
        Assert.Equal(["new"], tailer.ReadNewLines());
        Assert.Equal(1, rewritten);
    }

    [Fact]
    public void DetectsRewriteWithSameOrLongerLength()
    {
        File.WriteAllText(_path, "AAAA\n");
        var tailer = new LogTailer(_path);
        var rewritten = 0;
        tailer.Rewritten += () => rewritten++;
        tailer.ReadNewLines();

        File.WriteAllText(_path, "BBBB\nCCCC\n");
        Assert.Equal(["BBBB", "CCCC"], tailer.ReadNewLines());
        Assert.Equal(1, rewritten);
    }

    [Fact]
    public void MissingFileReturnsNothing() =>
        Assert.Empty(new LogTailer(_path + ".missing").ReadNewLines());

    [Fact]
    public void MonitorFollowsAppendedDraftAndResetsOnRewrite()
    {
        var fixture = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "logs", "quick_draft.log"));
        File.WriteAllLines(_path, fixture.Take(9)); // do prvního stavu s balíčkem P1p1

        using var monitor = new LogMonitor(_path);
        var changes = 0;
        monitor.StateChanged += _ => changes++;

        Assert.True(monitor.Poll());
        Assert.Equal((1, 1), (monitor.Snapshot.CurrentPack!.Pack, monitor.Snapshot.CurrentPack.Pick));

        File.AppendAllLines(_path, fixture.Skip(9));
        Assert.True(monitor.Poll());
        Assert.Equal(2, monitor.Snapshot.Pool.Count);
        Assert.False(monitor.Poll()); // nic nového
        Assert.Equal(2, changes);

        // Nový běh Areny přepíše log – stav se zahodí.
        File.WriteAllLines(_path, ["DETAILED LOGS: ENABLED"]);
        monitor.Poll();
        Assert.Null(monitor.Snapshot.Event);
        Assert.Empty(monitor.Snapshot.Pool);
    }
}
