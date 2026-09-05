using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;

using PvpDuel.Core.SelfCheck;
using PvpDuel.Duel;
using PvpDuel.Encounters;
using PvpDuel.SelfCheck;

using Xunit;

using PvpDuel.Logging;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #15 (T3): duel encounter + duel entry patch + scope gate.
/// Everything here runs against the real game DLL (headless-constructible
/// types); <see cref="ModelDb"/> is reset per test since it is process-global.
/// </summary>
public class DuelEntryTests
{
    public DuelEntryTests()
    {
        // The game's Logger static ctor calls into Godot natives (crashes the
        // headless test host); the mod log switch keeps PvpDuelLog off that path.
        PvpDuelLog.Enabled = false;
        ModelDb.ResetForTest();
    }

    // ---- ModelId: registration identity + save serialization ----

    [Fact]
    public void DuelEncounter_ModelId_EncounterCategoryWithDistinctEntry()
    {
        var id = ModelDb.GetId<PvpDuelEncounter>();
        Assert.Equal("ENCOUNTER", id.Category);
        Assert.Equal("PVP_DUEL_ENCOUNTER", id.Entry);
    }

    [Fact]
    public void DuelEncounter_ModelId_SerializesAndRoundTrips()
    {
        var id = ModelDb.GetId<PvpDuelEncounter>();
        var text = id.ToString();
        Assert.DoesNotContain("_MODEL", text);
        var roundTripped = ModelId.Deserialize(text);
        Assert.Equal(id, roundTripped);
    }

    [Fact]
    public void DuelEncounter_RegisteredId_ResolvesThroughSaveUtil()
    {
        ModelDb.Inject(typeof(PvpDuelEncounter));
        var resolved = SaveUtil.EncounterOrDeprecated(ModelDb.GetId<PvpDuelEncounter>());
        Assert.IsType<PvpDuelEncounter>(resolved);
    }

    // ---- Encounter contract (RoomType boss, rewards off) ----

    [Fact]
    public void DuelEncounter_IsBossRoom()
    {
        var encounter = new PvpDuelEncounter();
        Assert.Equal(RoomType.Boss, encounter.RoomType);
    }

    [Fact]
    public void DuelEncounter_GivesNoRewards()
    {
        // Reward-off decision (§12.4, ShouldGiveRewards path): the vanilla gate
        // covers OfferRoomEndRewards (CombatRoom.cs:254), the reward screen
        // (NCombatUi.cs:366) and RewardsCmd (RewardsCmd.cs:26).
        var encounter = new PvpDuelEncounter();
        Assert.False(encounter.ShouldGiveRewards);
    }

    [Fact]
    public void DuelEncounter_DeclaresNoMonstersInPools()
    {
        var encounter = new PvpDuelEncounter();
        Assert.Empty(encounter.AllPossibleMonsters);
    }

    // ---- BossSwap (pure entry-patch decision) ----

    [Fact]
    public void BossSwap_WhenDuelRunActive_ReplacesBossWithDuelEncounter()
    {
        ModelDb.Inject(typeof(PvpDuelEncounter));
        EncounterModel boss = new KaiserCrabBoss();

        BossSwap.ReplaceIfDuelRun(ref boss, duelRunActive: true);

        var duel = Assert.IsType<PvpDuelEncounter>(boss);
        Assert.Equal("KAISER_CRAB_BOSS", BossSwap.LastReplacedVanillaEntry);
    }

    [Fact]
    public void BossSwap_WhenGateClosed_LeavesVanillaBoss()
    {
        EncounterModel boss = new KaiserCrabBoss();

        BossSwap.ReplaceIfDuelRun(ref boss, duelRunActive: false);

        Assert.IsType<KaiserCrabBoss>(boss);
    }

    [Fact]
    public void BossSwap_AlreadySwappedBoss_IsLeftAlone()
    {
        // Already a duel encounter: the swap must short-circuit before the
        // ModelDb lookup (no inject needed here).
        EncounterModel boss = new PvpDuelEncounter();

        BossSwap.ReplaceIfDuelRun(ref boss, duelRunActive: true);

        Assert.IsType<PvpDuelEncounter>(boss);
    }

    [Fact]
    public void BossSwap_UnregisteredModel_LogsAndLeavesBossVanilla()
    {
        // No ModelDb.Inject here (fresh db): the registration guard must
        // degrade gracefully instead of crashing act generation.
        ModelDb.ResetForTest();
        EncounterModel boss = new KaiserCrabBoss();

        BossSwap.ReplaceIfDuelRun(ref boss, duelRunActive: true);

        Assert.IsType<KaiserCrabBoss>(boss);
    }

    // ---- Scope gate (acceptance: non-duel rooms are always false) ----

    [Fact]
    public void DuelScope_WithoutRun_IsNeverADuelRoom()
    {
        Assert.False(DuelScope.IsDuelRoom);
        Assert.Null(DuelScope.Current);
    }

    [Fact]
    public void DuelScope_WithoutRun_GateForBossSwapIsClosed()
    {
        Assert.False(DuelScope.DuelRunActive);
    }
}
