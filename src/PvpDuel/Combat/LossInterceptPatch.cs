using System.Reflection;

using Godot;

using HarmonyLib;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using PvpDuel.Core.Duel;
using PvpDuel.Duel;
using PvpDuel.Encounters;
using PvpDuel.Logging;
using PvpDuel.Localization;

namespace PvpDuel.Combat;

/// <summary>
/// Ticket #19 (T7, spec §7 #4): duel-loss interception and the Fate-Thread
/// safety net. Not attribute-patched: ModEntry applies the set manually, only
/// after the startup self-check confirmed every target.
///
/// Why the interception lives on the kill batch and not on LoseCombat alone
/// (decomp v0.111.0): <c>CombatManager.LoseCombat</c> (CombatManager.cs:1267-1274)
/// is the sole <c>PendingLoss</c> set-point, but it is only called from
/// <c>CreatureCmd.Kill</c>'s all-players-dead branch (CreatureCmd.cs:476-481),
/// which then calls <c>RunManager.OnEnded(isVictory: false)</c> and
/// <c>NRun.ShowGameOverScreen</c> directly (CreatureCmd.cs:487-488). In a
/// single-machine fake-player duel the harness opponent is never in
/// <c>RunState.Players</c>, so every local death reaches that branch. Skipping
/// LoseCombat alone therefore still shows the failure screen; the kill batch is
/// intercepted before the all-dead evaluation, the local player is Fate-Thread
/// revived (heal to 1 HP, SlayThePlayer pattern) and the opponent is force-killed
/// so the vanilla win-condition path (no alive primary enemy →
/// <c>EndCombatInternal</c>) ends the duel through the normal combat teardown —
/// the run never enters the failure settlement and the run teardown is never
/// touched. <see cref="LoseCombatPrefix"/> stays patched as the backstop: it
/// guarantees PendingLoss is never armed inside a duel and records the outcome
/// on abnormal paths.
///
/// Co-op mode (real remote end) is observation-only: the local event source
/// records what this end observed, while mechanics stay vanilla so lockstep
/// never diverges — the mirrored settlement and synced combat end land with the
/// net ticket (T11). Every decision is gated on <see cref="DuelScope.IsDuelRoom"/>
/// and the pure table in <see cref="LossInterceptPolicy"/>; non-duel rooms,
/// forced batches (abandon-run) and non-player batches always pass through.
/// </summary>
internal static class LossInterceptPatch
{
    public const string PatchSetId = "LossIntercept";

    private const string FallbackLocKey = "PVP_DUEL_LOSS_FALLBACK";

    private static bool _installed;

    /// <summary>Applies the set; throws when a target is gone (caller disables the set).</summary>
    public static void Apply(Harmony harmony)
    {
        var kill = AccessTools.Method(
                typeof(CreatureCmd), nameof(CreatureCmd.Kill),
                [typeof(IReadOnlyCollection<Creature>), typeof(bool)])
            ?? throw new MissingMethodException(
                typeof(CreatureCmd).FullName, "Kill(IReadOnlyCollection<Creature>, Boolean)");
        harmony.Patch(kill, prefix: Patch(nameof(KillPrefix)));

        var loseCombat = AccessTools.Method(typeof(CombatManager), nameof(CombatManager.LoseCombat))
            ?? throw new MissingMethodException(typeof(CombatManager).FullName, "LoseCombat");
        harmony.Patch(loseCombat, prefix: Patch(nameof(LoseCombatPrefix)));

        PvpDuelLog.Info("LossIntercept patch set applied (Kill batch observer/interceptor + LoseCombat backstop).");
    }
    /// <summary>
    /// Room-window wiring (no Harmony): settles land in the run-scoped history
    /// the save side-channel persists (<see cref="Save.DuelSaveState.History"/>),
    /// and a fresh outcome tracker is armed when a duel room opens and dropped
    /// on exit, mirroring the DuelScope window. Called once from the mod
    /// initializer; idempotent.
    /// </summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        DuelResultSource.SettledSink = static result => Save.DuelSaveState.History.Record(result);
        RunManager.Instance.RoomEntered += OnRoomEntered;
        RunManager.Instance.RoomExited += OnRoomExited;
        PvpDuelLog.Info("loss intercept installed (room-entered/exited window).");
    }

    private static void OnRoomEntered()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state?.CurrentRoom is CombatRoom { Encounter: PvpDuelEncounter })
            {
                DuelResultSource.BeginDuelCombat();
            }
        }
        catch (Exception ex)
        {
            // Tracker bookkeeping must never break room transitions.
            PvpDuelLog.Error($"loss intercept room-enter hook failed: {ex}");
        }
    }

    private static void OnRoomExited()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state?.CurrentRoom is not CombatRoom { Encounter: PvpDuelEncounter })
            {
                DuelResultSource.EndDuelCombat();
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"loss intercept room-exit hook failed: {ex}");
        }
    }

    /// <summary>
    /// Task-returning prefix on <c>CreatureCmd.Kill(IReadOnlyCollection, bool)</c>:
    /// returning a Task skips the vanilla batch (interception), null runs vanilla.
    /// Any failure degrades to vanilla — the vanilla kill pipeline must never
    /// break (the LoseCombat backstop stays armed regardless).
    /// </summary>
    private static Task? KillPrefix(IReadOnlyCollection<Creature> creatures, bool force)
    {
        try
        {
            if (creatures.Count == 0 || force || !DuelScope.IsDuelRoom)
            {
                return null;
            }

            var tracker = DuelResultSource.Current;
            if (tracker == null || LocalContext.NetId is null)
            {
                return null;
            }

            Creature? local = null;
            Creature? batchOpponent = null;
            var hasNonPlayer = false;
            foreach (var creature in creatures)
            {
                if (!creature.IsPlayer)
                {
                    hasNonPlayer = true;
                    break;
                }

                if (LocalContext.IsMe(creature))
                {
                    local ??= creature;
                }
                else
                {
                    batchOpponent ??= creature;
                }
            }

            var action = LossInterceptPolicy.DecideKill(
                isDuelRoom: true,
                isForced: force,
                hasNonPlayerCreature: hasNonPlayer,
                hasLocalPlayer: local != null,
                hasOpponentPlayer: batchOpponent != null,
                // A duel room outside a 2-player co-op session is the
                // single-machine fake-opponent harness (the boss swap only
                // produces one with the debug flag on).
                isSingleMachineDuel: !DuelScope.DuelRunActive);

            switch (action)
            {
                case KillInterceptAction.ObserveOnly:
                    if (local != null)
                    {
                        tracker.ObserveLocalDeath();
                    }

                    if (batchOpponent != null)
                    {
                        tracker.ObserveOpponentDeath();
                    }

                    TrySettle(tracker, local, batchOpponent);
                    return null; // vanilla sticks the death; its win-condition check ends the duel

                case KillInterceptAction.InterceptLocalDeath:
                    return InterceptLocalDeathAsync(tracker, local!, batchOpponent);

                case KillInterceptAction.PassThrough:
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"loss intercept Kill prefix failed, falling through to vanilla: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Single-machine local death: Fate-Thread the local player back to 1 HP,
    /// then deliver the opponent's death (force: bypasses death prevention —
    /// the duel outcome is already fixed) so the vanilla win-condition check
    /// ends the combat through the normal <c>EndCombatInternal</c> teardown.
    /// Heal strictly BEFORE the kill: the nested Kill's all-dead evaluation
    /// must see the local player alive, or the failure screen fires.
    /// </summary>
    private static async Task InterceptLocalDeathAsync(
        DuelOutcomeTracker tracker, Creature local, Creature? batchOpponent)
    {
        tracker.ObserveLocalDeath();

        if (local.IsDead)
        {
            await CreatureCmd.Heal(local, LossInterceptPolicy.FateThreadHp);
            PvpDuelLog.Info(
                $"fate thread: local player {local.Player?.NetId} restored to {LossInterceptPolicy.FateThreadHp} HP (duel loss intercepted).");
        }

        if (batchOpponent != null)
        {
            tracker.ObserveOpponentDeath();
        }

        var opponent = batchOpponent ?? FindOpponent(local);
        if (opponent != null)
        {
            tracker.ObserveOpponentDeath();
            if (batchOpponent != null || opponent.IsAlive)
            {
                // In-batch: vanilla was skipped, so this batch's death still
                // needs delivering. Out-of-batch and alive: the duel ends here.
                // Already-dead-and-processed opponents are left alone.
                await CreatureCmd.Kill(opponent, force: true);
            }
        }

        TrySettle(tracker, local, opponent);
    }

    /// <summary>
    /// Backstop on <c>CombatManager.LoseCombat</c> — the sole PendingLoss
    /// set-point. Inside a duel it never lets the vanilla loss chain arm:
    /// observed duel deaths are settled into the local event source, and the
    /// abnormal path is logged and surfaced to the player (acceptance #4).
    /// Unobserved batches (forced abandon, exotic paths) stay vanilla.
    /// </summary>
    private static bool LoseCombatPrefix()
    {
        try
        {
            if (!DuelScope.IsDuelRoom)
            {
                return true;
            }

            var tracker = DuelResultSource.Current;
            if (tracker is not { HasObservations: true })
            {
                // No duel death was intercepted: genuine vanilla loss path.
                return true;
            }

            TrySettle(tracker, null, null);
            PvpDuelLog.Warn(
                "duel loss reached the vanilla LoseCombat path — the Fate Thread did not hold; outcome recorded, never arming PendingLoss in a duel.");
            ShowFallbackBanner();
            return false;
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"LoseCombat backstop failed, falling through to vanilla: {ex}");
            return true;
        }
    }

    private static Creature? FindOpponent(Creature local) =>
        local.CombatState?.Enemies.FirstOrDefault(e => e.IsPlayer && !LocalContext.IsMe(e));

    /// <summary>
    /// Resolves the observed deaths into the mirrored outcome (once). The
    /// resolution raises <see cref="DuelResultSource.OutcomeSettledLocally"/>
    /// and forwards the result to <see cref="DuelResultSource.SettledSink"/>,
    /// which records it into the run-scoped
    /// <see cref="PvpDuel.Save.DuelSaveState.History"/> (#21 persistence).
    /// </summary>
    private static void TrySettle(DuelOutcomeTracker tracker, Creature? local, Creature? opponent)
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        var actIndex = DuelScope.Current?.ActIndex ?? state?.CurrentActIndex ?? 0;
        var firstHandNetId = DuelScope.Current?.FirstHandNetId ?? 0;
        var localNetId = LocalContext.NetId ?? local?.Player?.NetId ?? 0;
        var opponentNetId = opponent?.Player?.NetId
            ?? (local != null ? FindOpponent(local)?.Player?.NetId : null)
            ?? 0;

        tracker.TryResolve(actIndex, localNetId, opponentNetId, firstHandNetId);
    }

    /// <summary>
    /// Banner-style overlay (main-menu banner treatment) on the game root:
    /// non-interactive, never blocks input. Only shown when the loss fallback
    /// triggers (acceptance #4: clear log + user prompt).
    /// </summary>
    private static void ShowFallbackBanner()
    {
        try
        {
            var game = NGame.Instance;
            if (game == null)
            {
                return;
            }

            var banner = new Label
            {
                Name = "PvpDuelLossFallbackBanner",
                Text = Loc.Text(FallbackLocKey),
            };
            banner.AddThemeColorOverride("font_color", Colors.OrangeRed);
            banner.AddThemeColorOverride("font_outline_color", Colors.Black);
            banner.AddThemeConstantOverride("outline_size", 6);
            banner.Position = new Vector2(24, 8);
            banner.Size = new Vector2(900, 120);
            banner.MouseFilter = Control.MouseFilterEnum.Ignore;
            banner.ZIndex = 1000;
            game.AddChild(banner);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"loss fallback banner display failed: {ex}");
        }
    }

    private static HarmonyMethod Patch(string name)
    {
        var method = typeof(LossInterceptPatch).GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(LossInterceptPatch).FullName, name);
        return new HarmonyMethod(method);
    }
}
