using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Branching;
using PvpDuel.Core.Duel;
using PvpDuel.Core.Timing;
using PvpDuel.Duel;
using PvpDuel.Logging;
using PvpDuel.Net;

namespace PvpDuel.Timing;

/// <summary>
/// Act timer + first-hand determination (ticket #22, spec §4.2/§7 — the
/// architecture sketch's 计时层: zero Harmony, event-driven over the branch
/// layer). The HOST is the single clock source (dual-machine clocks are
/// untrusted): it stamps t0 when the act map becomes operable
/// (<c>SetActInternal</c> postfix → <see cref="BranchSync.ActChanged"/>) and
/// each player's boss-wait confirmation on the mirrored
/// <see cref="PvpDuel.Core.Duel.BranchTable"/>, then broadcasts one
/// <see cref="DuelTimerMessage"/> per player. Each end derives the identical
/// first hand locally (shorter time wins; a &lt;1s difference falls back to the
/// run-seed-derived RNG — the RNG result never travels the wire, spec §4.2)
/// and lands it on <see cref="DuelContext.FirstHandNetId"/>.
/// </summary>
public static class ActTimer
{
    private static readonly object Gate = new();
    private static readonly ActTimingTracker Timings = new();
    private static readonly TimerAttributionQueue ClientAttribution = new();
    private static readonly Dictionary<int, ulong> DecidedFirstHandByAct = [];
    private static bool _installed;

    /// <summary>
    /// One-time subscription to the branch layer's events. Called once from the
    /// mod initializer; idempotent. Purely passive until a split-map co-op run
    /// is live (the branch layer raises events only while active), so
    /// singleplayer and vanilla co-op are untouched (spec §5.1 red line).
    /// </summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        BranchSync.ActChanged += OnActChanged;
        BranchSync.BranchStateChanged += OnBranchStateChanged;
        PvpDuelLog.Info("act timer installed (host-clock DuelTimerMessage broadcast + first-hand).");
    }

    /// <summary>
    /// Settled first hand for an act, consumed by <c>DuelScope.OnRoomEntered</c>
    /// to seed fresh duel contexts; null before the decision landed.
    /// </summary>
    public static ulong? DecidedFirstHandFor(int actIndex)
    {
        lock (Gate)
        {
            return DecidedFirstHandByAct.TryGetValue(actIndex, out var netId) ? netId : null;
        }
    }

    // ---- Branch-layer events ----

    private static void OnActChanged(int actIndex)
    {
        try
        {
            lock (Gate)
            {
                if (actIndex == 0)
                {
                    // Every run starts at act 0: drop any prior run's decisions.
                    DecidedFirstHandByAct.Clear();
                }

                ClientAttribution.ResetAct(actIndex);
                // Both ends re-arm. The host measures on its own monotonic
                // clock; the client accumulates on the host-reported elapsed
                // axis, so its t0 is 0.
                Timings.ActStarted(actIndex, BranchSync.IsHost ? NowMs() : 0);
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"act timer act-start failed: {ex}");
        }
    }

    private static void OnBranchStateChanged(ulong playerNetId, BranchState state)
    {
        try
        {
            if (!state.InBossWait)
            {
                return;
            }

            if (BranchSync.IsHost)
            {
                // Host stop-stamp. The raise site guarantees the confirming
                // DuelBranchMessage already went out, so this timer broadcast
                // follows it on the reliable channel (client attribution order).
                ActTimingPair? rendezvous;
                lock (Gate)
                {
                    if (!Timings.RecordArrival(state.ActIndex, playerNetId, NowMs()))
                    {
                        return;
                    }

                    rendezvous = Timings.TryGetRendezvousPair(state.ActIndex, out var pair) ? pair : null;
                }

                if (Timings.TryGetElapsedMs(state.ActIndex, playerNetId, out var elapsedMs))
                {
                    SendTimer(state.ActIndex, elapsedMs);
                }

                if (rendezvous is { } completed)
                {
                    DecideAndLand(completed);
                }
            }
            else
            {
                lock (Gate)
                {
                    ClientAttribution.Confirmed(state.ActIndex, playerNetId);
                }
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"act timer boss-wait stamp failed: {ex}");
        }
    }

    /// <summary>
    /// Consumes one host-broadcast timer (client end). The host never consumes:
    /// it is the clock source and ignores its own echo.
    /// </summary>
    internal static void OnTimerReceived(int actIndex, long elapsedMs)
    {
        if (BranchSync.IsHost)
        {
            return;
        }

        try
        {
            ActTimingPair? rendezvous;
            lock (Gate)
            {
                if (!ClientAttribution.TryAssign(actIndex, out var playerNetId))
                {
                    PvpDuelLog.Error(
                        $"act timer: no pending boss-wait confirmation for act {actIndex + 1} — timer dropped; " +
                        "first hand falls back to the deterministic lower-NetId rule.");
                    return;
                }

                Timings.RecordArrival(actIndex, playerNetId, elapsedMs);
                rendezvous = Timings.TryGetRendezvousPair(actIndex, out var pair) ? pair : null;
            }

            if (rendezvous is { } completed)
            {
                DecideAndLand(completed);
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"act timer receive failed: {ex}");
        }
    }

    // ---- Decision ----

    private static void DecideAndLand(ActTimingPair pair)
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            PvpDuelLog.Warn("act timer: no run state for the tiebreak seed; first hand stays unset (lower-NetId fallback).");
            return;
        }

        // Rng(ulong seed, string name) == MegaRandom(seed + StringHelper hash of
        // the name) — decomp Rng.cs:55-58. Both ends share the run seed and run
        // this same derivation, so the fallback bit is identical everywhere.
        var derivedSeed =
            state.Rng.Seed + StringHelper.GetDeterministicHashCode($"pvp_firsthand_act{pair.ActIndex + 1}");
        var decision = FirstHandDecider.Decide(pair, derivedSeed);

        lock (Gate)
        {
            DecidedFirstHandByAct[pair.ActIndex] = decision.FirstHandNetId;
        }

        PvpDuelLog.Info(
            $"act timer: act {pair.ActIndex + 1} first hand -> player {decision.FirstHandNetId} " +
            $"(rngFallback={decision.UsedRngFallback}, players {pair.FirstNetId}/{pair.SecondNetId} " +
            $"at {pair.FirstElapsedMs}/{pair.SecondElapsedMs} ms).");

        // Race cover: the duel room may have opened before the decision — its
        // live context takes the declaration; fresh rooms are seeded at entry.
        if (DuelScope.Current is { } context && context.ActIndex == pair.ActIndex)
        {
            context.DeclareFirstHand(decision.FirstHandNetId);
        }
    }

    private static void SendTimer(int actIndex, long elapsedMs) =>
        RunManager.Instance.NetService?.SendMessage(new DuelTimerMessage
        {
            ActIndex = actIndex,
            ElapsedMs = elapsedMs,
        });

    private static long NowMs() => Environment.TickCount64;
}
