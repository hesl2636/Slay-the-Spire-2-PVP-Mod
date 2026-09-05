using PvpDuel.Core.Duel;
using System.Text.Json.Serialization;

namespace PvpDuel.Core.Save;

/// <summary>Current side-channel schema version. Bump on incompatible changes.</summary>
public static class SaveSchema
{
    public const int CurrentVersion = 1;
}

/// <summary>Per-player branch entry persisted in the side channel.</summary>
public sealed record SavedBranchEntry(
    [property: JsonPropertyName("playerNetId")] ulong PlayerNetId,
    [property: JsonPropertyName("state")] BranchStateDto State);

/// <summary>JSON-shaped branch state (coord split into col/row for stability).</summary>
public sealed record BranchStateDto(
    [property: JsonPropertyName("actIndex")] int ActIndex,
    [property: JsonPropertyName("coord")] int[] Coord,
    [property: JsonPropertyName("inBossWait")] bool InBossWait)
{
    public static BranchStateDto From(BranchState state) =>
        new(state.ActIndex, [state.Coord.Col, state.Coord.Row], state.InBossWait);

    public BranchState ToBranchState()
    {
        if (Coord is not { Length: 2 })
        {
            throw new FormatException($"coord must be [col,row], got length {Coord.Length}.");
        }

        return new BranchState(ActIndex, new MapCoord(Coord[0], Coord[1]), InBossWait);
    }

    // Value equality over the coord array (record default would compare references).
    public bool Equals(BranchStateDto? other) =>
        other is not null
        && ActIndex == other.ActIndex
        && InBossWait == other.InBossWait
        && Coord.AsSpan().SequenceEqual(other.Coord);


    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActIndex);
        hash.Add(InBossWait);
        foreach (var coord in Coord)
        {
            hash.Add(coord);
        }

        return hash.ToHashCode();
    }

}

/// <summary>
/// Root payload of the mod save side-channel. Stored as a separate JSON blob
/// keyed by run identity; the official <c>SerializableRun</c> is never touched.
/// </summary>
public sealed record DuelSavePayload(
    [property: JsonPropertyName("schema")] int Schema,
    [property: JsonPropertyName("runKey")] string RunKey,
    [property: JsonPropertyName("branches")] IReadOnlyList<SavedBranchEntry> Branches,
    [property: JsonPropertyName("duelResults")] IReadOnlyList<DuelResultDto> DuelResults,
    [property: JsonPropertyName("ancientPicks")] IReadOnlyList<AncientPickDto> AncientPicks)
{
    public static DuelSavePayload Empty(string runKey) =>
        new(SaveSchema.CurrentVersion, runKey, [], [], []);

    // Value equality over the entry collections (record default would compare list references).
    public bool Equals(DuelSavePayload? other) =>
        other is not null
        && Schema == other.Schema
        && RunKey == other.RunKey
        && Branches.SequenceEqual(other.Branches)
        && DuelResults.SequenceEqual(other.DuelResults)
        && AncientPicks.SequenceEqual(other.AncientPicks);

    public override int GetHashCode() => HashCode.Combine(Schema, RunKey);

}

/// <summary>JSON-shaped duel result.</summary>
public sealed record DuelResultDto(
    [property: JsonPropertyName("actIndex")] int ActIndex,
    [property: JsonPropertyName("winnerNetId")] ulong WinnerNetId,
    [property: JsonPropertyName("loserNetId")] ulong LoserNetId,
    [property: JsonPropertyName("reason")] string Reason)
{
    public static DuelResultDto From(DuelResult result) => new(
        result.ActIndex, result.WinnerNetId, result.LoserNetId, result.Reason.ToString());

    public DuelResult ToDuelResult() =>
        new(ActIndex, WinnerNetId, LoserNetId, ParseReason(Reason));

    private static DuelEndReason ParseReason(string reason) =>
        Enum.TryParse<DuelEndReason>(reason, ignoreCase: false, out var parsed)
            ? parsed
            : throw new FormatException($"Unknown DuelEndReason '{reason}'.");
}

/// <summary>JSON-shaped Ancient body pick (winner chose first, loser must not repeat).</summary>
public sealed record AncientPickDto(
    [property: JsonPropertyName("actIndex")] int ActIndex,
    [property: JsonPropertyName("playerNetId")] ulong PlayerNetId,
    [property: JsonPropertyName("ancientModelId")] string AncientModelId);
