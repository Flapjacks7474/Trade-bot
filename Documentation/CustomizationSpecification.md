# Customization Specification — status: RULES + CONTENT IMPLEMENTED, ART PENDING

_Last updated: 2026-11-11._ Source: `Scripts/Simulation/Characters/` (`Appearance.cs`, `Wardrobe.cs`), content
`Data/Appearance/appearance_catalog.json`, JS port `WebPrototype/src/sim/appearance.js`.

## Model (`CharacterAppearance`)
| Part | Range / content |
|---|---|
| Body | height 155-205 cm, weight, muscle, shoulder width, hip width, arm and leg length (0-1). Realistic adult proportions; the rig fixes the head-to-body ratio. |
| Face | 8 presets + 15 sliders (-1..1), each a blend shape `FFTS_<slider>` |
| Skin, eyes | 10 skin tones, 6 eye colours |
| Hair | 16 styles (incl. shoulder dreads, long locs, twists, cornrows, waves, high-top) × 9 colours; facial hair 7 styles |
| Outfit | 8 slots: Head, Top, Outerwear, Bottoms, Shoes, Neck, Wrist, Eyewear. Top, bottoms and shoes are required. |

**Rules:** every look is validated (`AppearanceCatalog.Check`): known ids, item in the right slot, colourway offered,
hair that fits under the headwear (e.g. dreads under a beanie or hood: yes; an afro under a cap: no). The presentation
layer never receives an impossible look.

## Content
- **7 original brands** (Third Coast Supply, Magnolia Mob, Pecan Athletic, Bayou Thread Co., Gulf Gold, SLAB City Headwear,
  Scrap Gym Fightwear). A test blocks real fashion and sportswear names.
- **45 clothing items** across streetwear, workwear, sport, gym and formal tags, 2-5 colourways each.
- **6 Southside stores**, including Fade Kings Barbershop ($15 a visit).
- **11 authored looks:** the player's default (from the owner's reference: shoulder dreads, ribbed beanie, layered chains,
  graphic tee, ripstop cargo pants, high-top court shoes, 185 cm and muscular) plus Dre, Rell, Brick, Grandma Ruth,
  Lou, Tone, Big Reece, Nia, Marlo and Deuce, each with a distinct silhouette.

## Wardrobe (`WardrobeManager`, saved in `GameSaveData.Wardrobe`)
- **Character creator:** free changes to face, body, skin, eyes, hair and starter clothes until the player commits (`LockCreator`).
- **Stores:** an item is bought once at the store that sells it (`wardrobe.buy.<item>`; it can never be charged twice), then
  worn in any of its colourways. Optional pieces can be taken off; required ones can't.
- **Barber:** hair, hair colour and facial hair, one paid visit per change (`barber.visit.<n>`).
- Restoring an old save without wardrobe data gives the starter wardrobe and the default look.

## NPC variety (`AppearanceGenerator`)
Deterministic per seed (the same NPC always looks the same), with a realistic height spread and optional style tag
(streetwear, workwear...). It rejects candidates too similar to looks already in the crowd (similarity ≤ 0.72, weighted toward
silhouette, skin, hair and outer clothing). A test checks that a 60-person crowd has no look-alikes.

## Not done yet
Tattoos, scars, grills, voice and walk-style presets, creator UI, store UI, mesh and blend-shape art (see
`CharacterAndAnimationPipeline.md`). `PlayerIdentity.Appearance` (the old string map) is kept for save compatibility and is superseded by the wardrobe look.
