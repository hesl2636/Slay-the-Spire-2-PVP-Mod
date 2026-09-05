using PvpDuel.Core.Diagnostics;
using PvpDuel.Core.Duel;
using PvpDuel.Core.Save;

using Xunit;

namespace PvpDuel.Core.Tests.Save;

/// <summary>
/// Ticket #21 (T9): the runtime-state ↔ payload mapping behind the save side
/// channel. Capture must produce a schema:1 payload that round-trips through
/// the codec, and Restore must make the payload the whole truth (replace, not
/// merge) while dropping individual corrupt entries.
/// </summary>
public class DuelSaveMapperTests
{
    private sealed class CapturingLog : IModLog
    {
        public List<string> Warnings { get; } = [];

        public void Info(string message) { }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message) { }
    }

    private static BranchState State(int act, int col, int row, bool bossWait = false) =>
        new(act, new MapCoord(col, row), bossWait);

    private static BranchTable PopulatedTable(int act = 1)
    {
        var table = new BranchTable();
        table.Reset(act);
        table.Set(1001, State(act, 2, 3));
        table.Set(2002, State(act, 5, 3, bossWait: true));
        return table;
    }

    private static DuelHistory PopulatedHistory() =>
        RecordResults(new DuelHistory(), new DuelResult(1, 1001, 2002, DuelEndReason.Kill));

    private static DuelHistory RecordResults(DuelHistory history, params DuelResult[] results)
    {
        foreach (var result in results)
        {
            history.Record(result);
        }

        return history;
    }

    private static (DuelSavePayload Payload, string Json) CapturedJson(
        BranchTable? table, DuelHistory history, IReadOnlyList<AncientPickDto> picks)
    {
        var payload = DuelSaveMapper.Capture("seed-xyz", "cfg-hash-1", table, history, picks);
        return (payload, DuelSaveCodec.Serialize(payload));
    }

    [Fact]
    public void Capture_RoundTrips_ThroughCodec_AndRestore()
    {
        var (payload, json) = CapturedJson(PopulatedTable(), PopulatedHistory(),
            [new AncientPickDto(1, 1001, "ANCIENT.A")]);

        var (read, status) = DuelSaveCodec.TryRead(json);
        Assert.Equal(DuelSaveReadStatus.Ok, status);

        var freshTable = new BranchTable();
        var freshHistory = new DuelHistory();
        List<AncientPickDto> freshPicks = [];
        DuelSaveMapper.Restore(read!, freshTable, freshHistory, freshPicks);

        Assert.Equal(1, freshTable.ActIndex);
        Assert.True(freshTable.TryGet(1001, out var mine));
        Assert.Equal(State(1, 2, 3), mine);
        Assert.True(freshTable.TryGet(2002, out var theirs));
        Assert.Equal(State(1, 5, 3, bossWait: true), theirs);
        Assert.Single(freshHistory.Results);
        Assert.True(freshHistory.Results[0].Mirrors(new DuelResult(1, 1001, 2002, DuelEndReason.Kill)));
        var pick = Assert.Single(freshPicks);
        Assert.Equal(new AncientPickDto(1, 1001, "ANCIENT.A"), pick);
    }

    [Fact]
    public void Capture_NullTable_ProducesPayloadWithoutBranches()
    {
        var (payload, json) = CapturedJson(null, PopulatedHistory(), []);

        Assert.Empty(payload.Branches);
        var (read, status) = DuelSaveCodec.TryRead(json);
        Assert.Equal(DuelSaveReadStatus.Ok, status);
        Assert.Empty(read!.Branches);
    }

    [Fact]
    public void Capture_OrdersBranches_ByNetId_AndCarriesConfigHash()
    {
        var table = new BranchTable();
        table.Reset(2);
        table.Set(900, State(2, 1, 1));
        table.Set(42, State(2, 0, 1));

        var payload = DuelSaveMapper.Capture("seed", "hash", table, new DuelHistory(), []);

        Assert.Equal([42, 900], payload.Branches.Select(b => b.PlayerNetId));
        Assert.Equal("hash", payload.ConfigHash);
    }

    [Fact]
    public void IsEmpty_TrueOnlyWhenNoContent()
    {
        Assert.True(DuelSaveMapper.IsEmpty(DuelSavePayload.Empty("seed")));
        var (_, json) = CapturedJson(null, PopulatedHistory(), []);
        Assert.False(DuelSaveMapper.IsEmpty(DuelSaveCodec.ReadOrThrow(json)));
    }

    [Fact]
    public void Restore_ReplacesPreviousState_Wholesale()
    {
        var staleTable = PopulatedTable(act: 9);
        var staleHistory = RecordResults(new DuelHistory(), new DuelResult(9, 1, 2, DuelEndReason.Forfeit));
        List<AncientPickDto> stalePicks = [new AncientPickDto(9, 1, "ANCIENT.STALE")];

        var (payload, _) = CapturedJson(PopulatedTable(), PopulatedHistory(),
            [new AncientPickDto(1, 1001, "ANCIENT.A")]);

        DuelSaveMapper.Restore(payload, staleTable, staleHistory, stalePicks);

        // Stale act-9 content is gone: the payload is the whole truth.
        Assert.Equal(1, staleTable.ActIndex);
        Assert.True(staleTable.TryGet(1001, out var restored));
        Assert.Equal(1, restored.ActIndex);
        Assert.Single(staleHistory.Results);
        Assert.Equal(1, staleHistory.Results[0].ActIndex);
        Assert.Equal("ANCIENT.A", Assert.Single(stalePicks).AncientModelId);
    }

    [Fact]
    public void Restore_DropsCorruptEntries_KeepsTheRest()
    {
        var json = """
            {"schema":1,"runKey":"seed","branches":[
                {"playerNetId":1001,"state":{"actIndex":1,"coord":[2,3],"inBossWait":false}},
                {"playerNetId":2002,"state":{"actIndex":1,"coord":[5],"inBossWait":false}}],
             "duelResults":[
                {"actIndex":1,"winnerNetId":1001,"loserNetId":2002,"reason":"Kill"}],
             "ancientPicks":[{"actIndex":1,"playerNetId":1001,"ancientModelId":"ANCIENT.A"}]}
            """;
        var payload = DuelSaveCodec.ReadOrThrow(json);
        var log = new CapturingLog();
        var table = new BranchTable();
        var history = new DuelHistory();
        List<AncientPickDto> picks = [];

        DuelSaveMapper.Restore(payload, table, history, picks, log);

        Assert.True(table.TryGet(1001, out _));
        Assert.False(table.TryGet(2002, out _), "bad-coord entry must be dropped");
        Assert.Single(history.Results);
        Assert.Equal(1, history.Results[0].ActIndex);
        Assert.Single(log.Warnings);
    }
}
