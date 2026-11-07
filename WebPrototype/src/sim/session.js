// Port of FFTS.Session.GameSession for the browser build: owns all state, wires cross-system rules, saves.
import { Rng } from './rng.js';
import { Bank, Reputation, Social, Story, Missions, DialogueRunner, fightReputation } from './story.js';
import { Car, Race, statsFor, rankFor } from './vehicles.js';
import { LivingWorld } from './world.js';

const SEGMENTS = (h) => (h >= 6 && h < 12 ? 'Morning' : h >= 12 && h < 17 ? 'Afternoon' : h >= 17 && h < 21 ? 'Evening' : h >= 21 || h < 2 ? 'Night' : 'LateNight');
export const SAVE_VERSION = 1;

export class Game {
  constructor(data, seed = 20261103) {
    this.data = data; this.rng = new Rng(seed);
    this.minutes = 8 * 60; this.clockAcc = 0;
    this.bank = new Bank(4000); this.rep = new Reputation(); this.social = new Social(); this.story = new Story();
    this.player = { contacts: [], inventory: {} };
    this.toasts = [];
    this.missions = new Missions(data.missions, this.story, (k, r) => this.grant(k, r));
    this.dialogues = new Map(data.dialogues.map((d) => [d.Id, d]));
    this.garage = { cars: [], active: null, next: 1, preset: 'Street', slots: {} };
    this.racing = { points: 0, attempts: {}, wins: {}, best: {}, resolved: [] };
    this.fightCount = 0;
    for (const p of data.npcs) this.social.personality[p.NpcId] = p.Personality;
    this.world = new LivingWorld(data.npcs, data.layout, (e) => this.pinMet(e));
    this.world.init(this.day, this.hour); this.lastHour = this.hour; this.dirty = false;
    this.missions.pump();
  }
  get day() { return Math.floor(this.minutes / 1440); }
  get hour() { return Math.floor(this.minutes % 1440 / 60); }
  get minute() { return this.minutes % 60; }
  get segment() { return SEGMENTS(this.hour); }
  get clockText() { return `${['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'][this.day % 7]} ${String(this.hour).padStart(2, '0')}:${String(this.minute).padStart(2, '0')}`; }
  toast(t) { this.toasts.push(t); }

  pinMet(e) {
    if (e.MissionActive && this.missions.status(e.MissionActive) !== 'Active') return false;
    for (const o of e.ObjectivesDone || []) if (!this.missions.objectiveDone(o)) return false;
    for (const o of e.ObjectivesNotDone || []) if (this.missions.objectiveDone(o)) return false;
    for (const f of e.RequireFlags || []) if (!this.story.has(f)) return false;
    return true;
  }

  tick(sec, px, pz, clockRunning) {
    if (clockRunning) { this.clockAcc += sec; const m = Math.floor(this.clockAcc); if (m > 0) { this.clockAcc -= m; this.minutes += m; } }
    if (this.hour !== this.lastHour || this.dirty) { this.lastHour = this.hour; this.dirty = false; this.world.plan(this.day, this.hour); }
    this.world.step(sec, px, pz);
  }
  sleepUntilMorning() { let m = (24 * 60 - this.minutes % 1440 + 8 * 60) % 1440; if (m === 0) m = 1440; this.minutes += m; }

  trigger(t) {
    if (!t) return;
    const before = this.missions.log.length;
    this.missions.report(t);
    for (const e of this.missions.log.slice(before)) {
      if (e.type === 'completed') this.toast('Mission complete: ' + e.title);
      else if (e.type === 'objective') this.toast('✓ ' + e.objective);
      else if (e.type === 'failed') this.toast('Mission failed: ' + e.title + ' — try again');
    }
    this.dirty = true;
  }

  grant(key, r) {
    switch (r.Type) {
      case 'Money':
        if (r.AmountCents > 0) { if (this.bank.post(key, 'cash', r.AmountCents, r.Memo) === 'Posted') this.toast('+' + Bank.fmt(r.AmountCents) + (r.Memo ? ' · ' + r.Memo : '')); }
        else if (this.bank.pay(key, -r.AmountCents, r.Memo) === 'Posted') this.toast('−' + Bank.fmt(-r.AmountCents) + (r.Memo ? ' · ' + r.Memo : ''));
        break;
      case 'Reputation': this.rep.apply({ face: r.FaceCard || {}, image: r.PublicImage || {} }); break;
      case 'Relationship': this.social.modify(r.Target, r.Relationship || {}); break;
      case 'Flag': this.story.set(r.Target, r.Value ?? 1); this.missions.refresh(); break;
      case 'Contact': if (!this.player.contacts.includes(r.Target)) { this.player.contacts.push(r.Target); this.toast('New contact: ' + this.npcName(r.Target)); } break;
      case 'Item': this.player.inventory[r.Target] = (this.player.inventory[r.Target] || 0) + (r.Value || 1); break;
    }
  }
  applyEffect(key, e) { if (this.missions.granted.has(key)) return; this.missions.granted.add(key); this.grant(key, e); }
  npcName(id) { return this.data.npcs.find((n) => n.NpcId === id)?.DisplayName || id.replace('npc.', ''); }

  startDialogue(id) { const d = this.dialogues.get(id); return d ? new DialogueRunner(d, this) : null; }

  /** What happens when the player talks to an NPC right now: first matching data rule wins. */
  talkTo(npcId) {
    for (const r of this.data.rules) {
      if (r.NpcId !== npcId) continue;
      if (r.MissionActive && this.missions.status(r.MissionActive) !== 'Active') continue;
      if ((r.ObjectivesDone || []).some((o) => !this.missions.objectiveDone(o))) continue;
      if ((r.ObjectivesNotDone || []).some((o) => this.missions.objectiveDone(o))) continue;
      if (r.Branch) { const [m, b] = r.Branch.split(':'); if (this.missions.prog.get(m)?.branch !== b) continue; }
      if ((r.RequireFlags || []).some((f) => !this.story.has(f))) continue;
      if ((r.ForbidFlags || []).some((f) => this.story.has(f))) continue;
      if ((r.Segments || []).length && !r.Segments.includes(this.segment)) continue;
      if (r.Activity) { const n = this.world.npcs.get(npcId); if (!n || n.activity !== r.Activity || n.status === 'Traveling') continue; }
      return r;
    }
    return null;
  }

  onFight(result, opponentId, venue, locationId, witnesses) {
    const won = result.winnerId != null && result.winnerId !== opponentId;
    const key = 'fight.' + (++this.fightCount);
    this.rep.apply(fightReputation(venue, won, result.method));
    const npc = 'npc.' + opponentId.replace('fighter.', '');
    this.social.modify(npc, won ? { Respect: 8, Resentment: result.method === 'Finisher' ? 15 : 8, Rivalry: 10, Fear: 5 } : { Respect: -3, Rivalry: 4 });
    this.social.record({ id: 'event.' + key, type: won ? 'player.won_fight' : 'player.lost_fight', actor: won ? 'player' : npc, target: won ? npc : 'player', location: locationId, witnesses: [npc, ...witnesses] });
    this.trigger((won ? 'fight.won.' : 'fight.lost.') + opponentId);
    return won;
  }

  // ------------------------------------------------------------ garage
  vdef(id) { return this.data.vehicles.Vehicles.find((v) => v.Id === id); }
  gdef(id) { return this.data.vehicles.Garages.find((g) => g.Id === id); }
  ownsGarage(id) { return !!this.gdef(id)?.StarterOwned; }
  capacity(id) { const g = this.gdef(id); return this.ownsGarage(id) ? Math.min(g.MaxCapacity, g.BaseCapacity + (this.garage.slots[id] || 0)) : 0; }
  assigned(id) { return this.garage.cars.filter((c) => c.garage === id).length; }
  buyCar(defId, garageId, key) {
    const d = this.vdef(defId), g = this.gdef(garageId);
    if (!d || !g || !g.ForSale.includes(defId)) return 'Not sold here';
    if (this.bank.wasPosted('vehicle.buy.' + key + '.cash') || this.bank.wasPosted('vehicle.buy.' + key + '.checking')) return 'Already bought';
    const home = this.data.vehicles.Garages.find((x) => this.ownsGarage(x.Id) && this.assigned(x.Id) < this.capacity(x.Id));
    if (!home) return 'No garage space';
    if (this.bank.pay('vehicle.buy.' + key, d.BaseValueCents, 'Bought ' + d.DisplayName) !== 'Posted') return 'Not enough money';
    const car = { id: 'veh.' + this.garage.next++, def: defId, damage: 0, km: 0, upgrades: [], garage: home.Id, state: 'Stored', x: 0, z: 0, heading: 0, color: '#9b2226' };
    this.garage.cars.push(car); this.toast(`Bought the ${d.DisplayName}. It's parked at ${home.DisplayName}.`);
    return 'Ok';
  }
  retrieve(carId) {
    const c = this.garage.cars.find((x) => x.id === carId); if (!c || c.state !== 'Stored') return null;
    const g = this.gdef(c.garage); Object.assign(c, { state: 'Out', x: g.SpawnX, z: g.SpawnZ, heading: g.SpawnHeadingDeg });
    this.garage.active = carId; return c;
  }
  makeCar(c) { const car = new Car(statsFor(this.vdef(c.def), c.upgrades, this.data.vehicles.Upgrades), this.garage.preset, c.damage); Object.assign(car, { x: c.x, z: c.z, heading: c.heading }); return car; }
  recordDrive(c, car, meters) { Object.assign(c, { x: car.x, z: car.z, heading: car.heading, damage: car.damage }); c.km += meters / 1000; if (car.wrecked) c.state = 'Wrecked'; }
  store(c, garageId) { if (!this.ownsGarage(garageId)) return false; c.state = 'Stored'; c.garage = garageId; if (this.garage.active === c.id) this.garage.active = null; return true; }
  repairCost(c) { return c.damage <= 0.01 ? 0 : 2500 + Math.round(this.vdef(c.def).BaseValueCents * 0.004 * c.damage); }
  repair(c) {
    const cost = this.repairCost(c); if (!cost) return 'Nothing to fix';
    if (this.bank.pay(`vehicle.repair.${c.id}.${c.repairs || 0}`, cost, 'Repair') !== 'Posted') return 'Not enough money';
    c.damage = 0; c.repairs = (c.repairs || 0) + 1; if (c.state === 'Wrecked') c.state = 'Stored'; return 'Ok';
  }
  tow(c) {
    if (c.state !== 'Out' && c.state !== 'Wrecked') return 'It is not out';
    if (this.bank.pay(`vehicle.tow.${c.id}.${Math.floor(this.minutes)}`, 15000, 'Tow') !== 'Posted') return 'Not enough money';
    if (c.state === 'Out') c.state = 'Stored'; if (this.garage.active === c.id) this.garage.active = null; return 'Ok';
  }
  upgrade(c, upId) {
    const u = this.data.vehicles.Upgrades.find((x) => x.Id === upId), d = this.vdef(c.def);
    if (!u || !d.UpgradeSlots.includes(u.Slot) || c.upgrades.includes(upId)) return 'Not available';
    if (this.bank.pay(`vehicle.upgrade.${c.id}.${upId}`, u.PriceCents, u.DisplayName) !== 'Posted') return 'Not enough money';
    c.upgrades = c.upgrades.filter((x) => this.data.vehicles.Upgrades.find((y) => y.Id === x)?.Slot !== u.Slot); c.upgrades.push(upId); return 'Ok';
  }

  // ------------------------------------------------------------ racing
  get racingRank() { return rankFor(this.racing.points); }
  beginRace(raceId, car) {
    const def = this.data.races.find((r) => r.Id === raceId);
    if (car.wrecked) return { error: 'Your car is wrecked' };
    const attempt = (this.racing.attempts[raceId] || 0) + 1;
    if (def.EntryFeeCents > 0 && this.bank.pay(`race.${raceId}.attempt.${attempt}.entry`, def.EntryFeeCents, def.DisplayName + ' entry') !== 'Posted') return { error: 'Entry costs ' + Bank.fmt(def.EntryFeeCents) };
    this.racing.attempts[raceId] = attempt;
    return { race: new Race(def, car, this.data.vehicles.Vehicles, BigInt(this.minutes) * 7919n + BigInt(attempt), attempt) };
  }
  resolveRace(race) {
    const def = race.def, key = `race.${def.Id}.attempt.${race.attempt}`, p = race.player;
    const out = { place: p.place, dnf: race.dnf, purse: 0, bonus: 0, points: 0, rankBefore: this.racingRank };
    if (this.racing.resolved.includes(key)) return { ...out, already: true };
    this.racing.resolved.push(key);
    if (!race.dnf) {
      const i = p.place - 1;
      if (def.PurseCents[i] > 0 && this.bank.post(key + '.purse', 'cash', def.PurseCents[i], def.DisplayName + ' purse') === 'Posted') out.purse = def.PurseCents[i];
      if (p.place === 1) {
        this.racing.wins[def.Id] = (this.racing.wins[def.Id] || 0) + 1;
        if (def.FirstWinBonusCents > 0 && this.bank.post(`race.${def.Id}.first_win`, 'cash', def.FirstWinBonusCents, 'First win bonus') === 'Posted') out.bonus = def.FirstWinBonusCents;
      }
      out.points = def.RankPoints[i] || 0; this.racing.points += out.points;
      if (!this.racing.best[def.Id] || p.time < this.racing.best[def.Id]) this.racing.best[def.Id] = p.time;
      const clean = p.impacts <= 1;
      if (p.place === 1) this.rep.apply({ face: { Respect: 3, ...(clean ? {} : { Notoriety: 2 }) }, image: { Popularity: clean ? 3 : 1, ...(clean ? {} : { Credibility: -1 }) } });
      else if (p.place <= 3) this.rep.apply({ face: { Respect: 1 } });
      this.trigger('race.finished.' + def.Id); if (p.place === 1) this.trigger('race.won.' + def.Id);
    } else this.rep.apply({ face: { Respect: -1 } });
    for (const r of race.racers) if (!r.isPlayer && r.npc) this.social.modify(r.npc, !race.dnf && p.place < r.place ? { Respect: 4, Rivalry: 5 } : { Respect: -1, Rivalry: 2 });
    out.rankAfter = this.racingRank;
    return out;
  }

  // ------------------------------------------------------------ saves
  capture(pos) {
    return { v: SAVE_VERSION, minutes: this.minutes, bank: this.bank.capture(), rep: this.rep.capture(), social: this.social.capture(),
      story: { chapter: this.story.chapter, flags: this.story.flags }, missions: this.missions.capture(), player: this.player,
      garage: this.garage, racing: this.racing, fightCount: this.fightCount, world: this.world.capture(), pos };
  }
  restore(s) {
    if (!s || s.v !== SAVE_VERSION) return false;
    this.minutes = s.minutes; this.bank.restore(s.bank); this.rep.restore(s.rep); this.social.restore(s.social);
    this.story.chapter = s.story.chapter; this.story.flags = s.story.flags; this.missions.restore(s.missions);
    this.player = s.player; this.garage = s.garage; this.racing = s.racing; this.fightCount = s.fightCount;
    this.world.restore(s.world); this.lastHour = -1; return true;
  }
}
