using System.Reflection;

using HarmonyLib;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Duel;
using PvpDuel.Logging;

namespace PvpDuel.Combat;

/// <summary>
/// Ticket #18 (T6) patch set "TurnDrive" — the opponent-turn driver
/// (spec §7 #3) plus the minimal vanilla fan-out gates that make turns
/// strictly alternate inside duel rooms (spec §12.3 verdict archived below).
///
/// Not attribute-patched: ModEntry applies the set manually, only after the
/// startup self-check confirmed every target (same pattern as SideFlip).
/// Every gate is gated on <see cref="DuelScope.IsDuelRoom"/>; non-duel combat
/// runs vanilla everywhere (acceptance #3: monster enemy turns zero change).
///
/// Turn model (decomp CombatManager.cs, sts2 v0.111.0):
/// - The flipped opponent is a player creature on the Enemy side
///   (SideFlip, ticket #17) and STAYS in <c>CombatState.Players</c>
///   (decomp CombatState.cs:334-339, docs/is-enemy-consumers.md #63).
/// - Player side = the local player's turn; Enemy side = the opponent's turn,
///   driven here by replacing <see cref="CombatManager.ExecuteEnemyTurn"/>
///   (decomp :1407-1440) with a mirror of <c>SetupPlayerTurn</c> (decomp
///   :882-929: energy reset → draw 5 → play phase) followed by a wait for the
///   opponent's EndTurn readiness and a mirror of their end turn.
/// - §12.3 verdict: <c>ActionQueueSynchronizer.SetCombatState</c> has NO
///   CurrentSide gate (decomp ActionQueueSynchronizer.cs:80-138); the only
///   gate is <c>RequestEnqueue</c> deferring CombatPlayPhaseOnly actions
///   while in NotPlayPhase (decomp :148-153). The driver therefore requests
///   PlayPhase explicitly for the opponent's play phase — their card plays
///   AND their EndPlayerTurnAction flow through the lockstep queue. No
///   additional synchronizer patch needed.
/// - Gated fan-out points (all CombatManager, all duel-only): SetupPlayerTurn
///   and RunAutoPrePlayPhase skip the flipped opponent (the driver mirrors
///   them); EndPlayerTurnPhaseOne/TwoInternal process the local player only
///   (vanilla would flush the opponent's hand before their driven turn);
///   AllPlayersReadyToEndTurn / SetReadyToBeginEnemyTurn use the duel ready
///   rules (vanilla waits for the opponent's readiness — a deadlock, since
///   they can only end their turn during their driven turn); SetUpCombat
///   starts the second-hand machine on the Enemy side so turn 1 belongs to
///   the first hand.
///
/// Harmony contract (pinned by TurnDriveGameSurfaceTests, probe-derived from
/// the game's own 0Harmony 2.4.2): prefixes on async originals must return
/// bool and hand replacements/skips through <c>ref Task __result</c> — a bare
/// Task-returning prefix is rejected at Patch() time and a bool-false skip
/// without __result yields a null task (caller await = NRE). All private
/// game methods the driver calls are reached via <see cref="Traverse"/>.
/// </summary>
internal static class OpponentTurnDrivePatch
{
    public const string PatchSetId = "TurnDrive";

    private const string TurnStateTypeName = "MegaCrit.Sts2.Core.Combat.CombatTurnState";

    // Cached Traverse handles over the private CombatManager methods the
    // driver invokes instead of reimplementing (spec acceptance #4).
    private static Traverse _endEnemyTurn = null!;
    private static Traverse _doTurnEnd = null!;
    private static Traverse _flushPlayerHand = null!;
    private static Traverse _waitQueueEmpty = null!;
    private static Traverse _checkWinCondition = null!;
    private static Traverse _checkForEmptyHand = null!;

    /// <summary>Applies the set; throws when a target is gone (caller disables the set).</summary>
    public static void Apply(Harmony harmony)
    {
        var turnStateType = AccessTools.TypeByName(TurnStateTypeName)
            ?? throw new MissingMethodException("MegaCrit.Sts2.Core.Combat", "CombatTurnState");
        // CombatManager.Instance is a process-wide singleton (decomp :91), so
        // instance-based Traverse handles resolve the private members against
        // the live manager — invoking instance methods needs a target.
        var managerTraverse = Traverse.Create(CombatManager.Instance);

        _endEnemyTurn = managerTraverse.Method("EndEnemyTurn", turnStateType);
        _doTurnEnd = managerTraverse.Method("DoTurnEnd", turnStateType, typeof(Player), typeof(PlayerChoiceContext));
        _flushPlayerHand = managerTraverse.Method("FlushPlayerHand", turnStateType, typeof(Player), typeof(HookPlayerChoiceContext));
        _waitQueueEmpty = managerTraverse.Method("WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction", turnStateType);
        _checkWinCondition = managerTraverse.Method("CheckWinCondition", turnStateType);
        _checkForEmptyHand = managerTraverse.Method("CheckForEmptyHand", turnStateType, typeof(PlayerChoiceContext), typeof(Player));

        harmony.Patch(RequireMethod(typeof(CombatManager), "ExecuteEnemyTurn", "CombatTurnState", "Func"),
            prefix: Patch(nameof(ExecuteEnemyTurnPrefix)));
        harmony.Patch(RequireMethod(typeof(CombatManager), "SetupPlayerTurn",
                "CombatTurnState", "Player", "HookPlayerChoiceContext"),
            prefix: Patch(nameof(SetupPlayerTurnPrefix)));
        harmony.Patch(RequireMethod(typeof(CombatManager), "RunAutoPrePlayPhase",
                "CombatTurnState", "HookPlayerChoiceContext", "Task", "Player"),
            prefix: Patch(nameof(RunAutoPrePlayPhasePrefix)));
        harmony.Patch(RequireMethod(typeof(CombatManager), "EndPlayerTurnPhaseOneInternal", "CombatTurnState"),
            prefix: Patch(nameof(EndPlayerTurnPhaseOnePrefix)));
        harmony.Patch(RequireMethod(typeof(CombatManager), "EndPlayerTurnPhaseTwoInternal", "CombatTurnState"),
            prefix: Patch(nameof(EndPlayerTurnPhaseTwoPrefix)));
        harmony.Patch(RequireMethod(typeof(CombatManager), "AllPlayersReadyToEndTurn", "CombatTurnState"),
            prefix: Patch(nameof(AllPlayersReadyToEndTurnPrefix)));
        harmony.Patch(RequireMethod(typeof(CombatManager), "SetReadyToBeginEnemyTurn", "Player", "Func"),
            prefix: Patch(nameof(SetReadyToBeginEnemyTurnPrefix)));
        harmony.Patch(RequireMethod(typeof(CombatManager), "SetUpCombat", "CombatState"),
            postfix: Patch(nameof(SetUpCombatPostfix)));

        PvpDuelLog.Info("TurnDrive patch set applied (ExecuteEnemyTurn driver + 7 fan-out gates).");
    }

    private static HarmonyMethod Patch(string name)
    {
        var method = typeof(OpponentTurnDrivePatch).GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(OpponentTurnDrivePatch).FullName, name);
        return new HarmonyMethod(method);
    }

    /// <summary>Resolves a private overload by simple parameter type names (CombatTurnState is internal).</summary>
    private static MethodInfo RequireMethod(Type type, string name, params string[] parameterSimpleNames)
    {
        var method = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == name
                && m.GetParameters() is var parameters
                && parameters.Length == parameterSimpleNames.Length
                && parameters.Zip(parameterSimpleNames, (p, e) => SimpleTypeName(p.ParameterType) == e).All(match => match))
            ?? throw new MissingMethodException(type.FullName, $"{name}({string.Join(", ", parameterSimpleNames)})");
        return method;
    }

    private static string SimpleTypeName(Type type)
    {
        var name = type.Name;
        var arity = name.IndexOf('`');
        return arity > 0 ? name[..arity] : name;
    }

    // --- (1) ExecuteEnemyTurn: the opponent-turn driver ----------------------

    /// <summary>Replaces the vanilla monster loop with the driven opponent turn inside duel rooms.</summary>
    private static bool ExecuteEnemyTurnPrefix(object turnState, Func<Task>? actionDuringEnemyTurn, ref Task __result)
    {
        if (!OpponentTurnPolicy.ShouldDriveOpponentTurn(
                DuelScope.IsDuelRoom, turnState != null, IsInProgress(turnState)))
        {
            return true; // vanilla monster turn
        }

        __result = DriveOpponentTurn(turnState, actionDuringEnemyTurn);
        return false;
    }

    private static async Task DriveSingleOpponentTurn(
        object turnState,
        CombatState state,
        Player opponent,
        CancellationToken ct)
    {
        var combatState = opponent.PlayerCombatState;
        if (opponent.Creature.IsDead || combatState == null)
        {
            PvpDuelLog.Info($"turn drive: opponent {opponent.NetId} dead or unprepared; skipping their turn.");
            return;
        }

        combatState.Phase = PlayerTurnPhase.Start;

        // Mirror of SetupPlayerTurn (decomp :882-929): energy reset → hooks →
        // innate reorder → draw 5, through the same public hook/action surfaces.
        var ctx = new HookPlayerChoiceContext(opponent, LocalContext.NetId!.Value, GameActionType.CombatPlayPhaseOnly);
        var setupTask = MirrorSetupPlayerTurn(state, opponent, ctx, ct);
        await ctx.WaitForPauseOrCompletionWithoutAssigningTask(setupTask); // vanilla :780
        ct.ThrowIfCancellationRequested();
        await setupTask;

        // Mirror of RunAutoPrePlayPhase (decomp :864-871): Start → AutoPrePlay → Play.
        combatState.Phase = PlayerTurnPhase.AutoPrePlay;
        await InvokeTask(_checkForEmptyHand, turnState, ctx, opponent);
        await Hook.AfterAutoPrePlayPhaseEntered(ctx, state, opponent);
        combatState.Phase = PlayerTurnPhase.Play;

        // Player-branch tail (decomp :836-837): hand control to the action
        // queue. §12.3 verdict: SetCombatState has no CurrentSide gate; PlayPhase
        // is what admits the opponent's CombatPlayPhaseOnly actions (card plays
        // AND their EndPlayerTurnAction) into the lockstep queue.
        RunManager.Instance.ActionExecutor.Unpause();
        RunManager.Instance.ActionQueueSynchronizer.SetCombatState(ActionSynchronizerCombatState.PlayPhase);

        // Wait for the opponent's EndTurn readiness (public surface, no internals).
        await WaitForOpponentEndTurn(turnState, opponent, ct);
        if (!IsInProgress(turnState))
        {
            return;
        }

        // Mirror their end turn: the vanilla path runs it on the opponent's own
        // machine; the state replica here must follow or the next turn's piles
        // diverge (unflushed hand, uncleaned turn flags).
        var sync = RunManager.Instance.ActionQueueSynchronizer;
        sync.SetCombatState(ActionSynchronizerCombatState.EndTurnPhaseOne);
        await InvokeTask(_waitQueueEmpty, turnState);
        ct.ThrowIfCancellationRequested();
        await RunEndTurnPhaseOneBody(turnState, state, opponent, ct);
        if (!IsLive(turnState))
        {
            return;
        }
        sync.SetCombatState(ActionSynchronizerCombatState.NotPlayPhase);
        await RunEndTurnPhaseTwoBody(turnState, state, opponent, ct);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Mirror of the vanilla <c>SetupPlayerTurn</c> body (decomp :882-929) for
    /// the driven opponent — same hook sequence and hand-draw computation.
    /// </summary>
    private static async Task MirrorSetupPlayerTurn(
        CombatState state,
        Player opponent,
        HookPlayerChoiceContext ctx,
        CancellationToken ct)
    {
        if (opponent.Creature.IsDead)
        {
            return;
        }
        var combatState = opponent.PlayerCombatState;
        if (combatState == null)
        {
            PvpDuelLog.Warn($"Player combat state is null during the driven setup (Player: {opponent.NetId}).");
            return;
        }
        if (Hook.ShouldPlayerResetEnergy(state, opponent))
        {
            combatState.ResetEnergy();
        }
        else
        {
            combatState.AddMaxEnergyToCurrent();
        }
        await Hook.AfterEnergyReset(state, opponent);
        ct.ThrowIfCancellationRequested();
        await Hook.BeforeHandDraw(state, opponent, ctx);
        ct.ThrowIfCancellationRequested();
        var handDraw = Hook.ModifyHandDraw(state, opponent, 5m, out var modifiers);
        await Hook.AfterModifyingHandDraw(state, modifiers);
        ct.ThrowIfCancellationRequested();
        if (combatState.TurnNumber == 1)
        {
            // Turn-1 pile shaping (decomp :910-925): bottom-start enchantments,
            // then Innate to the top, drawing at least the innate count.
            var pile = PileType.Draw.GetPile(opponent);
            var bottomList = pile.Cards
                .Where(c => c.Enchantment?.ShouldStartAtBottomOfDrawPile ?? false)
                .ToList();
            foreach (var card in bottomList)
            {
                pile.MoveToBottomInternal(card);
            }
            var innate = pile.Cards
                .Where(c => c.Keywords.Contains(CardKeyword.Innate))
                .Except(bottomList)
                .ToList();
            foreach (var card in innate)
            {
                pile.MoveToTopInternal(card);
            }
            handDraw = Math.Max(handDraw, innate.Count);
            handDraw = Math.Min(handDraw, CardPile.MaxCardsInHand);
        }
        await CardPileCmd.Draw(ctx, handDraw, opponent, fromHandDraw: true);
        ct.ThrowIfCancellationRequested();
        await Hook.AfterPlayerTurnStart(state, ctx, opponent);
    }

    /// <summary>
    /// Waits for the opponent to ready their end turn — in the harness via the
    /// harness driver calling <c>PlayerCmd.EndTurn</c>, in real multiplayer via
    /// their <c>EndPlayerTurnAction</c> delivered on the lockstep queue. Frame
    /// yielding follows the vanilla <c>WaitForUnpause</c> precedent
    /// (decomp :1954-1963); combat teardown/divergence frees the wait.
    /// </summary>
    private static async Task WaitForOpponentEndTurn(object turnState, Player opponent, CancellationToken ct)
    {
        var manager = CombatManager.Instance;
        while (OpponentTurnPolicy.ShouldKeepWaitingForOpponentEndTurn(
                   manager.IsPlayerReadyToEndTurn(opponent),
                   IsLive(turnState)))
        {
            ct.ThrowIfCancellationRequested();
            var game = NGame.Instance;
            if (game != null)
            {
                await game.AwaitProcessFrame(ct);
            }
            else
            {
                await Task.Delay(10, ct);
            }
        }
    }


    private static async Task DriveOpponentTurn(object turnState, Func<Task>? actionDuringEnemyTurn)
    {
        var ct = GetCt(turnState);
        ct.ThrowIfCancellationRequested();

        // Vanilla head (decomp :1414-1418): the test hook runs first.
        if (actionDuringEnemyTurn != null)
        {
            await actionDuringEnemyTurn();
            ct.ThrowIfCancellationRequested();
        }

        var state = GetState(turnState);
        var opponents = state.CreaturesOnCurrentSide
            .Where(c => c.IsPlayer && c.Player != null && !c.IsDead)
            .Select(c => c.Player!)
            .ToList();
        if (opponents.Count == 0)
        {
            // The flipped opponent is gone/dead: straight to the vanilla tail,
            // whose CheckWinCondition settles the combat (decomp :1438-1439).
            PvpDuelLog.Info("turn drive: no living opponent on the current side; running the enemy-turn tail.");
        }

        foreach (var opponent in opponents)
        {
            ct.ThrowIfCancellationRequested();
            await DriveSingleOpponentTurn(turnState, state, opponent, ct);
            if (!IsInProgress(turnState))
            {
                return; // combat ended during the opponent's turn
            }
        }

        RunManager.Instance.ChecksumTracker.GenerateChecksum("After enemy turn end", null);
        await InvokeTask(_endEnemyTurn, turnState); // WaitForUnpause → EndEnemyTurnInternal → CheckWinCondition → SwitchSides
    }

    // --- (2)/(3) vanilla setup fan-out gates ---------------------------------

    /// <summary>Async original: skip with a completed result task (caller awaits it).</summary>
    private static bool SetupPlayerTurnPrefix(object turnState, Player player, ref Task __result)
    {
        if (!OpponentTurnPolicy.ShouldSkipVanillaPlayerSetup(DuelScope.IsDuelRoom, LocalContext.IsMe(player)))
        {
            return true;
        }
        __result = Task.CompletedTask;
        return false;
    }

    /// <summary>Async original: skip with a completed result task (caller awaits it).</summary>
    private static bool RunAutoPrePlayPhasePrefix(
        object turnState,
        HookPlayerChoiceContext playerChoiceContext,
        Task setupPlayerTurnTask,
        Player player,
        ref Task __result)
    {
        if (!OpponentTurnPolicy.ShouldSkipVanillaPlayerSetup(DuelScope.IsDuelRoom, LocalContext.IsMe(player)))
        {
            return true;
        }
        __result = Task.CompletedTask;
        return false;
    }

    // --- (4)/(5) end-turn phases: local player only ---------------------------

    private static bool EndPlayerTurnPhaseOnePrefix(object turnState, ref Task __result)
    {
        if (!OpponentTurnPolicy.ShouldRunMeOnlyEndTurn(DuelScope.IsDuelRoom))
        {
            return true;
        }
        var state = GetState(turnState);
        var me = LocalContext.GetMe(state);
        __result = me == null
            ? Task.CompletedTask
            : RunEndTurnPhaseOneBody(turnState, state, me, GetCt(turnState));
        return false;
    }

    private static bool EndPlayerTurnPhaseTwoPrefix(object turnState, ref Task __result)
    {
        if (!OpponentTurnPolicy.ShouldRunMeOnlyEndTurn(DuelScope.IsDuelRoom))
        {
            return true;
        }
        var state = GetState(turnState);
        var me = LocalContext.GetMe(state);
        __result = me == null
            ? Task.CompletedTask
            : RunEndTurnPhaseTwoBody(turnState, state, me, GetCt(turnState));
        return false;
    }

    // --- (6)/(7) duel ready rules ---------------------------------------------

    /// <summary>Bool original: the local player's readiness alone ends the player side.</summary>
    private static bool AllPlayersReadyToEndTurnPrefix(object turnState, ref bool __result)
    {
        if (!DuelScope.IsDuelRoom)
        {
            return true;
        }
        var state = GetState(turnState);
        var me = LocalContext.GetMe(state);
        var readySet = Traverse.Create(turnState).Property("PlayersReadyToEndTurn")
            .GetValue<HashSet<Player>>() ?? [];
        var localReady = me != null && readySet.Contains(me);
        var sideOk = RunManager.Instance.IsSingleplayerOrFakeMultiplayer
            || state.CurrentSide == CombatSide.Player;
        __result = OpponentTurnPolicy.DuelAllPlayersReadyToEndTurn(localReady, sideOk);
        return false;
    }

    /// <summary>Void original: run the duel fire rule instead of the vanilla body.</summary>
    private static bool SetReadyToBeginEnemyTurnPrefix(
        object turnState,
        Player player,
        Func<Task>? actionDuringEnemyTurn)
    {
        if (!DuelScope.IsDuelRoom)
        {
            return true;
        }
        var tv = Traverse.Create(turnState);
        if (!tv.Property("IsInProgress").GetValue<bool>())
        {
            PvpDuelLog.Warn("Trying to set player ready to begin enemy turn, but combat is over!");
            return false;
        }
        var readySet = tv.Property("PlayersReadyToBeginEnemyTurn").GetValue<HashSet<Player>>() ?? [];
        if (!readySet.Add(player))
        {
            return false;
        }
        var state = GetState(turnState);
        var me = LocalContext.GetMe(state);
        var fire = OpponentTurnPolicy.DuelReadyToBeginEnemyTurn(
            state.CurrentSide == CombatSide.Player,
            me != null && readySet.Contains(me));
        var beginSource = tv.Property("BeginEnemyTurnSignalSource")
            .GetValue<TaskCompletionSource<Func<Task>?>>();
        if (fire && beginSource != null && !beginSource.TrySetResult(actionDuringEnemyTurn))
        {
            PvpDuelLog.Warn($"Ignoring ready-to-begin-enemy-turn for player {player.NetId}: a player-to-enemy transition has already been claimed for this turn.");
        }
        return false;
    }

    // --- (8) combat-start side assignment --------------------------------------

    private static void SetUpCombatPostfix(CombatState state)
    {
        if (!DuelScope.IsDuelRoom)
        {
            return;
        }
        if (!OpponentTurnPolicy.ShouldStartOnEnemySide(isDuelRoom: true, AmFirstHand(state)))
        {
            return;
        }
        state.CurrentSide = CombatSide.Enemy;
        PvpDuelLog.Info("turn drive: second-hand machine hosts turn 1 from the Enemy side.");
    }

    private static bool AmFirstHand(CombatState state)
    {
        var me = LocalContext.GetMe(state);
        if (me == null)
        {
            return true; // no local context: keep the vanilla player-side start
        }
        ulong? declared = DuelScope.Current?.FirstHandNetId;
        var opponentNetId = state.Players
            .Where(p => p != null && !LocalContext.IsMe(p))
            .Select(p => p.NetId)
            .DefaultIfEmpty(me.NetId)
            .Min();
        return OpponentTurnPolicy.ResolveFirstHandIsMe(declared, me.NetId, opponentNetId);
    }

    // --- shared mirror bodies ---------------------------------------------------

    private static async Task RunEndTurnPhaseOneBody(
        object turnState,
        CombatState state,
        Player player,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }
        var combatState = player.PlayerCombatState;
        if (combatState == null)
        {
            PvpDuelLog.Warn($"Player combat state is null during end-turn phase one (Player: {player.NetId}).");
            return;
        }

        // AutoPostPlay → End (decomp :1539-1558, degenerate single player).
        combatState.Phase = PlayerTurnPhase.AutoPostPlay;
        var ctx = new HookPlayerChoiceContext(player, LocalContext.NetId!.Value, GameActionType.CombatPlayPhaseOnly);
        var autoPostPlayTask = Hook.AfterAutoPostPlayPhaseEntered(ctx, state, player);
        await ctx.AssignTaskAndWaitForPauseOrCompletion(autoPostPlayTask);
        await ctx.WaitForCompletion();
        combatState.Phase = PlayerTurnPhase.End;

        await Hook.BeforeSideTurnEnd(state, state.CurrentSide, [player.Creature]);
        if (InvokeBool(_checkWinCondition, turnState))
        {
            return;
        }

        // Per-player turn-end effects (decomp :1568-1582) via the private body.
        var endCtx = new HookPlayerChoiceContext(player, LocalContext.NetId.Value, GameActionType.Combat);
        var endTask = InvokeTask(_doTurnEnd, turnState, player, endCtx);
        await endCtx.AssignTaskAndWaitForPauseOrCompletion(endTask);
        await endCtx.WaitForCompletion();
        if (InvokeBool(_checkWinCondition, turnState))
        {
            return;
        }

        await Hook.BeforeFlush(state, player);
        RunManager.Instance.ChecksumTracker.GenerateChecksum("After player turn phase one end", null);
        InvokeBool(_checkWinCondition, turnState);
    }

    private static async Task RunEndTurnPhaseTwoBody(
        object turnState,
        CombatState state,
        Player player,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }
        if (player.PlayerCombatState == null)
        {
            PvpDuelLog.Warn($"Player combat state is null during end-turn phase two (Player: {player.NetId}).");
            return;
        }

        // Hand flush + EndOfTurnCleanup (decomp :1758-1775) via the private body.
        var ctx = new HookPlayerChoiceContext(player, LocalContext.NetId!.Value, GameActionType.CombatPlayPhaseOnly);
        var flushTask = InvokeTask(_flushPlayerHand, turnState, player, ctx);
        await ctx.AssignTaskAndWaitForPauseOrCompletion(flushTask);
        await ctx.WaitForCompletion();

        await Hook.AfterSideTurnEnd(state, state.CurrentSide, [player.Creature]);
        RunManager.Instance.ChecksumTracker.GenerateChecksum("after player turn phase two end", null);
    }

    // --- turn-state accessors (CombatTurnState is internal) ----------------------

    private static CombatState GetState(object turnState) =>
        Traverse.Create(turnState).Property("State").GetValue<CombatState>()
            ?? throw new InvalidOperationException("CombatTurnState.State was null.");

    private static CancellationToken GetCt(object turnState) =>
        Traverse.Create(turnState).Property("Ct").GetValue<CancellationToken>();

    private static bool IsInProgress(object turnState) =>
        Traverse.Create(turnState).Property("IsInProgress").GetValue<bool>();

    private static bool IsLive(object turnState) =>
        Traverse.Create(turnState).Property("IsLive").GetValue<bool>();

    private static Task InvokeTask(Traverse method, params object?[] args) =>
        method.GetValue<Task>(args) ?? Task.CompletedTask;

    private static bool InvokeBool(Traverse method, params object?[] args) =>
        method.GetValue<bool>(args);
}
