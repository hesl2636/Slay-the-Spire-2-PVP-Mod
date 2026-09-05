using System.Diagnostics.CodeAnalysis;

using Godot;

using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Core.Duel;
using PvpDuel.Encounters;
using PvpDuel.Localization;
using PvpDuel.Logging;
using PvpDuel.Net;
using PvpDuel.Save;

namespace PvpDuel.Outcomes;

/// <summary>
/// Mirrored duel-outcome settlement (ticket #23, spec §4.2/§8). Takes over the
/// T7 <see cref="DuelResultSource.SettledSink"/>: a locally resolved outcome is
/// no longer recorded directly — it is reported to the peer through
/// <see cref="DuelOutcomeMessage"/> and fed into the pure
/// <see cref="DuelOutcomeConsensus"/>. Only full agreement settles: the agreed
/// result lands in <see cref="DuelSaveState.History"/> and the per-act events
/// of <see cref="DuelOutcomeDispatcher"/> fire (act 1/2 → ancient first-pick
/// rights, act 3 → terminal hook). Disagreement, an undecodable report, or a
/// report that never arrives applies divergence semantics (spec §8): the mod
/// banner (<c>PVP_ERROR_DIVERGENCE</c>) shows and the session is dropped with
/// the official <see cref="NetError.StateDivergence"/> teardown — both ends run
/// the same abort, and the vanilla pump routes our end into
/// <c>RunManager.LocalPlayerDisconnected</c> (return to main menu + error
/// popup), so the divergence is never silently continued.
///
/// Single-machine duels (fake-opponent harness, no net session) settle
/// immediately — there is no remote end to agree with, preserving the T7
/// behavior. Message-handler subscription follows the BranchSync lazy pattern:
/// the net service does not exist at mod init and is swapped per session.
/// </summary>
internal static class DuelOutcomeSync
{
    private const string DivergenceLocKey = "PVP_ERROR_DIVERGENCE";
    private const int WatchdogPollMs = 250;

    private static readonly object Gate = new();

    private static bool _installed;
    private static INetGameService? _subscribedService;

    private static DuelOutcomeConsensus? _pending;
    private static CancellationTokenSource? _pendingWatchdog;

    /// <summary>
    /// Debug switch (acceptance #2): flips winner/loser on the local report so
    /// the two ends disagree on purpose — both ends must abort and show the
    /// divergence error. Deliberately NOT part of PvpConfig (must never leak
    /// into the config hash, same rationale as the harness flag); driven by the
    /// <c>pvpdueloutcome divergence</c> console command.
    /// </summary>
    public static bool DebugForceDivergence { get; set; }

    /// <summary>
    /// Wires the local outcome source, the room window and the message handler.
    /// Called once from the mod initializer AFTER
    /// <c>LossInterceptPatch.Install</c> (rebinding the sink is the point);
    /// idempotent.
    /// </summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        DuelResultSource.SettledSink = OnLocalOutcome;
        RunManager.Instance.RoomEntered += OnRoomEntered;
        EnsureSubscribed();
        PvpDuelLog.Info("duel outcome sync installed (both-end consensus over DuelOutcomeMessage).");
    }

    private static void OnRoomEntered()
    {
        try
        {
            EnsureSubscribed();

            var state = RunManager.Instance.DebugOnlyGetState();
            if (state?.CurrentRoom is CombatRoom { Encounter: PvpDuelEncounter })
            {
                lock (Gate)
                {
                    DropStalePendingLocked("a new duel room opened while a settlement was still unresolved");
                }
            }
        }
        catch (Exception ex)
        {
            // Bookkeeping must never break room transitions.
            PvpDuelLog.Error($"duel outcome room-enter hook failed: {ex}");
        }
    }

    /// <summary>
    /// Registers the message handler on the current net service; idempotent and
    /// re-subscribes whenever the game swaps services per session.
    /// </summary>
    private static void EnsureSubscribed()
    {
        var service = RunManager.Instance.NetService;
        if (service == null || ReferenceEquals(service, _subscribedService))
        {
            return;
        }

        if (_subscribedService != null)
        {
            _subscribedService.UnregisterMessageHandler<DuelOutcomeMessage>(OnOutcomeMessage);
        }

        _subscribedService = service;
        service.RegisterMessageHandler<DuelOutcomeMessage>(OnOutcomeMessage);
        PvpDuelLog.Info("duel outcome sync subscribed (DuelOutcomeMessage handler).");
    }

    // ---- Local outcome (SettledSink) ----

    private static void OnLocalOutcome(DuelResult local)
    {
        try
        {
            if (!SessionAlive())
            {
                SettleImmediately(local, "single-machine duel — no remote end to agree with");
                return;
            }

            DuelOutcomeVerdict verdict;
            DuelResult? settled;
            DuelResult? localSnap;
            DuelResult? remoteSnap;
            lock (Gate)
            {
                DropStalePendingLocked("a fresh local outcome replaced the unfinished wait");
                var consensus = new DuelOutcomeConsensus();
                _pending = consensus;

                var report = DebugForceDivergence ? FlipForDebug(local) : local;
                consensus.ReportLocal(report);
                SendReport(report);
                if (DebugForceDivergence)
                {
                    PvpDuelLog.Warn("DEBUG divergence switch is ON: local report flipped — the session will abort.");
                }

                if (consensus.Verdict == DuelOutcomeVerdict.Pending)
                {
                    StartWatchdogLocked(consensus);
                }
                else
                {
                    // Resolved immediately against a buffered remote report:
                    // clear the field so late echoes hit the settled-act check
                    // instead of re-resolving this consensus.
                    ClearPendingLocked();
                }

                (verdict, settled, localSnap, remoteSnap) = Snapshot(consensus);
            }

            if (verdict == DuelOutcomeVerdict.Pending)
            {
                PvpDuelLog.Info($"duel outcome: local report sent ({Describe(localSnap)}) — awaiting remote agreement.");
            }

            ResolveVerdict(verdict, settled, localSnap, remoteSnap);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"duel outcome settlement of the local report failed: {ex}");
        }
    }

    // ---- Remote outcome (message handler) ----

    private static void OnOutcomeMessage(DuelOutcomeMessage message, ulong senderId)
    {
        try
        {
            if (!SessionAlive())
            {
                return; // no run/net session — mod messages cannot belong to one
            }

            DuelOutcomeVerdict verdict;
            DuelResult? settled;
            DuelResult? localSnap;
            DuelResult? remoteSnap;
            lock (Gate)
            {
                var consensus = _pending;
                if (consensus == null)
                {
                    if (TryDecode(message, out var buffered))
                    {
                        // An echo/duplicate for an act this run already settled
                        // (History is the run-scoped truth, restored on load)
                        // must not open a new wait — only open acts buffer.
                        if (DuelSaveState.History.GetForAct(buffered.ActIndex) != null)
                        {
                            PvpDuelLog.Info(
                                $"duel outcome: remote report for already-settled act {buffered.ActIndex + 1}; ignoring.");
                            return;
                        }

                        // Remote-first: the peer's report can cross ours in
                        // flight — buffer it; our own report lands seconds
                        // later (same vanilla combat teardown) and resolves
                        // the pair.
                        _pending = consensus = new DuelOutcomeConsensus();
                        consensus.ReportRemote(buffered);
                        StartWatchdogLocked(consensus);
                        PvpDuelLog.Info($"duel outcome: remote report buffered before ours ({Describe(buffered)}).");
                    }
                    else
                    {
                        PvpDuelLog.Error(
                            $"duel outcome: undecodable remote report (reason byte {message.Reason}) with no pending settlement; ignoring.");
                    }

                    return;
                }

                if (TryDecode(message, out var remote))
                {
                    consensus.ReportRemote(remote);
                }
                else
                {
                    PvpDuelLog.Error($"duel outcome: undecodable remote report (reason byte {message.Reason}).");
                    consensus.ReportUndecodableRemote();
                }

                if (consensus.Verdict == DuelOutcomeVerdict.Pending)
                {
                    return;
                }

                (verdict, settled, localSnap, remoteSnap) = Snapshot(consensus);
                ClearPendingLocked();
            }

            ResolveVerdict(verdict, settled, localSnap, remoteSnap);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"duel outcome message handling failed: {ex}");
        }
    }

    // ---- Verdict resolution ----

    private static void ResolveVerdict(
        DuelOutcomeVerdict verdict, DuelResult? settled, DuelResult? local, DuelResult? remote)
    {
        switch (verdict)
        {
            case DuelOutcomeVerdict.Settled:
                FinishSettlement(settled!);
                break;
            case DuelOutcomeVerdict.Diverged:
                AbortOnDivergence(local, remote);
                break;
        }
    }

    private static void FinishSettlement(DuelResult settled)
    {
        DuelSaveState.History.Record(settled);
        DuelOutcomeDispatcher.Dispatch(settled);
        PvpDuelLog.Info(
            $"duel outcome settled by both ends ({Describe(settled)}) — recorded in history and dispatched.");
    }

    private static void SettleImmediately(DuelResult local, string why)
    {
        DuelSaveState.History.Record(local);
        DuelOutcomeDispatcher.Dispatch(local);
        PvpDuelLog.Info($"duel outcome settled locally ({why}): {Describe(local)} — recorded and dispatched.");
    }

    /// <summary>
    /// Divergence abort (spec §8 — 不一致/超时视同 divergence，禁止静默继续):
    /// logs both reports, shows the localized divergence banner and drops the
    /// session with the official StateDivergence teardown. Called on the main
    /// thread (message handler) or marshaled there by the watchdog.
    /// </summary>
    private static void AbortOnDivergence(DuelResult? local, DuelResult? remote)
    {
        PvpDuelLog.Error(
            $"duel outcome DIVERGENCE: local [{Describe(local)}] vs remote [{Describe(remote)}] — aborting the session (spec §8).");
        ShowDivergenceBanner();
        try
        {
            // Both ends detect the disagreement and run this same abort; the
            // vanilla pump routes our end into RunManager's disconnect flow
            // (return to main menu + official error popup).
            RunManager.Instance.NetService?.Disconnect(NetError.StateDivergence);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"divergence disconnect failed: {ex}");
        }
    }

    /// <summary>
    /// Banner-style overlay (main-menu banner treatment) on the game root:
    /// non-interactive, survives the run teardown into the main menu so the
    /// player sees the localized divergence message next to the official popup.
    /// </summary>
    private static void ShowDivergenceBanner()
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
                Name = "PvpDuelOutcomeDivergenceBanner",
                Text = Loc.Text(DivergenceLocKey),
            };
            banner.AddThemeColorOverride("font_color", Colors.Red);
            banner.AddThemeColorOverride("font_outline_color", Colors.Black);
            banner.AddThemeConstantOverride("outline_size", 6);
            banner.Position = new Vector2(24, 140);
            banner.Size = new Vector2(900, 120);
            banner.MouseFilter = Control.MouseFilterEnum.Ignore;
            banner.ZIndex = 1000;
            game.AddChild(banner);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"divergence banner display failed: {ex}");
        }
    }

    // ---- Watchdog (remote-report timeout → divergence) ----

    private static void StartWatchdogLocked(DuelOutcomeConsensus consensus)
    {
        _pendingWatchdog?.Cancel();
        var cts = new CancellationTokenSource();
        _pendingWatchdog = cts;
        var started = DateTimeOffset.UtcNow;
        _ = Task.Run(() => WatchAsync(consensus, started, cts.Token));
    }

    private static async Task WatchAsync(DuelOutcomeConsensus consensus, DateTimeOffset started, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(WatchdogPollMs, ct);

                DuelResult? local;
                bool overdue;
                lock (Gate)
                {
                    if (!ReferenceEquals(_pending, consensus) || consensus.Verdict != DuelOutcomeVerdict.Pending)
                    {
                        return; // resolved or replaced while we waited
                    }

                    overdue = consensus.WaitOverdue(DateTimeOffset.UtcNow - started);
                    if (overdue)
                    {
                        local = consensus.Local;
                        ClearPendingLocked();
                    }
                    else
                    {
                        local = null;
                    }
                }

                if (overdue)
                {
                    PvpDuelLog.Error(
                        "duel outcome: remote report overdue — divergence semantics apply (spec §8): aborting the session.");
                    // Off the game thread here: marshal the abort to the main thread.
                    Callable.From(() => AbortOnDivergence(local, null)).CallDeferred();
                    return;
                }

                if (!SessionAlive())
                {
                    lock (Gate)
                    {
                        if (!ReferenceEquals(_pending, consensus) || consensus.Verdict != DuelOutcomeVerdict.Pending)
                        {
                            return;
                        }

                        ClearPendingLocked();
                    }
                    PvpDuelLog.Warn("duel outcome wait dropped: the run/session ended while awaiting the remote report.");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The wait resolved or was replaced — nothing to do.
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"duel outcome watchdog failed: {ex}");
        }
    }

    // ---- Internals ----

    private static void SendReport(DuelResult report)
    {
        var wire = DuelOutcomeCodec.Encode(report);
        RunManager.Instance.NetService?.SendMessage(new DuelOutcomeMessage
        {
            ActIndex = wire.ActIndex,
            WinnerNetId = wire.WinnerNetId,
            LoserNetId = wire.LoserNetId,
            Reason = wire.Reason,
        });
    }

    private static bool TryDecode(DuelOutcomeMessage message, [NotNullWhen(true)] out DuelResult? result) =>
        DuelOutcomeCodec.TryDecode(message.ActIndex, message.WinnerNetId, message.LoserNetId, message.Reason, out result);

    private static (DuelOutcomeVerdict Verdict, DuelResult? Settled, DuelResult? Local, DuelResult? Remote) Snapshot(
        DuelOutcomeConsensus consensus) =>
        (consensus.Verdict, consensus.Settled, consensus.Local, consensus.Remote);

    private static void ClearPendingLocked()
    {
        _pending = null;
        _pendingWatchdog?.Cancel();
        _pendingWatchdog = null;
    }

    private static void DropStalePendingLocked(string why)
    {
        if (_pending == null)
        {
            return;
        }

        PvpDuelLog.Warn($"duel outcome: dropping an unfinished wait ({why}).");
        ClearPendingLocked();
    }

    /// <summary>True only inside a live 2-player net run (the consensus population).</summary>
    private static bool SessionAlive()
    {
        var manager = RunManager.Instance;
        return manager != null
            && manager.NetService?.Type.IsMultiplayer() == true
            && manager.DebugOnlyGetState() != null;
    }

    /// <summary>Live consensus snapshot for the debug console (acceptance tooling).</summary>
    internal static string PendingDescription
    {
        get
        {
            lock (Gate)
            {
                return _pending == null
                    ? "none"
                    : $"verdict {_pending.Verdict}, local [{Describe(_pending.Local)}], remote [{Describe(_pending.Remote)}]";
            }
        }
    }

    private static DuelResult FlipForDebug(DuelResult result) =>
        result with { WinnerNetId = result.LoserNetId, LoserNetId = result.WinnerNetId };

    private static string Describe(DuelResult? result = null) =>
        result == null
            ? "<none>"
            : $"act {result.ActIndex + 1}, winner {result.WinnerNetId}, loser {result.LoserNetId}, reason {result.Reason}";
}

/// <summary>
/// Console wrapper for the settlement layer (DebugOnly ⇒ modded console only,
/// per NDevConsole.cs:359 shouldAllowDebugCommands). Subcommands:
/// divergence on|off|status (acceptance #2 —人为制造不一致), dump (both ends'
/// DuelHistory dump for the acceptance-1 comparison), pending (live consensus
/// state).
/// </summary>
public sealed class DuelOutcomeConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "pvpdueloutcome";

    public override string Args => "divergence|dump|pending";

    public override string Description =>
        "PvP Duel outcome settlement (debug): 'divergence on' flips the next local duel report so both ends diverge on purpose " +
        "(session must abort on both ends with the divergence error); 'dump' prints the settled duel history " +
        "(compare between both ends); 'pending' prints the live consensus state.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var sub = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        switch (sub)
        {
            case "divergence":
                return Divergence(args.Length > 1 ? args[1].ToLowerInvariant() : "status");
            case "dump":
                return Dump();
            case "pending":
                return Pending();
            case "status":
                return new CmdResult(
                    true,
                    $"pvpdueloutcome: divergence {(DuelOutcomeSync.DebugForceDivergence ? "ON" : "off")}; " +
                    $"settled results: {DuelSaveState.History.Results.Count}.");
            default:
                return new CmdResult(false, $"unknown subcommand '{args[0]}' (use divergence|dump|pending|status)");
        }
    }

    private static CmdResult Divergence(string arg)
    {
        switch (arg)
        {
            case "on":
                DuelOutcomeSync.DebugForceDivergence = true;
                return new CmdResult(true, "DEBUG divergence ON — the next duel report this end sends is flipped; both sessions must abort.");
            case "off":
                DuelOutcomeSync.DebugForceDivergence = false;
                return new CmdResult(true, "DEBUG divergence OFF.");
            case "status":
                return new CmdResult(true, $"DEBUG divergence: {(DuelOutcomeSync.DebugForceDivergence ? "ON" : "off")}.");
            default:
                return new CmdResult(false, $"unknown divergence argument '{arg}' (use on|off|status)");
        }
    }

    private static CmdResult Dump()
    {
        var results = DuelSaveState.History.Results;
        if (results.Count == 0)
        {
            return new CmdResult(true, "DuelHistory is empty (no settled duels this run).");
        }

        var lines = results.Select(r =>
            $"act {r.ActIndex + 1}: winner {r.WinnerNetId} loser {r.LoserNetId} reason {r.Reason}");
        return new CmdResult(true, $"DuelHistory ({results.Count}):\n" + string.Join("\n", lines));
    }

    private static CmdResult Pending() =>
        new CmdResult(true, $"duel outcome consensus: {DuelOutcomeSync.PendingDescription}.");
}
