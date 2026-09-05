using System.Reflection;

using Godot;

using HarmonyLib;

using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Branching;
using PvpDuel.Config;
using PvpDuel.Core.Disconnect;
using PvpDuel.Core.Duel;
using PvpDuel.Core.Save;
using PvpDuel.Duel;
using PvpDuel.Localization;
using PvpDuel.Logging;
using PvpDuel.Save;

namespace PvpDuel.Disconnect;

/// <summary>
/// Duel disconnect pause + reconnect-timeout forfeit (ticket #25, spec
/// §2.1(7)/§8). Zero Harmony: everything hangs off official events, so the
/// official disconnect paths stay byte-identical (regression red line — a
/// disconnect while running the map follows vanilla co-op behavior untouched).
///
/// Host authority only. When the peer drops inside a duel combat
/// (<c>RunLobby.RemotePlayerDisconnected</c> + <see cref="DuelScope.IsDuelRoom"/>),
/// the host opens the pure <see cref="DisconnectWaitMachine"/> wait with
/// <c>PvpConfig.DisconnectTimeoutSec</c> on its own clock and shows the
/// localized countdown overlay (<c>PVP_DUEL_RECONNECT</c>). The peer rejoins
/// through the official rejoin flow (the run + full combat state ride
/// <c>ClientRejoinResponseMessage</c>) → <see cref="DisconnectWaitPhase.Resumed"/>
/// clears the pause. The deadline expiring resolves exactly one
/// <c>DuelResult(Reason=DisconnectTimeout)</c> forfeit through the settled sink
/// — the T11 settlement layer records and dispatches it (the peer is absent,
/// so the settlement layer's peer-absent rule settles immediately instead of
/// waiting for a report that can never arrive) and the per-act dispatch moves
/// the winner on (next act / terminal flow).
///
/// The observing client never hosts a wait: a host drop tears the client down
/// through <c>RunManager.LocalPlayerDisconnected</c> (official), and the mod
/// does not interfere.
///
/// Rejoin mod-state resume: the rejoined end's side channel is file-local to
/// the host, so state arrives over the wire on request — the client flags its
/// own session loss (official <c>RunLobby.LocalPlayerDisconnected</c>) and asks
/// once a run is live again (<see cref="DuelStateBackfillRequestMessage"/>);
/// the host answers with the current side-channel snapshot
/// (<see cref="DuelStateBackfillMessage"/>) and the client applies it through
/// the same restore path a load uses (spec §4.3 payload, ticket #21 codec).
/// </summary>
public static class DisconnectWatch
{
    private const int PollMs = 250;
    private const string ReconnectLocKey = "PVP_DUEL_RECONNECT";
    private const string ForfeitLocKey = "PVP_DUEL_DISCONNECT_TIMEOUT";

    private static readonly object Gate = new();

    private static bool _installed;
    private static RunLobby? _subscribedLobby;
    private static INetGameService? _subscribedService;

    private static DisconnectWaitMachine? _machine;
    private static CancellationTokenSource? _ticker;
    private static int _displayedSeconds = -1;
    private static Label? _countdownLabel;
    private static Label? _forfeitBanner;

    private static string? _pendingBackfillJson;
    private static bool _needsBackfill;
    private static string? _requestedRunKey;

    private static FieldInfo? _branchTableField;

    /// <summary>Wires the official events. Called once from the mod initializer; idempotent.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        RunManager.Instance.RoomEntered += OnRoomEntered;
        RunManager.Instance.RoomExited += OnRoomExited;
        EnsureLobbySubscribed();
        EnsureNetSubscribed();
        PvpDuelLog.Info("disconnect watch installed (duel pause, host reconnect timeout, rejoin state backfill).");
    }

    // ---- Room window (subscription + wait scope + backfill triggers) ----

    private static void OnRoomEntered()
    {
        try
        {
            EnsureLobbySubscribed();
            EnsureNetSubscribed();
            RemoveForfeitBanner();
            TryApplyBackfill();
            RequestBackfillIfNeeded();
        }
        catch (Exception ex)
        {
            // Bookkeeping must never break room transitions.
            PvpDuelLog.Error($"disconnect watch room-enter hook failed: {ex}");
        }
    }

    private static void OnRoomExited()
    {
        try
        {
            lock (Gate)
            {
                if (_machine is { Phase: DisconnectWaitPhase.Waiting })
                {
                    // The duel room went away without an expiry (the outcome
                    // settled through the normal pipeline) — the wait is moot.
                    _machine.Reset();
                    StopTickerLocked();
                }
            }

            HideCountdownOnMain();
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect watch room-exit hook failed: {ex}");
        }
    }

    /// <summary>Registers the lobby/net handlers of the current run; idempotent, re-subscribes per session.</summary>
    private static void EnsureLobbySubscribed()
    {
        var lobby = RunManager.Instance.RunLobby;
        if (lobby == null || ReferenceEquals(lobby, _subscribedLobby))
        {
            return;
        }

        if (_subscribedLobby != null)
        {
            _subscribedLobby.RemotePlayerDisconnected -= OnRemotePlayerDisconnected;
            _subscribedLobby.PlayerRejoined -= OnPlayerRejoined;
            _subscribedLobby.LocalPlayerDisconnected -= OnLocalDisconnected;
        }

        _subscribedLobby = lobby;
        lobby.RemotePlayerDisconnected += OnRemotePlayerDisconnected;
        lobby.PlayerRejoined += OnPlayerRejoined;
        lobby.LocalPlayerDisconnected += OnLocalDisconnected;
    }

    private static void EnsureNetSubscribed()
    {
        var service = RunManager.Instance.NetService;
        if (service == null || ReferenceEquals(service, _subscribedService))
        {
            return;
        }

        if (_subscribedService != null)
        {
            _subscribedService.UnregisterMessageHandler<DuelStateBackfillMessage>(OnBackfillMessage);
            _subscribedService.UnregisterMessageHandler<DuelStateBackfillRequestMessage>(OnBackfillRequest);
        }

        _subscribedService = service;
        service.RegisterMessageHandler<DuelStateBackfillMessage>(OnBackfillMessage);
        service.RegisterMessageHandler<DuelStateBackfillRequestMessage>(OnBackfillRequest);
    }

    // ---- Disconnect (host side) ----

    private static void OnRemotePlayerDisconnected(ulong playerId)
    {
        try
        {
            // Host authority only: a client observing a host drop is torn down
            // by the official path (RunManager.LocalPlayerDisconnected) — the
            // mod never interferes with it (spec §5.1 red line).
            if (!BranchSync.IsHost)
            {
                return;
            }

            // Pause scope: duels only. Map-traversal disconnects follow the
            // official co-op behavior unchanged (spec §8 regression boundary).
            if (!DuelScope.IsDuelRoom)
            {
                return;
            }

            var timeout = WaitTimeout();
            lock (Gate)
            {
                // A fresh machine per drop: the configured timeout is read at
                // event time (config may change between waits) and the vanilla
                // drop event is single-shot per socket loss.
                _machine = new DisconnectWaitMachine(timeout);
                _machine.Begin(playerId);
                _displayedSeconds = -1;
                StartTickerLocked();
            }

            Callable.From(UpdateCountdownOnMain).CallDeferred();
            PvpDuelLog.Warn(
                $"duel disconnect: peer {playerId} dropped mid-duel — pausing, reconnect timeout {(int)timeout.TotalSeconds}s (host clock).");
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect watch begin failed: {ex}");
        }
    }

    private static void StartTickerLocked()
    {
        _ticker?.Cancel();
        var cts = new CancellationTokenSource();
        _ticker = cts;
        _ = Task.Run(() => TickAsync(cts.Token));
    }

    private static void StopTickerLocked()
    {
        _ticker?.Cancel();
        _ticker = null;
    }

    private static async Task TickAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(PollMs, ct);

                ulong waitingFor;
                int seconds;
                bool expired;
                lock (Gate)
                {
                    var machine = _machine;
                    if (machine == null || machine.Phase != DisconnectWaitPhase.Waiting)
                    {
                        return;
                    }

                    waitingFor = machine.DisconnectedNetId;
                    seconds = machine.SecondsRemaining;
                    machine.Poll();
                    expired = machine.Phase == DisconnectWaitPhase.Expired;

                    // The duel room went away without an expiry — wait dropped.
                    if (!expired && !DuelScope.IsDuelRoom)
                    {
                        machine.Reset();
                        StopTickerLocked();
                        seconds = -1;
                    }
                    else if (expired)
                    {
                        StopTickerLocked();
                    }
                }

                if (seconds == -1)
                {
                    Callable.From(HideCountdownOnMain).CallDeferred();
                    PvpDuelLog.Info("disconnect watch: duel room left while waiting — pause dropped (no forfeit).");
                    return;
                }

                if (expired)
                {
                    PvpDuelLog.Warn(
                        $"duel disconnect: reconnect timeout elapsed — peer {waitingFor} forfeits (DuelResult DisconnectTimeout).");
                    Callable.From(HideCountdownOnMain).CallDeferred();
                    var loserNetId = waitingFor;
                    Callable.From(() => SettleForfeit(loserNetId)).CallDeferred();
                    return;
                }

                if (seconds != _displayedSeconds)
                {
                    Callable.From(UpdateCountdownOnMain).CallDeferred();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The wait resolved or was replaced — nothing to do.
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect watch ticker failed: {ex}");
        }
    }

    /// <summary>
    /// Builds the forfeit result and hands it to the settled sink — the T11
    /// settlement layer records it in history and dispatches the per-act flow
    /// (the peer is absent, so the settlement resolves immediately instead of
    /// applying divergence semantics to a report that can never arrive).
    /// Main thread only (deferred by the ticker).
    /// </summary>
    private static void SettleForfeit(ulong loserNetId)
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null)
            {
                return;
            }

            var localNetId = LocalContext.NetId ?? 0;
            if (localNetId == 0)
            {
                PvpDuelLog.Error("disconnect forfeit aborted: local NetId unavailable.");
                return;
            }

            var actIndex = DuelScope.Current?.ActIndex ?? state.CurrentActIndex;
            var result = new DuelResult(actIndex, localNetId, loserNetId, DuelEndReason.DisconnectTimeout);
            ShowForfeitBanner(loserNetId);
            PvpDuelLog.Info(
                $"duel forfeit: act {actIndex + 1}, winner {localNetId}, loser {loserNetId} (DisconnectTimeout) — routed through the settlement pipeline.");
            DuelResultSource.SettledSink?.Invoke(result);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect forfeit settle failed: {ex}");
        }
    }

    // ---- Rejoin (both ends) ----

    private static void OnPlayerRejoined(RunLobbyPlayer player)
    {
        try
        {
            lock (Gate)
            {
                _machine?.NoteRejoined(player.id);
                if (_machine is { Phase: DisconnectWaitPhase.Resumed })
                {
                    StopTickerLocked();
                }
            }

            Callable.From(HideCountdownOnMain).CallDeferred();
            PvpDuelLog.Info($"disconnect watch: player {player.id} rejoined — any duel wait cleared.");

            if (BranchSync.IsHost)
            {
                SendBackfill(player.id);
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect watch rejoin handling failed: {ex}");
        }
    }

    /// <summary>
    /// The local end lost its session (official event; the official teardown
    /// proceeds untouched). When a run is live again, its materialization
    /// carries no file-local side channel on this end — flag the wire backfill
    /// request (host answers with the current snapshot).
    /// </summary>
    private static void OnLocalDisconnected() => _needsBackfill = true;

    private static void OnBackfillRequest(DuelStateBackfillRequestMessage message, ulong senderId)
    {
        try
        {
            if (!BranchSync.IsHost)
            {
                return;
            }

            SendBackfill(senderId);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect watch backfill request handling failed: {ex}");
        }
    }

    private static void SendBackfill(ulong peerId)
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            return;
        }

        var runKey = DuelRunKey.Of(state);
        if (runKey.Length == 0)
        {
            return;
        }

        var payload = DuelSaveMapper.Capture(
            runKey, PvpConfigStore.LoadOrDefault().Hash, BranchTableRef(),
            DuelSaveState.History, DuelSaveState.AncientPicks);
        var json = DuelSaveCodec.Serialize(payload);
        if (RunManager.Instance.NetService is INetHostGameService host)
        {
            host.SendMessage(new DuelStateBackfillMessage { PayloadJson = json }, peerId);
            PvpDuelLog.Info(
                $"disconnect watch: state backfill sent to peer {peerId} ({payload.Branches.Count} branches, {payload.DuelResults.Count} results, {payload.AncientPicks.Count} picks).");
        }
    }

    private static void OnBackfillMessage(DuelStateBackfillMessage message, ulong senderId)
    {
        try
        {
            if (BranchSync.IsHost)
            {
                return;
            }

            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null)
            {
                // The rejoin boot may not have materialized the run yet — keep
                // the snapshot for the next room entry.
                _pendingBackfillJson = message.PayloadJson;
                return;
            }

            ApplyBackfill(message.PayloadJson, state);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect watch backfill handling failed: {ex}");
        }
    }

    private static void TryApplyBackfill()
    {
        var json = _pendingBackfillJson;
        if (json == null)
        {
            return;
        }

        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            return;
        }

        _pendingBackfillJson = null;
        ApplyBackfill(json, state);
    }

    private static void ApplyBackfill(string json, RunState state)
    {
        var runKey = DuelRunKey.Of(state);
        var (payload, status) = DuelSaveCodec.TryRead(json);
        if (status != DuelSaveReadStatus.Ok || payload == null)
        {
            PvpDuelLog.Warn($"disconnect watch: backfill payload rejected ({status}); mod state unchanged.");
            return;
        }

        if (!string.Equals(payload.RunKey, runKey, StringComparison.Ordinal))
        {
            PvpDuelLog.Warn($"disconnect watch: backfill run key '{payload.RunKey}' does not match the current run '{runKey}'; ignored.");
            return;
        }

        DuelSaveMapper.Restore(payload, BranchTableRef(), DuelSaveState.History,
            DuelSaveState.MutableAncientPicks, GameLogSink.Instance);
        DuelSaveState.MarkRestored(payload.ConfigHash);
        _needsBackfill = false;
        PvpDuelLog.Info(
            $"disconnect watch: mod state backfill applied ({payload.Branches.Count} branches, {payload.DuelResults.Count} results, {payload.AncientPicks.Count} picks).");
    }

    /// <summary>
    /// Client side: ask the host for the current mod state exactly after the
    /// local session was lost and a run is live again (a rejoin materializes
    /// the run without the host's file-local side channel). The flag clears on
    /// a successful apply; a request for a run key is not repeated (reliable
    /// transport) so a normal session never sends spurious requests.
    /// </summary>
    private static void RequestBackfillIfNeeded()
    {
        if (BranchSync.IsHost || !_needsBackfill)
        {
            return;
        }

        var service = RunManager.Instance.NetService;
        var state = RunManager.Instance.DebugOnlyGetState();
        if (service == null || service.Type != NetGameType.Client || state == null)
        {
            return;
        }

        var runKey = DuelRunKey.Of(state);
        if (runKey.Length == 0 || string.Equals(_requestedRunKey, runKey, StringComparison.Ordinal))
        {
            return;
        }

        _requestedRunKey = runKey;
        service.SendMessage(new DuelStateBackfillRequestMessage());
        PvpDuelLog.Info("disconnect watch: state backfill requested from the host.");
    }

    // ---- Countdown / forfeit overlays (main-thread Godot labels) ----

    private static void UpdateCountdownOnMain()
    {
        try
        {
            int seconds;
            lock (Gate)
            {
                var machine = _machine;
                if (machine == null || machine.Phase != DisconnectWaitPhase.Waiting)
                {
                    return;
                }

                seconds = machine.SecondsRemaining;
                _displayedSeconds = seconds;
            }

            var game = NGame.Instance;
            if (game == null)
            {
                return;
            }

            if (_countdownLabel != null && !GodotObject.IsInstanceValid(_countdownLabel))
            {
                _countdownLabel = null;
            }

            _countdownLabel ??= CreateOverlayLabel(
                "PvpDuelReconnectCountdown", Colors.Orange, new Vector2(24, 260));
            if (_countdownLabel.GetParent() == null)
            {
                game.AddChild(_countdownLabel);
            }

            _countdownLabel.Text = Loc.Text(ReconnectLocKey, seconds);
            _countdownLabel.Visible = true;
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect countdown display failed: {ex}");
        }
    }

    private static void HideCountdownOnMain()
    {
        try
        {
            if (_countdownLabel != null && GodotObject.IsInstanceValid(_countdownLabel))
            {
                _countdownLabel.Visible = false;
            }
            else
            {
                _countdownLabel = null;
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect countdown hide failed: {ex}");
        }
    }

    private static void ShowForfeitBanner(ulong loserNetId)
    {
        try
        {
            var game = NGame.Instance;
            if (game == null)
            {
                return;
            }

            if (_forfeitBanner != null && GodotObject.IsInstanceValid(_forfeitBanner))
            {
                _forfeitBanner.QueueFree();
            }

            _forfeitBanner = null;
            _forfeitBanner = CreateOverlayLabel(
                "PvpDuelDisconnectForfeitBanner", Colors.OrangeRed, new Vector2(24, 200));
            _forfeitBanner.Text = Loc.Text(ForfeitLocKey, loserNetId, WaitTimeoutSeconds());
            game.AddChild(_forfeitBanner);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"disconnect forfeit banner display failed: {ex}");
        }
    }

    private static void RemoveForfeitBanner()
    {
        if (_forfeitBanner == null)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(_forfeitBanner))
        {
            _forfeitBanner.QueueFree();
        }

        _forfeitBanner = null;
    }

    /// <summary>Banner-style overlay (main-menu banner treatment): non-interactive, never blocks input.</summary>
    private static Label CreateOverlayLabel(string name, Color color, Vector2 position)
    {
        var label = new Label
        {
            Name = name,
            Text = string.Empty,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
        label.Position = position;
        label.Size = new Vector2(900, 60);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.ZIndex = 1000;
        return label;
    }

    // ---- Config / internals ----

    private static TimeSpan WaitTimeout() => TimeSpan.FromSeconds(PvpConfigStore.LoadOrDefault().Config.DisconnectTimeoutSec);

    private static int WaitTimeoutSeconds() => PvpConfigStore.LoadOrDefault().Config.DisconnectTimeoutSec;

    /// <summary>
    /// The branch table lives in the branch layer (BranchSync owns the single
    /// instance); read reflectively instead of widening that file's surface
    /// (same approach as the save side channel).
    /// </summary>
    private static BranchTable? BranchTableRef()
    {
        _branchTableField ??= AccessTools.Field(typeof(BranchSync), "Table");
        return _branchTableField?.GetValue(null) as BranchTable;
    }
}
