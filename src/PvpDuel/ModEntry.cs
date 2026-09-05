using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using PvpDuel.Core.SelfCheck;
using PvpDuel.Logging;
using PvpDuel.SelfCheck;

namespace PvpDuel;
/// <summary>
/// Mod entry point. Skeleton ticket #13: wires the log channel, runs the
/// startup patch-target self-check, and applies Harmony patches (currently only
/// the self-check banner). No gameplay behavior is changed in this ticket.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "pvpduel";
    public const string Version = "0.1.0";

    public static void Initialize()
    {
        PvpDuelLog.Enabled = true;
        PvpDuelLog.Info($"initializing (v{Version}, skeleton)…");

        // Startup self-check: verify every declared patch target before patching.
        // Missing targets disable their owning patch set (banner + log), never crash.
        var manifest = PatchTargetCatalog.Build();
        SelfCheckState.Report(PatchSelfCheck.Run(manifest, AccessToolsProbe.Instance, GameLogSink.Instance));

        // Config: load (defaults when absent) and compute the shared hash later
        // tickets use for the pre-duel both-ends consistency check.
        var (_, hash) = Config.PvpConfigStore.LoadOrDefault();
        PvpDuelLog.Info($"config hash: {hash}");

        // Ticket #16: swap generated act maps for the symmetric split map via
        // the official run-state hook subscription (zero Harmony).
        Maps.DuelMapHook.Install();

        // Ticket #15: the duel encounter is registered by ModelDb.Init's
        // automatic mod-assembly scan (OneTimeInitialization.ExecuteEssential,
        // which runs AFTER mod initializers). Do NOT call ModelDb.Inject here:
        // the auto-scan instantiates the type again and the AbstractModel
        // constructor throws DuplicateModelException on the already-injected id
        // (verified in the v0.111.0 live smoke).

        // Ticket #15: apply the duel entry patch (RoomSet.Boss setter prefix)
        // only when its patch set passed the self-check; attribute-patching
        // would bypass that gate, so this one is applied manually.
        PvpDuelLog.Info($"duel encounter ENCOUNTER.PVP_DUEL_ENCOUNTER declared (auto-registered by the ModelDb.Init mod scan).");
        if (SelfCheckState.IsPatchSetEnabled(Encounters.RoomSetBossPatch.PatchSetId))
        {
            try
            {
                Encounters.RoomSetBossPatch.Apply(new Harmony($"hesl2636.{Id}.duelentry"));
            }
            catch (Exception ex)
            {
                PvpDuelLog.Error($"{Encounters.RoomSetBossPatch.PatchSetId} patch set disabled: {ex.Message}");
            }
        }
        else
        {
            PvpDuelLog.Error($"{Encounters.RoomSetBossPatch.PatchSetId} patch set disabled by self-check.");
        }

        // Ticket #17 (T5): side flip + Monster-deref guards (spec §7 #2), and
        // the single-machine fake-opponent harness (spec §9, debug flag +
        // console command). Both applied manually after the self-check, same
        // pattern as the DuelEntry set above. No existing lines changed.
        ApplyPatchSetIfEnabled(Combat.CreatureSideFlipPatch.PatchSetId, Combat.CreatureSideFlipPatch.Apply);
        ApplyPatchSetIfEnabled(Combat.DuelHarnessPatch.PatchSetId, Combat.DuelHarnessPatch.Apply);

        static void ApplyPatchSetIfEnabled(string patchSetId, Action<Harmony> apply)
        {
            if (SelfCheckState.IsPatchSetEnabled(patchSetId))
            {
                try
                {
                    apply(new Harmony($"hesl2636.{Id}.{patchSetId.ToLowerInvariant()}"));
                }
                catch (Exception ex)
                {
                    PvpDuelLog.Error($"{patchSetId} patch set disabled: {ex.Message}");
                }
            }
            else
            {
                PvpDuelLog.Error($"{patchSetId} patch set disabled by self-check.");
            }
        }

        // Ticket #15: attach the duel room window (context lifecycle + config
        // consistency entry hook) to the official room events.
        Duel.DuelScope.Install();

        // Ticket #20: branch layer — install the DuelBranchMessage /
        // DuelTimerMessage handlers, then apply the split-path patch set only
        // when its self-check passed (manual application, same gate pattern
        // as the DuelEntry set above).
        Branching.BranchSync.Install();
        ApplyPatchSetIfEnabled(Branching.BranchingPatchSet.PatchSetId, Branching.BranchingPatchSet.Apply);

        // Ticket #21 (T9): mod save side channel — write DuelSavePayload beside
        // the official run save on SaveRun (host/singleplayer, non-empty state
        // only), probe the multiplayer save early, and restore the mod state
        // when a saved run materializes / a new run resets it. Manual
        // application, same gate pattern as the sets above. No existing lines changed.
        ApplyPatchSetIfEnabled(PatchTargetCatalog.PatchSetIds.SaveSideChannel, Save.DuelSavePatchSet.Apply);

        // Ticket #19 (T7): duel-loss interception + local duel-outcome event
        // source. Room-window tracker lifecycle (no Harmony) — settled results
        // land in Save.DuelSaveState.History (persisted by the #21 side
        // channel) — then the Kill/LoseCombat patch set, applied manually after
        // the self-check (same gate pattern as the sets above). No existing
        // lines changed.
        Combat.LossInterceptPatch.Install();
        ApplyPatchSetIfEnabled(PatchTargetCatalog.PatchSetIds.LossIntercept, Combat.LossInterceptPatch.Apply);

        // Ticket #18 (T6): the opponent-turn driver (spec §7 #3) — the
        // ExecuteEnemyTurn replacement plus the vanilla fan-out gates that
        // keep the player-side turn flow local-player-only while the flipped
        // duel opponent is driven on the Enemy side (§12.3 verdict archived
        // on the patch class). Manual application, same gate pattern as the
        // sets above. No existing lines changed.
        ApplyPatchSetIfEnabled(PatchTargetCatalog.PatchSetIds.TurnDrive, Combat.OpponentTurnDrivePatch.Apply);

        // Ticket #22 (T10): act timer + first-hand determination (spec §7
        // 计时层, zero Harmony). Subscribes the branch layer's act/boss-wait
        // transitions, runs the host-clock DuelTimerMessage broadcast and
        // lands DuelContext.FirstHand on both ends (shorter act time wins;
        // <1s difference falls back to the run-seed-derived RNG). No existing
        // lines changed.
        Timing.ActTimer.Install();

        // Ticket #23 (T11): mirrored duel-outcome settlement (spec §4.2/§8).
        // Takes over the #19 settled sink: local outcomes are reported to the
        // peer via DuelOutcomeMessage and settle only on both-end agreement —
        // agreed results land in Save.DuelSaveState.History and dispatch the
        // per-act events; disagreement/timeout aborts the session (divergence).
        // Event wiring only (no Harmony). No existing lines changed.
        Outcomes.DuelOutcomeSync.Install();

        // Ticket #24 (T12): winner-first Ancient body flow (spec §7 结算层).
        // Consumes the act 1/2 settlements (DuelOutcomeDispatcher.ActOutcomeSettled)
        // and opens the programmatically built DuelAncientEventModel room on
        // both ends once the duel combat room closes; the loser's body options
        // are mirror-locked via AncientPickMessage and the
        // EventSynchronizer.ChooseOptionForEvent prefix enforces the mutex at
        // the execution layer (manual application, same gate pattern as the
        // sets above). Act 3 settles straight into the terminal hook — no
        // Ancient flow. No existing lines changed.
        Ancients.DuelAncientFlow.Install();
        ApplyPatchSetIfEnabled(PatchTargetCatalog.PatchSetIds.AncientMutex, Ancients.AncientMutexPatch.Apply);

        // Ticket #25 (T13): duel disconnect pause + reconnect-timeout forfeit
        // (spec §2.1(7)/§8). Zero Harmony: the host-side wait hangs off the
        // official RunLobby.RemotePlayerDisconnected/PlayerRejoined events and
        // shows the PVP_DUEL_RECONNECT countdown (config DisconnectTimeoutSec,
        // host clock); expiry routes DuelResult(DisconnectTimeout) through the
        // #23 settlement pipeline; rejoin resumes the duel officially and
        // backfills the mod state over the wire. Map-traversal disconnects
        // stay on the official path untouched (regression red line). No
        // existing lines changed.
        Disconnect.DisconnectWatch.Install();

        new Harmony($"hesl2636.{Id}").PatchAll(typeof(ModEntry).Assembly);
        PvpDuelLog.Info("initialized.");
    }
}
