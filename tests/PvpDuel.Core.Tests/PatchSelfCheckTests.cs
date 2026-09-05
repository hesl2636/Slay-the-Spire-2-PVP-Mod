using PvpDuel.Core.Duel;
using PvpDuel.Core.SelfCheck;
using Xunit;

namespace PvpDuel.Core.Tests;

public class PatchSelfCheckTests
{
    private sealed class FakeProbe(IReadOnlyDictionary<string, Type> types) : IPatchTargetProbe
    {
        public Type? FindType(string fullTypeName) => types.GetValueOrDefault(fullTypeName);

        public bool MemberExists(Type type, string memberName, string[] parameterTypeNames) =>
            type.GetMembers().Any(m => m.Name == memberName);
    }

    private static readonly string TestType = typeof(PatchSelfCheckTests).FullName!;
    private static readonly string TableType = typeof(BranchTable).FullName!;
    private const string ExistingMember = "EmptyManifest_ReportsZeroFailures";

    private static FakeProbe Probe(params (string Name, Type Type)[] entries) =>
        new(entries.ToDictionary(e => e.Name, e => e.Type));

    [Fact]
    public void EmptyManifest_ReportsZeroFailures()
    {
        var result = PatchSelfCheck.Run(new PatchManifest(), Probe(("anything", typeof(BranchTable))));
        Assert.True(result.AllTargetsExist);
        Assert.Empty(result.PatchSets);
        Assert.Empty(result.DisabledPatchSets);
        Assert.Empty(result.Warnings());
    }

    [Fact]
    public void AllTargetsPresent_EnablesEverySet()
    {
        var manifest = new PatchManifest();
        manifest.Add(new PatchTargetSpec("Map", TestType, ExistingMember));
        manifest.Add(new PatchTargetSpec("Combat", TableType, "Reset"));

        var result = PatchSelfCheck.Run(manifest, Probe((TestType, typeof(PatchSelfCheckTests)), (TableType, typeof(BranchTable))));

        Assert.True(result.AllTargetsExist);
        Assert.True(result.IsPatchSetEnabled("Map"));
        Assert.True(result.IsPatchSetEnabled("Combat"));
        Assert.Empty(result.Warnings());
    }

    [Fact]
    public void TamperedTargetName_DisablesOnlyItsPatchSet()
    {
        var manifest = new PatchManifest();
        manifest.Add(new PatchTargetSpec("Map", "MegaCrit.Sts2.Core.Hook", "Reset"));
        manifest.Add(new PatchTargetSpec("Combat", "Game.CombatManager", "ExecuteEnemyTurn"));

        var result = PatchSelfCheck.Run(manifest, Probe(("MegaCrit.Sts2.Core.Hook", typeof(BranchTable))));

        Assert.False(result.AllTargetsExist);
        Assert.True(result.IsPatchSetEnabled("Map"));
        Assert.False(result.IsPatchSetEnabled("Combat"));
        Assert.Equal(["Combat"], result.DisabledPatchSets);
        var warnings = result.Warnings();
        Assert.Single(warnings);
        Assert.Contains("Combat", warnings[0]);
        Assert.Contains("ExecuteEnemyTurn", warnings[0]);
    }

    [Fact]
    public void TamperedMemberName_DisablesItsSet()
    {
        var manifest = new PatchManifest();
        manifest.Add(new PatchTargetSpec("Solo", TableType, "MethodThatDoesNotExist"));

        var result = PatchSelfCheck.Run(manifest, Probe((TableType, typeof(BranchTable))));

        Assert.False(result.IsPatchSetEnabled("Solo"));
        Assert.Contains("MethodThatDoesNotExist", result.PatchSets.Single().MissingTargets[0]);
    }

    [Fact]
    public void SetWithMixedTargets_EnabledOnlyWhenAllExist()
    {
        var manifest = new PatchManifest();
        manifest.Add(new PatchTargetSpec("Fatigue", TableType, "Reset"));
        manifest.Add(new PatchTargetSpec("Fatigue", TableType, "GhostMethod"));

        var result = PatchSelfCheck.Run(manifest, Probe((TableType, typeof(BranchTable))));
        Assert.False(result.IsPatchSetEnabled("Fatigue"));
        Assert.Single(result.PatchSets.Single().MissingTargets);
    }

    [Fact]
    public void TypeOnlySpec_ChecksTypeExistence()
    {
        var manifest = new PatchManifest();
        manifest.Add(new PatchTargetSpec("Types", TableType));

        Assert.True(PatchSelfCheck.Run(manifest, Probe((TableType, typeof(BranchTable)))).AllTargetsExist);
        Assert.False(PatchSelfCheck.Run(manifest, Probe()).AllTargetsExist);
    }

    [Fact]
    public void Manifest_RejectsIncompleteSpecs()
    {
        var manifest = new PatchManifest();
        Assert.Throws<ArgumentException>(() => manifest.Add(new PatchTargetSpec("", "Some.Type")));
        Assert.Throws<ArgumentException>(() => manifest.Add(new PatchTargetSpec("Set", "")));
    }
}
