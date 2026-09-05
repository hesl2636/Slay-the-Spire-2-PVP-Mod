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

        new Harmony($"hesl2636.{Id}").PatchAll(typeof(ModEntry).Assembly);
        PvpDuelLog.Info("initialized.");
    }
}
