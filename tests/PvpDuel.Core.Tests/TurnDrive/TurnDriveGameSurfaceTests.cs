using System.Reflection;

using HarmonyLib;

using PvpDuel.Combat;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;

using Xunit;

namespace PvpDuel.Core.Tests.TurnDrive;

/// <summary>
/// Ticket #18 (T6): static-reflection asserts over the game surfaces the
/// opponent-turn driver touches — the Traverse targets (private methods and
/// internal CombatTurnState members), the §12.3 synchronizer surface and the
/// Harmony 2.4.2 async-replacement contract the driver is built on. No game
/// singletons are touched (the game Logger static ctor 0xC0000005 rule).
///
/// CombatTurnState is internal, so member shapes are matched by simple
/// parameter type names instead of typeof().
/// </summary>
public class TurnDriveGameSurfaceTests
{
    private const string CombatManagerType = "MegaCrit.Sts2.Core.Combat.CombatManager";

    // --- catalog target methods exist with the exact shapes -----------------

    [Fact]
    public void GameDll_DriverReplacementTargetExists()
    {
        Assert.True(HasMethod(typeof(CombatManager), "ExecuteEnemyTurn", "CombatTurnState", "Func"));
        var method = GetMethod(typeof(CombatManager), "ExecuteEnemyTurn", "CombatTurnState", "Func");
        Assert.NotNull(method);
        Assert.True(method!.IsPrivate);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public void GameDll_VanillaFanOutGateTargetsExist()
    {
        Assert.True(HasMethod(typeof(CombatManager), "SetupPlayerTurn",
            "CombatTurnState", "Player", "HookPlayerChoiceContext"));
        Assert.True(HasMethod(typeof(CombatManager), "RunAutoPrePlayPhase",
            "CombatTurnState", "HookPlayerChoiceContext", "Task", "Player"));
        Assert.True(HasMethod(typeof(CombatManager), "EndPlayerTurnPhaseOneInternal", "CombatTurnState"));
        Assert.True(HasMethod(typeof(CombatManager), "EndPlayerTurnPhaseTwoInternal", "CombatTurnState"));
        Assert.True(HasMethod(typeof(CombatManager), "AllPlayersReadyToEndTurn", "CombatTurnState"));
        Assert.True(HasMethod(typeof(CombatManager), "SetReadyToBeginEnemyTurn", "Player", "Func"));
        Assert.True(HasMethod(typeof(CombatManager), "SetUpCombat", "CombatState"));
    }

    [Fact]
    public void GameDll_TraverseTargetsExist()
    {
        // Private methods the driver invokes instead of reimplementing them.
        Assert.True(HasMethod(typeof(CombatManager), "EndEnemyTurn", "CombatTurnState"));
        Assert.True(HasMethod(typeof(CombatManager), "DoTurnEnd",
            "CombatTurnState", "Player", "PlayerChoiceContext"));
        Assert.True(HasMethod(typeof(CombatManager), "FlushPlayerHand",
            "CombatTurnState", "Player", "HookPlayerChoiceContext"));
        Assert.True(HasMethod(typeof(CombatManager), "WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction",
            "CombatTurnState"));
        Assert.True(HasMethod(typeof(CombatManager), "CheckWinCondition", "CombatTurnState"));
        Assert.True(HasMethod(typeof(CombatManager), "CheckForEmptyHand",
            "CombatTurnState", "PlayerChoiceContext", "Player"));
    }

    [Fact]
    public void GameDll_TurnStateMembersExistForTraverse()
    {
        // CombatTurnState is internal; the driver reaches these via Traverse.
        var type = typeof(CombatManager).Assembly.GetType("MegaCrit.Sts2.Core.Combat.CombatTurnState");
        Assert.NotNull(type);
        Assert.NotNull(AccessTools.Property(type!, "State"));
        Assert.NotNull(AccessTools.Property(type!, "Ct"));
        Assert.NotNull(AccessTools.Property(type!, "IsInProgress"));
        Assert.NotNull(AccessTools.Property(type!, "IsLive"));
        Assert.NotNull(AccessTools.Property(type!, "PlayersReadyToEndTurn"));
        Assert.NotNull(AccessTools.Property(type!, "PlayersReadyToBeginEnemyTurn"));
        Assert.NotNull(AccessTools.Property(type!, "BeginEnemyTurnSignalSource"));
    }

    // --- §12.3 verdict surface: the synchronizer gate -----------------------

    [Fact]
    public void GameDll_SetCombatStateTakesTheEnum_NoSideGate()
    {
        // §12.3: ActionQueueSynchronizer.SetCombatState(ActionSynchronizerCombatState)
        // has no CurrentSide parameter or check (decomp :80-138) — the driver
        // explicitly requests PlayPhase for the opponent's play phase, which is
        // the only gate that matters (RequestEnqueue defers CombatPlayPhaseOnly
        // actions while in NotPlayPhase, decomp :148-153). No extra patch needed.
        var method = AccessTools.Method(typeof(ActionQueueSynchronizer),
            nameof(ActionQueueSynchronizer.SetCombatState), [typeof(ActionSynchronizerCombatState)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);
    }

    [Fact]
    public void GameDll_SynchronizerCombatStateHasAllPhasesTheDriverNeeds()
    {
        // Members the driver transitions through (decomp enum :3-27).
        Assert.Equal(0, (int)ActionSynchronizerCombatState.NotInCombat);
        Assert.Equal(2, (int)ActionSynchronizerCombatState.PlayPhase);
        Assert.Equal(3, (int)ActionSynchronizerCombatState.EndTurnPhaseOne);
        Assert.Equal(4, (int)ActionSynchronizerCombatState.NotPlayPhase);
    }

    [Fact]
    public void GameDll_HookSurfacesForTheSetupMirrorExist()
    {
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.ShouldPlayerResetEnergy),
            [typeof(ICombatState), typeof(Player)]));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.AfterEnergyReset)));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.BeforeHandDraw)));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.ModifyHandDraw)));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.AfterModifyingHandDraw)));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.AfterPlayerTurnStart)));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.AfterAutoPrePlayPhaseEntered)));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.AfterAutoPostPlayPhaseEntered)));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.BeforeSideTurnEnd)));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.BeforeFlush)));
        Assert.NotNull(AccessTools.Method(typeof(Hook), nameof(Hook.AfterSideTurnEnd)));
    }

    [Fact]
    public void GameDll_CardPileCmdDrawAndPublicReadinessSurfaceExist()
    {
        Assert.NotNull(AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
            [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)]));
        // The driver's wait uses these public members — no internals needed.
        Assert.NotNull(AccessTools.Method(typeof(CombatManager), nameof(CombatManager.IsPlayerReadyToEndTurn),
            [typeof(Player)]));
        Assert.NotNull(AccessTools.Property(typeof(CombatManager), nameof(CombatManager.IsEnding)));
        Assert.NotNull(typeof(CombatManager).GetEvent(nameof(CombatManager.PlayerEndedTurn)));
    }

    // --- the Harmony contract the driver is built on (probe-derived) --------

    [Fact]
    public async Task Harmony_ReplacingAnAsyncOriginalRequiresRefTaskResult()
    {
        // Derived from the ticket-#18 probe against the game's own 0Harmony
        // 2.4.2: a prefix on an async original must return bool and hand the
        // replacement through `ref Task __result` (return false). A bare
        // Task-returning prefix is REJECTED at Patch() time, and a bool-false
        // skip without __result yields a null task (caller await = NRE).
        // This test pins that contract: the driver's replacement pattern must
        // keep working after game/Harmony updates.
        var harmony = new Harmony("pvpduel.test.turndrive");
        var original = AccessTools.Method(typeof(AsyncSubject), nameof(AsyncSubject.Op));
        Assert.NotNull(original);

        try
        {
            harmony.Patch(original, prefix: new HarmonyMethod(typeof(HarmonyContractPrefix), nameof(HarmonyContractPrefix.Replace)));

            AsyncSubject.RanOriginal = false;
            HarmonyContractPrefix.ReplacementRan = false;
            var call = new AsyncSubject().Op();

            Assert.False(AsyncSubject.RanOriginal, "original must be skipped by the replacement prefix");
            Assert.False(call.IsCompleted, "caller must receive the replacement task, not a completed one");
            HarmonyContractPrefix.Gate.TrySetResult();
            await call;
            Assert.True(HarmonyContractPrefix.ReplacementRan);
        }
        finally
        {
            harmony.Unpatch(original!, HarmonyPatchType.Prefix, harmony.Id);
        }
    }

    [Fact]
    public async Task Harmony_SkippingAnAsyncOriginalNeedsACompletedResultTask()
    {
        // The SetupPlayerTurn/RunAutoPrePlayPhase guard shape: skip the original
        // but keep the caller's await well-defined (__result = Task.CompletedTask).
        var harmony = new Harmony("pvpduel.test.turndrive.skip");
        var original = AccessTools.Method(typeof(AsyncSubject), nameof(AsyncSubject.Op));
        Assert.NotNull(original);

        try
        {
            harmony.Patch(original, prefix: new HarmonyMethod(typeof(HarmonyContractPrefix), nameof(HarmonyContractPrefix.Skip)));

            AsyncSubject.RanOriginal = false;
            var call = new AsyncSubject().Op();
            await call; // must not NRE on a null task

            Assert.False(AsyncSubject.RanOriginal);
            Assert.True(call.IsCompletedSuccessfully);
        }
        finally
        {
            harmony.Unpatch(original!, HarmonyPatchType.Prefix, harmony.Id);
        }
    }

    [Fact]
    public void TurnDriveHarnessCmd_IsAutoRegistrableAndRejectsUnknownSubcommands()
    {
        // The game loads console commands via Activator.CreateInstance on
        // parameterless-ctor subtypes (ReflectionHelper.GetSubtypesInMods);
        // DebugOnly keeps it on the modded console (NDevConsole.cs:359).
        var consoleCmd = Activator.CreateInstance(typeof(TurnDriveHarnessConsoleCmd));
        Assert.IsType<TurnDriveHarnessConsoleCmd>(consoleCmd);
        Assert.Equal("pvpduelturn", ((TurnDriveHarnessConsoleCmd)consoleCmd!).CmdName);
        Assert.False(((TurnDriveHarnessConsoleCmd)consoleCmd).IsNetworked);

        // 'end'/'status' touch game singletons (in-game only); the unknown
        // subcommand path is pure and must fail closed.
        Assert.False(((TurnDriveHarnessConsoleCmd)consoleCmd).Process(null, ["bogus"]).success);
    }

    // --- simple-name reflection helpers (CombatTurnState is internal) -------

    private static MethodInfo? GetMethod(Type type, string name, params string[] parameterSimpleNames) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == name && Matches(m.GetParameters(), parameterSimpleNames));

    private static bool HasMethod(Type type, string name, params string[] parameterSimpleNames) =>
        GetMethod(type, name, parameterSimpleNames) != null;

    private static bool Matches(ParameterInfo[] parameters, string[] expected)
    {
        if (parameters.Length != expected.Length)
        {
            return false;
        }
        return parameters.Zip(expected, (p, e) => SimpleTypeName(p.ParameterType) == e).All(match => match);
    }

    private static string SimpleTypeName(Type type)
    {
        var name = type.Name;
        var arity = name.IndexOf('`');
        return arity > 0 ? name[..arity] : name;
    }
}

internal sealed class AsyncSubject
{
    public static bool RanOriginal;

    public async Task Op()
    {
        await Task.Yield();
        RanOriginal = true;
    }
}

internal static class HarmonyContractPrefix
{
    public static TaskCompletionSource Gate = new();

    public static bool ReplacementRan;

    public static bool Replace(ref Task __result)
    {
        __result = Replacement();
        return false;
    }

    private static async Task Replacement()
    {
        await Gate.Task;
        ReplacementRan = true;
    }

    public static bool Skip(ref Task __result)
    {
        __result = Task.CompletedTask;
        return false;
    }
}
