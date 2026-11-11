# Character & Fight-Animation Pipeline (Storyboard Pro → Blender → Unity / browser)

_Last updated: 2026-11-11_

This is how fight choreography goes from move data to animation on screen. Everything is generated from the same JSON move
data the game runs on, so timing can't drift between the board, the animation and the simulation.

```
Assets/_Project/Data/Moves/*.json   (frame data, combos, stance, AnimationKey)
        │
        ├─ Tools/Blender/ffts_fight_rig.py ───────► FFTS_Rig skeleton + mannequin + one Action per AnimationKey (60 fps)
        │                                              │
        │   Tools/Blender/ffts_export_and_boards.py ───┼─► export/FFTS_FightRig.glb  (browser build, glTF)
        │                                              ├─► export/FFTS_FightRig.fbx  (Unity, import as Humanoid)
        │                                              └─► storyboard/frames/<NN_style>/*.png  (anticipation / impact / recovery)
        │
        └─ Tools/StoryboardPro/generate_sbp_script.py ─► ffts_build_fight_boards.js ─► Storyboard Pro project
                                                         (sequence per style, scene per move, 3 panels, captions, durations)
```

Art workspace on the owner's PC: `Documents\FightForTheSouth_Art\` (`source/`, `blend/`, `export/`, `storyboard/`, `renders/`, `backup/`).
The Blender scripts fetch the move JSON straight from this branch on GitHub, so push data changes before re-running.

## 1. Plan: Storyboard Pro 27

Project: `storyboard/FFTS_FightChoreography/` (1920×1080, 24 fps).
Built by `ffts_build_fight_boards.js` (Script Editor → Sandbox → `include(".../ffts_build_fight_boards.js"); FFTS_BuildFightBoards();`).

| Board element | Where it comes from |
|---|---|
| One **sequence per style** (16: 10 styles + 6 hybrids), dissolve transition between sequences | style list |
| **Style intro / entrance** scene: stance render, stance and defence, strengths and weaknesses, entrance and camera direction | `Stance`, `Strengths`, `Weaknesses` |
| One **scene per move** with 3 panels: **a** anticipation, **b** impact, **c** recovery | `Moves[]` |
| Panel durations = startup / active + hit-stop / recovery ticks, converted to 24 fps | frame data |
| **Action Notes** caption: what happens, damage, range, knockdown, combo routes in and out | moves + `Links` |
| **Camera** caption: framing, lens, hit-stop, shake, cut rules per move kind (finishers get the cinematic treatment) | `generate_sbp_script.py` CAMERA table |
| **Timing** caption: exact tick numbers, cancel window, animation key | frame data |

Panels hold the Blender renders as the drawing layer. Board artists can draw over them on new layers. When the
choreography changes, edit the move data, re-run the Blender board render, then regenerate and re-run the board script (it
appends a fresh set of scenes; delete the old sequence first).

## 2. Build: Blender 5.2

### Hero model (`source/player.fbx`, Meshy export), inspected 2026-11-11
| Property | Value |
|---|---|
| Geometry | 1 mesh, 47,027 vertices, 94,062 triangles, one connected piece, 1.90 m tall |
| UVs / textures / materials | **none** (one empty material slot) |
| Skeleton / weights / animation | **none** |
| Pose | standing, **both hands inside the trouser pockets** (the hands are fused into the pockets: there is no hand geometry) |
| Look | short hair, tank top, chain, cargo pants, boots (no dreads or beanie) |

**Consequence:** this mesh can't be rigged for fighting. Even with a skeleton the arms would drag the hips and there are
no hands to make fists. It stays as the title-screen sculpt. What's needed from the owner: **a Meshy regeneration in a T-pose
(arms out, hands open, legs apart) with textures**, ideally with the reference look (dreads, beanie, layered chains, graphic tee).
`ffts_fight_rig.py`'s skeleton then gets fitted to it and skinned, and every animation below works on it unchanged.

### Fight rig (`blend/10_fight_rig.blend`)
- **Skeleton:** 22 bones, Mixamo / Unity Humanoid names (`Hips, Spine, Spine1, Spine2, Neck, Head, Left/RightShoulder, Arm,
  ForeArm, Hand, UpLeg, Leg, Foot, ToeBase`), T-pose, 1.85 m, realistic proportions (head ≈ 1/7.7 of height: no chibi).
- **Mannequin:** segmented, rigidly skinned blocking body in the hero's palette (skin, white top, olive cargos, dark boots, gold
  chain) so boards read as "our guy". It is a previs body, not final art.
- **Animation:** 252 actions. Per style (16): `stance_idle` (120 f loop), `stance_guard`, `shuffle_forward`, `shuffle_back`.
  Every move (175 across all styles and hybrids), keyed from move templates (straight, hook, uppercut, overhand, body shot,
  elbow, front/round/side/spin kicks, knees, sweeps, grabs, shots, five throw families, counters, taunts, ground attacks,
  finisher combos). 12 shared reactions: hit head light/heavy, hit body, block impact, guard break, knockdown, get up, dodge
  back, slip, grappled, thrown, stunned, defeated.
- **Timing rule:** 60 fps, frame = simulation tick. The impact pose is on frame `Startup` and holds through `Active`; the
  stance is back at `Startup + Active + Recovery`. Clinch strikes (instant in the sim) use a 20-frame clip, the clinch cooldown.
- **Poses** are authored in character space with 2-bone IK for hands and feet, so each style's stance (guard height, blade
  angle, stance width, lean, bounce) is distinct and every strike reaches the opponent's line.

### Quality level: honest
These are **blocking-pass animations**: correct key poses, timing and silhouettes, eased in-betweens, with no overlap,
follow-through on cloth and hair, finger poses or contact-accurate grapples. They are good enough to play-test feel and
camera, and they replace capsules. AAA-quality motion needs one of:
1. **Mocap** per the shot list (`fight_rig_report.txt` lists every clip with its frame count and impact frame), retargeted
   to this skeleton. Timing must keep the impact frame.
2. **Hand polish in Blender** over these keys (a polish pass per clip).
3. **CC0 libraries** (e.g. Quaternius Universal Animation Library) for locomotion and reactions, retargeted by bone name.

## 3. Ship: exports and integration
| File | Use |
|---|---|
| `export/FFTS_FightRig.glb` | Browser build (three.js `GLTFLoader`; clip names = AnimationKeys) |
| `export/FFTS_FightRig.fbx` | Unity: import → Rig = **Humanoid**, Avatar from this model; clips keep their names |

Unity side (written, **not run in Unity**): `Runtime/Combat/FighterAnimator.cs` plays clips through the Playables API with
no Animator Controller. The simulation's state and `MoveTick` pick the clip and its exact time, so the hit is on screen on the
tick the simulation resolves it. Speed-adjusted startups are rescaled so the authored impact frame still lines up.
`FightAnimationLibrary` (ScriptableObject) holds the clips. Add a `FighterAnimator` to the player and opponent prefabs and
`FightDirector` drives it automatically.

## 4. Test and iterate
1. Play fights in Unity (or the browser build) with the clips. Log issues in `BUG_TRACKER.md` with the clip key and tick.
2. Fix in Blender: edit the move's template in `ffts_fight_rig.py` (or hand-key over it), re-run, re-export.
3. If choreography changes (timing, new beats), update the move JSON, re-render boards, regenerate the Storyboard Pro script.

## Character identity & customisation art requirements
The customisation system (`Simulation/Characters/`, `Data/Appearance/appearance_catalog.json`) defines what art must exist.
Every id below is a mesh or material key the art team delivers against:
- **Bodies:** one base body per sex on the FFTS_Rig skeleton with blend shapes for `Weight`, `Muscle`, shoulder/hip width and
  limb length; height by uniform root scale (155-205 cm).
- **Faces:** 8 presets (`face.*` mesh keys) and 15 blend shapes named `FFTS_<slider>` (jaw width, chin length, cheekbones,
  brow height and depth, nose width, length and bridge, lip fullness, mouth width, eye spacing and size, ear size,
  face width, forehead).
- **Hair:** 16 styles as card-based meshes (`hair.*`), including shoulder dreads, long locs, twists, cornrows, high-top and
  waves. Each one declares which headwear fits over it (e.g. dreads fit under a beanie or hood; an afro fits under nothing).
- **Clothing:** 45 items in 8 slots, skinned to FFTS_Rig, colourways as material variants. **Original brands only**
  (Third Coast Supply, Magnolia Mob, Pecan Athletic, Bayou Thread Co., Gulf Gold, SLAB City Headwear, Scrap Gym Fightwear).
  The reference image's third-party logos (XO, Nike-style swoosh) must not appear on any texture. A test fails the build
  if a real brand name appears in the catalog.
- **Named characters:** 11 authored looks (player default plus 10 Southside NPCs), checked by a test to be visually distinct
  from each other (silhouette, skin, hair, outfit). Unnamed NPCs come from the deterministic generator, which rejects
  look-alikes in the same crowd.
