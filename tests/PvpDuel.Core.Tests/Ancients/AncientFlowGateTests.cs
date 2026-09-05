using PvpDuel.Core.Ancients;
using PvpDuel.Core.Duel;

using Xunit;

namespace PvpDuel.Core.Tests.Ancients;

/// <summary>
/// Ticket #24 (spec §7 结算层): the session-scoped pending/open state machine
/// between the settled act outcome and the programmatically opened picker room.
/// Act 3 settles straight into the terminal hook — no Ancient flow at all
/// (acceptance #2); the picker opens exactly once, only after the duel combat
/// room has closed, and only while still in the settled act.
/// </summary>
public class AncientFlowGateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Act12Settlement_RegistersPending(int actIndex)
    {
        Assert.Equal(actIndex, AncientFlowGate.RegisterSettledAct(null, actIndex));
    }

    [Fact]
    public void FinalActSettlement_DoesNotRegister()
    {
        // Act 3 victory is the endgame (FinalOutcomeSettled consumers); the
        // ancient flow never opens for it.
        Assert.Null(AncientFlowGate.RegisterSettledAct(null, DuelOutcomeDispatcher.FinalActIndex));
    }

    [Fact]
    public void FreshSettlement_ReplacesStalePending()
    {
        Assert.Equal(1, AncientFlowGate.RegisterSettledAct(0, 1));
    }

    [Fact]
    public void MapRoomEntry_AfterDuelRoomClosed_OpensForPendingAct()
    {
        Assert.True(AncientFlowGate.ShouldOpenPicker(pendingAct: 0, enteredActIndex: 0, enteredRoomIsCombat: false));
    }

    [Fact]
    public void CombatRoomEntry_DoesNotOpen()
    {
        // The settlement fires during the combat teardown; the picker must
        // wait until the vanilla post-combat flow walked back to the map.
        Assert.False(AncientFlowGate.ShouldOpenPicker(pendingAct: 0, enteredActIndex: 0, enteredRoomIsCombat: true));
    }

    [Fact]
    public void ActMismatch_DoesNotOpen()
    {
        Assert.False(AncientFlowGate.ShouldOpenPicker(pendingAct: 0, enteredActIndex: 1, enteredRoomIsCombat: false));
    }

    [Fact]
    public void NoPending_DoesNotOpen()
    {
        Assert.False(AncientFlowGate.ShouldOpenPicker(pendingAct: null, enteredActIndex: 0, enteredRoomIsCombat: false));
    }

    [Fact]
    public void PendingForAnotherAct_IsDropped()
    {
        // Restored sessions / stale settles must not linger: a room entry in a
        // different act retires the pending registration.
        Assert.True(AncientFlowGate.ShouldDropPending(pendingAct: 0, enteredActIndex: 1));
        Assert.False(AncientFlowGate.ShouldDropPending(pendingAct: 1, enteredActIndex: 1));
    }
}
