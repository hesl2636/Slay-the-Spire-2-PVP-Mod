using Godot;

using MegaCrit.Sts2.Core.Nodes;

using PvpDuel.Config;
using PvpDuel.Logging;
using PvpDuel.Localization;

namespace PvpDuel.Duel;

/// <summary>
/// Config-consistency gate (spec §4.4): before a duel starts, the local config
/// hash is compared against the remote end's hash. Entry hook only in this
/// ticket — the remote hash arrives with the config handshake message in a later
/// ticket, which plugs its value into <see cref="RemoteHashProvider"/>. Until
/// then the check defers (logged, never blocks). On a real mismatch the duel is
/// refused with a banner-style warning overlay (same label treatment as the
/// main-menu self-check banner).
/// </summary>
public static class DuelConfigGate
{
    /// <summary>
    /// Seam for the config-message ticket: returns the remote end's config hash,
    /// or null when not (yet) known. Never throws (wrapped by the provider).
    /// </summary>
    public static Func<string?>? RemoteHashProvider { get; set; }

    /// <summary>Called when a duel room is entered (see <see cref="DuelScope"/>).</summary>
    public static void OnDuelEntering()
    {
        try
        {
            var (_, localHash) = PvpConfigStore.LoadOrDefault();
            var remoteHash = ReadRemoteHash();

            if (remoteHash == null)
            {
                PvpDuelLog.Info($"config consistency: remote hash unknown, check deferred (local {localHash}).");
                return;
            }

            if (!string.Equals(remoteHash, localHash, StringComparison.Ordinal))
            {
                PvpDuelLog.Error($"config mismatch: local {localHash} vs remote {remoteHash} — duel refused (spec §4.4).");
                ShowRefusalBanner(localHash, remoteHash);
            }
            else
            {
                PvpDuelLog.Info($"config consistency: hashes match ({localHash}).");
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"config consistency check failed: {ex}");
        }
    }

    private static string? ReadRemoteHash()
    {
        try
        {
            return RemoteHashProvider?.Invoke();
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"remote config hash provider failed: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Banner-style overlay (main-menu banner treatment) on the game root:
    /// non-interactive, never blocks input, survives until the room is left.
    /// </summary>
    private static void ShowRefusalBanner(string localHash, string remoteHash)
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
                Name = "PvpDuelConfigMismatchBanner",
                Text = Loc.Text("PVP_DUEL_CONFIG_MISMATCH", localHash[..Math.Min(8, localHash.Length)], remoteHash[..Math.Min(8, remoteHash.Length)]),
            };
            banner.AddThemeColorOverride("font_color", Colors.OrangeRed);
            banner.AddThemeColorOverride("font_outline_color", Colors.Black);
            banner.AddThemeConstantOverride("outline_size", 6);
            banner.Position = new Vector2(24, 8);
            banner.Size = new Vector2(900, 120);
            banner.MouseFilter = Control.MouseFilterEnum.Ignore;
            banner.ZIndex = 1000;
            game.AddChild(banner);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"config mismatch banner display failed: {ex}");
        }
    }
}
