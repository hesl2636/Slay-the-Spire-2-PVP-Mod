using System.Reflection;

using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

using PvpDuel.Core.SelfCheck;
using PvpDuel.SelfCheck;

using Xunit;

namespace PvpDuel.Core.Tests.Save;

/// <summary>
/// Ticket #21 (T9): the SaveSideChannel patch set declares its five targets
/// and each one exists in the running game build (v0.111 decomp evidence:
/// SaveManager.SaveRun :621 public, SaveManager.LoadAndCanonicalizeMultiplayerRunSave
/// :1091 public, RunManager.SetUpSavedSingleplayer :356 / SetUpSavedMultiplayer
/// :384 public, RunManager.InitializeNewRun :528 private).
/// </summary>
public class SaveSideChannelPatchTargetTests
{
    [Fact]
    public void Catalog_DeclaresFiveSaveSideChannelTargets()
    {
        var targets = PatchTargetCatalog.Build().Targets
            .Where(t => t.PatchSetId == "SaveSideChannel")
            .ToList();

        Assert.Equal(5, targets.Count);
        Assert.Contains(targets, t => t.TypeName.Contains("Saves.SaveManager") && t.MemberName == "SaveRun");
        Assert.Contains(targets, t => t.TypeName.Contains("Saves.SaveManager") && t.MemberName == "LoadAndCanonicalizeMultiplayerRunSave");
        Assert.Contains(targets, t => t.TypeName.Contains("Runs.RunManager") && t.MemberName == "SetUpSavedSingleplayer");
        Assert.Contains(targets, t => t.TypeName.Contains("Runs.RunManager") && t.MemberName == "SetUpSavedMultiplayer");
        Assert.Contains(targets, t => t.TypeName.Contains("Runs.RunManager") && t.MemberName == "InitializeNewRun");
    }

    [Fact]
    public void Catalog_SaveSideChannelSetPassesSelfCheckAgainstLoadedGameAssembly()
    {
        var result = PatchSelfCheck.Run(PatchTargetCatalog.Build(), AccessToolsProbe.Instance);
        var set = result.PatchSets.Single(s => s.PatchSetId == "SaveSideChannel");

        Assert.True(set.Enabled, string.Join("\n", set.MissingTargets));
    }

    [Fact]
    public void GameDll_TargetsExist_ViaDirectReflection()
    {
        Assert.NotNull(Method(typeof(SaveManager), "SaveRun"));
        Assert.NotNull(Method(typeof(SaveManager), "LoadAndCanonicalizeMultiplayerRunSave"));
        Assert.NotNull(Method(typeof(RunManager), "SetUpSavedSingleplayer"));
        Assert.NotNull(Method(typeof(RunManager), "SetUpSavedMultiplayer"));
        Assert.NotNull(Method(typeof(RunManager), "InitializeNewRun"));
    }

    /// <summary>Resolves a member by name, any overload (parameterless preferred).</summary>
    private static MethodInfo? Method(Type type, string name) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.Name == name)
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault();
}
