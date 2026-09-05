using System.Collections.Generic;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using PvpDuel.Core.Maps;
using PvpDuel.Logging;

namespace PvpDuel.Maps;

/// <summary>
/// Run-state hook model that swaps the generated act map for the symmetric
/// split map. Registered through the official mod API
/// <see cref="ModHelper.SubscribeForRunStateHooks"/> — zero Harmony; the same
/// seam vanilla's GoldenCompass uses to return its GoldenPathActMap
/// (RunManager.GenerateMap → Hook.ModifyGeneratedMap).
///
/// Ownership: ModelDb.Init auto-discovers and instantiates every
/// AbstractModel subtype found in mod assemblies, and the base constructor
/// rejects a second instance with the same ModelId (DuplicateModelException).
/// This model therefore never constructs itself — the subscription delegate
/// resolves the ModelDb-owned instance lazily at hook time (after ModelDb
/// initialization, which runs before the main menu).
///
/// Determinism: the topology is a pure function of (runState.Rng.Seed,
/// actIndex) and the act's room count — no RNG is consumed, no wall clock —
/// so both clients independently build byte-identical maps (verified by the
/// Core dump-equality tests).
///
/// Scope per spec #11 §7 patch table #0: applied on every fresh map
/// generation ("永远"); saved-map restore (ModifyGeneratedMapLate path) and
/// everything downstream of the map is untouched.
/// </summary>
public sealed class DuelMapHookModel : AbstractModel
{
    public override bool ShouldReceiveCombatHooks => false;

    public override ActMap ModifyGeneratedMap(IRunState runState, ActMap map, int actIndex)
    {
        int pathRows = runState.Act.GetNumberOfRooms(runState.Players.Count > 1);
        PvpDuelLog.Info($"map hook: act {actIndex} -> DuelSplitActMap (rows={pathRows})");
        DuelSplitTopology topology = DuelSplitTopologyGenerator.Generate(runState.Rng.Seed, actIndex, pathRows);
        return new DuelSplitActMap(topology);
    }
}

/// <summary>Subscription entry point; called once from ModEntry.Initialize.</summary>
public static class DuelMapHook
{
    public const string SubscriptionId = "pvpduel";

    public static void Install()
    {
        PvpDuelLog.Info($"map hook: subscribing for run-state hooks (id '{SubscriptionId}', model '{typeof(DuelMapHookModel).Name}')");
        ModHelper.SubscribeForRunStateHooks(SubscriptionId, static _ => ResolveOwnedModel());
    }

    /// <summary>
    /// The single DuelMapHookModel instance is owned by ModelDb (auto-registered
    /// by the mod-assembly scan). Returns it, or an empty sequence if the
    /// subscription fires before ModelDb initialization has registered it.
    /// </summary>
    private static IEnumerable<AbstractModel> ResolveOwnedModel()
    {
        DuelMapHookModel? model = ModelDb.GetByIdOrNull<DuelMapHookModel>(ModelDb.GetId(typeof(DuelMapHookModel)));
        return model is null ? [] : [model];
    }
}
