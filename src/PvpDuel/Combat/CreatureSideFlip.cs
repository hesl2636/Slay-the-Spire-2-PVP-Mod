using System.Reflection;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

using PvpDuel.Logging;

namespace PvpDuel.Combat;

/// <summary>
/// Writes the <c>Creature.Side</c> backing field. The property itself is
/// get-only (decomp Creature.cs:118, backing field <c>&lt;Side&gt;k__BackingField</c>),
/// so the duel's opponent flip (spec §7 #2) goes through reflection. The field
/// is resolved once: exact auto-property name first, then a declared-field scan
/// by type as a game-update fallback.
/// </summary>
public static class CreatureSideFlip
{
    private static FieldInfo? _sideField;

    public static FieldInfo SideField =>
        _sideField ??= ResolveSideField()
            ?? throw new MissingFieldException(
                typeof(Creature).FullName, "<Side>k__BackingField (CombatSide)");

    /// <summary>Flips the creature to the Enemy side. Returns true when written.</summary>
    public static bool FlipToEnemy(Creature creature)
    {
        if (creature == null)
        {
            return false;
        }
        SideField.SetValue(creature, CombatSide.Enemy);
        return true;
    }

    private static FieldInfo? ResolveSideField()
    {
        var byName = typeof(Creature).GetField(
            "<Side>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (byName != null && byName.FieldType == typeof(CombatSide) && byName.DeclaringType == typeof(Creature))
        {
            return byName;
        }

        // Game-update fallback: the only declared CombatSide field on Creature.
        return typeof(Creature)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType == typeof(CombatSide) && f.DeclaringType == typeof(Creature));
    }
}
