using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.GameActions;

using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Core.Branching;
using PvpDuel.Core.Duel;
using PvpDuel.Logging;
using PvpDuel.Maps;
using PvpDuel.Net;

// The game coordinate is the patch-facing shape (EnterMapCoord / MoveToMapCoordAction);
// the Core record is the branch-table shape (BranchState).
using GameCoord = MegaCrit.Sts2.Core.Map.MapCoord;


namespace PvpDuel.Branching;

/// <summary>
/// Runtime branch layer (ticket #20, spec §4.2/§6): host-authoritative per-player
/// path table over <see cref="BranchTable"/>, synchronized with the vanilla
/// co-op session through <c>DuelBranchMessage</c> / <c>DuelTimerMessage</c>.
///
/// Authority rules (spec §5.3):
/// - host: mutates the table and broadcasts every change;
/// - client: applies host-broadcast states only; local changes (own position,
///   boss-wait) are *requested* to the host and become visible after the host's
///   confirming broadcast — no client self-authority.
///
/// Movement works by per-end coordinate redirection: the vanilla
/// <c>MoveToMapCoordAction</c> still fans out the host's chosen coordinate to
/// both ends in lockstep, but <c>RunManager.EnterMapCoord</c> is patched to
/// retarget each end onto its own assigned branch coordinate. Both ends derive
/// identical decisions from the mirrored table, so no extra lockstep plumbing is
/// needed for travel.
/// </summary>
public static class BranchSync
{
    private static readonly BranchTable Table = new();
    private static INetGameService? _subscribedService;

    /// <summary>
    /// Raised after a changed branch state is stored AND (host side) its
    /// authoritative broadcast went out. Consumed by the timer layer for
    /// boss-wait confirmations (ticket #22); the raise-after-broadcast order
    /// guarantees a following DuelTimerMessage lands after its confirmation.
    /// </summary>
    public static event Action<ulong, BranchState>? BranchStateChanged;

    /// <summary>Raised after the per-act table reset (SetActInternal postfix); act = the new act index.</summary>
    public static event Action<int>? ActChanged;

    private static bool _installed;

    /// <summary>
    /// Branch layer is live: an active 2-player co-op run on the symmetric split
    /// map. Every patch entry point checks this first; anything else stays 100%
    /// vanilla (spec §5.1 regression red line).
    /// </summary>
    public static bool Active =>
        Duel.DuelScope.DuelRunActive
        && RunManager.Instance.DebugOnlyGetState()?.Map is DuelSplitActMap;

    /// <summary>True on the host end of a session (authority for branch changes).</summary>
    public static bool IsHost =>
        RunManager.Instance.NetService is { } service && service.Type == NetGameType.Host;

    /// <summary>
    /// Shared-state validation/scaling exclusion (spec §5.5): while branches are
    /// diverged, the peers are in different rooms/combat — checksums are skipped
    /// on both ends (identical decision from the mirrored table keeps
    /// <c>NextId</c> aligned) and scaling counts one combat participant.
    /// </summary>
    public static bool ValidationExcluded => Active && Table.Diverged;

    /// <summary>The local player's assigned branch state; null before the host resolves the act's votes.</summary>
    public static BranchState? LocalBranch =>
        TryGetNetId(out var netId) && Table.TryGet(netId, out var state) ? state : null;

    /// <summary>Both players waiting in this act's boss room (rendezvous, spec §6).</summary>
    public static bool BossRendezvous
    {
        get
        {
            if (!Active || !TryGetNetId(out var localNetId))
            {
                return false;
            }

            var other = OtherPlayer();
            return other != null
                && Table.TryGet(other.NetId, out var otherState)
                && Table.TryGet(localNetId, out var localState)
                && BranchTable.IsBossRendezvous(localState, otherState);
        }
    }

    /// <summary>
    /// One-time event subscription. The message handlers themselves register
    /// lazily (see <see cref="EnsureSubscribed"/>): the net service does not
    /// exist yet at mod-init time and is swapped whenever a new session starts.
    /// </summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        RunManager.Instance.RoomEntered += OnRoomEnteredEnsureSubscribed;
        EnsureSubscribed();
        PvpDuelLog.Info("branch sync installed (lazy DuelBranchMessage/DuelTimerMessage handlers).");
    }

    private static void OnRoomEnteredEnsureSubscribed() => EnsureSubscribed();

    /// <summary>
    /// Registers the message handlers on the current net service; idempotent
    /// and re-subscribes whenever the game swaps services per session. Called
    /// from Install and defensively from every branch patch entry point, so
    /// handlers exist before the first message can arrive.
    /// </summary>
    public static void EnsureSubscribed()
    {
        var service = RunManager.Instance.NetService;
        if (service == null || ReferenceEquals(service, _subscribedService))
        {
            return;
        }

        if (_subscribedService != null)
        {
            _subscribedService.UnregisterMessageHandler<DuelBranchMessage>(OnBranchMessage);
            _subscribedService.UnregisterMessageHandler<DuelTimerMessage>(OnTimerMessage);
        }

        _subscribedService = service;
        service.RegisterMessageHandler<DuelBranchMessage>(OnBranchMessage);
        service.RegisterMessageHandler<DuelTimerMessage>(OnTimerMessage);
        PvpDuelLog.Info("branch sync subscribed (DuelBranchMessage/DuelTimerMessage handlers).");
    }

    // ---- Host: vote resolution (MapSelectionSynchronizer.MoveToMapCoord replacement) ----

    /// <summary>
    /// Host-only: groups the collected map votes into branches, publishes every
    /// player's branch state (host-authoritative broadcast), and reports whether
    /// a branch assignment exists for the local player. Called from the
    /// <c>MoveToMapCoord</c> prefix; on false the caller falls back to vanilla.
    /// </summary>
    public static bool ResolveFromVotes()
    {
        // Vanilla never calls MoveToMapCoord on clients, but the prefix runs
        // everywhere — enforce host authority here too (spec §5.3).
        if (!IsHost)
        {
            return false;
        }

        var manager = RunManager.Instance;
        var state = manager.DebugOnlyGetState();
        if (state == null || manager.MapSelectionSynchronizer == null)
        {
            return false;
        }

        var votes = new List<BranchVote>();
        for (var slot = 0; slot < state.Players.Count; slot++)
        {
            var player = state.Players[slot];
            var vote = manager.MapSelectionSynchronizer.GetVote(player);
            votes.Add(new BranchVote(
                slot,
                player.NetId,
                vote.HasValue ? new MapCoord(vote.Value.coord.col, vote.Value.coord.row) : null));
        }

        var unvoted = new List<ulong>();
        var groups = BranchVoteGrouper.Group(votes, unvoted);
        if (groups.Count == 0)
        {
            PvpDuelLog.Warn("branch resolve: no votes to group; falling back to vanilla convergence.");
            return false;
        }

        var actIndex = state.CurrentActIndex;
        foreach (var group in groups)
        {
            foreach (var netId in group.PlayerNetIds)
            {
                Publish(netId, new BranchState(actIndex, group.Coord, InBossWait: false));
            }
        }

        var summary = string.Join(
            " | ",
            groups.Select(g => $"({g.Coord.Col},{g.Coord.Row})x{g.PlayerNetIds.Count}"));
        PvpDuelLog.Info($"branch resolve: act {actIndex + 1}, {groups.Count} branch(es): {summary}");
        return LocalBranch is not null;
    }

    /// <summary>
    /// Enqueues the vanilla <c>MoveToMapCoordAction</c> for the local player's
    /// own branch coordinate; the per-end EnterMapCoord redirection turns the
    /// shared action into per-branch travel.
    /// </summary>
    public static void EnqueueLocalTravel()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null || LocalBranch is not { } mine)
        {
            return;
        }

        var me = LocalContext.GetMe(state);
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
            new MoveToMapCoordAction(me, new GameCoord(mine.Coord.Col, mine.Coord.Row)));
    }

    // ---- Per-end coordinate redirection (EnterMapCoord / LoadIntoLatestMapCoord) ----

    /// <summary>
    /// Returns the coordinate this end must actually travel to, or null to keep
    /// the requested one. Redirect only while branches are diverged; when both
    /// players share a coordinate (start, boss rendezvous) vanilla travel runs
    /// untouched.
    /// </summary>
    public static GameCoord? Redirect(GameCoord requested)
    {
        if (!ValidationExcluded || LocalBranch is not { } mine)
        {
            return null;
        }

        var mineCoord = new GameCoord(mine.Coord.Col, mine.Coord.Row);
        if (requested == mineCoord)
        {
            return null;
        }

        return mineCoord;
    }

    /// <summary>
    /// Redirection for <c>LoadIntoLatestMapCoord</c> (room-stack restore path):
    /// the shared "last visited coordinate" must become this end's own branch
    /// coordinate while branches are diverged. Returns null when the last
    /// visited coordinate already is the branch coordinate (in-session
    /// per-end divergence makes that the common case) or no assignment exists
    /// yet — fresh-load branch restore lands with the save side-channel ticket.
    /// </summary>
    public static GameCoord? RedirectLatest()
    {
        if (!ValidationExcluded || LocalBranch is not { } mine)
        {
            return null;
        }

        var visited = RunManager.Instance.DebugOnlyGetState()?.VisitedMapCoords;
        var mineCoord = new GameCoord(mine.Coord.Col, mine.Coord.Row);
        return visited is { Count: > 0 } && visited[visited.Count - 1] == mineCoord
            ? null
            : mineCoord;
    }

    /// <summary>After travel (post-redirection): marks boss-wait when the coordinate is the split map's shared boss point.</summary>
    public static void OnEnteredCoord(GameCoord entered)
    {
        if (!Active)
        {
            return;
        }

        var state = RunManager.Instance.DebugOnlyGetState();
        if (state?.Map is DuelSplitActMap split && entered == split.BossMapPoint.coord)
        {
            MarkLocalBossWait(new MapCoord(entered.col, entered.row));
        }
    }

    /// <summary>Local player reached the shared boss point: publish <c>InBossWait</c> (host broadcast / client request).</summary>
    public static void MarkLocalBossWait(MapCoord? fallbackCoord = null)
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null || !TryGetNetId(out var netId))
        {
            return;
        }

        var current = Table.TryGet(netId, out var known)
            ? known
            : new BranchState(
                state.CurrentActIndex,
                fallbackCoord ?? new MapCoord(0, 0),
                InBossWait: false);
        if (current.InBossWait)
        {
            return;
        }

        var waiting = current with { InBossWait = true };
        if (IsHost)
        {
            Publish(netId, waiting);
        }
        else
        {
            SendToHost(new DuelBranchMessage
            {
                PlayerNetId = netId,
                ActIndex = waiting.ActIndex,
                Col = waiting.Coord.Col,
                Row = waiting.Coord.Row,
                InBossWait = waiting.InBossWait,
            });
            PvpDuelLog.Info($"branch: boss-wait requested (act {waiting.ActIndex + 1}); awaiting host confirmation.");
        }
    }

    /// <summary>
    /// Act changed (SetActInternal postfix): reset the per-act table; the next
    /// act's votes re-assign branches. Raises <see cref="ActChanged"/> for the
    /// timer layer's t0 re-arm (ticket #22).
    /// </summary>
    public static void OnActChanged(int actIndex)
    {
        if (!Active)
        {
            return;
        }

        Table.Reset(actIndex);
        PvpDuelLog.Info($"branch table reset for act {actIndex + 1}.");
        ActChanged?.Invoke(actIndex);
    }

    /// <summary>
    /// Room-scope replacement (spec §7): while branches are diverged, branch
    /// rooms receive a branch-scoped run state view (only the branch's players);
    /// converged rooms keep the vanilla state.
    /// </summary>
    public static IRunState? ScopeRoom(IRunState? runState)
    {
        if (runState == null || !ValidationExcluded)
        {
            return runState;
        }

        var branchPlayers = new List<Player>();
        var mine = LocalBranch;
        foreach (var player in runState.Players)
        {
            if (mine is { } local && Table.TryGet(player.NetId, out var state) && state.Coord == local.Coord)
            {
                branchPlayers.Add(player);
            }
        }

        if (mine is { } scope)
        {
            PvpDuelLog.Info($"branch room scope: {branchPlayers.Count} player(s) in branch ({scope.Coord.Col},{scope.Coord.Row}).");
        }

        return new BranchScopedRunState(runState, branchPlayers);
    }

    // ---- Message handlers ----

    private static void OnBranchMessage(DuelBranchMessage message, ulong senderId)
    {
        try
        {
            var incoming = new BranchState(
                message.ActIndex, new MapCoord(message.Col, message.Row), message.InBossWait);
            if (!IsHost)
            {
                // Client: apply host-authoritative state only (no self-authority).
                if (Table.Set(message.PlayerNetId, incoming))
                {
                    BranchStateChanged?.Invoke(message.PlayerNetId, incoming);
                }

                return;
            }

            // Host receives a client *request*: apply, then rebroadcast — the
            // client acts only after this confirming broadcast.
            if (TryGetNetId(out var localNetId) && senderId == localNetId)
            {
                return;
            }

            if (Table.Set(message.PlayerNetId, incoming))
            {
                Broadcast(message.PlayerNetId, message.ActIndex, message.Col, message.Row, message.InBossWait);
                PvpDuelLog.Info($"branch: host confirmed player {message.PlayerNetId} state (act {message.ActIndex + 1}, bossWait={message.InBossWait}).");
                BranchStateChanged?.Invoke(message.PlayerNetId, incoming);
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"branch message handling failed: {ex}");
        }
    }

    private static void OnTimerMessage(DuelTimerMessage message, ulong senderId)
    {
        try
        {
            // Ticket #22: the timer layer consumes the host-clock elapsed
            // values for first-hand determination (spec §7). The host never
            // consumes — it is the single clock source and ignores its echo.
            Timing.ActTimer.OnTimerReceived(message.ActIndex, message.ElapsedMs);
            PvpDuelLog.Info($"branch: act timer received (act {message.ActIndex + 1}, {message.ElapsedMs} ms).");
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"timer message handling failed: {ex}");
        }
    }

    // ---- Internals ----

    /// <summary>
    /// Host-side publish: mutate the table, then broadcast (the only mutation
    /// path). The state-changed event fires after the broadcast so a following
    /// timer message keeps confirmation order (ticket #22).
    /// </summary>
    private static void Publish(ulong playerNetId, BranchState state)
    {
        if (Table.Set(playerNetId, state))
        {
            Broadcast(playerNetId, state.ActIndex, state.Coord.Col, state.Coord.Row, state.InBossWait);
            BranchStateChanged?.Invoke(playerNetId, state);
        }
    }

    private static void Broadcast(ulong playerNetId, int actIndex, int col, int row, bool inBossWait)
    {
        RunManager.Instance.NetService?.SendMessage(new DuelBranchMessage
        {
            PlayerNetId = playerNetId,
            ActIndex = actIndex,
            Col = col,
            Row = row,
            InBossWait = inBossWait,
        });
    }

    private static void SendToHost(DuelBranchMessage message)
    {
        RunManager.Instance.NetService?.SendMessage(message);
    }

    private static bool TryGetNetId(out ulong netId)
    {
        netId = RunManager.Instance.NetService?.NetId ?? 0;
        return netId != 0;
    }


    private static Player? OtherPlayer()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null || !TryGetNetId(out var localNetId))
        {
            return null;
        }

        foreach (var player in state.Players)
        {
            if (player.NetId != localNetId)
            {
                return player;
            }
        }

        return null;
    }
}
