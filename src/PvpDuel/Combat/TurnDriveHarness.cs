using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Logging;

namespace PvpDuel.Combat;

/// <summary>
/// Ticket #18 (T6) single-machine drive helpers for the fake-opponent duel
/// (spec §9, test-only). The vanilla End Turn button cannot act for the fake
/// opponent — it only ever ends the LOCAL player's turn
/// (decomp NEndTurnButton.cs:606-607) — so the 5+ round alternation evidence
/// needs a debug way to end the FAKE opponent's turn, plus a state printout
/// for the per-round energy/draw assertions.
///
/// Console command <c>pvpduelturn</c> (DebugOnly ⇒ modded console only, per
/// NDevConsole.cs:359 shouldAllowDebugCommands):
/// - <c>end</c> — enqueues <see cref="EndPlayerTurnAction"/> for the flipped
///   opponent through <c>ActionQueueSynchronizer.RequestEnqueue</c> — the exact
///   vanilla lockstep path of the real End Turn button, no local bypass
///   (acceptance #2).
/// - <c>status</c> — prints energy / hand / draw pile / turn number for both
///   duel players (per-round energy-reset and draw-5 evidence).
/// </summary>
public sealed class TurnDriveHarnessConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "pvpduelturn";

    public override string Args => "end|status";

    public override string Description =>
        "PvP Duel turn drive (debug): 'end' ends the fake opponent's turn through the lockstep action queue; " +
        "'status' prints both duel players' energy/hand/piles/turn numbers.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var sub = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        return sub switch
        {
            "end" => EndOpponentTurn(),
            "status" => Status(),
            _ => new CmdResult(false, $"unknown subcommand '{args[0]}' (use end|status)"),
        };
    }

    private static CmdResult EndOpponentTurn()
    {
        var opponent = FindLivingOpponent(out var error);
        if (opponent == null)
        {
            return new CmdResult(false, error);
        }

        var turnNumber = opponent.PlayerCombatState?.TurnNumber ?? -1;
        // Vanilla End Turn button path (decomp NEndTurnButton.cs:606-607):
        // the action rides the lockstep queue, so both "machines" of the
        // single-machine harness process it identically.
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(opponent, turnNumber));
        PvpDuelLog.Info($"turn drive harness: EndPlayerTurnAction enqueued for the fake opponent (netId {opponent.NetId}, turn {turnNumber}).");
        return new CmdResult(true, $"fake opponent end turn enqueued (netId {opponent.NetId}, turn {turnNumber}).");
    }

    private static CmdResult Status()
    {
        var opponent = FindLivingOpponent(out var error);
        if (opponent == null)
        {
            return new CmdResult(false, error);
        }
        var me = LocalContext.GetMe(opponent.Creature.CombatState);
        if (me?.PlayerCombatState == null || opponent.PlayerCombatState == null)
        {
            return new CmdResult(false, "duel players lack combat state");
        }

        string Row(string label, Player p) =>
            $"{label}: netId {p.NetId}, turn {p.PlayerCombatState.TurnNumber}, " +
            $"energy {p.PlayerCombatState.Energy}/{p.PlayerCombatState.MaxEnergy}, " +
            $"hand {p.PlayerCombatState.Hand.Cards.Count}, draw {p.PlayerCombatState.DrawPile.Cards.Count}, " +
            $"discard {p.PlayerCombatState.DiscardPile.Cards.Count}";

        return new CmdResult(true, $"{Row("me", me)} | {Row("opponent", opponent)}");
    }

    private static Player? FindLivingOpponent(out string error)
    {
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var creature = combatState?.CreaturesOnCurrentSide
            .FirstOrDefault(c => c.IsPlayer && c.Player != null && !c.IsDead);
        if (creature?.Player == null)
        {
            error = "no living opponent on the current side (are you in a duel room during the opponent's turn?)";
            return null;
        }
        error = string.Empty;
        return creature.Player;
    }
}
