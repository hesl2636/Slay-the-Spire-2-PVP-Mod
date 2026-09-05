using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using PvpDuel.Logging;

namespace PvpDuel.Encounters;

/// <summary>
/// The duel encounter: the product of the entry patch (boss room swap) and the
/// basis of every scope gate. Registered into <see cref="ModelDb"/> at mod init
/// (inject is idempotent — v0.111's <c>ModelDb.Init</c> also auto-scans mod
/// assemblies for <c>AbstractModel</c> subtypes).
///
/// Stateless by design: the duel does not wrap the rolled vanilla boss instance.
/// Placeholder monsters (this ticket) are derived deterministically from the
/// current act's vanilla boss pool — identical on both ends under every flow
/// (new run, save/load, session restart), which is the lockstep requirement.
/// The combat ticket (T6) replaces monster generation with the real player duel.
/// </summary>
public class PvpDuelEncounter : EncounterModel
{
    /// <summary>Duel rooms use the boss room layout.</summary>
    public override RoomType RoomType => RoomType.Boss;

    /// <summary>
    /// Reward-off decision (spec §12.4, option A): the vanilla gate already
    /// covers every reward surface — CombatRoom.cs:254 skips
    /// <c>OfferRoomEndRewards</c>, NCombatUi.cs:366 skips the reward screen,
    /// RewardsCmd.cs:26 yields an empty reward set. No extra patch needed.
    /// </summary>
    public override bool ShouldGiveRewards => false;
    /// <summary>
    /// Duels fight the opposing player creature, not generated monsters (T6).
    /// Kept empty: this list feeds encounter pools/scaling, never the duel room.
    /// </summary>
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [];

    /// <summary>
    /// Forward the placeholder boss's map-node art so the duel boss point renders
    /// with vanilla boss assets instead of missing <c>pvp_duel_encounter</c> ones.
    /// </summary>
    public override string BossNodePath =>
        ResolvePlaceholderSource()?.BossNodePath ?? base.BossNodePath;

    public override MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSkeletonDataResource? BossNodeSpineResource =>
        ResolvePlaceholderSource()?.BossNodeSpineResource;

    /// <summary>Forward the vanilla boss track while the duel is a placeholder combat.</summary>
    public override string CustomBgm => ResolvePlaceholderSource()?.CustomBgm ?? "";

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        var source = ResolvePlaceholderSource();
        var runState = RunManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("PvpDuelEncounter.GenerateMonsters outside a run.");
        if (source == null)
        {
            PvpDuelLog.Warn("duel encounter: no vanilla boss found for the current act; generating no enemies.");
            return [];
        }

        // Generate through a mutable clone of the vanilla boss so its per-encounter
        // RNG (seeded from the shared run state) and slot assignment stay intact.
        var mutableSource = source.ToMutable();
        mutableSource.GenerateMonstersWithSlots(runState);
        PvpDuelLog.Info($"duel encounter: placeholder enemies from {source.Id.Entry} ({mutableSource.MonstersWithSlots.Count} slot(s)).");
        return mutableSource.MonstersWithSlots;
    }

    /// <summary>
    /// Deterministic placeholder source: the current act's first non-deprecated
    /// vanilla boss encounter. Both ends resolve the same value from canonical
    /// act data — no per-instance state that could diverge across save/load.
    /// </summary>
    internal static EncounterModel? ResolvePlaceholderSource()
    {
        var act = RunManager.Instance.DebugOnlyGetState()?.Act;
        return act?.AllBossEncounters.FirstOrDefault(e => e is not DeprecatedEncounter);
    }
}
