// Mirrors key C# combat tests so the browser build plays by the same rules.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { CombatSim, Fighter, Moveset, defaultConfig, FighterAI, C, S } from '../src/sim/combat.js';
import { Rng } from '../src/sim/rng.js';

const root = new URL('../../Assets/_Project/Data/', import.meta.url);
const styles = JSON.parse(readFileSync(new URL('Moves/street_fighting.json', root))).Styles;
const fighters = JSON.parse(readFileSync(new URL('Characters/fighters.json', root))).Fighters;
const F = (id) => fighters.find((f) => f.Id === id);
const fight = (a = 'fighter.player', b = 'fighter.rell', d = 1, cfg) => {
  const ms = new Moveset(styles);
  return new CombatSim(cfg || defaultConfig(), new Fighter(F(a), ms, -d / 2), new Fighter(F(b), new Moveset(styles), d / 2), 't');
};
const idle = { move: 0, block: false, command: C.None };
const press = (c) => ({ move: 0, block: false, command: c });
const run = (sim, n, fa, fb) => { const ev = []; for (let i = 0; i < n && !sim.isOver; i++) { sim.step(fa ? fa(i) : idle, fb ? fb(i) : idle); ev.push(...sim.events); } return ev; };

test('L-L-H combo lands three scaled hits', () => {
  const sim = fight();
  const ev = run(sim, 80, (i) => (i === 0 || i === 8 ? press(C.Light) : i === 18 ? press(C.Heavy) : idle));
  assert.deepEqual(ev.filter((e) => e.type === 'Hit').map((e) => e.moveId), ['street.jab', 'street.cross', 'street.haymaker']);
});

test('timed block parries into a counter', () => {
  const sim = fight();
  const ev = run(sim, 40, (i) => (i === 0 ? press(C.Light) : idle), (i) => (i >= 3 ? { move: 0, block: true, command: C.None } : idle));
  const c = ev.find((e) => e.type === 'Countered');
  assert.equal(c.actor, 'fighter.rell'); assert.equal(c.detail, 'parry');
});

test('grapple then heavy throws and knocks down', () => {
  const sim = fight();
  const ev = run(sim, 120, (i) => (i === 0 ? press(C.Grapple) : i === 25 ? press(C.Heavy) : idle), () => ({ move: 0, block: true, command: C.None }));
  assert.ok(ev.some((e) => e.type === 'Thrown'));
  assert.ok(ev.some((e) => e.type === 'KnockedDown' && e.actor === 'fighter.rell'));
});

test('finisher defeats weakened opponent', () => {
  const sim = fight();
  sim.A.applyScenario({ momentum: 100 }); sim.B.applyScenario({ health: sim.B.maxHealth * 0.3 });
  run(sim, 120, (i) => (i === 0 ? press(C.Finisher) : idle));
  assert.equal(sim.result.method, 'Finisher'); assert.equal(sim.result.winnerId, 'fighter.player');
});

test('AI beats a passive opponent and AI fights resolve', () => {
  for (const id of ['fighter.rell', 'fighter.brick']) {
    const sim = fight(id, 'fighter.player', 2); const f = F(id);
    const ai = new FighterAI(sim.A, f.AiStyle, f.AiDifficulty, new Rng(7));
    for (let i = 0; i < 60 * 120 && !sim.isOver; i++) sim.step(ai.think(sim), idle);
    assert.equal(sim.result.winnerId, id);
  }
});
