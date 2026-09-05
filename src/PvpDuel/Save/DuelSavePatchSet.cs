using System.Reflection;

using HarmonyLib;

using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

using PvpDuel.Logging;

namespace PvpDuel.Save;

/// <summary>
/// Ticket #21 save side-channel patch set (spec §4.3/§5.7/§8). Postfixes around
/// the official run-save flow, applied manually via <see cref="Apply"/> only
/// when the startup self-check passes (never attribute-patched):
/// - <c>SaveManager.SaveRun</c> postfix → write the mod JSON beside the
///   official run save (host/singleplayer + non-empty state only);
/// - <c>SaveManager.LoadAndCanonicalizeMultiplayerRunSave</c> postfix → early
///   diagnostic read (Warn-and-drop on corrupt / schema-mismatched payloads);
/// - <c>RunManager.SetUpSavedSingleplayer/SetUpSavedMultiplayer</c> postfix →
///   restore the mod state exactly when a saved run materializes;
/// - <c>RunManager.InitializeNewRun</c> postfix → reset the mod state for a
///   fresh run.
/// Every postfix body is fail-safe (spec §5.7): a mod-side failure degrades to
/// Warn and never blocks or corrupts the official save.
/// </summary>
public static class DuelSavePatchSet
{
    public const string PatchSetId = "SaveSideChannel";

    /// <summary>Applies the whole set; throws when a target is gone (caller disables the set).</summary>
    public static void Apply(Harmony harmony)
    {
        (MethodBase, string, string)[] postfixes =
        [
            (SaveRunPatch(), nameof(SaveRunPostfix), "SaveManager.SaveRun"),
            (LoadAndCanonicalizePatch(), nameof(LoadAndCanonicalizePostfix), "SaveManager.LoadAndCanonicalizeMultiplayerRunSave"),
            (SetUpSavedSingleplayerPatch(), nameof(SetUpSavedSingleplayerPostfix), "RunManager.SetUpSavedSingleplayer"),
            (SetUpSavedMultiplayerPatch(), nameof(SetUpSavedMultiplayerPostfix), "RunManager.SetUpSavedMultiplayer"),
            (InitializeNewRunPatch(), nameof(InitializeNewRunPostfix), "RunManager.InitializeNewRun"),
        ];

        foreach (var (target, postfixName, label) in postfixes)
        {
            harmony.Patch(target, postfix: HarmonyMethodOf(postfixName));
            PvpDuelLog.Info($"patched: {label}.");
        }

        PvpDuelLog.Info("save side-channel patch set applied (5 targets).");
    }

    private static HarmonyMethod HarmonyMethodOf(string name) => new(
        typeof(DuelSavePatchSet).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"DuelSavePatchSet.{name} missing."));

    private static MethodBase SaveRunPatch() =>
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.SaveRun))
        ?? throw new MissingMethodException(nameof(SaveManager), "SaveRun");

    private static MethodBase LoadAndCanonicalizePatch() =>
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.LoadAndCanonicalizeMultiplayerRunSave))
        ?? throw new MissingMethodException(nameof(SaveManager), "LoadAndCanonicalizeMultiplayerRunSave");

    private static MethodBase SetUpSavedSingleplayerPatch() =>
        AccessTools.Method(typeof(RunManager), nameof(RunManager.SetUpSavedSingleplayer))
        ?? throw new MissingMethodException(nameof(RunManager), "SetUpSavedSingleplayer");

    private static MethodBase SetUpSavedMultiplayerPatch() =>
        AccessTools.Method(typeof(RunManager), nameof(RunManager.SetUpSavedMultiplayer))
        ?? throw new MissingMethodException(nameof(RunManager), "SetUpSavedMultiplayer");

    private static MethodBase InitializeNewRunPatch() =>
        AccessTools.Method(typeof(RunManager), "InitializeNewRun")
        ?? throw new MissingMethodException(nameof(RunManager), "InitializeNewRun");

    // ---- Postfix bodies (all fail-safe: gate first, never throw) ----

    private static void SaveRunPostfix() => DuelRunSaveStore.WriteForCurrentRun();

    private static void LoadAndCanonicalizePostfix(ReadSaveResult<SerializableRun> __result) =>
        DuelRunSaveStore.ProbeMultiplayerRunSave(__result);

    private static void SetUpSavedSingleplayerPostfix(RunState state) =>
        DuelRunSaveStore.RestoreForLoadedRun(state, multiplayer: false);

    private static void SetUpSavedMultiplayerPostfix(RunState state) =>
        DuelRunSaveStore.RestoreForLoadedRun(state, multiplayer: true);

    private static void InitializeNewRunPostfix() => DuelSaveState.Clear();
}
