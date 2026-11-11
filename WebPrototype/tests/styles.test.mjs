// Fighting styles, stances and hybrids. Mirrors the C# StyleTests so both builds agree on the rules.
import test from 'node:test';
import assert from 'node:assert/strict';
import { gameData } from '../build-data.mjs';
import { CombatSim, Fighter, Moveset, defaultConfig, FighterAI, C, S, buildMoveset, findHybrid } from '../src/sim/combat.js';
import { validateStyles, longestChain, StyleLoadout } from '../src/sim/styles.js';
import { Bank } from '../src/sim/story.js';
import { Rng } from '../src/sim/rng.js';

const data = gameData();
const styles = data.styles;
const base = styles.filter((s) => !s.HybridOf);
const hybrids = styles.filter((s) => s.HybridOf);
const F = (id) => data.fighters.find((f) => f.Id === id);
const press = (c) => ({ move: 0, block: false, command: c });
const idle = { move: 0, block: false, command: C.None };
const run = (sim, n, fa, fb) => { const ev = []; for (let i = 0; i < n && !sim.isOver; i++) { sim.step(fa ? fa(i) : idle, fb ? fb(i) : idle); ev.push(...sim.events); } return ev; };
const fightWith = (aStyles, bStyles = ['style.street'], gap = 1) => new CombatSim(defaultConfig(),
  new Fighter(F('fighter.player'), buildMoveset(styles, aStyles), -gap / 2), new Fighter(F('fighter.rell'), buildMoveset(styles, bStyles), gap / 2), 't');

test('ten base styles and six hybrids ship and all validate', () => {
  assert.equal(base.length, 10);
  assert.ok(hybrids.length >= 6);
  assert.deepEqual(validateStyles(styles), []);
});

test('every base style is a complete kit with its own finisher, counter and a 3+ hit combo', () => {
  for (const s of base) {
    const ms = new Moveset([s]);
    for (const slot of ['Light', 'Heavy', 'Kick', 'Grapple', 'Counter', 'Finisher', 'ClinchStrike', 'ClinchThrow']) assert.ok(ms[slot], `${s.Id} ${slot}`);
    assert.ok(ms.Finisher.Id.startsWith(s.Moves[0].Id.split('.')[0] + '.'), `${s.Id} finisher is its own`);
    assert.ok(longestChain(ms) >= 3, `${s.Id} longest chain ${longestChain(ms)}`);
  }
});

test('styles are distinct: no two share a stance profile, and every animation key is unique', () => {
  const sig = (st) => JSON.stringify([st.WalkSpeed, st.StrikeDamage, st.KickDamage, st.ThrowDamage, st.ParryWindowBonus, st.CounterWindowBonus, st.BlockChip,
    st.GuardDrain, st.DodgeDistance, st.DodgeCost, st.EscapeRate, st.ClinchStrikesBonus]);
  const seen = new Map();
  for (const s of styles) { const k = sig(s.Stance); assert.ok(!seen.has(k), `${s.Id} has the same stance numbers as ${seen.get(k)}`); seen.set(k, s.Id); }
  const keys = new Set();
  for (const s of styles) for (const m of s.Moves) { assert.ok(!keys.has(m.AnimationKey), m.AnimationKey); keys.add(m.AnimationKey); }
});

test('each base style belongs to at least one hybrid, and hybrids add moves the parents do not have', () => {
  for (const s of base) assert.ok(hybrids.some((h) => h.HybridOf.includes(s.Id)), `${s.Id} has no hybrid`);
  for (const h of hybrids) {
    assert.ok(h.Moves.length >= 3, h.Id);
    assert.ok(h.Moves.some((m) => m.Kind === 'Finisher'), `${h.Id} needs its own finisher`);
    assert.ok(h.Links.some((l) => l.From.split('.')[0] !== l.To.split('.')[0]), `${h.Id} needs a cross-style link`);
  }
});

test('hybrid applies only for its parent pair (either order) and puts its own finisher and stance first', () => {
  assert.equal(findHybrid(styles, 'style.boxing', 'style.street').Id, 'hybrid.southside_hands');
  assert.equal(findHybrid(styles, 'style.street', 'style.boxing').Id, 'hybrid.southside_hands');
  assert.equal(findHybrid(styles, 'style.boxing', 'style.karate'), null);
  const ms = buildMoveset(styles, ['style.street', 'style.boxing']);
  assert.equal(ms.hybridId, 'hybrid.southside_hands');
  assert.equal(ms.Finisher.Id, 'southside_hands.last_call');
  assert.equal(ms.stance.Name, 'Low Shell');
  // hybrid-only cross-style link: boxing jab -> southside double hook
  assert.equal(ms.findLink('boxing.jab', 'Heavy').Id, 'southside_hands.double_hook');
  // locked hybrid falls back to the plain merge
  assert.equal(buildMoveset(styles, ['style.street', 'style.boxing'], []).hybridId, null);
});

test('stance changes the fight: Muay Thai gets 5 clinch knees, Street gets 3', () => {
  const knees = (styleId) => {
    const sim = fightWith([styleId]);
    const ev = run(sim, 200, (i) => (i === 0 ? press(C.Grapple) : i % 21 === 15 ? press(C.Light) : idle), () => ({ move: 0, block: true, command: C.None }));
    return ev.filter((e) => e.type === 'ClinchStrike').length;
  };
  assert.equal(knees('style.street'), 3);
  assert.equal(knees('style.muay_thai'), 5);
});

test('stance damage: Taekwondo kicks harder than Boxing punches with the body-shot button', () => {
  const firstHit = (styleId, cmd) => run(fightWith([styleId], ['style.street'], 1.2), 60, (i) => (i === 0 ? press(cmd) : idle)).find((e) => e.type === 'Hit');
  const tkd = firstHit('style.taekwondo', C.Kick), street = firstHit('style.street', C.Kick);
  assert.ok(tkd && street);
  // 10 base damage x1.3 stance vs street front kick 8 x1.0
  assert.ok(tkd.amount > street.amount * 1.4, `${tkd.amount} vs ${street.amount}`);
});

test('boxing parries earlier than street (stance parry bonus)', () => {
  const parryAt = (styleId, blockFrom) => {
    const sim = new CombatSim(defaultConfig(), new Fighter(F('fighter.rell'), buildMoveset(styles, ['style.street']), -0.5),
      new Fighter(F('fighter.player'), buildMoveset(styles, [styleId]), 0.5), 't');
    const ev = run(sim, 40, (i) => (i === 0 ? press(C.Light) : idle), (i) => (i >= blockFrom ? { move: 0, block: true, command: C.None } : idle));
    return ev.some((e) => e.type === 'Countered');
  };
  // Raising the guard early: street's window has closed, boxing's +3 ticks still parries.
  const early = [...Array(6).keys()].find((k) => parryAt('style.boxing', k) && !parryAt('style.street', k));
  assert.ok(early !== undefined, 'boxing parries in a window street cannot');
});

test('every style has a working combo that lands three hits on a passive opponent', () => {
  for (const s of base) {
    const ms = new Moveset([s]);
    // Find a 3-hit route from the Light entry, or from Heavy/Kick.
    let route = null;
    for (const entry of ['Light', 'Heavy', 'Kick']) {
      const first = ms[entry]; if (!first) continue;
      for (const l1 of ms.links.filter((l) => l.From === first.Id)) {
        const l2 = ms.links.find((l) => l.From === l1.To && ms.get(l.To).Kind !== 'Grapple');
        if (l2 && ms.get(l1.To).Kind !== 'Grapple') { route = [entry, l1.Command, l2.Command]; break; }
      }
      if (route) break;
    }
    assert.ok(route, `${s.Id} has no 3-hit route`);
    const sim = new CombatSim(defaultConfig(), new Fighter(F('fighter.player'), ms, -0.45), new Fighter(F('fighter.rell'), buildMoveset(styles, ['style.street']), 0.45), 't');
    sim.A.applyScenario({ stamina: 500 });
    const hits = []; let step = 0;
    for (let i = 0; i < 200 && !sim.isOver; i++) {
      let cmd = C.None;
      if (step < 3 && (i === 0 || (sim.A.moveConnected && sim.A.inRecovery))) cmd = route[step++];
      sim.step(cmd === C.None ? idle : press(cmd), idle);
      hits.push(...sim.events.filter((e) => e.type === 'Hit' && e.actor === 'fighter.player'));
      sim.B.x = sim.A.x + 0.9; // keep the dummy in range so the test checks combo routing, not footsies
    }
    assert.ok(hits.length >= 3, `${s.Id} route ${route} landed ${hits.length}`);
  }
});

test('learning styles costs money once, hybrids need both parents, loadout survives save/restore', () => {
  const bank = new Bank(100000);
  const lo = new StyleLoadout(styles, bank);
  assert.equal(lo.learn('hybrid.southside_hands'), 'ParentsNotLearned');
  assert.equal(lo.learn('style.boxing'), 'Ok');
  const after = bank.total;
  assert.equal(lo.learn('style.boxing'), 'AlreadyLearned');
  assert.equal(bank.total, after);
  assert.equal(bank.pay('style.learn.style.boxing', 25000, 'x'), 'Duplicate'); // key already used: never charged twice
  assert.equal(lo.equip('style.street', 'style.boxing'), 'Ok');
  assert.equal(lo.activeHybrid, null);
  assert.equal(lo.learn('hybrid.southside_hands'), 'Ok');
  assert.equal(lo.activeHybrid.Id, 'hybrid.southside_hands');
  assert.equal(lo.buildMoveset().Finisher.Id, 'southside_hands.last_call');
  assert.equal(lo.equip('hybrid.southside_hands'), 'InvalidLoadout');
  const poor = new StyleLoadout(styles, new Bank(1000));
  assert.equal(poor.learn('style.mma'), 'InsufficientFunds');
  const copy = new StyleLoadout(styles, bank); copy.restore(JSON.parse(JSON.stringify(lo.capture())));
  assert.deepEqual(copy.capture(), lo.capture());
  copy.restore({ Learned: ['style.gone'], Equipped: ['style.gone'] });
  assert.deepEqual(copy.equipped, ['style.street']);
});

test('AI round robin: every style can win and none dominates (same fighter stats on both sides)', () => {
  const ids = base.map((s) => s.Id);
  const wins = Object.fromEntries(ids.map((id) => [id, 0])); let fights = 0;
  const def = { ...F('fighter.rell'), AiStyle: 'Technical', AiDifficulty: 'Normal' };
  for (const a of ids) for (const b of ids) {
    if (a === b) continue;
    for (let seed = 1; seed <= 3; seed++) {
      const sim = new CombatSim(defaultConfig(), new Fighter({ ...def, Id: 'A' }, buildMoveset(styles, [a]), -1.2), new Fighter({ ...def, Id: 'B' }, buildMoveset(styles, [b]), 1.2), 'rr');
      sim.cfg.roundTicks = 60 * 90;
      const ai1 = new FighterAI(sim.A, 'Technical', 'Normal', new Rng(seed * 7 + 1)), ai2 = new FighterAI(sim.B, 'Aggressive', 'Normal', new Rng(seed * 13 + 5));
      for (let t = 0; t < sim.cfg.roundTicks + 5 && !sim.isOver; t++) sim.step(ai1.think(sim), ai2.think(sim));
      fights++;
      if (sim.result.winnerId === 'A') wins[a]++; else if (sim.result.winnerId === 'B') wins[b]++;
    }
  }
  const per = (ids.length - 1) * 2 * 3;
  const rates = Object.fromEntries(Object.entries(wins).map(([k, v]) => [k, +(v / per).toFixed(2)]));
  console.log('win rates', JSON.stringify(rates));
  for (const [id, r] of Object.entries(rates)) {
    assert.ok(r >= 0.25, `${id} too weak: ${r}`);
    assert.ok(r <= 0.75, `${id} dominates: ${r}`);
  }
});
