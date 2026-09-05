using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using HarmonyLib;

using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Core.Save;
using PvpDuel.Logging;
using PvpDuel.Net;
using PvpDuel.Save;

namespace PvpDuel.Ancients;

/// <summary>
/// The self-built "pick your Ancient body" event (ticket #24, spec §4.1/§7
/// 结算层). Opened programmatically on BOTH ends after an act 1/2 duel settles
/// (<c>RunManager.EnterRoom(new EventRoom(...))</c>, official precedent
/// EventConsoleCmd.cs:42-44), so the vanilla co-op event machinery runs the
/// flow: per-player mutable instances, index-aligned options mirrored through
/// <c>OptionIndexChosenMessage</c>, per-player deterministic blessing draws.
///
/// Two pages per instance:
/// 1. Body pick — one option per Ancient of the current act's pool (vanilla
///    pool derivation via <see cref="AncientPoolResolver"/>, incl. the shared
///    Darv subset and the UnlockState epoch gating). The winner picks first;
///    the loser's options are mirror-locked until the winner's
///    <see cref="AncientPickMessage"/> lands, and the winner's body stays
///    locked for the loser. The pick is recorded in
///    <see cref="DuelSaveState.RecordAncientPick"/> (save side channel) and
///    broadcast for the loser's mirror lock.
/// 2. Blessing — the chosen Ancient's own <c>GenerateInitialOptions</c> random
///    draw ("祝福照旧三选一"): the draw runs on a tool clone of the chosen
///    Ancient whose Rng is SHARED with this instance (the per-player
///    deterministic stream from BeginEvent), and each drawn relic option is
///    transplanted onto this instance via <see cref="EventOption.FromRelic"/>
///    so the vanilla callbacks (RelicCmd.Obtain + Done) execute inside this
///    event on both ends and the end-of-event checksum converges.
///
/// Native gaps (acceptance #4, documented): blessing pools are hardcoded per
/// Ancient (no hook) — the delegation above reuses the original draw logic
/// through reflection instead of duplicating it; blessing options that carry
/// no relic cannot be transplanted and are skipped, degrading to the unified
/// PROCEED when nothing actionable remains (spec §12.1 兜底).
///
/// Instance state is value-typed on purpose: <see cref="AbstractModel"/>
/// clones are MemberwiseClone — reference-type per-player state would leak
/// across the per-player instances.
/// </summary>
public class DuelAncientEventModel : AncientEventModel
{
    private enum Phase
    {
        BodyPick,
        Blessing,
    }

    private Phase _phase = Phase.BodyPick;
    private string? _chosenBodyId;

    /// <summary>True while the body options are on screen (mutex patch gate).</summary>
    internal bool IsInBodyPickPhase => _phase == Phase.BodyPick;

    /// <summary>Body id (ancient model entry) behind a body-page option index.</summary>
    internal string? BodyIdForOption(int optionIndex)
    {
        var options = CurrentOptions;
        return optionIndex >= 0 && optionIndex < options.Count ? options[optionIndex].TextKey : null;
    }

    /// <summary>
    /// Body options: the act pool with real names/intros. Used for the page,
    /// for the winner's free pick and for the mirror-lock rebuilds.
    /// </summary>
    public override IEnumerable<EventOption> AllPossibleOptions =>
        RunManager.Instance.DebugOnlyGetState() is { } state
            ? BuildBodyOptions(state, _ => false)
            : [];

    protected override AncientDialogueSet DefineDialogues() => CreateDialogueSet();

    /// <summary>
    /// Minimal valid dialogue set: the ancient layout draws one dialogue via
    /// <c>Rng.Chaotic.NextItem(GetValidDialogues(...))</c> — an empty set would
    /// throw on the empty draw, so the picker ships exactly one visit-0
    /// character-agnostic line (loc keys shipped in the "ancients" table).
    /// Stats for a modded ancient always resolve to null → 0 visits, so the
    /// same line matches forever.
    /// </summary>
    public static AncientDialogueSet CreateDialogueSet() => new()
    {
        FirstVisitEverDialogue = null,
        CharacterDialogues = new Dictionary<string, IReadOnlyList<AncientDialogue>>(),
        // IsRepeating with no VisitIndex matches EVERY visit through the
        // repeating pool (PopulateLocKeys only ever upgrades IsRepeating to
        // true, so the manual flag survives PopulateLines' suffix check).
        AgnosticDialogues = [new AncientDialogue(string.Empty) { IsRepeating = true }],
    };

    /// <summary>Body-pick page: the act pool, all unlocked.</summary>
    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        var state = RunManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("DuelAncientEventModel.GenerateInitialOptions outside a run.");
        _phase = Phase.BodyPick;
        _chosenBodyId = null;
        return BuildBodyOptions(state, _ => false);
    }

    /// <summary>
    /// The picker's asset paths join its own preload session
    /// (<see cref="AncientAssetAlias"/>): the session then skips loading them
    /// (cache hit) and keeps them, so missing mod art can neither fail nor
    /// unload mid-room.
    /// </summary>
    public override IEnumerable<string> GetAssetPaths(IRunState runState)
    {
        var paths = new List<string>(base.GetAssetPaths(runState));
        var entry = Id.Entry.ToLowerInvariant();
        paths.Add(AncientAssetAlias.BackgroundScenePath(entry));
        paths.Add(AncientAssetAlias.PortraitPath(entry));
        paths.Add(AncientAssetAlias.RunHistoryIconPath(entry));
        paths.Add(AncientAssetAlias.RunHistoryIconOutlinePath(entry));
        paths.Add(AncientAssetAlias.MapIconPath(entry));
        paths.Add(AncientAssetAlias.MapIconOutlinePath(entry));
        return paths;
    }

    /// <summary>
    /// Local-instance-only UX pass (AfterEventStarted runs for the locally
    /// owned event alone): the loser starts in the mutex-lock period — every
    /// body option a locked twin — until the winner's pick arrives; a late
    /// opener skips straight to unlocked-minus-taken. Replica instances are
    /// never touched (their options stay uniform for index-aligned mirroring).
    /// </summary>
    public override Task AfterEventStarted()
    {
        try
        {
            RefreshBodyLocks();
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"ancient flow: body lock refresh failed: {ex}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Mirror-lock refresh driven by <see cref="AncientPickMessage"/>: unlocks
    /// the loser's body options with the winner's body locked. Safe no-op when
    /// this instance already advanced past the body page.
    /// </summary>
    internal void ApplyWinnerPickMirror(string ancientModelId)
    {
        if (_phase != Phase.BodyPick)
        {
            return;
        }

        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            return;
        }

        SetEventState(
            L10NLookup(Id.Entry + ".pages.INITIAL.description"),
            BuildBodyOptions(state, bodyId => bodyId == ancientModelId));
    }

    private void RefreshBodyLocks()
    {
        if (_phase != Phase.BodyPick || Owner == null || !LocalContext.IsMine(this))
        {
            return; // replica instances and canonical lookups keep uniform options
        }

        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            return;
        }

        var settled = DuelSaveState.History.GetForAct(state.CurrentActIndex);
        if (settled == null || LocalContext.NetId is not { } localId || localId != settled.LoserNetId)
        {
            return; // the winner (and any unsettled state) picks freely right away
        }

        var winnerPick = DuelSaveState.AncientPicks.FirstOrDefault(
            p => p.ActIndex == state.CurrentActIndex && p.PlayerNetId == settled.WinnerNetId);
        if (winnerPick != null)
        {
            ApplyWinnerPickMirror(winnerPick.AncientModelId);
            return;
        }

        // Mutex-lock period: all body options locked until the winner's pick.
        SetEventState(
            L10NLookup(Id.Entry + ".pages.WAITING.description"),
            BuildBodyOptions(state, _ => true));
    }

    private List<EventOption> BuildBodyOptions(IRunState state, Func<string, bool> isLocked) =>
        AncientPoolResolver.Resolve(state)
            .Select(ancient => BuildBodyOption(ancient, isLocked(ancient.Id.Entry)))
            .ToList();

    private EventOption BuildBodyOption(AncientEventModel ancient, bool locked) => new(
        this,
        locked ? null : new Func<Task>(() => ChooseBody(ancient.Id.Entry)),
        ancient.Title,
        ancient.InitialDescription,
        ancient.Id.Entry,
        []);

    /// <summary>
    /// Body option execution: record + broadcast + advance to the blessing
    /// page. Runs for the local pick and (via the vanilla mirror) for the
    /// replica instance on the peer — both ends therefore record both picks
    /// and the mutex state converges without extra bookkeeping.
    /// </summary>
    private async Task ChooseBody(string ancientId)
    {
        if (_phase == Phase.Blessing && _chosenBodyId == ancientId)
        {
            return; // mirrored replay of the same pick — nothing to rewind
        }

        _phase = Phase.Blessing;
        _chosenBodyId = ancientId;

        var actIndex = RunManager.Instance.DebugOnlyGetState()?.CurrentActIndex ?? -1;
        DuelSaveState.RecordAncientPick(new AncientPickDto(actIndex, Owner!.NetId, ancientId));
        if (LocalContext.IsMe(Owner))
        {
            RunManager.Instance.NetService?.SendMessage(new AncientPickMessage
            {
                ActIndex = actIndex,
                AncientModelId = ancientId,
            });
            PvpDuelLog.Info($"ancient flow: player {Owner.NetId} claimed {ancientId} for act {actIndex + 1}.");
        }

        EnterBlessingPhase(ancientId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Blessing page: the chosen Ancient's own random draw, transplanted onto
    /// this instance ("祝福阶段保持原 GenerateInitialOptions 随机抽取").
    /// </summary>
    private void EnterBlessingPhase(string ancientId)
    {
        var state = RunManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("DuelAncientEventModel.EnterBlessingPhase outside a run.");
        var canonical = AncientPoolResolver.Resolve(state).FirstOrDefault(a => a.Id.Entry == ancientId)
            ?? ModelDb.All.OfType<AncientEventModel>().FirstOrDefault(a => a.Id.Entry == ancientId)
            ?? throw new InvalidOperationException($"Chosen ancient '{ancientId}' is not registered.");

        var options = BuildBlessingOptions(canonical);
        if (options.Count == 0)
        {
            // Unified PROCEED fallback (spec §12.1 兜底): never soft-lock the
            // room on a pool without transplantable options.
            PvpDuelLog.Warn($"ancient flow: {ancientId} yielded no transplantable blessing options — unified PROCEED fallback.");
            SetEventState(InitialDescription, [BuildProceedOption()]);
            return;
        }

        SetEventState(canonical.InitialDescription, options);
    }

    private List<EventOption> BuildBlessingOptions(AncientEventModel canonical)
    {
        var options = new List<EventOption>();
        var clone = (AncientEventModel)canonical.ToMutable();

        // Draw-only tool clone: never begun (no heal, no node, no state
        // machine). Owner drives the pool filters; Rng is SHARED with this
        // instance so the draw is the per-player deterministic stream both
        // ends derive identically for this (run seed, slot, body) triple.
        Traverse.Create(clone).Property(nameof(Owner)).SetValue(Owner);
        Traverse.Create(clone).Property(nameof(Rng)).SetValue(Rng);

        var generate = AccessTools.Method(typeof(AncientEventModel), nameof(GenerateInitialOptions))
            ?? throw new MissingMethodException(typeof(AncientEventModel).FullName, nameof(GenerateInitialOptions));
        if (generate.Invoke(clone, null) is not IEnumerable<EventOption> drawn)
        {
            return options;
        }

        foreach (var option in drawn)
        {
            if (option?.Relic is not { } relic)
            {
                // Native gap: non-relic blessing callbacks are bound to the
                // draw clone and cannot be transplanted — skip with a record.
                PvpDuelLog.Warn($"ancient flow: {canonical.Id.Entry} blessing option '{option?.TextKey ?? "<null>"}' carries no relic — skipped.");
                continue;
            }

            options.Add(EventOption.FromRelic(relic, this, () => OnBlessingChosen(relic), option.TextKey));
        }

        return options;
    }

    private async Task OnBlessingChosen(RelicModel relic)
    {
        // Vanilla AncientEventModel.RelicOption semantics: obtain + Done —
        // executed on THIS instance on both ends, so the relic grant mirrors
        // and the end-of-event checksum converges (§12.1 mitigation).
        await RelicCmd.Obtain(relic, Owner!);
        Done();
    }

    private EventOption BuildProceedOption() =>
        new(this, () =>
        {
            Done();
            return Task.CompletedTask;
        }, "PROCEED", false, true);
}
