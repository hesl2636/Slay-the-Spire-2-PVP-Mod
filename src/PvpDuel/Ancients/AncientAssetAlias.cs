using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

using PvpDuel.Logging;

namespace PvpDuel.Ancients;

/// <summary>
/// Art for the self-built picker room (ticket #24). The mod ships no PCK, but
/// the ancient layout pulls model-derived assets unconditionally:
/// <see cref="MegaCrit.Sts2.Core.Nodes.Events.NAncientEventLayout"/> instantiates
/// the background scene, the base event layout resolves the portrait and the
/// dialogue lines pull the run-history icons. A missing resource is marked
/// failed by <c>AssetLoadingSession</c> (AssetLoadingSession.cs:225-236) and
/// then throws <c>AssetLoadException</c> on access (AssetCache.cs:45-50) — the
/// room would never open.
///
/// The aliases park a FIXED vanilla ancient's (Darv — shared-pool, always
/// registered) resources under the picker's derived paths right before the
/// room entry, and the picker's <c>GetAssetPaths</c> override keeps the aliased
/// paths inside its own preload session, so the session can neither mark them
/// failed nor unload them mid-room. Other rooms' sessions unload the aliases
/// afterwards; they are re-aliased on every entry.
/// </summary>
public static class AncientAssetAlias
{
    // Pure path builders — the same derivations the vanilla model properties
    // perform, locked by DuelAncientEventModelSurfaceTests.
    public static string BackgroundScenePath(string entry) =>
        SceneHelper.GetScenePath("events/background_scenes/" + Slug(entry));

    public static string PortraitPath(string entry) =>
        ImageHelper.GetImagePath("events/" + Slug(entry) + ".png");

    public static string RunHistoryIconPath(string entry) =>
        ImageHelper.GetImagePath("ui/run_history/" + Slug(entry) + ".png");

    public static string RunHistoryIconOutlinePath(string entry) =>
        ImageHelper.GetImagePath("ui/run_history/" + Slug(entry) + "_outline.png");

    public static string MapIconPath(string entry) =>
        ImageHelper.GetImagePath("packed/map/ancients/ancient_node_" + Slug(entry) + ".png");

    public static string MapIconOutlinePath(string entry) =>
        ImageHelper.GetImagePath("packed/map/ancients/ancient_node_" + Slug(entry) + "_outline.png");

    /// <summary>
    /// Re-aliases every consumer path onto the fixed vanilla ancient's
    /// resources. Called immediately before the picker room entry; idempotent.
    /// </summary>
    internal static void EnsureAliased(string entry)
    {
        var source = Slug(ModelDb.AncientEvent<global::MegaCrit.Sts2.Core.Models.Events.Darv>().Id.Entry);
        var cache = PreloadManager.Cache;

        SetAsset(cache, BackgroundScenePath(entry), "events/background_scenes/" + source, SceneKind.Scene);
        SetAsset(cache, PortraitPath(entry), "events/" + source + ".png", SceneKind.Texture);
        SetAsset(cache, RunHistoryIconPath(entry), "ui/run_history/" + source + ".png", SceneKind.Texture);
        SetAsset(cache, RunHistoryIconOutlinePath(entry), "ui/run_history/" + source + "_outline.png", SceneKind.Texture);
        SetAsset(cache, MapIconPath(entry), "packed/map/ancients/ancient_node_" + source + ".png", SceneKind.Texture);
        SetAsset(cache, MapIconOutlinePath(entry), "packed/map/ancients/ancient_node_" + source + "_outline.png", SceneKind.Texture);
        PvpDuelLog.Info($"ancient flow: room assets aliased onto the vanilla ancient '{source}'.");
    }

    private enum SceneKind
    {
        Scene,
        Texture,
    }


    private static void SetAsset(AssetCache cache, string targetPath, string sourceInnerPath, SceneKind kind)
    {
        var sourcePath = kind == SceneKind.Scene
            ? SceneHelper.GetScenePath(sourceInnerPath)
            : ImageHelper.GetImagePath(sourceInnerPath);
        var resource = kind == SceneKind.Scene
            ? (Godot.Resource)cache.GetScene(sourcePath)
            : (Godot.Resource)cache.GetTexture2D(sourcePath);
        cache.SetAsset(targetPath, resource);
    }

    private static string Slug(string entry) => entry.ToLowerInvariant();
}
