# Known Limitations

## Verification gaps (most important)
- **Not opened in Unity.** No editor in the development environment. Scenes, materials, URP assets and `.meta` files
  do not exist until FFTS menu commands are run in the editor.
- `ProjectVersion.txt` says `6000.3.0f1` — **unverified patch number**. Package versions in `Packages/manifest.json`
  (URP 17.3.0, Input System 1.14.2, Cinemachine 3.1.4, Addressables 2.7.4, Test Framework 1.6.0, Newtonsoft 3.2.1,
  uGUI 2.0.0, AI Navigation 2.0.9) are **best guesses**. Let Package Manager pick the versions bundled with the installed editor.
- Runtime scripts were compile-checked only against **UnityEngine 2021.3** reference assemblies. Input System code
  and all Editor scripts have **not been compiled at all**.
- `Shader.Find("Universal Render Pipeline/Lit")` for graybox materials may be stripped from player builds. If
  geometry renders magenta in a build, add a material asset reference or include the shader in Always Included Shaders.

## Placeholders
- All characters are capsules; buildings are boxes with floating labels; no interiors yet (interactions sit outside fronts).
- No animations, audio, music, VFX or voice. `MoveDefinition.AnimationKey` and combat events are the integration hooks.
- UI is IMGUI (TD-011). Not touch-friendly; no gamepad UI navigation in dialogue (mouse or number keys).
- Fight camera is hand-written (TD-009).

## Simplifications
- Combat is 1v1 on a line. No group fights or environmental props beyond arena walls. One style (Street Fighting) authored.
- Mission 3 tutorial steps are optional objectives, so a player who wins without blocking isn't soft-locked.
- NPCs are static placements with objective-based visibility (Dre moves to the gym). No schedules or travel yet.
- Opponent fighter-id → NPC-id mapping is by naming convention (`fighter.rell` → `npc.rell`).
- Bank ledger keeps the last 5,000 entries in saves. Archived keys aren't persisted individually, so very old idempotency
  keys could theoretically be reused after a reload. Acceptable while every reward key is also in the mission ledger.
- Negative money "rewards" (costs) fail silently with insufficient funds. Needs UI feedback and a design rule.
- Grandma's Errand starts automatically once available. Side-mission offers should eventually be NPC-initiated.

## Not implemented (see PROJECT_STATUS.md)
Vehicles, racing, police, phone, music career gameplay, businesses, property, customization, crews/territory
simulation, rival encounters, world events, chapters 2–8, other districts, mobile controls, accessibility options,
settings persistence, Addressables streaming.

## Platform risks
- Mobile: IMGUI HUD and the per-frame `WorldInteractable.All` scan must be replaced. Combat sim cost is negligible (tested at thousands of ticks/ms).
- Browser (WebGL): `FileSaveStorage` needs a WebGL-specific storage (IndexedDB via `persistentDataPath` sync).
  `File.Replace` is handled with a fallback. See PlatformRequirements.md.

## Legal / licensing
- Repo `LICENSE` is GPL-3.0 (inherited). Decision needed before release.
- Real-artist casting is a proposal only. Names stay behind the rights gate.
