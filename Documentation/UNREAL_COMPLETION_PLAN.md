# FIGHT FOR THE SOUTH — Unreal completion plan

_Written 2026-11-17. Unreal 5.8 is the main build; Unity/C# and the browser build are references._
Status key: ✅ done and verified · 🟢 done this session, verified by tests here · 🟡 written, not yet verified on the PC ·
⏳ not started · ✂️ dropped after review (reason given).

The rule for every line below: before building, check whether the repo already has it (C# `Assets/_Project/Scripts/Simulation`,
JS `WebPrototype/src/sim`, C++ `Unreal/FightForTheSouth/Source`). Port; don't reinvent. Every gameplay rule lives in the
engine-free C++ core (`FFTSCore`) with a test in `CoreTests/`, and Unreal code only presents it.

## A. Requirements to finish the game

### A1. Foundation (Unreal)
| # | Requirement | Status | Notes / evidence |
|---|---|---|---|
| 1 | Project compiles with MSVC in UE 5.8, editor + packaged Win64 | ✅ | Built 2026-11-13; `Builds\Windows\FightForTheSouth.exe` ran |
| 2 | Engine-free core with stand-alone tests | ✅ | `CoreTests/run.sh` |
| 3 | JSON content loaded from `Content/FFTSData` (synced from Unity data) | ✅ | `UFFTSGameSubsystem`, `Tools/sync-unreal-data.sh` |
| 4 | Repeatable asset pipeline (Blender → FBX cm → import → Manny retarget) | ✅ | `ffts_rebuild_cm.py` |
| 5 | Test build packaging | ✅ | `ffts_package.py` |
| 6 | Keyboard + Xbox controller input, rumble, device-aware prompts | 🟡 | Code on PC + GitHub, not compiled yet |

### A2. Combat (M3)
| # | Requirement | Status | Notes |
|---|---|---|---|
| 7 | Combat rules, 10 styles + 6 hybrids, AI, stances | ✅ | FFTSCore, 13 C++ tests; JS + C# mirrors |
| 8 | Manny body with all 252 clips, full hip/IK motion | ✅ | Verified in play-test |
| 9 | Clip timing matches move data | ⏳ | BUG-017: five clips 1 tick off; needs a Blender re-run on the PC |
| 10 | Animation quality pass on all 10 styles | ⏳ | Needs the PC (visual) |
| 11 | Hit-stop, camera shake, hit sounds | ⏳ | Recommendation R3 |

### A3. Story, missions, economy, saves (M1/M5/M6) — the open-world backbone
| # | Requirement | Status | Notes |
|---|---|---|---|
| 12 | FINESS BANK (accounts, idempotent ledger) in C++ | 🟢 | Port of `story.js` Bank |
| 13 | Face Card / Public Image reputation in C++ | 🟢 | |
| 14 | Relationships, personality, memories, attitudes in C++ | 🟢 | |
| 15 | Story flags + Chairman reveal guard (Ch 7) in C++ | 🟢 | Guard must not be bypassable |
| 16 | Missions (prereqs, branches, failure/recovery, reward ledger) in C++ | 🟢 | |
| 17 | Dialogue runner + NPC conversation rules in C++ | 🟢 | |
| 18 | Game session (clock, triggers, grants, fights → reputation) in C++ | 🟢 | |
| 19 | Save/load (JSON, versioned, round-trip) in C++ | 🟢 | JSON writer added to `FFTSJson`; round-trip test is byte-identical |
| 20 | Full Chapter 1 playthrough test in C++ | 🟢 | Same assertions as the JS/C# tests |
| 21 | Unreal subsystem exposing the session (Blueprint-callable), JSON save slots | 🟡 | `UFFTSGameSubsystem`: Trigger, TickWorld, clock/money/mission text, toasts, SaveToSlot/LoadFromSlot (atomic write). Needs a compile on the PC; nothing calls TickWorld until the open world exists |

### A4. World, NPCs, vehicles, racing (M2/M4/M5)
| # | Requirement | Status | Notes |
|---|---|---|---|
| 22 | NPC schedules + road travel in C++ | 🟢 | `FFTSWorld` (road graph, schedules with mission pins, travel tiers) |
| 23 | Vehicles, garages, upgrades, repair/tow in C++ | ⏳ | Port `vehicles.js` + session garage |
| 24 | Racing in C++ | ⏳ | |
| 25 | Southside level in Unreal (graybox from `southside_layout.json`, then art) | ⏳ | Needs the PC |
| 26 | Third-person player movement + camera outside fights | ⏳ | Needs the PC |
| 27 | Drivable cars (Chaos Vehicles) driven by the C++ model | ⏳ | |
| 28 | Interiors: house, studio, gym, venue | ⏳ | |

### A5. UI and phone (M6)
| # | Requirement | Status | Notes |
|---|---|---|---|
| 29 | Main menu, pause, settings, save slots | ⏳ | |
| 30 | Dialogue UI, mission tracker, toasts | ⏳ | Session API is ready for it after A3 |
| 31 | FINESS OS phone (Map, Contacts, Messages, Bank) | ⏳ | |
| 32 | Character creator, barber, stores, gyms (learn/equip styles) | ⏳ | Rules exist (appearance, StyleLoadout) |

### A6. Later milestones
| # | Requirement | Status |
|---|---|---|
| 33 | Living world: rivals, encounters, crews, world events (M7) | ⏳ |
| 34 | Music career (M8) | ⏳ |
| 35 | Businesses / property (M9) | ⏳ |
| 36 | Chapters 2–8, six endings (M10) | ⏳ |
| 37 | Full city, 8 districts, streaming (M11) | ⏳ |
| 38 | Hero + named NPCs as real characters (owner's reference) | ⏳ needs T-pose model from Meshy |
| 39 | Release prep: performance, accessibility, regression (M12) | ⏳ |

## B. Recommendations (checked against what exists)
| # | Recommendation | Already have it? | Decision |
|---|---|---|---|
| R1 | One C++ test command that also checks the story content loads | Partly (combat only) | 🟢 `run.sh` builds every engine-free source and runs combat + session suites |
| R2 | Sanitizer run (ASan/UBSan) of the core tests | No | 🟢 `CoreTests/run.sh asan` — clean |
| R3 | Hit-stop + camera shake + hit sounds on impact | No | ⏳ Needs the PC to tune; small |
| R4 | Controller rumble on hits | Written | 🟡 Part of the controller pass |
| R5 | Autosave on mission complete / chapter change | C# has autosave rotation | ⏳ After A3 is in Unreal |
| R6 | Photo mode / replay of last fight (deterministic sim makes replays cheap) | No | ⏳ Later; deterministic inputs already make it easy |
| R7 | Accessibility: remappable controls, subtitles, hold-to-tap toggles | Input remap data exists in C# | ⏳ With the UI |
| R8 | Packaged-build smoke test that boots the .exe and checks the log | No | ⏳ Needs the PC |
| R9 | Version stamp on the HUD (commit + date) so test builds are identifiable | No | ⏳ Small; do with the next build |
| R10 | Port the 3D web graphics to Unreal | ✂️ | Dropped: Unreal replaces it, not worth porting |
| R11 | Re-implement saves with Unreal SaveGame objects | ✂️ | Dropped: the core's JSON save keeps one format for C#, JS and C++ and is testable off-engine |
| R12 | Multiplayer | ✂️ | Out of scope (PlatformRequirements.md) |

## C. Session log
- 2026-11-17 self-review findings, all fixed:
  - **Duplicate `ffts::Bank`**: the combat code had a cash-only bank and the new story code a full one with the same name
    (an ODR violation that would break the Unreal build). Merged into one `FFTSBank.h`; style training now pays through the
    same FINESS BANK as missions, with the JS memo ("Training: <style>").
  - `LivingWorld::near` renamed `nearby`: `near` is a Windows header macro (the same trap as the earlier `near` local).
  - A test expectation was wrong (Fear 40 at average courage scales to 36, below the Intimidated threshold); checked
    against the JS reference, fixed the test, not the code.
  - Pointer-difference narrowing (`long` is 32-bit on Windows) replaced with `auto` / `ptrdiff_t`.
- Verification here: combat 13/13 and session 10/10 with g++ and clang, C++20 and C++17, and under ASan/UBSan; JS 28/28.
  Values cross-checked against the JS implementation (relationship scaling, attitudes, money formatting, Chapter 1 total).
- 2026-11-17: PC offline all session, so work was limited to what can be verified here (C++ core + tests, code review).
