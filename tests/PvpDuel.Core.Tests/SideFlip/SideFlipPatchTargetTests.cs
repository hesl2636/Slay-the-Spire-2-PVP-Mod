using System.Reflection;
using System.Runtime.CompilerServices;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Rooms;

using PvpDuel.Combat;
using PvpDuel.Core.SelfCheck;
using PvpDuel.Logging;
using PvpDuel.SelfCheck;

using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #17 (T5): the SideFlip/DuelHarness patch targets are declared in the
/// catalog and actually exist in the running game build; the side flip really
/// writes the <c>&lt;Side&gt;k__BackingField</c> that every IsEnemy consumer
/// reads. Game-DLL asserts use static reflection only (no game singletons).
/// </summary>
public class SideFlipPatchTargetTests
{
    public SideFlipPatchTargetTests()
    {
        // The game Logger static ctor crashes the test host (0xC0000005).
        PvpDuelLog.Enabled = false;
    }

    /// <summary>Probe mirroring AccessToolsProbe semantics with plain reflection.</summary>
    private sealed class ReflectionProbe : IPatchTargetProbe
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

        public bool MemberExists(Type type, string memberName, string[] parameterTypeNames)
        {
            if (memberName == ".ctor")
            {
                return type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Any(c => Matches(c.GetParameters(), parameterTypeNames));
            }

            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (members.OfType<MethodInfo>().Any(m =>
                    m.Name == memberName
                    && (parameterTypeNames.Length == 0 || Matches(m.GetParameters(), parameterTypeNames))))
            {
                return true;
            }
            return members.Any(m =>
                m.MemberType is MemberTypes.Property or MemberTypes.Field && m.Name == memberName);
        }

        private static bool Matches(ParameterInfo[] parameters, string[] expected)
        {
            if (parameters.Length != expected.Length)
            {
                return false;
            }
            return parameters.Zip(expected, (p, e) => SimpleTypeName(p.ParameterType) == e)
                .All(match => match);
        }

        private static string SimpleTypeName(Type type)
        {
            var name = type.Name;
            var arity = name.IndexOf('`');
            return arity > 0 ? name[..arity] : name;
        }
    }

    [Fact]
    public void Catalog_DeclaresT5SetsWithAllTargets()
    {
        // The catalog is shared with sibling patch-set tickets (e.g. Branching):
        // assert only this ticket's own sets and targets here.
        var targets = PatchTargetCatalog.Build().Targets;

        Assert.Equal(5, targets.Count(t => t.PatchSetId == "SideFlip"));

        var sideFlip = targets.Where(t => t.PatchSetId == "SideFlip").ToList();
        Assert.Equal(5, sideFlip.Count);
        Assert.Contains(sideFlip, t => t.TypeName.EndsWith("Creature") && t.MemberName == ".ctor");
        Assert.Contains(sideFlip, t => t.TypeName.EndsWith("Creature") && t.MemberName == "AfterAddedToRoom");
        Assert.Contains(sideFlip, t => t.TypeName.EndsWith("Creature") && t.MemberName == "PrepareForNextTurn");
        Assert.Contains(sideFlip, t => t.TypeName.EndsWith("Creature") && t.MemberName == "TakeTurn");
        Assert.Contains(sideFlip, t => t.TypeName.EndsWith("CombatManager") && t.MemberName == "AfterCreatureAdded");

        var harness = targets.Where(t => t.PatchSetId == "DuelHarness").ToList();
        Assert.Equal(3, harness.Count);
        Assert.Contains(harness, t => t.TypeName.EndsWith("RoomSet") && t.MemberName == "Boss");
        Assert.Contains(harness, t => t.TypeName == "PvpDuel.Encounters.PvpDuelEncounter" && t.MemberName == "GenerateMonsters");
        Assert.Contains(harness, t => t.TypeName.EndsWith("CombatRoom") && t.MemberName == "StartCombat");
    }

    [Fact]
    public void Catalog_T5TargetsPassSelfCheckAgainstLoadedGameAssembly()
    {
        // Scope to this ticket's sets: the catalog is shared with sibling
        // patch-set tickets, whose targets are asserted by their own tests.
        var manifest = new PatchManifest();
        var own = new[] { "DuelEntry", "SideFlip", "DuelHarness" };
        manifest.AddRange(PatchTargetCatalog.Build().Targets.Where(t => own.Contains(t.PatchSetId)));

        var result = PatchSelfCheck.Run(manifest, new ReflectionProbe());
        Assert.True(result.AllTargetsExist, string.Join("\n", result.Warnings()));
        Assert.True(result.IsPatchSetEnabled("SideFlip"));
        Assert.True(result.IsPatchSetEnabled("DuelHarness"));
        Assert.True(result.IsPatchSetEnabled("DuelEntry"));
    }

    [Fact]
    public void GameDll_CreatureSideBackingFieldExistsAndMatchesFlipHelper()
    {
        var field = typeof(Creature).GetField("<Side>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(CombatSide), field!.FieldType);
        Assert.Same(field, CreatureSideFlip.SideField);
    }

    [Fact]
    public void FlipToEnemy_RewritesSideThatAllIsEnemyConsumersRead()
    {
        // No constructor: the Creature ctor would need a Player (ModelDb). The
        // flip only writes one backing field, so an uninitialized instance is a
        // faithful host — but the powers list must exist for the IsEnemy
        // consumers that iterate it.
        var creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        var powersField = typeof(Creature).GetField("_powers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(powersField);
        powersField!.SetValue(creature, Activator.CreateInstance(powersField.FieldType));

        Assert.Equal(CombatSide.None, creature.Side); // zero-initialized backing field
        Assert.False(creature.IsEnemy);

        Assert.True(CreatureSideFlip.FlipToEnemy(creature));

        Assert.Equal(CombatSide.Enemy, creature.Side);
        Assert.True(creature.IsEnemy);        // Creature.cs:244 consumer
        Assert.True(creature.IsPrimaryEnemy); // Creature.cs:253-263 consumer
        Assert.False(creature.IsSecondaryEnemy);
    }

    [Fact]
    public void GameDll_CreatureGuardTargetsExist()
    {
        Assert.NotNull(typeof(Creature).GetMethod(nameof(Creature.AfterAddedToRoom), BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(Creature).GetMethod(nameof(Creature.PrepareForNextTurn), BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(Creature).GetMethod(nameof(Creature.TakeTurn), BindingFlags.Public | BindingFlags.Instance));

        // The private turn-state-relative overload the guard prefixes (2 params).
        Assert.NotNull(typeof(CombatManager).GetMethod("AfterCreatureAdded",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(Creature), typeof(CombatState)]));
    }

    [Fact]
    public void GameDll_HarnessTargetsExist()
    {
        Assert.NotNull(typeof(RoomSet).GetProperty(nameof(RoomSet.Boss))?.SetMethod);
        Assert.NotNull(typeof(CombatRoom).GetMethod("StartCombat",
            BindingFlags.NonPublic | BindingFlags.Instance, [typeof(MegaCrit.Sts2.Core.Runs.IRunState)]));
        Assert.NotNull(typeof(PvpDuel.Encounters.PvpDuelEncounter).GetMethod(
            "GenerateMonsters", BindingFlags.NonPublic | BindingFlags.Instance));
    }

    [Fact]
    public void HarnessConsoleCmd_IsAutoRegistrableAndPureSubcommandsWork()
    {
        // The game loads console commands via Activator.CreateInstance on
        // parameterless-ctor subtypes (ReflectionHelper.GetSubtypesInMods).
        var consoleCmd = Activator.CreateInstance(typeof(DuelHarnessConsoleCmd));
        Assert.IsType<DuelHarnessConsoleCmd>(consoleCmd);
        Assert.Equal("pvpduel", ((DuelHarnessConsoleCmd)consoleCmd!).CmdName);

        // Pure subcommands must not touch any game singleton.
        var on = ((DuelHarnessConsoleCmd)consoleCmd).Process(null, ["on"]);
        Assert.True(on.success);
        Assert.True(DuelHarness.DebugSinglePlayerDuel);

        var status = ((DuelHarnessConsoleCmd)consoleCmd).Process(null, ["status"]);
        Assert.True(status.success);
        Assert.Contains("ON", status.msg);

        var off = ((DuelHarnessConsoleCmd)consoleCmd).Process(null, ["off"]);
        Assert.True(off.success);
        Assert.False(DuelHarness.DebugSinglePlayerDuel);

        Assert.False(((DuelHarnessConsoleCmd)consoleCmd).Process(null, ["bogus"]).success);
    }
}
