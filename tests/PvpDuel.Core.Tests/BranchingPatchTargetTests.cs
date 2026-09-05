using System.Reflection;

using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Core.SelfCheck;
using PvpDuel.SelfCheck;

using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #20 (T8): the Branching patch set declares all eleven Forked-Road
/// targets and each one exists in the running game build (v0.111 decomp
/// evidence: MapSelectionSynchronizer.MoveToMapCoord — private, RunManager
/// EnterMapCoord/LoadIntoLatestMapCoord/SetActInternal, the four room
/// EnterInternal overrides, ChecksumTracker GenerateChecksum +
/// OnReceivedChecksumDataMessage — private, and the
/// MultiplayerScalingModel.ModifyBlockMultiplicative override).
/// </summary>
public class BranchingPatchTargetTests
{
    [Fact]
    public void Catalog_DeclaresElevenBranchingTargets()
    {
        var targets = PatchTargetCatalog.Build().Targets
            .Where(t => t.PatchSetId == "Branching")
            .ToList();

        Assert.Equal(11, targets.Count);
        Assert.Contains(targets, t => t.TypeName.Contains("MapSelectionSynchronizer") && t.MemberName == "MoveToMapCoord");
        Assert.Contains(targets, t => t.TypeName.Contains("Runs.RunManager") && t.MemberName == "EnterMapCoord");
        Assert.Contains(targets, t => t.TypeName.Contains("Runs.RunManager") && t.MemberName == "LoadIntoLatestMapCoord");
        Assert.Contains(targets, t => t.TypeName.Contains("Runs.RunManager") && t.MemberName == "SetActInternal");
        Assert.Contains(targets, t => t.TypeName.Contains("EventRoom") && t.MemberName == "EnterInternal");
        Assert.Contains(targets, t => t.TypeName.Contains("MerchantRoom") && t.MemberName == "EnterInternal");
        Assert.Contains(targets, t => t.TypeName.Contains("TreasureRoom") && t.MemberName == "EnterInternal");
        Assert.Contains(targets, t => t.TypeName.Contains("RestSiteRoom") && t.MemberName == "EnterInternal");
        Assert.Contains(targets, t => t.TypeName.Contains("ChecksumTracker") && t.MemberName == "GenerateChecksum");
        Assert.Contains(targets, t => t.TypeName.Contains("ChecksumTracker") && t.MemberName == "OnReceivedChecksumDataMessage");
        Assert.Contains(targets, t => t.TypeName.Contains("MultiplayerScalingModel") && t.MemberName == "ModifyBlockMultiplicative");
    }

    [Fact]
    public void Catalog_BranchingSetPassesSelfCheckAgainstLoadedGameAssembly()
    {
        var result = PatchSelfCheck.Run(PatchTargetCatalog.Build(), AccessToolsProbe.Instance);
        Assert.True(result.AllTargetsExist, string.Join("\n", result.Warnings()));
        Assert.True(result.IsPatchSetEnabled("Branching"));
    }

    [Fact]
    public void GameDll_TargetsExist_ViaDirectReflection()
    {
        // The private convergence method the patch replaces (Forked Road seam).
        Assert.NotNull(Method(typeof(MapSelectionSynchronizer), "MoveToMapCoord"));

        // Per-end retargeting targets.
        Assert.NotNull(Method(typeof(RunManager), "EnterMapCoord", [typeof(MegaCrit.Sts2.Core.Map.MapCoord)]));
        Assert.NotNull(Method(typeof(RunManager), "LoadIntoLatestMapCoord"));
        Assert.NotNull(Method(typeof(RunManager), "SetActInternal"));
        Assert.NotNull(Method(typeof(RunManager), "EnterMapCoordInternal"));

        // Room-scope replacement targets: EnterInternal with an IRunState parameter.
        foreach (var room in new[] { typeof(EventRoom), typeof(MerchantRoom), typeof(TreasureRoom), typeof(RestSiteRoom) })
        {
            var enter = Method(room, "EnterInternal");
            Assert.NotNull(enter);
            Assert.Equal(typeof(IRunState), enter!.GetParameters()[0].ParameterType);
        }

        // Checksum exclusion targets.
        Assert.NotNull(Method(typeof(ChecksumTracker), "GenerateChecksum"));
        Assert.NotNull(Method(typeof(ChecksumTracker), "OnReceivedChecksumDataMessage"));

        // Branch combat scaling target.
        Assert.NotNull(Method(typeof(MultiplayerScalingModel), "ModifyBlockMultiplicative"));
    }

    private static MethodInfo? Method(Type type, string name, Type[]? parameterTypes = null)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        return type.GetMethods(flags).FirstOrDefault(m =>
            m.Name == name
            && (parameterTypes is null
                || parameterTypes.SequenceEqual(m.GetParameters().Select(p => p.ParameterType))));
    }
}
