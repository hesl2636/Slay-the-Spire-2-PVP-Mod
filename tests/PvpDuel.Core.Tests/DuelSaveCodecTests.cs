using PvpDuel.Core.Duel;
using PvpDuel.Core.Save;

using Xunit;
namespace PvpDuel.Core.Tests;

public class DuelSaveCodecTests
{
    private static DuelSavePayload SamplePayload() => new(
        SaveSchema.CurrentVersion,
        "run-abc-123",
        [
            new SavedBranchEntry(1001, new BranchStateDto(1, [2, 3], false)),
            new SavedBranchEntry(2002, new BranchStateDto(1, [5, 3], true)),
        ],
        [
            new DuelResultDto(1, 1001, 2002, nameof(DuelEndReason.Kill)),
        ],
        [
            new AncientPickDto(1, 1001, "THE_ANCIENT_A"),
        ]);

    [Fact]
    public void Serialize_Then_Read_RoundTrips()
    {
        var payload = SamplePayload();
        var json = DuelSaveCodec.Serialize(payload);

        var (read, status) = DuelSaveCodec.TryRead(json);
        Assert.Equal(DuelSaveReadStatus.Ok, status);
        Assert.NotNull(read);
        Assert.Equal(payload, read);
        Assert.Equal(SaveSchema.CurrentVersion, read!.Schema);
    }

    [Fact]
    public void Read_NullOrGarbage_IsCorrupt()
    {
        Assert.Equal(DuelSaveReadStatus.Corrupt, DuelSaveCodec.TryRead(null).Status);
        Assert.Equal(DuelSaveReadStatus.Corrupt, DuelSaveCodec.TryRead("").Status);
        Assert.Equal(DuelSaveReadStatus.Corrupt, DuelSaveCodec.TryRead("   ").Status);
        Assert.Equal(DuelSaveReadStatus.Corrupt, DuelSaveCodec.TryRead("{not json").Status);
        Assert.Equal(DuelSaveReadStatus.Corrupt, DuelSaveCodec.TryRead("[]").Status);
    }

    [Fact]
    public void Read_StructurallyWrongTypes_IsCorrupt()
    {
        Assert.Equal(DuelSaveReadStatus.Corrupt, DuelSaveCodec.TryRead("""{"schema":1,"runKey":"x","branches":[{"playerNetId":"oops","state":null}],"duelResults":[],"ancientPicks":[]}""").Status);
        Assert.Equal(DuelSaveReadStatus.Corrupt, DuelSaveCodec.TryRead("""{"schema":1,"runKey":"x","duelResults":[{"actIndex":1,"winnerNetId":1,"loserNetId":2,"reason":"NotAReason"}],"branches":[],"ancientPicks":[]}""").Status);
    }

    [Fact]
    public void Read_VersionMismatch_IsReported_NotOk()
    {
        var payload = SamplePayload() with { Schema = SaveSchema.CurrentVersion + 1 };
        var (read, status) = DuelSaveCodec.TryRead(DuelSaveCodec.Serialize(payload));
        Assert.Equal(DuelSaveReadStatus.VersionMismatch, status);
        Assert.NotNull(read); // returned so the caller can Warn with context
        Assert.Equal(2, read!.Schema);
    }

    [Fact]
    public void ReadOlderSchema_IsAlsoMismatch()
    {
        var payload = SamplePayload() with { Schema = 0 };
        Assert.Equal(DuelSaveReadStatus.VersionMismatch, DuelSaveCodec.TryRead(DuelSaveCodec.Serialize(payload)).Status);
    }

    [Fact]
    public void ReadOrThrow_MapsStatuses()
    {
        var ok = DuelSaveCodec.ReadOrThrow(DuelSaveCodec.Serialize(SamplePayload()));
        Assert.Equal(SamplePayload(), ok);
        Assert.Throws<FormatException>(() => DuelSaveCodec.ReadOrThrow("garbage{"));
        var future = SamplePayload() with { Schema = SaveSchema.CurrentVersion + 5 };
        Assert.Throws<FormatException>(() => DuelSaveCodec.ReadOrThrow(DuelSaveCodec.Serialize(future)));
    }

    [Fact]
    public void Empty_Payload_HasSchemaOne()
    {
        var empty = DuelSavePayload.Empty("run-x");
        Assert.Equal(SaveSchema.CurrentVersion, empty.Schema);
        Assert.Equal("run-x", empty.RunKey);
        Assert.Empty(empty.Branches);
        Assert.Empty(empty.DuelResults);
        Assert.Empty(empty.AncientPicks);
    }

    [Fact]
    public void Serialize_EmptyRunKey_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DuelSaveCodec.Serialize(SamplePayload() with { RunKey = "" }));
    }

    [Fact]
    public void Materialize_RebuildsDomainState_AndDropsBadEntries()
    {
        var payload = SamplePayload();
        payload = payload with
        {
            Branches = [.. payload.Branches, new SavedBranchEntry(3003, new BranchStateDto(1, [], false))],
            DuelResults = [.. payload.DuelResults, new DuelResultDto(2, 1, 2, "Nope")],
        };

        var (branches, results) = DuelSaveCodec.Materialize(payload);

        Assert.Equal(2, branches.Count);
        Assert.Single(results);
        Assert.Equal(DuelEndReason.Kill, results[0].Reason);
    }

    [Fact]
    public void BranchStateDto_Coord_MustHaveTwoEntries()
    {
        var dto = new BranchStateDto(1, [1], false);
        Assert.Throws<FormatException>(() => dto.ToBranchState());
    }
}
