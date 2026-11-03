# Platform Requirements

## PC (primary)
Windows 10/11 x64, keyboard + mouse and XInput/DualSense via the Input System. Target 60 fps at 1080p on a GTX 1060-class
GPU for the slice (unverified; profile in M12). Saves in `Application.persistentDataPath/saves`.

## Mobile (design-compatible, not built)
- Input already abstracted (`InputFrame`). Needed: on-screen controls (left stick, camera drag, contextual button, Light,
  Heavy, Grapple, Block, Dodge, Counter, Finisher; driving: accelerator, brake, steering, handbrake, nitrous), layout editing.
- Counter has its own action so touch players don't need parry timing.
- Replace IMGUI. Cap active combatants (planned: 4 on mobile), crowd density setting, LOD/texture budgets, URP mobile renderer.

## Browser (feasibility notes; no commitment)
| Concern | Assessment |
|---|---|
| Engine | Unity 6 Web build supports URP (WebGL2; WebGPU experimental) |
| Build size | Open world plus audio will exceed comfortable download sizes; needs Addressables streaming from a CDN |
| Memory | Browser heap limits (~2–4 GB) conflict with full-city streaming; slice-only demo plausible |
| Saves | `File.Replace` unsupported (fallback implemented); persistence needs an IndexedDB sync step |
| Input | Pointer lock required for the mouse camera; gamepad via the Gamepad API works with the Input System |
| Threads | Limited multithreading; the simulation is single-threaded already |
Verdict: a **Southside slice demo** in the browser is feasible to try; a full equivalent release is not promised.

## Multiplayer
Future only. The deterministic, engine-independent simulation keeps a server-authoritative or rollback path open.
