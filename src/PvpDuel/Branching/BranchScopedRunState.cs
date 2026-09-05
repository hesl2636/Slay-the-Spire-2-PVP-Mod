using System.Collections.Generic;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace PvpDuel.Branching;

/// <summary>
/// Branch-scoped view of the shared run state (Forked Road pattern, research #3
/// Q2 step 4): handed to branch rooms (event / merchant / treasure / rest site)
/// so room internals only see the players of the branch — in a two-player duel
/// branch that is the branch owner alone, which also drops
/// <see cref="ICardScope"/>-level "multiplayer" conveniences (e.g.
/// <c>CardMultiplayerConstraint</c>, derived from <see cref="Players"/>) to
/// single-branch semantics. Everything else — RNG, odds, history, rooms,
/// unlocks — delegates to the real run state; <see cref="GetPlayerSlotIndex"/>
/// and <see cref="GetPlayer"/> still resolve against the full player list so
/// slot-indexed structures (merchant inventories) keep their vanilla alignment.
/// </summary>
public sealed class BranchScopedRunState : IRunState
{
    private readonly IRunState _inner;
    private readonly IReadOnlyList<Player> _branchPlayers;

    public BranchScopedRunState(IRunState inner, IReadOnlyList<Player> branchPlayers)
    {
        _inner = inner;
        _branchPlayers = branchPlayers;
    }

    public IReadOnlyList<Player> Players => _branchPlayers;

    public int GetPlayerSlotIndex(Player player) => _inner.GetPlayerSlotIndex(player);

    public Player? GetPlayer(ulong netId) => _inner.GetPlayer(netId);

    public IReadOnlyList<ActModel> Acts => _inner.Acts;

    public int CurrentActIndex
    {
        get => _inner.CurrentActIndex;
        set => _inner.CurrentActIndex = value;
    }

    public ActModel Act => _inner.Act;

    public ActMap Map
    {
        get => _inner.Map;
        set => _inner.Map = value;
    }

    public MapCoord? CurrentMapCoord => _inner.CurrentMapCoord;

    public GameMode GameMode => _inner.GameMode;

    public MapPoint? CurrentMapPoint => _inner.CurrentMapPoint;

    public RunLocation RunLocation => _inner.RunLocation;

    public MapLocation MapLocation => _inner.MapLocation;

    public int ActFloor
    {
        get => _inner.ActFloor;
        set => _inner.ActFloor = value;
    }

    public int TotalFloor => _inner.TotalFloor;

    public int CurrentRoomCount => _inner.CurrentRoomCount;

    public AbstractRoom? CurrentRoom => _inner.CurrentRoom;

    public AbstractRoom? BaseRoom => _inner.BaseRoom;

    public bool IsGameOver => _inner.IsGameOver;

    public int AscensionLevel => _inner.AscensionLevel;

    public RunRngSet Rng => _inner.Rng;

    public RunOddsSet Odds => _inner.Odds;

    public RelicGrabBag SharedRelicGrabBag => _inner.SharedRelicGrabBag;

    public UnlockState UnlockState => _inner.UnlockState;

    public IReadOnlyList<ModifierModel> Modifiers => _inner.Modifiers;

    public IReadOnlyList<BadgeModel> BadgeModels => _inner.BadgeModels;

    public MultiplayerScalingModel? MultiplayerScalingModel => _inner.MultiplayerScalingModel;

    public IReadOnlyList<IReadOnlyList<MapPointHistoryEntry>> MapPointHistory => _inner.MapPointHistory;

    public MapPointHistoryEntry? CurrentMapPointHistoryEntry => _inner.CurrentMapPointHistoryEntry;

    public ExtraRunFields ExtraFields => _inner.ExtraFields;

    public T CreateCard<T>(Player owner) where T : CardModel => _inner.CreateCard<T>(owner);

    public CardModel CreateCard(CardModel canonicalCard, Player owner) => _inner.CreateCard(canonicalCard, owner);

    public CardModel CloneCard(CardModel mutableCard) => _inner.CloneCard(mutableCard);

    public void AddCard(CardModel mutableCard, Player owner) => _inner.AddCard(mutableCard, owner);

    public void RemoveCard(CardModel card) => _inner.RemoveCard(card);

    public bool ContainsCard(CardModel card) => _inner.ContainsCard(card);

    public CardModel LoadCard(SerializableCard serializableCard, Player owner) => _inner.LoadCard(serializableCard, owner);

    public void AppendToMapPointHistory(MapPointType mapPointType, RoomType initialRoomType, ModelId? modelId) =>
        _inner.AppendToMapPointHistory(mapPointType, initialRoomType, modelId);

    public MapPointHistoryEntry? GetHistoryEntryFor(MapLocation location) => _inner.GetHistoryEntryFor(location);

    public IEnumerable<AbstractModel> IterateHookListeners(ICombatState? childCombatState) =>
        _inner.IterateHookListeners(childCombatState);

    public int GetAndIncrementNextRoomId() => _inner.GetAndIncrementNextRoomId();
}
