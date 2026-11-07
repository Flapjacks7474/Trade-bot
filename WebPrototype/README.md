# Fight for the South: browser version (playable)

A browser-playable version of Chapter 1 in Southside, built from **the same JSON content** as the Unity project
(`Assets/_Project/Data`). It runs on phones (touch controls) and desktops (keyboard and mouse) with no install.

Published link: https://claude.ai/artifact/CV2ZgWjGkitz2gnQwXVdEj (private to the owner until shared).

## What's in it
- 3D Southside built from `southside_layout.json`: roads, buildings, signs, street lights, day/night lighting from the game clock.
- Chapter 1 missions, dialogue choices, conversation rules, story flags, rewards and reputation.
- Fights against Dre, Rell and Brick: a line-by-line port of the C# combat simulation and fighter AI (combos, parry, counters,
  grapples, throws, knockdowns, finishers).
- NPC daily schedules and walking/driving along roads (port of the living-world system).
- Garage (buy, drive, park, repair, tow, upgrades, handling presets), driving model with crashes and damage, and the Pecan Street
  Sprint against Rell and Deuce with prize money and racing rank.
- Saves to the device (`localStorage`), autosave after missions and every minute.

## Layout
| Path | What |
|---|---|
| `src/sim/` | Game rules ported from `Assets/_Project/Scripts/Simulation` (combat, story, vehicles, world, session) |
| `src/game/` | Three.js client: input, 3D world, modes, HUD |
| `page.html` | Page shell: HUD, touch pads, sheets, styles |
| `build.mjs`, `build-data.mjs` | Bundles everything (data + three.js + code) into `dist/fight-for-the-south.html` |
| `tests/*.test.mjs` | Node tests mirroring the C# tests (combat, Chapter 1 playthrough, racing, NPC travel) |
| `tests/smoke.mjs` | Headless-browser run of real gameplay (walk, talk, fight, buy, drive, race, save/reload, phone layout) |

## Commands
```bash
npm install            # three 0.160.0, esbuild 0.20.2
npm test               # rule tests (node)
npm run build          # writes dist/fight-for-the-south.html
PW_ROOT=$(npm root -g) node tests/smoke.mjs <screenshot-dir>   # needs Playwright + Chromium
```

The Unity project stays the real game. When rules change in the C# simulation, mirror them in `src/sim/` and re-run both test suites.
