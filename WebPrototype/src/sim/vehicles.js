// Port of FFTS.Vehicles (VehicleDynamics, GarageManager essentials) and FFTS.Racing (RaceSession, RaceDriverAI, RaceManager).
import { Rng, clamp, clamp01 } from './rng.js';

export const norm = (d) => { d %= 360; if (d > 180) d -= 360; if (d <= -180) d += 360; return d; };

export function statsFor(def, upgrades = [], upgradeDefs = []) {
  const s = { maxSpeed: def.MaxSpeedKph / 3.6, accel: def.Acceleration, braking: def.Braking, steer: def.SteeringResponse, grip: def.Grip,
    traction: def.Traction, durability: def.Durability, nitrous: 0 };
  for (const id of upgrades) {
    const u = upgradeDefs.find((x) => x.Id === id); if (!u) continue;
    s.accel += u.AccelerationBonus || 0; s.maxSpeed += (u.MaxSpeedBonusKph || 0) / 3.6; s.braking += u.BrakingBonus || 0;
    s.grip += u.GripBonus || 0; s.steer += u.SteeringBonus || 0; s.nitrous += u.NitrousCapacity || 0;
  }
  return s;
}

export class Car {
  constructor(stats, preset = 'Street', damage = 0) {
    this.s = stats; this.preset = preset; this.damage = damage; this.x = 0; this.z = 0; this.heading = 0; this.speed = 0; this.drift = 0;
    this.nos = stats.nitrous; this.nosActive = false; this.distance = 0; this.topSpeed = 0;
  }
  get gripMul() { return this.preset === 'Arcade' ? 1.25 : this.preset === 'Expert' ? 0.9 : 1; }
  get steerMul() { return this.preset === 'Arcade' ? 1.2 : 1; }
  get brakeMul() { return this.preset === 'Arcade' ? 1.2 : 1; }
  get recovery() { return this.preset === 'Arcade' ? 8 : this.preset === 'Expert' ? 3.5 : 5; }
  get wrecked() { return this.damage >= 100; }
  get accelFactor() { return this.damage >= 90 ? 0.5 : this.damage >= 60 ? 0.75 : 1; }
  get maxSpeed() { return this.s.maxSpeed * (this.damage >= 90 ? 0.8 : 1) * (this.nosActive ? 1.15 : 1); }
  get kph() { return this.speed * 3.6; }

  step(inp, dt) {
    const surface = inp.surface > 0 ? clamp(inp.surface, 0.2, 1.2) : 1;
    const throttle = this.wrecked ? 0 : clamp01(inp.throttle || 0), brake = clamp01(inp.brake || 0), steer = clamp(inp.steer || 0, -1, 1);
    this.nosActive = !!inp.nitrous && !this.wrecked && this.nos > 0 && throttle > 0.1 && this.speed > 1;
    if (this.nosActive) this.nos = Math.max(0, this.nos - dt);
    const max = this.maxSpeed;
    if (throttle > 0 && this.speed >= -0.1) {
      const r = clamp01(this.speed / max), launch = this.speed < 8 ? clamp(this.s.traction * surface, 0.3, 1.2) : 1;
      this.speed += throttle * (this.s.accel * this.accelFactor * launch * (1 - r * r) + (this.nosActive ? this.s.accel * 0.6 : 0)) * dt;
    }
    if (brake > 0) {
      if (this.speed > 0.05) this.speed = Math.max(0, this.speed - brake * this.s.braking * this.brakeMul * surface * dt);
      else if (this.speed > -7) this.speed = Math.max(-7, this.speed - brake * this.s.accel * 0.5 * dt);
    }
    if (throttle > 0 && this.speed < -0.1) this.speed = Math.min(0, this.speed + this.s.braking * dt);
    const drag = (0.25 + 0.0005 * this.speed * this.speed) * dt;
    this.speed = Math.abs(this.speed) <= drag ? (throttle > 0 || brake > 0 ? this.speed : 0) : this.speed - Math.sign(this.speed) * drag;
    if (this.speed > max) this.speed = Math.max(max, this.speed - 6 * dt);

    const grip = this.s.grip * this.gripMul * surface * (inp.handbrake ? 0.35 : 1);
    const abs = Math.abs(this.speed);
    const sf = clamp01(abs / 6) * (1 - 0.45 * clamp01(abs / this.s.maxSpeed));
    let yaw = steer * this.s.steer * this.steerMul * sf * Math.sign(this.speed === 0 ? 1 : this.speed);
    if (inp.handbrake && abs > 4) { yaw *= 1.35; this.speed -= Math.sign(this.speed) * Math.min(abs, 3.5 * dt); }
    this.heading = norm(this.heading + yaw * dt);
    let target = clamp(yaw * 0.35 / clamp(grip, 0.25, 2) * clamp01(abs / 12), -45, 45);
    this.drift += (target - this.drift) * clamp01(this.recovery * clamp(grip, 0.2, 2) * dt);
    if (Math.abs(this.drift) > 8) this.speed -= Math.sign(this.speed) * Math.min(Math.abs(this.speed), Math.abs(this.drift) * 0.04 * dt);
    const t = (this.heading - this.drift) * Math.PI / 180;
    const dx = Math.sin(t) * this.speed * dt, dz = Math.cos(t) * this.speed * dt;
    this.x += dx; this.z += dz; this.distance += Math.hypot(dx, dz); this.topSpeed = Math.max(this.topSpeed, this.speed);
  }
  impact(closing) {
    closing = Math.abs(closing);
    if (closing < 1.5) { this.speed *= 0.9; return 0; }
    const dmg = closing * 1.4 * (1.25 - this.s.durability / 133);
    this.damage = Math.min(100, this.damage + dmg); this.speed *= closing > 12 ? 0.15 : 0.4; this.drift *= 0.5;
    return dmg;
  }
}

export class RaceDriverAI {
  constructor(skill, rng) { this.skill = clamp01(skill); this.rng = rng; this.mistakeFor = -1; this.mistake = false; }
  think(race, me) {
    const car = me.car, cps = race.def.Checkpoints, i = me.next, c = cps[i];
    const n = i + 1 < cps.length ? cps[i + 1] : race.def.Laps > 1 ? cps[0] : null;
    const dC = Math.hypot(car.x - c.X, car.z - c.Z);
    const blend = n ? clamp01(1 - dC / (18 + 12 * this.skill)) * (0.3 + 0.4 * this.skill) : 0;
    let tx = c.X, tz = c.Z; if (n) { tx += (n.X - c.X) * blend * 0.5; tz += (n.Z - c.Z) * blend * 0.5; }
    const desired = Math.atan2(tx - car.x, tz - car.z) * 180 / Math.PI;
    const err = norm(desired - car.heading);
    let steer = clamp(err / (20 + 10 * (1 - this.skill)), -1, 1);
    for (const o of race.racers) {
      if (o === me) continue;
      const ox = o.car.x - car.x, oz = o.car.z - car.z, d = Math.hypot(ox, oz);
      if (d > 9 || d < 0.01) continue;
      const bearing = norm(Math.atan2(ox, oz) * 180 / Math.PI - car.heading);
      if (Math.abs(bearing) < 25) steer = clamp(steer + (bearing >= 0 ? -0.35 : 0.35), -1, 1);
    }
    let corner = car.s.maxSpeed;
    if (n) {
      const ih = Math.atan2(c.X - car.x, c.Z - car.z) * 180 / Math.PI, oh = Math.atan2(n.X - c.X, n.Z - c.Z) * 180 / Math.PI;
      corner = car.s.maxSpeed * clamp(1 - Math.abs(norm(oh - ih)) / 120, 0.3, 1);
    }
    if (this.mistakeFor !== i) { this.mistakeFor = i; this.mistake = this.rng.chance((1 - this.skill) * 0.35); }
    const margin = this.mistake ? 0.55 : 0.9 + 0.1 * this.skill, v = car.speed;
    const need = v > corner ? (v * v - corner * corner) / (2 * car.s.braking) : 0;
    const inp = { steer, throttle: 0.82 + 0.18 * this.skill, brake: 0 };
    if (dC * margin < need) { inp.throttle = 0; inp.brake = 1; }
    if (Math.abs(err) > 70 && v > 12) { inp.throttle = 0.3; inp.handbrake = this.skill > 0.6; if (!inp.handbrake) inp.brake = 0.6; }
    return inp;
  }
}

export class Race {
  constructor(def, playerCar, vehicleDefs, seed, attempt) {
    this.def = def; this.attempt = attempt; this.elapsed = 0; this.countdown = def.CountdownSeconds; this.over = false; this.dnf = false;
    this.events = []; this.finished = 0; this.lastSec = -1; this.racers = [];
    this.player = this.add('racer.player', 'You', null, playerCar, 0);
    const rng = new Rng(seed);
    def.Opponents.forEach((o, i) => {
      const car = new Car(statsFor(vehicleDefs.find((v) => v.Id === o.VehicleId)));
      const r = this.add(o.RacerId, o.DisplayName, o.NpcId, car, i + 1);
      r.ai = new RaceDriverAI(o.Skill, new Rng(rng.state ^ BigInt(i + 1) * 0x9E3779B97F4A7C15n));
      r.vehicleId = o.VehicleId;
    });
  }
  add(id, name, npc, car, slot) {
    const g = this.def.Grid[slot]; car.x = g.X; car.z = g.Z; car.heading = g.HeadingDeg; car.speed = 0; car.drift = 0;
    const r = { id, name, npc, car, next: 0, lap: 0, done: false, time: 0, place: 0, impacts: 0, isPlayer: id === 'racer.player' };
    this.racers.push(r); return r;
  }
  step(input, dt) {
    this.events = [];
    if (this.over) return;
    if (this.countdown > 0) {
      this.countdown -= dt; const sec = Math.ceil(Math.max(0, this.countdown));
      if (sec !== this.lastSec) { this.lastSec = sec; this.events.push({ type: sec > 0 ? 'Countdown' : 'Go', value: sec }); }
      for (const r of this.racers) r.car.speed = 0;
      return;
    }
    this.elapsed += dt;
    for (const r of this.racers) {
      if (r.done) { r.car.step({ brake: 1 }, dt); continue; }
      r.car.step(r.isPlayer ? input : r.ai.think(this, r), dt);
      const c = this.def.Checkpoints[r.next];
      if (Math.hypot(r.car.x - c.X, r.car.z - c.Z) <= c.Radius) {
        this.events.push({ type: 'Checkpoint', racer: r.id, value: r.next });
        r.next++;
        if (r.next >= this.def.Checkpoints.length) {
          r.lap++;
          if (r.lap >= this.def.Laps) { r.done = true; r.time = this.elapsed; r.place = ++this.finished; this.events.push({ type: 'Finished', racer: r.id, value: r.place }); }
          else r.next = 0;
        }
      }
    }
    if (this.player.done) return this.finish();
    if (this.elapsed >= this.def.TimeLimitSeconds) { this.dnf = true; this.events.push({ type: 'TimeExpired' }); this.finish(); }
  }
  progress(r) {
    if (r.done) return 1e9 - r.time;
    const c = this.def.Checkpoints[r.next];
    return (r.lap * this.def.Checkpoints.length + r.next) * 10000 - Math.hypot(r.car.x - c.X, r.car.z - c.Z);
  }
  standings() { return [...this.racers].sort((a, b) => this.progress(b) - this.progress(a)); }
  position(r) { return this.standings().indexOf(r) + 1; }
  finish() {
    this.over = true; let place = this.finished;
    for (const r of this.standings()) if (!r.done && !(r.isPlayer && this.dnf)) r.place = ++place;
    if (this.dnf) this.player.place = this.racers.length;
  }
}

export const RANKS = ['Unknown Driver', 'Neighborhood Runner', 'City Contender', 'Street Circuit Elite', 'Southern Road Legend'];
export const RANK_POINTS = [0, 100, 300, 700, 1500];
export const rankFor = (p) => { let r = 0; RANK_POINTS.forEach((t, i) => { if (p >= t) r = i; }); return r; };
