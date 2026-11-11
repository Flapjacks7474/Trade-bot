// Character appearance and customisation. Mirrors the C# AppearanceTests.
import test from 'node:test';
import assert from 'node:assert/strict';
import { gameData } from '../build-data.mjs';
import { Catalog, Wardrobe, generate, similarity, MAX_SIMILARITY } from '../src/sim/appearance.js';
import { Bank } from '../src/sim/story.js';

const cat = new Catalog(gameData().appearance);
const clone = (x) => JSON.parse(JSON.stringify(x));

test('appearance catalog and every authored look validate', () => {
  assert.deepEqual(cat.validate(), []);
});

test('brands are original: no real-world sportswear or fashion names', () => {
  const banned = /\b(nike|jordan|adidas|puma|reebok|supreme|xo|gucci|louis|vuitton|balenciaga|off-white|bape|yeezy|new balance|converse|vans|timberland|champion)\b/i;
  for (const b of cat.Brands) assert.ok(!banned.test(b.DisplayName), b.DisplayName);
  for (const c of cat.Clothing) assert.ok(!banned.test(c.DisplayName + ' ' + (c.Description || '')), c.DisplayName);
});

test('player default look follows the reference: dreads under a beanie, chains, cargo pants, high-tops', () => {
  const l = cat.Looks['look.player.default'];
  assert.equal(l.Hair, 'hair.shoulder_dreads');
  assert.equal(l.Outfit.Head.ItemId, 'cloth.knit_beanie');
  assert.equal(l.Outfit.Neck.ItemId, 'cloth.layered_chains');
  assert.equal(l.Outfit.Bottoms.ItemId, 'cloth.cargo_pants');
  assert.equal(l.Outfit.Shoes.ItemId, 'cloth.court_hightop');
  assert.ok(l.Body.HeightCm >= 180 && l.Body.Muscle >= 0.7);
});

test('named characters are all clearly distinct from each other', () => {
  const looks = Object.entries(cat.Looks);
  for (let i = 0; i < looks.length; i++) for (let j = i + 1; j < looks.length; j++) {
    const s = similarity(looks[i][1], looks[j][1]);
    assert.ok(s < 0.6, `${looks[i][0]} vs ${looks[j][0]} similarity ${s.toFixed(2)}`);
  }
  const heights = new Set(looks.map(([, l]) => l.Body.HeightCm));
  assert.ok(heights.size >= 9, 'named characters need a spread of heights');
});

test('invalid looks are rejected with reasons', () => {
  const l = clone(cat.Looks['look.player.default']);
  l.Body.HeightCm = 120; l.Hair = 'hair.afro';           // afro can't go under a beanie; height out of range
  delete l.Outfit.Shoes;
  l.Outfit.Top = { ItemId: 'cloth.cargo_pants', Colorway: 'fab.olive' };
  const p = cat.check(l).join(' | ');
  for (const frag of ['height', "can't be worn under a Beanie", 'missing Shoes', 'is a Bottoms item']) assert.ok(p.includes(frag), p);
});

test('NPC generator: deterministic per seed, always valid, and a 60-person crowd has no look-alikes', () => {
  assert.deepEqual(generate(cat, 42), generate(cat, 42));
  assert.notDeepEqual(generate(cat, 42), generate(cat, 43));
  const crowd = [];
  for (let i = 1; i <= 60; i++) {
    const a = generate(cat, 1000 + i, i % 3 === 0 ? 'streetwear' : i % 3 === 1 ? 'workwear' : null, crowd);
    assert.deepEqual(cat.check(a), [], `npc ${i}`);
    crowd.push(a);
  }
  let worst = 0;
  for (let i = 0; i < crowd.length; i++) for (let j = i + 1; j < crowd.length; j++) worst = Math.max(worst, similarity(crowd[i], crowd[j]));
  assert.ok(worst <= MAX_SIMILARITY, `closest pair ${worst.toFixed(2)}`);
});

test('wardrobe: buy once (never charged twice), wear colourways, barber visits are separate charges, save round-trip', () => {
  const bank = new Bank(100000);
  const w = new Wardrobe(cat, bank);
  assert.equal(w.wear('cloth.puffer', 'fab.orange'), 'NotOwned');
  assert.equal(w.buy('cloth.puffer', 'store.pecan_kicks'), 'NotSoldHere');
  assert.equal(w.buy('cloth.puffer', 'store.magnolia_flagship'), 'Ok');
  const after = bank.total;
  assert.equal(w.buy('cloth.puffer', 'store.magnolia_flagship'), 'AlreadyOwned');
  assert.equal(bank.total, after);
  assert.equal(w.wear('cloth.puffer', 'fab.orange'), 'Ok');
  assert.equal(w.look.Outfit.Outerwear.Colorway, 'fab.orange');
  assert.equal(w.remove('Shoes'), 'InvalidLook');
  assert.equal(w.remove('Outerwear'), 'Ok');
  // barber: afro + beanie is invalid, so take the beanie off first
  assert.equal(w.barber('store.fade_kings', 'hair.afro', 'haircol.black', null), 'InvalidLook');
  assert.equal(w.remove('Head'), 'Ok');
  const before = bank.total;
  assert.equal(w.barber('store.fade_kings', 'hair.afro', 'haircol.black', null), 'Ok');
  assert.equal(w.barber('store.fade_kings', 'hair.waves', 'haircol.black', 'facial.stubble'), 'Ok');
  assert.equal(before - bank.total, 3000);
  assert.equal(w.barber('store.southside_thrift', 'hair.buzz', 'haircol.black', null), 'NotABarber');
  const copy = new Wardrobe(cat, bank); copy.restore(clone(w.capture()));
  assert.deepEqual(copy.capture(), w.capture());
});

test('character creator works until locked; it only accepts owned clothes', () => {
  const w = new Wardrobe(cat, new Bank(0));
  const look = clone(cat.Looks['look.player.default']);
  look.Body.HeightCm = 172; look.SkinTone = 'skin.08'; look.Face['face.nose_width'] = 0.4;
  assert.equal(w.creatorApply(look), 'Ok');
  look.Outfit.Outerwear = { ItemId: 'cloth.varsity', Colorway: 'fab.navy' };
  assert.equal(w.creatorApply(look), 'NotOwned');
  w.creatorLocked = true;
  assert.equal(w.creatorApply(clone(cat.Looks['look.player.default'])), 'CreatorLocked');
});
