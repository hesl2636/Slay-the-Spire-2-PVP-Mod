using System.Reflection;

using HarmonyLib;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

using PvpDuel.Combat;
using PvpDuel.Logging;

using Xunit;

namespace PvpDuel.Core.Tests.SideFlip;

/// <summary>
/// Ticket #17 rework: the living contract pinning the three SideFlip guard
/// prefixes on async originals (<c>Creature.AfterAddedToRoom</c>,
/// <c>Creature.TakeTurn</c>, <c>CombatManager.AfterCreatureAdded(Creature,
/// CombatState)</c>) — same style as <see cref="TurnDriveGameSurfaceTests"/>
/// (static-reflection shape asserts + live Harmony against the loaded game
/// assembly; no game singletons are invoked).
///
/// The patch class itself is internal to the mod assembly, so every lookup
/// here goes through reflection off the public <see cref="SideFlipPolicy"/>
/// marker — mirroring how the game surfaces are probed below.
/// The pinned contract (probe-derived from the game's own 0Harmony 2.4.2):
/// prefixes on async originals must return <c>bool</c> and hand the skip
/// through <c>ref Task __result</c>. A bare Task?-returning prefix is rejected
/// at Patch() time — which previously crashed the SideFlip set at mod startup;
/// a regression now fails HERE, at test time, instead.
///
/// The skip branch itself is not reachable in the test process (it is gated on
/// <see cref="DuelScope.IsDuelRoom"/>, which needs a live duel room; faking
/// RunManager.State would race parallel test collections). It is pinned by
/// composition: SideFlipPolicyTests (when skip fires),
/// TurnDriveGameSurfaceTests.Harmony_SkippingAnAsyncOriginalNeedsACompletedResultTask
/// (what the shape does at runtime) and the Patch()/passthrough acceptance
/// here (that these exact prefixes carry that shape onto the real originals).
/// </summary>
public class SideFlipGameSurfaceTests
{
    public SideFlipGameSurfaceTests()
    {
        // The game Logger static ctor crashes the test host (0xC0000005).
        PvpDuelLog.Enabled = false;
    }

    private const string AfterAddedToRoomPrefix = "AfterAddedToRoomPrefix";
    private const string TakeTurnPrefix = "TakeTurnPrefix";
    private const string AfterCreatureAddedPrefix = "AfterCreatureAddedPrefix";

    // --- game surface shapes the `ref Task __result` binding depends on -----

    [Fact]
    public void GameDll_GuardOriginalsReturnPlainTask()
    {
        // `ref Task __result` only binds when the original returns exactly
        // Task (decomp Creature.cs:415/716, CombatManager.cs:1130 — all
        // async Task). A game update to Task<T> or void must fail here.
        var afterAdded = CreatureMethod(nameof(Creature.AfterAddedToRoom));
        var takeTurn = CreatureMethod(nameof(Creature.TakeTurn));
        var afterCreatureAdded = AfterCreatureAddedMethod();

        Assert.Equal(typeof(Task), afterAdded.ReturnType);
        Assert.Equal(typeof(Task), takeTurn.ReturnType);
        Assert.Equal(typeof(Task), afterCreatureAdded.ReturnType);

        Assert.False(afterAdded.IsStatic);
        Assert.False(takeTurn.IsStatic);
        Assert.True(afterCreatureAdded.IsStatic);
    }

    [Fact]
    public void SideFlipTaskPrefixes_KeepTheBoolRefTaskResultShape()
    {
        foreach (var name in new[] { AfterAddedToRoomPrefix, TakeTurnPrefix, AfterCreatureAddedPrefix })
        {
            var prefix = AccessTools.Method(PatchType(), name)
                ?? throw new MissingMethodException(PatchType().FullName, name);
            var result = prefix.GetParameters().SingleOrDefault(p => p.Name == "__result");
            Assert.True(result != null, $"{name} must declare __result to hand the skip through");
            Assert.True(
                result.ParameterType.IsByRef && result.ParameterType.GetElementType() == typeof(Task),
                $"{name}.__result must be `ref Task` — a bare Task return is rejected by 0Harmony 2.4.2");
        }
    }

    // --- live Harmony acceptance + passthrough behavior ----------------------

    [Fact]
    public async Task Harmony_SideFlipTaskPrefixesPatchTheirGameOriginals_PassThroughOutsideDuels()
    {
        var harmony = new Harmony("pvpduel.test.sideflip");
        var afterAdded = CreatureMethod(nameof(Creature.AfterAddedToRoom));
        var takeTurn = CreatureMethod(nameof(Creature.TakeTurn));
        var afterCreatureAdded = AfterCreatureAddedMethod();

        try
        {
            // The regression pin: 0Harmony 2.4.2 rejects bare Task?-returning
            // prefixes on async originals at Patch() time (ticket-#18 probe).
            // With the old `Task?` prefixes these calls threw — crashing the
            // SideFlip set at mod startup instead of failing here.
            harmony.Patch(afterAdded, prefix: Prefix(AfterAddedToRoomPrefix));
            harmony.Patch(takeTurn, prefix: Prefix(TakeTurnPrefix));
            harmony.Patch(afterCreatureAdded, prefix: Prefix(AfterCreatureAddedPrefix));

            // Outside duel rooms (DuelScope.IsDuelRoom is false in the test
            // process — RunManager has no run state) every guard must run
            // vanilla (acceptance #2: non-duel Creature behavior zero change).
            // A player-side creature is vanilla-safe for all three originals:
            // Creature(Player, hp, hp) is pure field assignment (no game
            // singletons) and Side=Player short-circuits both bodies.
            var creature = new Creature(null!, 10, 10);
            Assert.False(creature.IsPlayer);
            Assert.Equal(CombatSide.Player, creature.Side);

            // AfterAddedToRoom passthrough: vanilla body is a no-op for
            // Side=Player — the caller's await completes normally.
            var afterAddedCall = (Task)afterAdded.Invoke(creature, null)!;
            await afterAddedCall;
            Assert.True(afterAddedCall.IsCompletedSuccessfully);

            // TakeTurn passthrough: vanilla throws for anything that is not an
            // enemy monster ("Only enemy monsters can take automated turns.",
            // decomp Creature.cs:716-721) — the exact crash the guard exists
            // for. The prefix must NOT swallow it outside duel rooms.
            var takeTurnCall = (Task)takeTurn.Invoke(creature, null)!;
            var takeTurnError = await Assert.ThrowsAsync<InvalidOperationException>(() => takeTurnCall);
            Assert.Contains("automated turns", takeTurnError.Message);

            // AfterCreatureAdded passthrough: vanilla reads creature.AfterAddedToRoom
            // (patched above, no-op) then short-circuits on IsEnemy before any
            // CombatState access — completing even with a null state.
            var afterCreatureAddedCall = (Task)afterCreatureAdded.Invoke(null, [creature, null])!;
            await afterCreatureAddedCall;
            Assert.True(afterCreatureAddedCall.IsCompletedSuccessfully);
        }
        finally
        {
            harmony.Unpatch(afterAdded, HarmonyPatchType.Prefix, harmony.Id);
            harmony.Unpatch(takeTurn, HarmonyPatchType.Prefix, harmony.Id);
            harmony.Unpatch(afterCreatureAdded, HarmonyPatchType.Prefix, harmony.Id);
        }
    }
    private static Type PatchType() =>
        typeof(SideFlipPolicy).Assembly.GetType("PvpDuel.Combat.CreatureSideFlipPatch", throwOnError: true)
            ?? throw new MissingMethodException("PvpDuel", "PvpDuel.Combat.CreatureSideFlipPatch");

    private static HarmonyMethod Prefix(string name) =>
        new(AccessTools.Method(PatchType(), name)
            ?? throw new MissingMethodException(PatchType().FullName, name));


    private static MethodInfo CreatureMethod(string name) =>
        AccessTools.Method(typeof(Creature), name)
            ?? throw new MissingMethodException(typeof(Creature).FullName, name);

    private static MethodInfo AfterCreatureAddedMethod() =>
        AccessTools.Method(typeof(CombatManager), "AfterCreatureAdded", [typeof(Creature), typeof(CombatState)])
            ?? throw new MissingMethodException(typeof(CombatManager).FullName, "AfterCreatureAdded(Creature, CombatState)");
}
