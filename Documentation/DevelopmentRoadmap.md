# Development Roadmap

| # | Milestone | Status | Exit criteria |
|---|---|---|---|
| 0 | Repository audit | ✅ Done | Findings in PROJECT_STATUS.md |
| 1 | Core foundation (architecture, input, player, camera, interaction, IDs, events, saves) | ✅ Simulation tested · ⚠️ Unity layer unverified | Slice runs in Unity 6.3; Test Runner green |
| 2 | Southside vertical slice | 🟡 Graybox generated from data; no interiors | All slice locations reachable and interactive in-editor; interiors for house, studio, gym, venue |
| 3 | Combat | ✅ Simulation complete & tested · ⚠️ presentation placeholder | Feel pass in-editor; animation hookup plan; hit-stop and audio markers |
| 4 | Driving | ✅ Simulation tested · ⚠️ Unity layer unverified | Done: 3 vehicles, enter/exit, physics, 3 cameras, collisions, ownership, garages, repair/tow, upgrades, Pecan Street Sprint with AI. Left: traffic, feel pass in-editor |
| 5 | Characters & missions | ✅ Simulation tested · ⚠️ Unity layer unverified | Done: schedules, road travel, tiers, personalities, time-aware dialogue, six Ch1 missions + side mission. Left: background crowds, in-editor playthrough |
| 6 | Phone & persistence | 🟡 Saves done; phone not started | FINESS OS with Map, Contacts, Messages, Bank, mission tracking |
| 7 | Living world | 🟡 Memory and rumors done | Rival recognition, dynamic encounters, temporary alliances, crew behaviour, world events |
| 8 | Music career | ⏳ | Studio workflow → release → promotion → stats → revenue |
| 9 | Economy & businesses | 🟡 Bank done | Buy business, staff, revenue, expenses, property, investments, Business app |
| 10 | Campaign expansion | ⏳ | Chapters 2–8, Southern Table, reveal, six endings |
| 11 | Full city | ⏳ | Eight districts, streaming, interiors, territory simulation |
| 12 | Release prep | ⏳ | Regression, save, performance, input, accessibility, platform builds, asset verification |

Order follows the brief. Each milestone ships as complete, tested sub-features; no empty classes.

## Immediate backlog (ordered)
1. In-editor verification pass (README "Opening in Unity") and bug triage.
2. Replace IMGUI dialogue/HUD with uGUI (needed before mobile work).
3. In-editor driving feel pass (steering, camera, collision response).
5. M6: FINESS OS shell plus Bank/Contacts/Messages/Map apps backed by existing systems.
