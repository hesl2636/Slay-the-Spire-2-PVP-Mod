using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace PvpDuel.Save;

/// <summary>
/// Run identity for the save side channel (ticket #21, spec §4.3 "键：run 标识"):
/// the run's string seed. It survives the official save/load round-trip
/// (decomp RunRngSet.ToSerializable sets <c>SerializableRunRngSet.Seed =
/// StringSeed</c>, and LoadFromSerializable asserts it) and both ends of a
/// co-op session share it (same-seed session, spec §1), so payloads never
/// attach to the wrong run.
/// </summary>
public static class DuelRunKey
{
    /// <summary>Run key of a serialized run (load side); empty when the save carries no RNG set.</summary>
    public static string Of(SerializableRun save) => save.SerializableRng?.Seed ?? string.Empty;

    /// <summary>Run key of a materialized run state (restore / capture side).</summary>
    public static string Of(RunState state) => state.Rng.StringSeed;
}
