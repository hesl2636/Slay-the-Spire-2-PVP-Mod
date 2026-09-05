using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

using PvpDuel.Localization;
using PvpDuel.Logging;

namespace PvpDuel.SelfCheck;

/// <summary>
/// Main-menu banner: when the startup self-check disabled any patch set, a
/// warning label is overlaid on the main menu. Implemented as a postfix on
/// NMainMenu._Ready so it fires exactly once the menu exists. When no warnings
/// are pending the postfix is a no-op — zero behavior change.
/// </summary>
[HarmonyPatch(typeof(NMainMenu), "_Ready")]
internal static class MainMenuBanner
{
    private static void Postfix(NMainMenu __instance)
    {
        try
        {
            var warnings = SelfCheckState.TakePendingWarnings();
            if (warnings == null || warnings.Count == 0)
            {
                return;
            }

            var banner = new Label
            {
                Name = "PvpDuelSelfCheckBanner",
                Text = Loc.Text("PVP_DUEL_SELF_CHECK_BANNER", string.Join("\n", warnings)),
            };
            banner.AddThemeColorOverride("font_color", Colors.OrangeRed);
            banner.AddThemeColorOverride("font_outline_color", Colors.Black);
            banner.AddThemeConstantOverride("outline_size", 6);
            banner.Position = new Vector2(24, 8);
            banner.Size = new Vector2(900, 120);
            banner.MouseFilter = Control.MouseFilterEnum.Ignore;
            banner.ZIndex = 1000;

            __instance.AddChild(banner);
            PvpDuelLog.Warn("main-menu banner displayed (patch set(s) disabled).");
        }
        catch (Exception ex)
        {
            // Never let diagnostics break the menu.
            Log.Error($"[pvpduel] banner display failed: {ex}");
        }
    }
}
