// Port of FFTS.Characters: AppearanceCatalog.Check / Similarity, AppearanceGenerator and WardrobeManager.
import { Rng, clamp } from './rng.js';

export const SLOTS = ['Head', 'Top', 'Outerwear', 'Bottoms', 'Shoes', 'Neck', 'Wrist', 'Eyewear'];
export const REQUIRED_SLOTS = ['Top', 'Bottoms', 'Shoes'];
const HEAD_TYPES = new Set(['Beanie', 'Cap', 'Durag', 'Hood', 'Hat']);
const clone = (x) => JSON.parse(JSON.stringify(x));

export class Catalog {
  constructor(json) {
    Object.assign(this, json);
    this.items = new Map(this.Clothing.map((c) => [c.Id, c]));
    this.has = (list, id) => this[list].some((x) => x.Id === id);
  }
  item(id) { return this.items.get(id) || null; }
  hair(id) { return this.HairStyles.find((h) => h.Id === id) || null; }
  store(id) { return this.Stores.find((s) => s.Id === id) || null; }

  /** Every reason the look is invalid (empty when valid). */
  check(a) {
    const p = [];
    if (!a) return ['no appearance'];
    const b = a.Body || {};
    if (!(b.HeightCm >= 155 && b.HeightCm <= 205)) p.push(`height ${b.HeightCm}cm outside 155-205`);
    if (['Weight', 'Muscle', 'ShoulderWidth', 'HipWidth', 'ArmLength', 'LegLength'].some((k) => b[k] < 0 || b[k] > 1)) p.push('body sliders must be 0..1');
    if (!this.has('SkinTones', a.SkinTone)) p.push(`unknown skin tone '${a.SkinTone}'`);
    if (!this.has('FacePresets', a.FacePreset)) p.push(`unknown face preset '${a.FacePreset}'`);
    if (!this.has('EyeColors', a.EyeColor)) p.push(`unknown eye colour '${a.EyeColor}'`);
    for (const [k, v] of Object.entries(a.Face || {})) {
      if (!this.has('FaceSliders', k)) p.push(`unknown face slider '${k}'`);
      if (v < -1 || v > 1) p.push(`face slider ${k} outside -1..1`);
    }
    const hair = this.hair(a.Hair);
    if (!hair) p.push(`unknown hair '${a.Hair}'`);
    if (!this.has('HairColors', a.HairColor)) p.push(`unknown hair colour '${a.HairColor}'`);
    if (a.FacialHair != null && !this.has('FacialHair', a.FacialHair)) p.push(`unknown facial hair '${a.FacialHair}'`);
    const outfit = a.Outfit || {};
    for (const slot of REQUIRED_SLOTS) if (!outfit[slot]) p.push(`missing ${slot}`);
    for (const [slot, piece] of Object.entries(outfit)) {
      if (!piece) continue;
      const item = this.item(piece.ItemId);
      if (!item) { p.push(`unknown item '${piece.ItemId}'`); continue; }
      if (item.Slot !== slot) p.push(`${item.Id} is a ${item.Slot} item, not ${slot}`);
      if (!item.Colorways.includes(piece.Colorway)) p.push(`${item.Id} has no colourway '${piece.Colorway}'`);
      if (slot === 'Head' && hair && hair.Length !== 'Bald' && !hair.FitsUnder.includes(item.HeadwearType)) p.push(`${hair.DisplayName} can't be worn under a ${item.HeadwearType}`);
    }
    return p;
  }

  /** Content validation (mirrors AppearanceCatalog.Validate). */
  validate() {
    const errors = []; const ids = new Set();
    for (const list of ['Brands', 'SkinTones', 'HairColors', 'EyeColors', 'FabricColors', 'FaceSliders', 'FacePresets', 'HairStyles', 'FacialHair', 'Clothing', 'Stores'])
      for (const x of this[list]) { if (ids.has(x.Id)) errors.push(`duplicate id '${x.Id}'`); ids.add(x.Id); }
    for (const s of [...this.SkinTones, ...this.HairColors, ...this.EyeColors, ...this.FabricColors]) if (!/^#[0-9a-fA-F]{6}$/.test(s.Hex)) errors.push(`Swatch ${s.Id}: bad hex`);
    for (const h of this.HairStyles) for (const t of h.FitsUnder) if (!HEAD_TYPES.has(t)) errors.push(`Hair ${h.Id}: unknown headwear ${t}`);
    const starter = new Set();
    for (const c of this.Clothing) {
      if (!c.DisplayName || !c.MeshKey) errors.push(`Clothing ${c.Id}: needs name and mesh key`);
      if (!this.has('Brands', c.BrandId)) errors.push(`Clothing ${c.Id}: unknown brand`);
      if (c.StoreId && !this.store(c.StoreId)) errors.push(`Clothing ${c.Id}: unknown store`);
      if (!c.Colorways.length || c.Colorways.some((cw) => !this.has('FabricColors', cw))) errors.push(`Clothing ${c.Id}: bad colourways`);
      if ((c.Slot === 'Head') !== HEAD_TYPES.has(c.HeadwearType || '')) errors.push(`Clothing ${c.Id}: HeadwearType only (and always) on head items`);
      if (!c.StarterOwned && !c.StoreId) errors.push(`Clothing ${c.Id}: not sold anywhere`);
      if (c.StarterOwned) starter.add(c.Slot);
    }
    for (const s of REQUIRED_SLOTS) if (!starter.has(s)) errors.push(`no starter ${s}`);
    for (const [k, look] of Object.entries(this.Looks)) for (const p of this.check(look)) errors.push(`Look '${k}': ${p}`);
    if (!this.Looks['look.player.default']) errors.push('missing look.player.default');
    return errors;
  }
}

export function similarity(a, b) {
  let s = 0, w = 0; const add = (weight, same) => { s += weight * same; w += weight; };
  add(2, 1 - Math.min(1, Math.abs(a.Body.HeightCm - b.Body.HeightCm) / 20));
  add(2, 1 - Math.min(1, Math.abs(a.Body.Weight - b.Body.Weight) * 2));
  add(1, 1 - Math.min(1, Math.abs(a.Body.Muscle - b.Body.Muscle) * 2));
  add(2, a.SkinTone === b.SkinTone ? 1 : 0); add(2, a.Hair === b.Hair ? 1 : 0); add(1, a.HairColor === b.HairColor ? 1 : 0);
  add(1, a.FacialHair === b.FacialHair ? 1 : 0); add(1, a.FacePreset === b.FacePreset ? 1 : 0);
  for (const slot of SLOTS) {
    const pa = a.Outfit[slot], pb = b.Outfit[slot];
    const weight = slot === 'Top' || slot === 'Outerwear' || slot === 'Head' ? 1.5 : 0.75;
    if (!pa && !pb) { add(weight, 1); continue; }
    if (!pa || !pb) { add(weight, 0); continue; }
    add(weight, pa.ItemId === pb.ItemId ? (pa.Colorway === pb.Colorway ? 1 : 0.5) : 0);
  }
  return w <= 0 ? 0 : s / w;
}

export const MAX_SIMILARITY = 0.72;
const M = (1n << 64n) - 1n;
const pick = (list, rng) => list[rng.range(0, list.length)];

export function generate(cat, seed, styleTag = null, avoid = null) {
  const rng = new Rng(0); rng.state = (BigInt(seed) * 0x9E3779B97F4A7C15n + 0x632BE59BD9B4E019n) & M;
  let best = null, bestScore = Infinity;
  for (let attempt = 0; attempt < 12; attempt++) {
    const a = roll(cat, rng, styleTag);
    let worst = 0; if (avoid) for (const o of avoid) worst = Math.max(worst, similarity(a, o));
    if (worst <= MAX_SIMILARITY) return a;
    if (worst < bestScore) { bestScore = worst; best = a; }
  }
  return best;
}

function roll(cat, rng, tag) {
  const h = 178 + (rng.value() + rng.value() + rng.value() - 1.5) * 18;
  const a = {
    Body: { HeightCm: clamp(h, 156, 204), Weight: rng.value(), Muscle: rng.value(), ShoulderWidth: 0.3 + rng.value() * 0.4, HipWidth: 0.3 + rng.value() * 0.4,
      ArmLength: 0.35 + rng.value() * 0.3, LegLength: 0.35 + rng.value() * 0.3 },
    SkinTone: pick(cat.SkinTones, rng).Id, FacePreset: pick(cat.FacePresets, rng).Id, EyeColor: pick(cat.EyeColors, rng).Id, HairColor: pick(cat.HairColors, rng).Id,
    Face: {}, Outfit: {}
  };
  for (const s of cat.FaceSliders) a.Face[s.Id] = Math.round((rng.value() * 2 - 1) * 0.6 * 100) / 100;
  a.Hair = pick(cat.HairStyles, rng).Id;
  a.FacialHair = rng.chance(0.55) && cat.FacialHair.length ? pick(cat.FacialHair, rng).Id : null;
  for (const slot of SLOTS) {
    let options = cat.Clothing.filter((c) => c.Slot === slot && (!tag || c.Tags.includes(tag) || c.Tags.includes('basic')));
    if (!options.length) options = cat.Clothing.filter((c) => c.Slot === slot);
    const required = REQUIRED_SLOTS.includes(slot);
    if (!options.length || (!required && !rng.chance(slot === 'Head' ? 0.4 : 0.3))) continue;
    const item = pick(options, rng);
    const hair = cat.hair(a.Hair);
    if (slot === 'Head' && hair && hair.Length !== 'Bald' && !hair.FitsUnder.includes(item.HeadwearType)) continue;
    a.Outfit[slot] = { ItemId: item.Id, Colorway: pick(item.Colorways, rng) };
  }
  return a;
}

/** Port of WardrobeManager. */
export class Wardrobe {
  constructor(cat, bank) { this.cat = cat; this.bank = bank; this.reset(); }
  reset() {
    this.owned = new Set(this.cat.Clothing.filter((c) => c.StarterOwned).map((c) => c.Id));
    this.look = clone(this.cat.Looks['look.player.default']); this.creatorLocked = false; this.barberVisits = 0;
  }
  buy(itemId, storeId) {
    const item = this.cat.item(itemId);
    if (!item) return 'UnknownItem';
    if (this.owned.has(itemId)) return 'AlreadyOwned';
    if (item.StoreId !== storeId) return 'NotSoldHere';
    if (item.PriceCents > 0) {
      const st = this.bank ? this.bank.pay('wardrobe.buy.' + itemId, item.PriceCents, 'Bought ' + item.DisplayName) : 'InsufficientFunds';
      if (st !== 'Posted' && st !== 'Duplicate') return 'InsufficientFunds';
    }
    this.owned.add(itemId); return 'Ok';
  }
  wear(itemId, colorway) {
    const item = this.cat.item(itemId);
    if (!item) return 'UnknownItem';
    if (!this.owned.has(itemId)) return 'NotOwned';
    const next = clone(this.look); next.Outfit[item.Slot] = { ItemId: itemId, Colorway: colorway || item.Colorways[0] };
    return this.commit(next);
  }
  remove(slot) {
    if (REQUIRED_SLOTS.includes(slot)) return 'InvalidLook';
    const next = clone(this.look); delete next.Outfit[slot]; return this.commit(next);
  }
  creatorApply(look) {
    if (this.creatorLocked) return 'CreatorLocked';
    for (const p of Object.values(look.Outfit || {})) if (p && !this.owned.has(p.ItemId)) return 'NotOwned';
    return this.commit(clone(look));
  }
  barber(storeId, hair, hairColor, facialHair) {
    const store = this.cat.store(storeId);
    if (!store || !store.IsBarber) return 'NotABarber';
    const next = clone(this.look); next.Hair = hair; next.HairColor = hairColor; next.FacialHair = facialHair;
    if (this.cat.check(next).length) return 'InvalidLook';
    if (store.BarberPriceCents > 0) {
      const st = this.bank ? this.bank.pay('barber.visit.' + (this.barberVisits + 1), store.BarberPriceCents, store.DisplayName) : 'InsufficientFunds';
      if (st !== 'Posted' && st !== 'Duplicate') return 'InsufficientFunds';
    }
    this.barberVisits++; return this.commit(next);
  }
  commit(next) { if (this.cat.check(next).length) return 'InvalidLook'; this.look = next; return 'Ok'; }
  capture() { return { Owned: [...this.owned].sort(), Look: clone(this.look), CreatorLocked: this.creatorLocked, BarberVisits: this.barberVisits }; }
  restore(s) {
    this.reset(); if (!s) return;
    for (const id of s.Owned || []) if (this.cat.item(id)) this.owned.add(id);
    this.creatorLocked = !!s.CreatorLocked; this.barberVisits = Math.max(0, s.BarberVisits || 0);
    if (s.Look && !this.cat.check(s.Look).length && Object.values(s.Look.Outfit).every((p) => !p || this.owned.has(p.ItemId))) this.look = clone(s.Look);
  }
}
