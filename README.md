# PvpDuel — Slay the Spire 2 PvP Duel Mod

Two-player PvP duel runs over the official co-op session (same seed, symmetric
split-path acts, boss-room deck duels, Ancient first-picks, per-turn play caps).
Spec: issue #11. This repo currently contains the **ticket #13 skeleton**
(tracer bullet #0): project skeleton, startup self-check framework, config +
hash contract, save side-channel schema, localization, and stub contracts that
later tickets (T3+) fill in. No gameplay behavior changes yet.

## Layout

```
PvpDuel.sln
├─ src/PvpDuel.Core/        Pure logic, zero game deps, fully unit-tested
│  ├─ Fatigue/FatigueRule            per-turn play cap (default 12)
│  ├─ Duel/DuelResult, DuelEndReason mirrored duel outcome
│  ├─ Duel/BranchState/BranchTable   host-authoritative per-player path table
│  ├─ Duel/DuelHistory               settled duel results per act
│  ├─ Config/PvpConfig, ConfigHash   playCap=12, disconnectTimeoutSec=120, fatigueEnabled + SHA-256
│  ├─ Save/DuelSaveCodec             schema:1 save side-channel (JSON, lenient read)
│  ├─ SelfCheck/*                    declarative patch manifest + startup self-check runner
│  └─ Text/LocalizationCatalog       flat key/value tables with fallback chain
├─ src/PvpDuel/             Game-facing assembly (refs sts2/0Harmony/GodotSharp, Private=false)
│  ├─ ModEntry            [ModInitializer] + Harmony.PatchAll skeleton
│  ├─ Logging/PvpDuelLog  "[pvpduel]" channel, runtime-toggleable
│  ├─ SelfCheck/          AccessTools probe, declarative catalog, main-menu banner
│  ├─ Localization/Loc    embedded locales (en-US/zh-CN), PVP_* keys only
│  ├─ Config/PvpConfigStore  pvpduel_config.json beside the manifest
│  ├─ Duel/DuelScope      scope gate (stub: IsDuelRoom=false)
│  ├─ Encounters/PvpDuelEncounter    duel encounter (boss-room swap product)
│  ├─ Ancients/                      T12: DuelAncientEventModel picker room, flow driver,
│  │                                 AncientPickMessage handler, ChooseOptionForEvent mutex
│  └─ Net/DuelMessages    DuelBranch/DuelTimer/DuelOutcome/AncientPick (wire format ready;
│                         v0.111.0 auto-registers mod INetMessage subtypes — no Harmony needed)
├─ tests/PvpDuel.Core.Tests/  xUnit suite (45 tests)
├─ scripts/build.ps1          one-shot build + stage (build/mods/pvpduel/)
├─ scripts/pack_pck.gd        PCKPacker script (optional PCK)
├─ scripts/setup_godot_cli.ps1  downloads Godot 4.5.1 CLI into .tools/
├─ godot/pvpduel/…            resources for the optional PCK
└─ mod_manifest.json          id=pvpduel, affects_gameplay=true, v0.1.0
```

## Build

Requires .NET 9 SDK and the game at `F:\Steam\steamapps\common\Slay the Spire 2`
(override with `-p:GameDir=<path>`).

```powershell
dotnet build          # whole solution
dotnet test           # Core unit tests
powershell -ExecutionPolicy Bypass -File scripts/build.ps1              # build + stage
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Install     # + copy into <game>/mods/
powershell -ExecutionPolicy Bypass -File scripts/setup_godot_cli.ps1    # optional: Godot CLI for PCK
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -PackPck     # optional: also emit pvpduel.pck
```

Output: `build/mods/pvpduel/{pvpduel.json, pvpduel.dll[, pvpduel.pck]}`.
The skeleton ships DLL-only (`has_pck=false`); the loader treats that as valid.

## Install

Copy `build/mods/pvpduel/` into `<game>/mods/pvpduel/` (or use `-Install`).
Both duel participants must run the same mod version + identical config hash;
`affects_gameplay=true` makes the native handshake enforce mod presence.

## Startup self-check

Every Harmony target later tickets register into `PatchTargetCatalog` is
verified at startup (Traverse/AccessTools probe) before patching. A missing
target disables its whole patch set, logs `[pvpduel]` errors, and shows a
main-menu banner — the game never crashes from signature drift. With the
(currently empty) catalog the self-check passes with 0 failures.

## Save side-channel

Mod state (branch table, settled duel results, Ancient picks) lives in a
separate JSON blob keyed by run identity (`"schema": 1`). A failed/corrupt read
only drops mod-side data with a Warn — the official `SerializableRun` is never
touched.

- Remaining skeleton stubs (`DuelScope` gating surface, message handlers not
  yet wired by their tickets) are filled by T3+; the Ancient flow (T12) and
  the settlement layer (T11) are live.
- PCK packing requires the Godot 4.5.1 CLI (`setup_godot_cli.ps1`); the
  skeleton runs DLL-only with locales embedded in the DLL.
