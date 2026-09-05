using System.Reflection;

using Godot;

using HarmonyLib;

using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Core.Ancients;
using PvpDuel.Localization;
using PvpDuel.Logging;
using PvpDuel.Save;

namespace PvpDuel.Ancients;

/// <summary>
/// The execution-layer Ancient body mutex (ticket #24, spec §7 #6). Not
/// attribute-patched: ModEntry applies the set manually, only after the
/// startup self-check confirmed the target.
///
/// <c>EventSynchronizer.ChooseOptionForEvent</c> (EventSynchronizer.cs:272-287)
/// is the single funnel for both the local pick (<c>ChooseLocalOption</c>) and
/// the mirrored remote pick (<c>HandleEventOptionChosenMessage</c>), so one
/// prefix guards both ends. While the picker event's body page is on screen,
/// the chosen body is evaluated against the pure
/// <see cref="AncientMutexPolicy"/> over the mirrored settlement + pick
/// records; a taken body is rejected (the original method never runs, so
/// neither end executes the option and the event page stays put) with a
/// localized local-only prompt (<c>PVP_ANCIENT_PICK_TAKEN</c>). Every other
/// event — and the picker's blessing/proceed pages — passes straight through;
/// failures fail open (vanilla behavior), never breaking a non-duel event.
/// </summary>
internal static class AncientMutexPatch
{
    public const string PatchSetId = "AncientMutex";

    private const string TakenLocKey = "PVP_ANCIENT_PICK_TAKEN";

    /// <summary>Applies the set; throws when the target is gone (caller disables the set).</summary>
    public static void Apply(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent")
            ?? throw new MissingMethodException(typeof(EventSynchronizer).FullName, "ChooseOptionForEvent");
        harmony.Patch(target, prefix: Patch(nameof(ChooseOptionForEventPrefix)));
        PvpDuelLog.Info("AncientMutex patch set applied (ChooseOptionForEvent prefix).");
    }

    private static bool ChooseOptionForEventPrefix(EventSynchronizer __instance, Player player, int optionIndex)
    {
        try
        {
            if (__instance.GetEventForPlayer(player) is not DuelAncientEventModel picker || !picker.IsInBodyPickPhase)
            {
                return true; // vanilla events and the blessing/proceed pages pass through
            }

            var bodyId = picker.BodyIdForOption(optionIndex);
            var state = RunManager.Instance.DebugOnlyGetState();
            if (string.IsNullOrEmpty(bodyId) || state == null)
            {
                return true; // malformed index: vanilla's own guard throws after us
            }

            var verdict = AncientMutexPolicy.Evaluate(
                DuelSaveState.History.GetForAct(state.CurrentActIndex),
                DuelSaveState.AncientPicks,
                state.CurrentActIndex,
                player.NetId,
                bodyId);
            if (verdict == AncientPickVerdict.Allowed)
            {
                return true;
            }

            PvpDuelLog.Info($"ancient mutex: pick of {bodyId} by player {player.NetId} rejected ({verdict}).");
            ShowTakenToast(player);
            return false;
        }
        catch (Exception ex)
        {
            // Fail-open: never break an event over mutex bookkeeping.
            PvpDuelLog.Error($"ancient mutex prefix failed: {ex}");
            return true;
        }
    }

    /// <summary>
    /// Local-only prompt (spec §5: rejections happen before any network action
    /// — the pick simply does not execute and the options stay on screen).
    /// </summary>
    private static void ShowTakenToast(Player player)
    {
        try
        {
            if (!LocalContext.IsMe(player))
            {
                return;
            }

            var game = NGame.Instance;
            if (game == null)
            {
                return;
            }

            var toast = new Label
            {
                Name = "PvpDuelAncientTakenToast",
                Text = Loc.Text(TakenLocKey),
            };
            toast.AddThemeColorOverride("font_color", Colors.Orange);
            toast.AddThemeColorOverride("font_outline_color", Colors.Black);
            toast.AddThemeConstantOverride("outline_size", 6);
            toast.Position = new Vector2(24, 74);
            toast.Size = new Vector2(900, 60);
            toast.MouseFilter = Control.MouseFilterEnum.Ignore;
            toast.ZIndex = 1000;
            game.AddChild(toast);
            game.GetTree().CreateTimer(2.5).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(toast))
                {
                    toast.QueueFree();
                }
            };
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"ancient mutex toast display failed: {ex}");
        }
    }

    private static HarmonyMethod Patch(string name)
    {
        var method = typeof(AncientMutexPatch).GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(AncientMutexPatch).FullName, name);
        return new HarmonyMethod(method);
    }
}
