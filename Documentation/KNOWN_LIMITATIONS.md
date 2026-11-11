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

## Session 2026-11-11 gaps
- **The C# changes were not compiled** (no .NET SDK in that session). The JS port of the same rules passes 28 tests. Run `Tools/run-tests.sh` before anything else.
- The browser build was **not rebuilt** (three.js could not be installed without network). `build-data.mjs` now loads every style and the appearance catalog; `FFTS_FightRig.glb` is in `WebPrototype/assets/animations/` but the client doesn't load it yet.
- **Hero model can't be rigged**: the Meshy mesh has the hands fused into the pockets, no UVs and no textures. It needs a T-pose re-export (see `CharacterAndAnimationPipeline.md`).
- Fight clips are **blocking quality** (key poses, correct timing), not final animation. The mannequin is a previs body.
- Storyboard panels use Blender renders as drawings; opponent reactions in throw panels aren't posed (BUG-016); intro frames are cropped (BUG-015).
- No creator, store or style-learning UI yet: the rules, content and saves exist.

## Placeholders
- Characters in Unity are capsules until rigged prefabs with `FighterAnimator` are set up; the fight rig and 252 blocking-quality clips now exist (`Assets/_Project/Art/Characters/FFTS_FightRig.fbx`); buildings are boxes with floating labels; no interiors yet (interactions sit outside fronts).
- No animations, audio, music, VFX or voice. `MoveDefinition.AnimationKey` and combat events are the integration hooks.
- UI is IMGUI (TD-011). Not touch-friendly; no gamepad UI navigation in dialogue (mouse or number keys).
- Fight camera is hand-written (TD-009).

## Simplifications
- Combat is 1v1 on a line. No group fights or environmental props beyond arena walls. Ten styles and six hybrids are authored; fighters in the world (Dre, Rell, Brick) still use Street Fighting.
- Mission 3 tutorial steps are optional objectives, so a player who wins without blocking isn't soft-locked.
- NPCs follow schedules and walk or drive along road centrelines. They don't avoid the player or cars, and there are no background crowds yet.
- Walking NPCs are slow relative to the clock (1 game minute = 1 real second by default): a long walk can take hours of game time.
- Opponent fighter-id → NPC-id mapping is by naming convention (`fighter.rell` → `npc.rell`).
- Bank ledger keeps the last 5,000 entries in saves. Archived keys aren't persisted individually, so very old idempotency
  keys could theoretically be reused after a reload. Acceptable while every reward key is also in the mission ledger.
- Negative money "rewards" (costs) fail silently with insufficient funds. Needs UI feedback and a design rule.
- Grandma's Errand starts automatically once available. Side-mission offers should eventually be NPC-initiated.

## Driving (Milestone 4)
- Cars are boxes with block wheels; no engine audio, skid marks or particles. No traffic vehicles yet.
- Collision is a kinematic box sweep: cars stop and take damage, they don't bounce or push each other.
- Analogue triggers are read as on/off by the Input System adapter, so there's no partial throttle on gamepad yet.
- A wrecked car can be repaired at any garage panel before it's been towed. Should require towing first.
- Only the active vehicle exists in the world; other vehicles left "Out" must be towed from a garage.
- Race opponents ignore buildings (they drive the checkpoint line; Unity collisions stop them if they clip a corner).

## Not implemented (see PROJECT_STATUS.md)
Traffic, police, phone, music career gameplay, businesses, property, customization, crews/territory
simulation, rival encounters, world events, chapters 2–8, other districts, mobile controls, accessibility options,
settings persistence, Addressables streaming.

## Platform risks
- Mobile: IMGUI HUD and the per-frame `WorldInteractable.All` scan must be replaced. Combat sim cost is negligible (tested at thousands of ticks/ms).
- Browser (WebGL): `FileSaveStorage` needs a WebGL-specific storage (IndexedDB via `persistentDataPath` sync).
  `File.Replace` is handled with a fallback. See PlatformRequirements.md.

## Legal / licensing
- Repo `LICENSE` is GPL-3.0 (inherited). Decision needed before release.
- Real-artist casting is a proposal only. Names stay behind the rights gate.
