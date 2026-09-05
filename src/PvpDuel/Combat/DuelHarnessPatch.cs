using System.Reflection;

using HarmonyLib;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Duel;
using PvpDuel.Encounters;
using PvpDuel.Logging;

namespace PvpDuel.Combat;

/// <summary>
/// Ticket #17 (T5) patch set "DuelHarness" — single-machine fake-opponent duel
/// (test-only, all three patches no-op unless
/// <see cref="DuelHarness.DebugSinglePlayerDuel"/> is on):
///
/// 1. RoomSet.Boss setter second prefix: with the debug flag on, singleplayer
///    runs swap the act boss for the duel encounter too (vanilla 2-player path
///    is untouched — RoomSetBossPatch's own prefix already swapped, then this
///    one is a no-op on a PvpDuelEncounter value).
/// 2. PvpDuelEncounter.GenerateMonsters postfix: the fake duel fights the
///    opposing player creature, so the placeholder boss monsters are dropped
///    (empty MonstersWithSlots) in harness duel rooms.
/// 3. CombatRoom.StartCombat prefix: injects the fake opponent creature into
///    the duel CombatState BEFORE combat setup, so NCombatRoom's
///    CreateEnemyNodes spawns a real, targetable node on the enemy side.
///
/// Not attribute-patched: applied by ModEntry only after the self-check.
/// </summary>
internal static class DuelHarnessPatch
{
    public const string PatchSetId = "DuelHarness";

    /// <summary>Applies the set; throws when a target is gone (caller disables the set).</summary>
    public static void Apply(Harmony harmony)
    {
        var bossSetter = AccessTools.PropertySetter(typeof(RoomSet), nameof(RoomSet.Boss))
            ?? throw new MissingMethodException(typeof(RoomSet).FullName, "set_Boss");
        harmony.Patch(bossSetter, prefix: Patch(nameof(BossSetterPrefix)));

        var generateMonsters = AccessTools.DeclaredMethod(typeof(PvpDuelEncounter), "GenerateMonsters")
            ?? throw new MissingMethodException(typeof(PvpDuelEncounter).FullName, "GenerateMonsters");
        harmony.Patch(generateMonsters, postfix: Patch(nameof(GenerateMonstersPostfix)));

        var startCombat = AccessTools.Method(typeof(CombatRoom), "StartCombat", [typeof(IRunState)])
            ?? throw new MissingMethodException(typeof(CombatRoom).FullName, "StartCombat(IRunState)");
        harmony.Patch(startCombat, prefix: Patch(nameof(StartCombatPrefix)));

        PvpDuelLog.Info("DuelHarness patch set applied (boss swap + no-monster duel + fake-opponent injection).");
    }

    private static HarmonyMethod Patch(string name)
    {
        var method = typeof(DuelHarnessPatch).GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(DuelHarnessPatch).FullName, name);
        return new HarmonyMethod(method);
    }

    private static void BossSetterPrefix(ref EncounterModel value)
    {
        try
        {
            if (!DuelHarness.DebugSinglePlayerDuel || value == null || value is PvpDuelEncounter)
            {
                return;
            }
            BossSwap.ReplaceIfDuelRun(ref value, duelRunActive: true);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"harness boss swap failed: {ex}");
        }
    }

    private static void GenerateMonstersPostfix(ref IReadOnlyList<(MonsterModel, string?)> __result)
    {
        if (!DuelHarness.DebugSinglePlayerDuel || !DuelScope.IsDuelRoom)
        {
            return;
        }
        __result = [];
        PvpDuelLog.Info("harness: placeholder monsters suppressed (fake-opponent duel).");
    }

    private static void StartCombatPrefix(CombatRoom __instance)
    {
        try
        {
            if (!DuelHarness.DebugSinglePlayerDuel || !DuelScope.IsDuelRoom)
            {
                return;
            }
            DuelHarness.InjectFakeOpponent(__instance.CombatState);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"harness fake-opponent injection failed: {ex}");
        }
    }
}
