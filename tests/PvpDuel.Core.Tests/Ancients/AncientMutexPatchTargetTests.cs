using System.Reflection;

using HarmonyLib;

using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Unlocks;
using Xunit;

namespace PvpDuel.Core.Tests.Ancients;

/// <summary>
/// Ticket #24 (spec §7 #6): static-reflection asserts over the mutex patch
/// target and the native-gap seams, mirroring the decomp evidence. No game
/// singletons are touched (the game Logger static ctor 0xC0000005 rule).
/// </summary>
public class AncientMutexPatchTargetTests
{
    [Fact]
    public void GameDll_ChooseOptionForEventTargetExists()
    {
        // EventSynchronizer.cs:272-287 — the private executor is the single
        // funnel for BOTH local picks (ChooseLocalOption) and mirrored remote
        // picks (HandleEventOptionChosenMessage), so the mutex prefix covers
        // both ends with one patch.
        var method = AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent")
            ?? throw new Xunit.Sdk.XunitException("EventSynchronizer.ChooseOptionForEvent is gone.");
        Assert.True(method.IsPrivate);
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.Equal(
            [typeof(Player), typeof(int)],
            [.. method.GetParameters().Select(p => p.ParameterType)]);
    }

    [Fact]
    public void GameDll_EventSynchronizerExposesPerPlayerEvents()
    {
        // The prefix resolves the picker instance per player and the flow's
        // message handler resolves the local instance through GetLocalEvent.
        Assert.NotNull(AccessTools.Method(typeof(EventSynchronizer), nameof(EventSynchronizer.GetEventForPlayer), [typeof(Player)]));
        Assert.NotNull(AccessTools.Method(typeof(EventSynchronizer), nameof(EventSynchronizer.GetLocalEvent)));
    }

    [Fact]
    public void GameDll_SharedAncientSubsetIsPrivateNoGetter()
    {
        // Native gap #4 (ticket: "ActModel:PullAncient 无 hook"): the per-act
        // shared subset (RunManager:GenerateRooms 753-760 →
        // ActModel:SetSharedAncientSubset 311-316) has no public getter — the
        // pool resolver reads the field via reflection and degrades to the
        // unlocked act pool when it drifts.
        var field = typeof(ActModel).GetField("_sharedAncientSubset", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(List<AncientEventModel>), field!.FieldType);
        Assert.Null(typeof(ActModel).GetProperty("SharedAncientSubset"));
    }

    [Fact]
    public void GameDll_ActModelPoolDerivationSurfaceExists()
    {
        // The pool is exactly what vanilla ancient-room generation draws from
        // (ActModel.cs:397): GetUnlockedAncients(unlockState) + the shared
        // subset; plus the per-act unlock gate (UnlockState gating 89-93).
        Assert.NotNull(AccessTools.Method(typeof(ActModel), nameof(ActModel.GetUnlockedAncients), [typeof(UnlockState)]));
        Assert.NotNull(AccessTools.Method(typeof(ActModel), nameof(ActModel.SetSharedAncientSubset), [typeof(List<AncientEventModel>)]));
        Assert.NotNull(AccessTools.Property(typeof(UnlockState), nameof(UnlockState.SharedAncients)));
    }
}
