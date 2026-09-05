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

        new Harmony($"hesl2636.{Id}").PatchAll(typeof(ModEntry).Assembly);
        PvpDuelLog.Info("initialized.");
    }
}
