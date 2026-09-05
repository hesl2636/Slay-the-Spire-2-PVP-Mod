using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace PvpDuel.Encounters;

/// <summary>
/// The duel encounter: the product of the entry patch (boss room swap) and the
/// basis of every scope gate. STUB (ticket #13): type + signatures only, never
/// instantiated in this ticket — construction goes through ModelDb at runtime.
/// </summary>
public class PvpDuelEncounter : EncounterModel
{
    /// <summary>Duel rooms use the boss room layout.</summary>
    public override RoomType RoomType => RoomType.Boss;

    /// <summary>Duels fight player creatures, not generated monsters.</summary>
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [];

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
        throw new NotImplementedException("Filled in by the encounter-entry ticket.");
}
