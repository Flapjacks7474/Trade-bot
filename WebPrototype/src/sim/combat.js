// Port of FFTS.Combat (CombatSimulation, FighterRuntime, Moveset, FighterAI). Same numbers as the C# version.
import { Rng, clamp, roundAway } from './rng.js';

export const S = Object.freeze({
  Idle: 'Idle', Moving: 'Moving', Attacking: 'Attacking', Combo: 'Combo', Finisher: 'Finisher', Blocking: 'Blocking',
  Countering: 'Countering', Grappling: 'Grappling', Throwing: 'Throwing', KnockedDown: 'KnockedDown', GettingUp: 'GettingUp',
  Stunned: 'Stunned', Defeated: 'Defeated', HitStun: 'HitStun', BlockStun: 'BlockStun', Dodging: 'Dodging', Grappled: 'Grappled',
  FinisherVictim: 'FinisherVictim'
});
export const C = Object.freeze({ None: 'None', Light: 'Light', Heavy: 'Heavy', Kick: 'Kick', Grapple: 'Grapple', Dodge: 'Dodge', Counter: 'Counter', Finisher: 'Finisher', Taunt: 'Taunt' });

export const defaultConfig = () => ({
  tickRate: 60, arenaHalfWidth: 6, minSeparation: 0.7, bodyReach: 0.35, wallThrowDistance: 1.3, inputBufferTicks: 8,
  dodgeTicks: 20, dodgeInvulnStart: 2, dodgeInvulnEnd: 14, dodgeDistance: 1.6, dodgeStaminaCost: 12,
  counterStanceTicks: 26, counterStaminaCost: 6, knockdownTicks: 70, getUpTicks: 24, quickRiseAfterTicks: 20,
  stunTicks: 90, guardBreakStunTicks: 75, clinchMaxTicks: 110, clinchStrikeCooldown: 20, clinchStrikesMax: 3,
  escapeThreshold: 100, reversalWindowTicks: 14, reversalMomentumCost: 25, breakawayMinCombo: 3, breakawayMomentumCost: 30,
  breakawayStaminaCost: 15, momentumMax: 100, finisherMomentum: 100, finisherHealthFraction: 0.35, finisherRange: 1.8,
  comboDamageDecay: 0.15, minDamageScale: 0.35, hitStunDecay: 0.12, minHitStunScale: 0.4, chipDamageFraction: 0.1,
  staminaRegenPerSecond: 14, tauntMomentum: 15, roundTicks: 60 * 180, get dt() { return 1 / this.tickRate; }
});

const isStrike = (k) => ['Light', 'Heavy', 'Kick', 'Counter', 'Signature', 'GroundAttack'].includes(k);

export class Moveset {
  constructor(styles) {
    this.moves = new Map(); this.links = [];
    for (const s of styles) { for (const m of s.Moves) if (!this.moves.has(m.Id)) this.moves.set(m.Id, m); this.links.push(...s.Links); }
    const slots = ['Light', 'Heavy', 'Kick', 'Grapple', 'Counter', 'Finisher', 'GroundAttack', 'Taunt', 'ClinchStrike', 'ClinchThrow', 'EnvironmentalThrow', 'Reversal'];
    for (const slot of slots) for (const s of styles) { if (!this[slot] && s[slot]) this[slot] = this.moves.get(s[slot]); }
  }
  get(id) { return this.moves.get(id) || null; }
  findLink(from, cmd) { const l = this.links.find((x) => x.From === from && x.Command === cmd); return l ? this.get(l.To) : null; }
  entryFor(c) { return { Light: this.Light, Heavy: this.Heavy, Kick: this.Kick, Grapple: this.Grapple, Taunt: this.Taunt }[c] || null; }
}

export class Fighter {
  constructor(def, moves, x) {
    this.id = def.Id; this.def = def; this.moves = moves; this.stats = def.Stats;
    this.maxHealth = 100 + this.stats.Durability; this.maxStamina = 60 + this.stats.Stamina * 0.8;
    this.health = this.maxHealth; this.stamina = this.maxStamina; this.momentum = 0; this.x = x; this.facing = 1;
    this.state = S.Idle; this.stateTicks = 0; this.stateDuration = 0;
    this.move = null; this.moveTick = 0; this.moveStartup = 0; this.moveConnected = false; this.moveWasDodged = false;
    this.combo = 0; this.blockStartTick = 0; this.buffered = C.None; this.bufferedAge = 0; this.prevBlock = false;
    this.clinchTicks = 0; this.clinchStrikes = 0; this.clinchCooldown = 0; this.escape = 0; this.counters = 0; this.throws = 0;
  }
  applyScenario({ health, momentum, stamina } = {}) {
    if (health != null) this.health = clamp(health, 1, this.maxHealth);
    if (momentum != null) this.momentum = clamp(momentum, 0, 100);
    if (stamina != null) this.stamina = clamp(stamina, 0, this.maxStamina);
  }
  get healthFraction() { return this.health / this.maxHealth; }
  get walkSpeed() { return 2.2 + this.stats.Speed / 50; }
  get strMul() { return 0.7 + this.stats.Strength / 166; }
  get durMul() { return 1.15 - this.stats.Durability / 333; }
  get counterWindow() { return 6 + Math.trunc(this.stats.Technique / 15); }
  get parryWindow() { return 3 + Math.trunc(this.stats.Technique / 25); }
  adjustStartup(s) { return Math.max(1, roundAway(s * (1.15 - this.stats.Speed / 400))); }
  get inActive() { return this.move && this.moveTick >= this.moveStartup && this.moveTick < this.moveStartup + this.move.Active; }
  get inStartup() { return this.move && this.moveTick < this.moveStartup; }
  get inRecovery() { return this.move && this.moveTick >= this.moveStartup + this.move.Active; }
  dodgeInvuln(c) { return this.state === S.Dodging && this.stateTicks >= c.dodgeInvulnStart && this.stateTicks < c.dodgeInvulnEnd; }
}

export class CombatSim {
  constructor(config, a, b, fightId) {
    this.cfg = config || defaultConfig(); this.A = a; this.B = b; this.fightId = fightId;
    this.tick = 0; this.result = null; this.events = []; this.updateFacing();
  }
  get isOver() { return !!this.result; }
  other(f) { return f === this.A ? this.B : this.A; }
  get distance() { return Math.abs(this.A.x - this.B.x); }

  step(ia, ib) {
    this.events = [];
    if (this.isOver) return;
    this.tick++;
    this.buffer(this.A, ia); this.buffer(this.B, ib);
    this.update(this.A, this.B, ia); this.update(this.B, this.A, ib);
    this.resolveHits(); this.resolvePositions();
    if (!this.isOver && this.tick >= this.cfg.roundTicks) this.endOnTime();
  }

  buffer(f, i) {
    if (i.command && i.command !== C.None) { f.buffered = i.command; f.bufferedAge = 0; }
    else if (f.buffered !== C.None && ++f.bufferedAge > this.cfg.inputBufferTicks) f.buffered = C.None;
    if (i.block && !f.prevBlock) f.blockStartTick = this.tick;
    f.prevBlock = !!i.block;
  }
  take(f) { const c = f.buffered; f.buffered = C.None; f.bufferedAge = 0; return c; }

  update(f, o, input) {
    if (f.state === S.Defeated || this.isOver) return;
    f.stateTicks++;
    const c = this.cfg;
    switch (f.state) {
      case S.Idle: case S.Moving: case S.Blocking: this.neutral(f, o, input); break;
      case S.Attacking: case S.Combo: case S.Throwing: case S.Finisher: this.advance(f, o); break;
      case S.Countering: if (f.stateTicks >= c.counterStanceTicks) this.set(f, S.Idle); break;
      case S.HitStun: if (this.breakaway(f, o)) break; if (f.stateTicks >= f.stateDuration) this.set(f, S.Idle); break;
      case S.BlockStun: case S.Stunned: if (f.stateTicks >= f.stateDuration) this.set(f, S.Idle); break;
      case S.Dodging: if (f.stateTicks <= 12) f.x -= f.facing * c.dodgeDistance / 12; if (f.stateTicks >= f.stateDuration) this.set(f, S.Idle); break;
      case S.KnockedDown:
        if (f.buffered === C.Dodge && f.stateTicks >= c.quickRiseAfterTicks) { this.take(f); this.emit('QuickRise', f); this.set(f, S.GettingUp, c.getUpTicks / 2); }
        else if (f.stateTicks >= f.stateDuration) this.set(f, S.GettingUp, c.getUpTicks);
        break;
      case S.GettingUp: if (f.stateTicks >= f.stateDuration) { this.set(f, S.Idle); this.emit('GotUp', f); } break;
      case S.Grappling: this.clinch(f, o); break;
      case S.Grappled: this.clinchDefender(f, o); break;
      case S.FinisherVictim: if (o.state !== S.Finisher) this.set(f, S.Idle); break;
    }
    this.regen(f);
  }

  neutral(f, o, input) {
    if (f.buffered !== C.None && this.tryCommand(f, o, this.take(f))) return;
    if (input.block) { if (f.state !== S.Blocking) this.set(f, S.Blocking); return; }
    if (Math.abs(input.move || 0) > 0.1) {
      f.x += f.facing * clamp(input.move, -1, 1) * f.walkSpeed * this.cfg.dt;
      if (f.state !== S.Moving) this.set(f, S.Moving);
    } else if (f.state !== S.Idle) this.set(f, S.Idle);
  }

  tryCommand(f, o, cmd) {
    const c = this.cfg;
    switch (cmd) {
      case C.Finisher: { const d = this.canFinish(f); if (d === 'None') this.startFinisher(f, o); else this.emit('FinisherDenied', f, o, null, 0, d); return true; }
      case C.Dodge:
        if (f.stamina < c.dodgeStaminaCost) { this.emit('NotEnoughStamina', f, null, 'dodge'); return true; }
        f.stamina -= c.dodgeStaminaCost; this.set(f, S.Dodging, c.dodgeTicks); return true;
      case C.Counter:
        if (!f.moves.Counter) return false;
        if (f.stamina < c.counterStaminaCost) { this.emit('NotEnoughStamina', f, null, 'counter'); return true; }
        f.stamina -= c.counterStaminaCost; this.set(f, S.Countering, c.counterStanceTicks); return true;
      default: {
        if (cmd === C.Heavy && o.state === S.KnockedDown && f.moves.GroundAttack && this.distance <= f.moves.GroundAttack.Range + c.bodyReach) {
          this.startMove(f, f.moves.GroundAttack, S.Attacking); return true;
        }
        const m = f.moves.entryFor(cmd);
        if (!m) return false;
        this.startMove(f, m, S.Attacking); return true;
      }
    }
  }

  startMove(f, m, state) {
    if (f.stamina < m.StaminaCost) { this.emit('NotEnoughStamina', f, null, m.Id, m.StaminaCost); return false; }
    f.stamina -= m.StaminaCost; this.set(f, state);
    f.move = m; f.moveTick = 0; f.moveStartup = f.adjustStartup(m.Startup); f.moveConnected = false; f.moveWasDodged = false;
    this.emit('MoveStarted', f, null, m.Id, 0, m.Kind); return true;
  }

  advance(f, o) {
    const m = f.move;
    if (!m) { this.set(f, S.Idle); return; }
    f.moveTick++;
    if (!f.moveConnected && f.moveTick >= f.moveStartup) {
      if (f.state === S.Throwing) { this.applyThrow(f, o, m); return; }
      if (f.state === S.Finisher) { this.applyFinisher(f, o, m); return; }
    }
    const rs = f.moveStartup + m.Active;
    if (f.moveConnected && f.moveTick >= rs && f.moveTick - rs < m.CancelWindow && f.buffered !== C.None && f.state !== S.Throwing && f.state !== S.Finisher) {
      const next = f.moves.findLink(m.Id, f.buffered);
      if (next) { this.take(f); this.startMove(f, next, S.Combo); return; }
    }
    if (f.moveTick >= rs + m.Recovery) {
      if (m.Kind === 'Taunt') { f.momentum = Math.min(this.cfg.momentumMax, f.momentum + this.cfg.tauntMomentum); this.emit('TauntCompleted', f, null, m.Id, this.cfg.tauntMomentum); }
      this.set(f, S.Idle);
    }
  }

  breakaway(f, o) {
    const c = this.cfg;
    if (f.buffered !== C.Dodge || f.combo < c.breakawayMinCombo) return false;
    if (f.momentum < c.breakawayMomentumCost || f.stamina < c.breakawayStaminaCost) return false;
    this.take(f); f.momentum -= c.breakawayMomentumCost; f.stamina -= c.breakawayStaminaCost;
    this.set(f, S.Dodging, c.dodgeTicks); f.stateTicks = c.dodgeInvulnStart;
    if (o.state === S.Attacking || o.state === S.Combo) this.set(o, S.BlockStun, 24);
    this.push(o, f, 0.8); this.emit('Breakaway', f, o); return true;
  }

  clinch(a, d) {
    const c = this.cfg;
    a.clinchTicks++; if (a.clinchCooldown > 0) a.clinchCooldown--;
    if (d.state !== S.Grappled) { this.set(a, S.Idle); return; }
    if (a.clinchTicks >= c.clinchMaxTicks) { this.set(a, S.Idle); this.set(d, S.Idle); this.push(d, a, 0.6); this.emit('ClinchReleased', a, d); return; }
    const cmd = a.buffered;
    if (cmd === C.Light && a.clinchCooldown === 0 && a.clinchStrikes < c.clinchStrikesMax && a.moves.ClinchStrike) {
      this.take(a); const m = a.moves.ClinchStrike;
      if (a.stamina < m.StaminaCost) { this.emit('NotEnoughStamina', a, null, m.Id); return; }
      a.stamina -= m.StaminaCost;
      const dmg = this.scaled(a, d, m); d.health -= dmg; d.combo++;
      this.addMomentum(a, m.MomentumGain); a.clinchStrikes++; a.clinchCooldown = c.clinchStrikeCooldown;
      this.emit('ClinchStrike', a, d, m.Id, dmg);
      if (d.health <= 0) { this.set(a, S.Idle); this.defeat(d, a, 'Knockout'); }
    } else if (cmd === C.Heavy || cmd === C.Grapple) {
      this.take(a);
      const near = c.arenaHalfWidth - Math.abs(d.x) <= c.wallThrowDistance;
      const tm = cmd === C.Grapple && near && a.moves.EnvironmentalThrow ? a.moves.EnvironmentalThrow : a.moves.ClinchThrow;
      if (tm) this.startMove(a, tm, S.Throwing);
    }
  }

  clinchDefender(d, a) {
    const c = this.cfg;
    if (a.state !== S.Grappling && a.state !== S.Throwing) { this.set(d, S.Idle); return; }
    if (a.state === S.Throwing) return;
    const cmd = this.take(d);
    if (cmd === C.Counter && a.clinchTicks <= c.reversalWindowTicks && d.momentum >= c.reversalMomentumCost && d.moves.Reversal && d.stamina >= d.moves.Reversal.StaminaCost) {
      d.momentum -= c.reversalMomentumCost; this.set(a, S.Grappled); this.startMove(d, d.moves.Reversal, S.Throwing);
      this.emit('Reversal', d, a, d.moves.Reversal.Id);
    } else if (cmd === C.Grapple || cmd === C.Dodge) {
      const gain = 16 + d.stats.Technique * 0.2 + d.stats.Strength * 0.1 - a.stats.Strength * 0.08;
      d.escape += Math.max(6, gain);
      if (d.escape >= c.escapeThreshold) {
        d.escape = 0; this.set(a, S.HitStun, 20); this.set(d, S.Idle); this.push(a, d, 0.5); this.push(d, a, 0.5);
        this.emit('GrappleEscaped', d, a);
      }
    }
  }

  resolveHits() {
    let at = this.attempt(this.A, this.B), bt = this.attempt(this.B, this.A);
    if (at === 'G' && bt === 'G') {
      this.set(this.A, S.BlockStun, 14); this.set(this.B, S.BlockStun, 14); this.push(this.A, this.B, 0.4); this.push(this.B, this.A, 0.4);
      this.emit('Clash', this.A, this.B); return;
    }
    if (at === 'G' && bt === 'S') at = null;
    if (bt === 'G' && at === 'S') bt = null;
    const am = this.A.move, bm = this.B.move;
    if (at === 'S') this.applyStrike(this.A, this.B, am); else if (at === 'G') this.applyGrapple(this.A, this.B);
    if (this.isOver) return;
    if (bt === 'S') this.applyStrike(this.B, this.A, bm); else if (bt === 'G' && this.B.state === S.Attacking) this.applyGrapple(this.B, this.A);
  }

  attempt(f, o) {
    if (f.state !== S.Attacking && f.state !== S.Combo) return null;
    const m = f.move;
    if (!m || f.moveConnected || !f.inActive) return null;
    if (['Taunt', 'Throw', 'EnvironmentalThrow', 'Finisher', 'ClinchStrike'].includes(m.Kind)) return null;
    if (this.distance > m.Range + this.cfg.bodyReach) return null;
    if ([S.Defeated, S.GettingUp, S.FinisherVictim, S.Grappled, S.Grappling, S.Throwing, S.Finisher].includes(o.state)) return null;
    if (o.dodgeInvuln(this.cfg)) { if (!f.moveWasDodged) { f.moveWasDodged = true; this.emit('Dodged', o, f, m.Id); } return null; }
    const down = o.state === S.KnockedDown;
    if (m.Kind === 'GroundAttack') return down ? 'S' : null;
    if (down) return null;
    return m.Kind === 'Grapple' ? 'G' : 'S';
  }

  applyStrike(a, d, m) {
    a.moveConnected = true;
    const parry = d.state === S.Blocking && this.tick - d.blockStartTick <= d.parryWindow;
    const stance = d.state === S.Countering && d.stateTicks <= d.counterWindow;
    if (m.Kind !== 'GroundAttack' && (parry || stance) && d.moves.Counter) { this.applyCounter(d, a, parry ? 'parry' : 'stance'); return; }
    if (d.state === S.Blocking || d.state === S.BlockStun) { this.applyBlock(a, d, m); return; }
    this.applyHit(a, d, m);
  }

  applyCounter(c, v, how) {
    const cm = c.moves.Counter; const dmg = this.scaled(c, v, cm);
    v.health -= dmg; v.combo++; this.addMomentum(c, cm.MomentumGain); c.counters++;
    this.emit('Countered', c, v, cm.Id, dmg, how);
    if (v.health <= 0) { this.defeat(v, c, 'Knockout'); return; }
    this.set(v, S.HitStun, cm.HitStun);
    this.set(c, S.Attacking); c.move = cm; c.moveStartup = c.adjustStartup(cm.Startup); c.moveTick = c.moveStartup + cm.Active; c.moveConnected = true; c.moveWasDodged = false;
  }

  applyBlock(a, d, m) {
    const chip = m.Damage * this.cfg.chipDamageFraction * a.strMul;
    d.health = Math.max(1, d.health - chip); d.stamina -= m.StaminaDamage; this.addMomentum(a, m.MomentumGain * 0.3);
    this.push(d, a, m.Pushback * 0.7);
    if (d.stamina <= 0) { d.stamina = d.maxStamina * 0.25; this.set(d, S.Stunned, this.cfg.guardBreakStunTicks); this.emit('GuardBreak', a, d, m.Id, chip); return; }
    this.set(d, S.BlockStun, m.BlockStun); this.emit('Blocked', a, d, m.Id, chip);
  }

  applyHit(a, d, m) {
    const c = this.cfg; const dmg = this.scaled(a, d, m);
    const stunScale = Math.max(c.minHitStunScale, 1 - c.hitStunDecay * d.combo);
    d.health -= dmg; d.combo++; this.addMomentum(a, m.MomentumGain); this.addMomentum(d, m.MomentumGain * 0.25);
    this.emit('Hit', a, d, m.Id, dmg, 'combo ' + d.combo); this.push(d, a, m.Pushback);
    if (d.health <= 0) { this.defeat(d, a, 'Knockout'); return; }
    if (m.Knockdown) { this.knockdown(d); return; }
    this.set(d, S.HitStun, Math.max(4, roundAway(m.HitStun * stunScale)));
  }

  applyGrapple(a, d) {
    a.moveConnected = true; this.set(a, S.Grappling); a.clinchTicks = 0; a.clinchStrikes = 0; a.clinchCooldown = 10;
    this.set(d, S.Grappled); d.escape = 0;
    d.x = clamp(a.x + a.facing * this.cfg.minSeparation, -this.cfg.arenaHalfWidth, this.cfg.arenaHalfWidth);
    this.emit('GrappleStarted', a, d);
  }

  applyThrow(a, d, m) {
    a.moveConnected = true; const env = m.Kind === 'EnvironmentalThrow'; const dmg = this.scaled(a, d, m);
    d.health -= dmg; d.combo++; this.addMomentum(a, m.MomentumGain); a.throws++;
    this.emit('Thrown', a, d, m.Id, dmg, env ? 'environment' : 'throw');
    const h = this.cfg.arenaHalfWidth;
    d.x = env ? Math.sign(d.x === 0 ? a.facing : d.x) * h : clamp(a.x + a.facing * 1.4, -h, h);
    if (d.health <= 0) { this.defeat(d, a, 'Knockout'); return; }
    this.knockdown(d);
  }

  knockdown(d) {
    const t = Math.max(30, roundAway(this.cfg.knockdownTicks * (1.1 - d.stats.Durability / 500)));
    this.set(d, S.KnockedDown, t); this.emit('KnockedDown', d);
  }

  canFinish(f) {
    const o = this.other(f), c = this.cfg;
    if (!f.moves.Finisher) return 'NoFinisherMove';
    if (![S.Idle, S.Moving, S.Blocking].includes(f.state)) return 'AttackerBusy';
    if (f.momentum < c.finisherMomentum) return 'NotEnoughMomentum';
    if (![S.Idle, S.Moving, S.Blocking, S.BlockStun, S.HitStun, S.Stunned, S.Countering].includes(o.state)) return 'OpponentStateInvalid';
    if (!(o.healthFraction <= c.finisherHealthFraction || o.state === S.Stunned)) return 'OpponentNotVulnerable';
    if (this.distance > c.finisherRange) return 'OutOfRange';
    return 'None';
  }

  startFinisher(f, o) {
    f.momentum = 0; const m = f.moves.Finisher; f.stamina += m.StaminaCost;
    this.startMove(f, m, S.Finisher); this.set(o, S.FinisherVictim); this.emit('FinisherStarted', f, o, m.Id);
  }

  applyFinisher(a, d, m) {
    a.moveConnected = true; const dmg = m.Damage * a.strMul * d.durMul;
    d.health -= dmg; this.emit('Hit', a, d, m.Id, dmg, 'finisher');
    if (d.health <= 0) { this.defeat(d, a, 'Finisher'); return; }
    this.knockdown(d);
  }

  scaled(a, d, m) { return m.Damage * a.strMul * d.durMul * Math.max(this.cfg.minDamageScale, 1 - this.cfg.comboDamageDecay * d.combo); }
  addMomentum(f, n) { f.momentum = clamp(f.momentum + n, 0, this.cfg.momentumMax); }

  set(f, s, duration = 0) {
    if (![S.Attacking, S.Combo, S.Throwing, S.Finisher].includes(s)) f.move = null;
    f.state = s; f.stateTicks = 0; f.stateDuration = duration;
    if ([S.Idle, S.Moving, S.Blocking, S.GettingUp].includes(s)) f.combo = 0;
  }

  regen(f) {
    const r = { Idle: 1, Moving: 1, Blocking: 0.25, KnockedDown: 0.5, Grappled: 0.5 }[f.state] || 0;
    if (r <= 0) return;
    f.stamina = Math.min(f.maxStamina, f.stamina + this.cfg.staminaRegenPerSecond * (0.5 + f.stats.Stamina / 200) * this.cfg.dt * r);
  }

  push(t, from, amt) { let dir = Math.sign(t.x - from.x); if (dir === 0) dir = from.facing; t.x += dir * amt; }
  updateFacing() { if (this.A.x < this.B.x) { this.A.facing = 1; this.B.facing = -1; } else if (this.A.x > this.B.x) { this.A.facing = -1; this.B.facing = 1; } }

  resolvePositions() {
    const h = this.cfg.arenaHalfWidth, A = this.A, B = this.B;
    A.x = clamp(A.x, -h, h); B.x = clamp(B.x, -h, h);
    const clinch = A.state === S.Grappling || B.state === S.Grappling;
    const gap = this.distance;
    if (!clinch && gap < this.cfg.minSeparation) {
      const push = (this.cfg.minSeparation - gap) / 2;
      const left = A.x <= B.x ? A : B, right = left === A ? B : A;
      left.x -= push; right.x += push;
      if (left.x < -h) { right.x += -h - left.x; left.x = -h; }
      if (right.x > h) { left.x -= right.x - h; right.x = h; }
    }
    this.updateFacing();
  }

  defeat(loser, winner, method) {
    loser.health = 0; this.set(loser, S.Defeated); this.emit('Defeated', winner, loser, null, 0, method);
    this.result = this.makeResult(winner, loser, method);
  }
  endOnTime() {
    this.emit('TimeUp', this.A, this.B);
    const a = this.A.healthFraction, b = this.B.healthFraction;
    if (Math.abs(a - b) < 1e-4) { this.result = this.makeResult(null, null, 'Draw'); return; }
    this.result = a > b ? this.makeResult(this.A, this.B, 'TimeDecision') : this.makeResult(this.B, this.A, 'TimeDecision');
  }
  makeResult(w, l, method) { return { fightId: this.fightId, winnerId: w ? w.id : null, loserId: l ? l.id : null, method, durationTicks: this.tick }; }
  emit(type, actor, target, moveId = null, amount = 0, detail = null) {
    this.events.push({ type, actor: actor ? actor.id : null, target: target ? target.id : null, moveId, amount, tick: this.tick, detail });
  }
}

// ------------------------------------------------------------------ AI (port of FighterAI)
const DIFF = {
  Easy: { react: 24, interval: 40, def: 0.2, counter: 0.05, combo: 0.25, escape: 3, mistake: 0.3, adapt: 0.1 },
  Normal: { react: 14, interval: 26, def: 0.4, counter: 0.15, combo: 0.55, escape: 5, mistake: 0.15, adapt: 0.25 },
  Hard: { react: 10, interval: 18, def: 0.6, counter: 0.3, combo: 0.8, escape: 7, mistake: 0.08, adapt: 0.35 },
  Expert: { react: 8, interval: 14, def: 0.72, counter: 0.4, combo: 0.9, escape: 9, mistake: 0.04, adapt: 0.45 },
  Boss: { react: 6, interval: 12, def: 0.8, counter: 0.5, combo: 0.95, escape: 10, mistake: 0.02, adapt: 0.55 }
};

export class FighterAI {
  constructor(self, style, difficulty, rng) {
    this.self = self; this.style = style; this.p = DIFF[difficulty] || DIFF.Normal; this.rng = rng || new Rng(1);
    this.queue = []; this.lastSeen = S.Idle; this.blockHold = 0; this.nextDecision = 0; this.lastDefended = -1000;
    this.openers = {}; this.total = 0; this.blockTendency = 0;
  }
  freq(k) { return this.total <= 0 ? 0 : (this.openers[k] || 0) / this.total; }
  observeOpener(k) { for (const key in this.openers) this.openers[key] *= 0.93; this.total = this.total * 0.93 + 1; this.openers[k] = (this.openers[k] || 0) + 1; }

  think(sim) {
    const me = this.self, opp = sim.other(me), p = this.p, rng = this.rng, c = sim.cfg;
    this.queue.push({ tick: sim.tick, state: opp.state, kind: opp.move ? opp.move.Kind : null, startup: !!opp.inStartup,
      recovery: !!(opp.inRecovery && !opp.moveConnected), range: opp.move ? opp.move.Range : 0, x: opp.x });
    let seen = null;
    while (this.queue.length && sim.tick - this.queue[0].tick >= p.react) seen = this.queue.shift();
    if (seen) {
      const started = seen.state === S.Attacking && this.lastSeen !== S.Attacking && this.lastSeen !== S.Combo;
      if (started && seen.kind) this.observeOpener(seen.kind);
      this.blockTendency = this.blockTendency * 0.995 + (seen.state === S.Blocking ? 0.005 : 0);
      this.lastSeen = seen.state;
    } else seen = { state: S.Idle, x: opp.x, startup: false, recovery: false, kind: null, range: 0, tick: -1 };
    const dist = Math.abs(me.x - seen.x);
    const none = { move: 0, block: false, command: C.None };
    const press = (cmd) => ({ move: 0, block: false, command: cmd });

    switch (me.state) {
      case S.Defeated: case S.FinisherVictim: case S.GettingUp: case S.Throwing: case S.Finisher: case S.Countering:
      case S.Dodging: case S.BlockStun: case S.Stunned: return none;
      case S.Grappled:
        if (opp.state === S.Grappling && opp.clinchTicks <= c.reversalWindowTicks && me.momentum >= c.reversalMomentumCost && rng.chance(p.counter * 0.15)) return press(C.Counter);
        return rng.chance(p.escape / c.tickRate) ? press(C.Grapple) : none;
      case S.Grappling: {
        if (!rng.chance(0.08)) return none;
        const wall = c.arenaHalfWidth - Math.abs(opp.x) <= c.wallThrowDistance;
        if (wall && me.moves.EnvironmentalThrow) return press(C.Grapple);
        if (me.clinchTicks < 40 && rng.chance(0.6)) return press(C.Light);
        return press(C.Heavy);
      }
      case S.HitStun:
        if (me.combo >= c.breakawayMinCombo && me.momentum >= c.breakawayMomentumCost && rng.chance(p.def * 0.3)) return press(C.Dodge);
        return none;
      case S.KnockedDown: return me.stateTicks >= c.quickRiseAfterTicks && rng.chance(p.def * 0.1) ? press(C.Dodge) : none;
      case S.Attacking: case S.Combo: {
        if (!me.moveConnected || !me.inRecovery || !me.move) return none;
        const opts = me.moves.links.filter((l) => l.From === me.move.Id).map((l) => l.Command);
        if (!opts.length || !rng.chance(p.combo)) return none;
        return press(opts[rng.range(0, opts.length)]);
      }
    }
    if (sim.canFinish(me) === 'None') return press(C.Finisher);

    // Defend against what was seen.
    if (seen.startup && seen.kind && seen.tick !== this.lastDefended && dist <= seen.range + 0.8) {
      this.lastDefended = seen.tick;
      const adapt = this.freq(seen.kind) * p.adapt;
      if (rng.chance(Math.min(0.95, p.def + adapt))) {
        if (seen.kind === 'Grapple') return me.stamina >= 12 ? press(C.Dodge) : press(C.Light);
        const tech = this.style === 'Technical' || this.style === 'Defensive';
        if (tech && me.moves.Counter && rng.chance(p.counter + adapt)) return press(C.Counter);
        if (this.style === 'Showman' && rng.chance(0.3) && me.stamina >= 12) return press(C.Dodge);
        this.blockHold = 18 + rng.range(0, 10);
        return { move: 0, block: true, command: C.None };
      }
    }
    if (this.blockHold > 0) { this.blockHold--; return { move: 0, block: true, command: C.None }; }

    // Anticipation from habits.
    if (dist <= 1.9 && (seen.state === S.Idle || seen.state === S.Moving)) {
      const strike = Math.max(this.freq('Heavy'), this.freq('Light'), this.freq('Kick')), grab = this.freq('Grapple');
      const perTick = p.adapt * 0.08;
      if (grab > 0.5 && me.stamina > 20 && rng.chance(grab * perTick)) return press(C.Dodge);
      if (strike > 0.5 && rng.chance(strike * perTick)) { this.blockHold = 24; return { move: 0, block: true, command: C.None }; }
    }

    if (sim.tick < this.nextDecision) {
      if (this.style === 'Defensive' && dist < 2.5) return none;
      return dist > 1.4 ? { move: 0.8, block: false, command: C.None } : none;
    }
    this.nextDecision = sim.tick + p.interval + rng.range(0, Math.trunc(p.interval / 2) + 1);
    if (rng.chance(p.mistake)) return none;

    const punish = this.style === 'Wrestler' ? C.Grapple : C.Heavy;
    const pm = me.moves.entryFor(punish);
    if (seen.recovery && pm && dist <= pm.Range + 0.35 && me.stamina >= pm.StaminaCost) return press(punish);
    if (seen.state === S.KnockedDown && me.moves.GroundAttack && dist <= me.moves.GroundAttack.Range + 0.35) return press(C.Heavy);
    if (me.stamina < me.maxStamina * 0.25) return { move: -0.6, block: false, command: C.None };

    const preferred = this.style === 'Wrestler' ? 0.9 : 1.1;
    if (dist > preferred + 0.3) return { move: 1, block: false, command: C.None };
    let w;
    switch (this.style) {
      case 'Aggressive': w = { l: .4, h: .3, k: .2, g: .1, wait: 0, t: 0 }; break;
      case 'Technical': w = { l: .45, h: .15, k: .25, g: .05, wait: .1, t: 0 }; break;
      case 'Wrestler': w = { l: .2, h: .2, k: .1, g: .5, wait: 0, t: 0 }; break;
      case 'Defensive': w = { l: .3, h: .15, k: .1, g: .05, wait: .4, t: 0 }; break;
      default: w = { l: .3, h: .25, k: .25, g: .05, wait: 0, t: .15 };
    }
    const aff = this.style === 'Wrestler' ? 1 : this.style === 'Aggressive' ? 0.5 : 0.25;
    w.g += Math.min(0.5, this.blockTendency * 2) * p.adapt * 2 * aff;
    let r = rng.value() * (w.l + w.h + w.k + w.g + w.wait + w.t);
    if ((r -= w.l) < 0) return press(C.Light);
    if ((r -= w.h) < 0) return press(C.Heavy);
    if ((r -= w.k) < 0) return press(C.Kick);
    if ((r -= w.g) < 0) return press(C.Grapple);
    if ((r -= w.t) < 0 && dist > 1.5) return press(C.Taunt);
    this.blockHold = 20;
    return { move: 0, block: true, command: C.None };
  }
}

export function createFight(data, playerId, opponentId, seed) {
  const styles = new Map(data.styles.map((s) => [s.Id, s]));
  const fighters = new Map(data.fighters.map((f) => [f.Id, f]));
  const pd = fighters.get(playerId), od = fighters.get(opponentId);
  const ms = (d) => new Moveset(d.Styles.map((id) => styles.get(id)));
  const sim = new CombatSim(defaultConfig(), new Fighter(pd, ms(pd), -1.1), new Fighter(od, ms(od), 1.1), 'fight.' + seed);
  const ai = new FighterAI(sim.B, od.AiStyle, od.AiDifficulty, new Rng(seed));
  return { sim, ai };
}
