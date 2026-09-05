using PvpDuel.Core.Duel;
using PvpDuel.Core.Save;

namespace PvpDuel.Core.Ancients;

/// <summary>Execution-layer verdict for one Ancient body pick attempt.</summary>
public enum AncientPickVerdict
{
    /// <summary>The pick executes (including an idempotent replay of the same body).</summary>
    Allowed,

    /// <summary>The opponent's recorded body — rejected with the taken prompt.</summary>
    TakenByOpponent,

    /// <summary>This player already claimed a different body — rejected.</summary>
    AlreadyPicked,

    /// <summary>No settled duel for the act, or the chooser took no side in it.</summary>
    NotADuelParticipant,
}

/// <summary>
/// Ticket #24 (spec §4.1/§7 结算层): pure mutex decision for the Ancient body
/// pick. The winner of the settled act duel picks first, the loser picks
/// second without repeats; the record is replace-per-(act, player) so mirrored
/// option executions can replay idempotently. Both ends derive the same
/// verdict from the mirrored <see cref="DuelHistory"/> and pick records.
/// </summary>
public static class AncientMutexPolicy
{
    /// <summary>
    /// Evaluates one pick attempt. <paramref name="settled"/> is the act's
    /// settled duel (null before settlement), <paramref name="picks"/> the
    /// pick records visible on this end (all acts; filtered here).
    /// </summary>
    public static AncientPickVerdict Evaluate(
        DuelResult? settled,
        IReadOnlyList<AncientPickDto> picks,
        int actIndex,
        ulong chooserNetId,
        string ancientModelId)
    {
        if (string.IsNullOrEmpty(ancientModelId)
            || settled is null
            || settled.ActIndex != actIndex
            || (chooserNetId != settled.WinnerNetId && chooserNetId != settled.LoserNetId))
        {
            return AncientPickVerdict.NotADuelParticipant;
        }

        var mine = FindPick(picks, actIndex, chooserNetId);
        if (mine is not null)
        {
            return mine.AncientModelId == ancientModelId
                ? AncientPickVerdict.Allowed
                : AncientPickVerdict.AlreadyPicked;
        }

        var opponentNetId = chooserNetId == settled.WinnerNetId ? settled.LoserNetId : settled.WinnerNetId;
        var theirs = FindPick(picks, actIndex, opponentNetId);
        return theirs is not null && theirs.AncientModelId == ancientModelId
            ? AncientPickVerdict.TakenByOpponent
            : AncientPickVerdict.Allowed;
    }

    private static AncientPickDto? FindPick(IReadOnlyList<AncientPickDto> picks, int actIndex, ulong playerNetId)
    {
        for (var i = 0; i < picks.Count; i++)
        {
            var pick = picks[i];
            if (pick.ActIndex == actIndex && pick.PlayerNetId == playerNetId)
            {
                return pick;
            }
        }

        return null;
    }
}
