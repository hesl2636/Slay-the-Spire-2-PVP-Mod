using PvpDuel.Duel;

using Xunit;

namespace PvpDuel.Core.Tests.Timing;

/// <summary>
/// Ticket #22: the first-hand landing contract on the shared duel context
/// (spec §6/§7). The decision can arrive after the duel room opened — the
/// boss-wait confirmations race the room window — so the context accepts one
/// declaration and stays deterministic: 0 remains the unset sentinel and the
/// lower-NetId fallback (OpponentTurnPolicy) covers a missing decision.
/// </summary>
public class DuelContextFirstHandLandingTests
{
    [Fact]
    public void DeclaredFirstHand_IsVisible()
    {
        var context = new DuelContext { ActIndex = 1 };
        context.DeclareFirstHand(22);

        Assert.Equal(22ul, context.FirstHandNetId);
    }

    [Fact]
    public void FirstDeclarationWins_SecondIsIgnored()
    {
        var context = new DuelContext { ActIndex = 1 };
        context.DeclareFirstHand(11);
        context.DeclareFirstHand(22);

        Assert.Equal(11ul, context.FirstHandNetId);
    }

    [Fact]
    public void UnsetSentinel_IsRejected()
    {
        var context = new DuelContext { ActIndex = 1 };
        context.DeclareFirstHand(0);

        Assert.Equal(0ul, context.FirstHandNetId);
    }

    [Fact]
    public void UnsetContext_KeepsSentinel_UntilDeclared()
    {
        var context = new DuelContext { ActIndex = 2 };

        Assert.Equal(0ul, context.FirstHandNetId);
        context.DeclareFirstHand(7);
        Assert.Equal(7ul, context.FirstHandNetId);
    }
}
