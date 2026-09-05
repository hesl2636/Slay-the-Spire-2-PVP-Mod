using MegaCrit.Sts2.Core.Saves.Managers;

namespace PvpDuel.Save;

/// <summary>
/// Side-channel file paths, mirrored one-to-one on the official run saves:
/// <c>&lt;profile&gt;/saves/pvpduel_current_run[mp].save.json</c> sits next to
/// the game's current_run.save / current_run_mp.save (spec §5.7: separate file,
/// the official SerializableRun is never touched). Built through
/// <see cref="RunSaveManager.GetRunSavePath"/> so the location always tracks
/// the game's own resolution (profile dir, saves dir, mod-state override).
/// </summary>
public static class DuelSavePaths
{
    public const string SingleplayerSideChannelFile = "pvpduel_current_run.save.json";
    public const string MultiplayerSideChannelFile = "pvpduel_current_run_mp.save.json";

    public static string SideChannelPath(int profileId, bool multiplayer) =>
        RunSaveManager.GetRunSavePath(profileId, multiplayer ? MultiplayerSideChannelFile : SingleplayerSideChannelFile);
}
