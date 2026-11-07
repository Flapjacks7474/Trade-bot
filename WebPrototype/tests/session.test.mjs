// Plays Chapter 1 through the browser session API, mirroring the C# CampaignTests.
import test from 'node:test';
import assert from 'node:assert/strict';
import { gameData } from '../build-data.mjs';
import { Game } from '../src/sim/session.js';
import { Car, statsFor } from '../src/sim/vehicles.js';

const talk = (g, id, ...choices) => { const r = g.startDialogue(id); let fight = null; for (const c of choices) { const o = r.choose(c); assert.ok(o, `${id}: option ${c} not available`); fight = o.fight || fight; } return fight; };
const win = (o) => ({ winnerId: 'fighter.player', loserId: o, method: 'Knockout' });

test('full Chapter 1 reaches Chapter 2 with correct money', () => {
  const g = new Game(gameData());
  assert.equal(g.missions.status('mission.ch1.home_again'), 'Active');
  g.trigger('location.southside.grandma_house'); talk(g, 'dialogue.grandma.welcome', 'respect', 'photo', 'ok');
  talk(g, 'dialogue.lou.barbershop', 'ask', 'more', 'sharp'); g.trigger('location.southside.corner_store'); g.trigger('location.southside.tone_studio');
  talk(g, 'dialogue.dre.reunion', 'challenge', 'go'); g.trigger('location.southside.community_gym');
  assert.equal(g.talkTo('npc.dre').FightId, 'fighter.dre');
  g.onFight(win('fighter.dre'), 'fighter.dre', 'Sanctioned', 'location.southside.community_gym', []);
  g.trigger('location.southside.tone_studio');
  assert.equal(talk(g, 'dialogue.rell.confrontation', 'fight'), 'fighter.rell');
  g.onFight(win('fighter.rell'), 'fighter.rell', 'Street', 'location.southside.tone_studio', ['npc.tone']);
  talk(g, 'dialogue.tone.studio', 'record', 'thanks');
  g.trigger('location.southside.underground_venue'); talk(g, 'dialogue.reece.venue', 'yes');
  g.onFight({ ...win('fighter.brick'), method: 'Finisher' }, 'fighter.brick', 'Underground', 'location.southside.underground_venue', []);
  talk(g, 'dialogue.nia.invitation', 'yes'); talk(g, 'dialogue.grandma.invitation', 'tell', 'promise');
  assert.equal(g.missions.status('mission.ch1.the_invitation'), 'Completed');
  assert.equal(g.story.chapter, 2);
  assert.equal(g.bank.total, 4000 + 20000 + 10000 + 50000);
  assert.equal(g.story.has('story.chairman.identity_revealed'), false);
});

test('buy a car, race, rewards once, save round trip', () => {
  const g = new Game(gameData());
  g.bank.post('test', 'cash', 200000);
  assert.equal(g.buyCar('vehicle.magnolia_cruiser', 'garage.southside_motors', 'k1'), 'Ok');
  assert.equal(g.buyCar('vehicle.magnolia_cruiser', 'garage.southside_motors', 'k1'), 'Already bought');
  const c = g.retrieve(g.garage.cars[0].id); const car = g.makeCar(c);
  const { race } = g.beginRace('race.southside.pecan_sprint', car);
  for (let i = 0; i < 200; i++) race.step({}, 1 / 60);
  for (const cp of race.def.Checkpoints) { race.player.car.x = cp.X; race.player.car.z = cp.Z; race.player.car.speed = 0; race.step({}, 1 / 60); }
  const out = g.resolveRace(race);
  assert.equal(out.place, 1); assert.equal(out.purse, 30000); assert.equal(out.bonus, 25000);
  const money = g.bank.total;
  assert.ok(g.resolveRace(race).already); assert.equal(g.bank.total, money);
  const g2 = new Game(gameData()); g2.restore(JSON.parse(JSON.stringify(g.capture({ x: 1, z: 2 }))));
  assert.equal(g2.bank.total, money); assert.equal(g2.racing.points, 60); assert.equal(g2.garage.cars.length, 1);
});

test('AI racers finish the sprint on their own', () => {
  const d = gameData(); const g = new Game(d); g.bank.post('t', 'cash', 100000);
  const car = new Car(statsFor(d.vehicles.Vehicles[0]));
  const { race } = g.beginRace('race.southside.pecan_sprint', car);
  for (let i = 0; i < 60 * 140; i++) race.step({}, 1 / 60);
  for (const r of race.racers.filter((r) => !r.isPlayer)) assert.ok(r.done, r.name + ' did not finish');
});

test('NPCs follow schedules and travel along roads', () => {
  const g = new Game(gameData());
  const lou = g.world.npcs.get('npc.lou');
  assert.equal(lou.loc, 'location.southside.apartments');
  g.minutes += 60; g.tick(0.1, 0, 0, false);
  assert.equal(lou.status, 'Traveling');
  for (let i = 0; i < 400 && lou.status === 'Traveling'; i++) g.tick(1, lou.x, lou.z, false);
  assert.equal(lou.loc, 'location.southside.barbershop');
});
