# PvpDuel — Slay the Spire 2 PvP Duel Mod

Two-player PvP duel runs over the official co-op session: symmetric split-path
acts, boss-room deck duels, act timing with first-hand rights, Ancient
first-picks, per-turn play caps, and a disconnect forfeit protocol.

Spec: issue #11 · Epic: issue #12 · Tickets: #13–#26 · Target game: **EA v0.111.0**

## Features

- **Duel runs** (`pvpduel on`): both players start split-path copies of the same
  act map; every branch is resolved deterministically on both ends (host table +
  broadcast). Ends at a boss-room duel per act.
- **Symmetric combat**: enemy-side flip and a driven opponent turn (all async
  Harmony prefixes use the `ref Task __result` contract — live-tested against
  the game's own 0Harmony 2.4.2); outcomes settle through a two-report consensus
  (`DuelOutcomeMessage`) with a divergence abort path.
- **Loss interception**: kill-batch interception decides duel outcome before the
  vanilla game-over screen; fate-thread style revival keeps the run alive.
- **Act timer & first hand**: the host times map-traversal per act; the shorter
  time picks first-hand; <1s differences resolve through a bit-exact mirror of
  the game's RNG stream (seed-derived, nothing on the wire).
- **Ancient flow**: act 1/2 winners pick an Ancient body first (programmatic
  event room); loser picks second with execution-layer mutual exclusion
  (`EventSynchronizer.ChooseOptionForEvent` prefix). Act 3 goes straight to the
  final settlement.
- **Disconnect protocol**: in-duel disconnects pause with a localized countdown
  (host-authoritative, `disconnectTimeoutSec`, default 120); rejoin restores
  official combat state + mod state backfill; timeout forfeits through the same
  consensus pipeline and the run continues. Map-traversal disconnects stay 100%
  vanilla.
- **Fatigue cap**: 12 plays per turn (configurable, part of the config hash).
- **Persistence**: a separate save side-channel JSON (`schema: 1`) keyed by run
  identity; corrupt/mismatched reads drop mod data only — the official save is
  never touched.

## Requirements

- Slay the Spire 2 (tested on EA **0.111.0**, `min_game_version: 0.111.0`)
- Both participants run the **same mod version** and an **identical config
  hash** (`affects_gameplay: true` makes the native handshake enforce presence).

## Build

Requires the .NET 9 SDK. The game path defaults to
`F:\Steam\steamapps\common\Slay the Spire 2` (override `-p:GameDir=<path>`).

```powershell
dotnet build
dotnet test                                                # 346 tests
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 # build + stage → build/mods/pvpduel/
```

## Install

Copy `build/mods/pvpduel/` into `<game>/mods/pvpduel/` on **both** machines.
The mod is DLL-only (`has_pck: false`); locales are embedded in the DLL.

## Playing

1. Host starts a co-op lobby (`pvpduel on` arms the duel scope on both ends).
2. Duel scope activates only in duel rooms/boss fights — normal co-op is
   untouched when `pvpduel off` (or without arming).
3. Console commands (developer console):
   - `pvpduel on|off` — arm/disarm the duel scope
   - `pvpduelturn end|status` — drive/inspect the opponent turn (lockstep queue)
   - `pvpdueloutcome on|off|status|dump|pending` — debug the outcome consensus
     (`on`/`off` force-settles divergence for testing)

## Config

`pvpduel_config.json` beside the manifest: `playCap` (12), `disconnectTimeoutSec`
(120), `fatigueEnabled`. The SHA-256 config hash must match on both ends —
mismatch blocks the duel with `PVP_DUEL_CONFIG_MISMATCH`.

## Architecture

```
PvpDuel.sln
├─ src/PvpDuel.Core/    pure logic, zero game deps (Fatigue, Duel, Timing,
│                       Outcomes, Ancients, Disconnect, Save, SelfCheck, Text)
├─ src/PvpDuel/         game-facing assembly (patch sets + installs, one per area:
│                       Combat/ Branching/ Timing/ Outcomes/ Ancients/ Disconnect/
│                       Save/ Encounters/ SelfCheck/ Net/)
├─ tests/PvpDuel.Core.Tests/   xUnit suite (346) incl. live reflection/wire
│                              contract tests against the real sts2.dll
├─ docs/manual-items/   attended verification checklists per ticket
└─ mod_manifest.json    id=pvpduel, affects_gameplay=true
```

Declarative patch sets register in `PatchTargetCatalog` and are verified at
startup before patching: a missing target (EA signature drift) disables its
whole patch set, logs `[pvpduel]` errors, and shows a main-menu banner — the
game never crashes from drift.

## Known limitations

- `ActModel:PullAncient` has no hook and per-act Ancient pools are hardcoded —
  Ancients are only obtainable through duel wins (by design; the split map has
  no per-act Ancient rooms).
- Non-relic blessings cannot be transplanted between players; if nothing in the
  winner's draw is transplantable, both ends fall back to a unified PROCEED.
- A disconnect inside the ~30s outcome-consensus window routes to the official
  divergence-abort teardown (no soft-lock; escalation deferred until observed).
- A client observing the *host* disconnect gets official teardown only (the mod
  never patches that path).

## Troubleshooting

| Symptom | Meaning | Action |
|---|---|---|
| Main-menu banner + `[pvpduel] patch set disabled` | EA update changed a target signature | Run the EA migration runbook below |
| `PVP_ERROR_DIVERGENCE` + disconnect to menu | End-state reports disagreed | Send both `[pvpduel]` logs; the console `dump` command shows each end's history |
| `PVP_DUEL_CONFIG_MISMATCH` | Config hashes differ | Align `pvpduel_config.json` on both ends |
| `save side channel … dropping mod-side data` | Side-channel JSON corrupt/schema-mismatched | Official save unaffected; delete `<profile>/saves/pvpduel_current_run_mp.save.json` |

## EA update migration runbook

See `docs/ea-migration-runbook.md`. Short form: rebuild → run the 346-test
suite → launch with the mod → confirm `patch self-check passed` with the
expected target count → re-run smoke checks 1–3 → fix drifted targets.

## Workshop

Release packaging uses the official `megacrit/sts2-mod-uploader` flow against
`mod_manifest.json` (version bump per release). After an EA update, re-run the
runbook, bump the manifest version, and re-upload — subscribers get the update
through the normal Workshop pipeline.
