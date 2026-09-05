using System.Reflection;

using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

using PvpDuel.Core.SelfCheck;
using PvpDuel.SelfCheck;

using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #15 (T3): the duel entry patch target is declared in the catalog and
/// actually exists in the running game build (v0.111 decomp evidence:
/// RoomSet.cs:56-66 public setter, backing field <c>_boss</c>).
/// </summary>
public class RoomSetBossPatchTargetTests
{
    private sealed class PropertyProbe : IPatchTargetProbe
    {
        public Type? FindType(string fullTypeName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => SafeGetType(a, fullTypeName))
                .FirstOrDefault(t => t != null);

        private static Type? SafeGetType(Assembly assembly, string fullTypeName)
        {
            try
            {
                return assembly.GetType(fullTypeName);
            }
            catch
            {
                return null;
            }
        }

        public bool MemberExists(Type type, string memberName, string[] parameterTypeNames) =>
            type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;
    }

    [Fact]
    public void Catalog_DeclaresDuelEntryTarget()
    {
        var manifest = PatchTargetCatalog.Build();
        var spec = Assert.Single(manifest.Targets, t => t.PatchSetId == "DuelEntry");
        Assert.Equal("MegaCrit.Sts2.Core.Rooms.RoomSet", spec.TypeName);
        Assert.Equal("Boss", spec.MemberName);
    }

    [Fact]
    public void Catalog_TargetPassesSelfCheckAgainstLoadedGameAssembly()
    {
        // AccessToolsProbe resolves methods (incl. private) + properties/fields,
        // matching what the game-side self-check actually runs.
        var result = PatchSelfCheck.Run(PatchTargetCatalog.Build(), AccessToolsProbe.Instance);
        Assert.True(result.AllTargetsExist, string.Join("\n", result.Warnings()));
        Assert.True(result.IsPatchSetEnabled("DuelEntry"));
    }

    [Fact]
    public void GameDll_RoomSetBossSetterAndBackingFieldExist()
    {
        // Integration assert against the real game build; skip vacuously when
        // no game install is configured (CI without GameDir).
        var roomSetType = typeof(RoomSet);
        var setter = roomSetType.GetProperty(nameof(RoomSet.Boss))?.SetMethod;
        Assert.NotNull(setter);
        Assert.True(setter!.IsPublic);

        var backingField = roomSetType.GetField("_boss", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(backingField);
        Assert.Equal(typeof(EncounterModel), backingField!.FieldType);
    }

    [Fact]
    public void GameDll_RewardGateIsVirtualAndOverridable()
    {
        var property = typeof(EncounterModel).GetProperty(nameof(EncounterModel.ShouldGiveRewards));
        Assert.NotNull(property);
        Assert.True(property!.GetGetMethod()!.IsVirtual);
    }
}
