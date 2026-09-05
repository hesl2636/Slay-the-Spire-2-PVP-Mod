using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Core.Ancients;
using PvpDuel.Core.Duel;
using PvpDuel.Core.Save;
using PvpDuel.Core.Text;
using PvpDuel.Localization;
using PvpDuel.Logging;
using PvpDuel.Net;
using PvpDuel.Save;

namespace PvpDuel.Ancients;

/// <summary>
/// The Ancient first-pick flow driver (ticket #24, spec §7 结算层). Consumes
/// the mirrored act 1/2 settlements (<see cref="DuelOutcomeDispatcher"/> via
/// <see cref="AncientFlowGate"/>) and opens the programmatically built
/// <see cref="DuelAncientEventModel"/> room on each end once the duel combat
/// room has closed. Subscribes <see cref="AncientPickMessage"/> (not
/// location-targeted, like <c>DuelOutcomeMessage</c>): the winner's pick
/// lands within milliseconds on the loser's end, records the mutex state and
/// drives the loser's option mirror lock — the vanilla
/// <c>OptionIndexChosenMessage</c> mirror remains the state-machine channel.
/// Room-entry timing is session-scoped (the settlement fires during the
/// combat teardown — the picker opens only from a later non-combat room).
/// </summary>
internal static class DuelAncientFlow
{
    private static bool _installed;
    private static INetGameService? _subscribedService;
    private static int? _pendingAct;

    /// <summary>
    /// Wires the settlement consumer and the room-entry trigger. Called once
    /// from the mod initializer AFTER <c>DuelOutcomeSync.Install</c> (the
    /// dispatcher events fire from there); idempotent.
    /// </summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        DuelOutcomeDispatcher.ActOutcomeSettled += OnActSettled;
        RunManager.Instance.RoomEntered += OnRoomEntered;
        PvpDuelLog.Info("ancient flow installed (act settlements open the Ancient body pick room).");
    }

    private static void OnActSettled(DuelResult settled)
    {
        _pendingAct = AncientFlowGate.RegisterSettledAct(_pendingAct, settled.ActIndex);
        if (_pendingAct == settled.ActIndex)
        {
            PvpDuelLog.Info(
                $"ancient flow: act {settled.ActIndex + 1} settled (winner {settled.WinnerNetId}) — " +
                "the Ancient body pick opens when the duel room closes.");
        }
    }

    private static void OnRoomEntered()
    {
        try
        {
            EnsureSubscribed();

            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null)
            {
                _pendingAct = null;
                return;
            }

            if (AncientFlowGate.ShouldDropPending(_pendingAct, state.CurrentActIndex))
            {
                PvpDuelLog.Info($"ancient flow: pending act {_pendingAct + 1} retired (now in act {state.CurrentActIndex + 1}).");
                _pendingAct = null;
            }

            if (!AncientFlowGate.ShouldOpenPicker(_pendingAct, state.CurrentActIndex, state.CurrentRoom is CombatRoom))
            {
                return;
            }

            _pendingAct = null;
            OpenPicker(state);
        }
        catch (Exception ex)
        {
            // Flow bookkeeping must never break room transitions.
            PvpDuelLog.Error($"ancient flow room-enter hook failed: {ex}");
        }
    }

    private static void OpenPicker(IRunState state)
    {
        var model = ModelDb.All.OfType<DuelAncientEventModel>().FirstOrDefault()
            ?? throw new InvalidOperationException("DuelAncientEventModel is not registered in ModelDb.");
        AncientAssetAlias.EnsureAliased(model.Id.Entry.ToLowerInvariant());
        EnsureLocMerged(model.Id);
        PvpDuelLog.Info($"ancient flow: opening the Ancient body pick room for act {state.CurrentActIndex + 1}.");
        TaskHelper.RunSafely(RunManager.Instance.EnterRoom(new EventRoom(model)));
    }

    // ---- AncientPickMessage (winner → loser, host forwarded) ----

    private static void OnAncientPick(AncientPickMessage message, ulong senderId)
    {
        try
        {
            if (RunManager.Instance.DebugOnlyGetState() == null || string.IsNullOrEmpty(message.AncientModelId))
            {
                return; // no run — mod messages cannot belong to one
            }

            // The sender is the picking player (vanilla resolves players by
            // this id, EventSynchronizer.cs:214). Record first: the mutex
            // prefix must see the taken body even before the location-mirrored
            // option execution replays.
            DuelSaveState.RecordAncientPick(new AncientPickDto(message.ActIndex, senderId, message.AncientModelId));
            PvpDuelLog.Info($"ancient flow: opponent claimed {message.AncientModelId} for act {message.ActIndex + 1} — mirror lock applied.");

            var synchronizer = RunManager.Instance.EventSynchronizer;
            if (synchronizer is { } sync && sync.Events.Count > 0 && sync.GetLocalEvent() is DuelAncientEventModel picker)
            {
                picker.ApplyWinnerPickMirror(message.AncientModelId);
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"ancient pick message handling failed: {ex}");
        }
    }

    /// <summary>
    /// Registers the message handler on the current net service; idempotent
    /// and re-subscribed whenever the game swaps services per session (the
    /// BranchSync/DuelOutcomeSync lazy pattern).
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
            _subscribedService.UnregisterMessageHandler<AncientPickMessage>(OnAncientPick);
        }

        _subscribedService = service;
        service.RegisterMessageHandler<AncientPickMessage>(OnAncientPick);
        PvpDuelLog.Info("ancient flow subscribed (AncientPickMessage handler).");
    }

    // ---- Loc mirroring ----

    /// <summary>
    /// Mirrors the picker's player-visible strings into the game's "ancients"
    /// table (the event UI reads the game loc, not the mod catalog). The keys
    /// ship in the mod locale files under the picker's entry prefix; merged
    /// before every room entry so language switches are covered.
    /// </summary>
    private static void EnsureLocMerged(ModelId modelId)
    {
        try
        {
            var manager = LocManager.Instance;
            if (manager == null)
            {
                return;
            }

            var gameTable = manager.GetTable("ancients");
            var catalog = Loc.Catalog();
            var entries = catalog.GetTable(Loc.MapLocale(manager.Language))
                ?? catalog.GetTable(Loc.DefaultLanguage);
            if (entries == null)
            {
                return;
            }

            var prefix = modelId.Entry + ".";
            var gameKeys = entries.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (gameKeys.Count > 0)
            {
                gameTable.MergeWith(gameKeys);
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Warn($"ancient flow: game loc merge skipped ({ex.Message}).");
        }
    }
}
