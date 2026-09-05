namespace PvpDuel.Core.Duel;

/// <summary>
/// Duel results that have been settled, in act order. Act 1/2 winners drive
/// Ancient first-pick rights; the act 3 result ends the whole run. Persisted
/// through the save side-channel so both ends restore it on load.
/// </summary>
public sealed class DuelHistory
{
    private readonly List<DuelResult> _results = [];

    public IReadOnlyList<DuelResult> Results => _results;

    /// <summary>
    /// Records a settled duel. Re-recording the same act replaces the previous
    /// entry (last write wins, as both ends converge on the agreed result).
    /// </summary>
    public void Record(DuelResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _results.RemoveAll(r => r.ActIndex == result.ActIndex);
        _results.Add(result);
        _results.Sort((a, b) => a.ActIndex.CompareTo(b.ActIndex));
    }

    public DuelResult? GetForAct(int actIndex) => _results.FirstOrDefault(r => r.ActIndex == actIndex);

    /// <summary>The act whose winner gets Ancient first-pick (null when none settled yet).</summary>
    public DuelResult? Latest => _results.Count > 0 ? _results[^1] : null;

    public void Clear() => _results.Clear();
}
