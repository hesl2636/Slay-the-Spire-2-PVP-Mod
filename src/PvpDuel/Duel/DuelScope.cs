using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

using MegaCrit.Sts2.Core.Multiplayer.Game;
using PvpDuel.Core.Fatigue;
using PvpDuel.Encounters;
using PvpDuel.Logging;

namespace PvpDuel.Duel;

/// <summary>
/// Scope gate: the single authority deciding whether the current combat is a
/// duel. Every combat/settlement patch checks <see cref="IsDuelRoom"/> first and
/// passes through to the original method for anything else (spec §5.1).
///
/// The room window is attached event-driven — <see cref="Install"/> subscribes
/// the official <c>RunManager.RoomEntered</c>/<c>RoomExited</c> events (no extra
/// Harmony), so <see cref="Current"/> tracks the duel room exactly while it is
/// the current room. Turn-scoped fields (play counters) stay stubbed for T6.
/// </summary>
public static class DuelScope
{
    private static DuelContext? _current;
    private static bool _installed;

    /// <summary>
    /// True only while the current room is a duel combat room. Derived purely
    /// from the encounter type, so it is false in every non-duel room —
    /// single player never produces a <see cref="PvpDuelEncounter"/>.
    /// </summary>
    public static bool IsDuelRoom =>
        RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is CombatRoom
        {
            Encounter: PvpDuelEncounter,
        };

    /// <summary>
    /// Context of the running duel; null outside duel rooms. Evaluated against
    /// the live room every time, so a missed exit event can never leak context.
    /// </summary>
    public static DuelContext? Current => IsDuelRoom ? _current : null;

    /// <summary>
    /// Gate for the boss-room swap: true only in an active 2-player co-op run.
    /// Single player and 1-player/3+player sessions keep vanilla bosses.
    /// </summary>
    public static bool DuelRunActive
    {
        get
        {
            var manager = RunManager.Instance;
            if (manager.NetService?.Type.IsMultiplayer() != true || manager.RunLobby is not { } lobby)
            {
                return false;
            }

            return lobby.Players.Count == 2;
        }
    }

    /// <summary>
    /// Subscribes the room-window events. Called once from the mod initializer;
    /// idempotent so tests can call it too.
    /// </summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        RunManager.Instance.RoomEntered += OnRoomEntered;
        RunManager.Instance.RoomExited += OnRoomExited;
        PvpDuelLog.Info("duel scope installed (room-entered/exited window).");
    }

    private static void OnRoomEntered()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state?.CurrentRoom is CombatRoom { Encounter: PvpDuelEncounter })
            {
                _current = new DuelContext { ActIndex = state.CurrentActIndex };
                PvpDuelLog.Info($"duel room entered (act {state.CurrentActIndex + 1}); duel scope armed.");
                DuelConfigGate.OnDuelEntering();
            }
            else
            {
                _current = null;
            }
        }
        catch (Exception ex)
        {
            // Gate bookkeeping must never break room transitions.
            PvpDuelLog.Error($"duel room-enter hook failed: {ex}");
        }
    }

    private static void OnRoomExited()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state?.CurrentRoom is not CombatRoom { Encounter: PvpDuelEncounter })
            {
                _current = null;
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"duel room-exit hook failed: {ex}");
        }
    }
}

/// <summary>
/// Combat-scoped duel state shared by the combat patches: opponent, first-hand
/// side, act index, per-turn play counts and the fatigue rule. Created when a
/// duel room opens (room scope, this ticket), cleared on room exit;
/// <c>EndCombatInternal</c>-scoped clearing and the turn counters land with the
/// combat ticket (T6).
/// </summary>
public sealed class DuelContext
{
    public int ActIndex { get; init; }

    /// <summary>NetId of the player with first-hand (shorter act timer).</summary>
    public ulong FirstHandNetId { get; init; }

    public FatigueRule Fatigue { get; init; } = new(FatigueRule.DefaultCap);

    /// <summary>Cards already played this turn, keyed by player net id. STUB (T6).</summary>
    public int PlaysThisTurn(ulong playerNetId) =>
        throw new NotImplementedException("Filled in by the combat ticket (T6).");

    public void CountPlay(ulong playerNetId) =>
        throw new NotImplementedException("Filled in by the combat ticket (T6).");

    public void ResetTurnCounters() =>
        throw new NotImplementedException("Filled in by the combat ticket (T6).");
}
