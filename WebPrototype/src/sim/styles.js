// Port of the style rules in FFTS.Combat: CombatDatabase.Validate (styles part) and StyleLoadout.
import { findHybrid, buildMoveset } from './combat.js';

const SLOTS = ['Light', 'Heavy', 'Kick', 'Grapple', 'Counter', 'Finisher', 'GroundAttack', 'Taunt', 'ClinchStrike', 'ClinchThrow', 'EnvironmentalThrow', 'Reversal'];
const REQUIRED = ['Light', 'Heavy', 'Kick', 'Grapple', 'Counter', 'Finisher', 'ClinchStrike', 'ClinchThrow'];
const pairKey = (a, b) => (a < b ? a + '+' + b : b + '+' + a);

/** Mirrors CombatDatabase.Validate for styles; returns a list of error strings (empty = valid). */
export function validateStyles(styles) {
  const errors = [];
  const byId = new Map();
  for (const s of styles) { if (byId.has(s.Id)) errors.push(`duplicate style ${s.Id}`); byId.set(s.Id, s); }
  const moveIds = new Set(); const pairs = new Set();
  for (const s of styles) {
    for (const m of s.Moves) {
      if (moveIds.has(m.Id)) errors.push(`duplicate move ${m.Id}`); moveIds.add(m.Id);
      if (m.Startup < 1 || m.Active < 1 || m.Recovery < 0) errors.push(`Move ${m.Id}: invalid frame data.`);
      if (m.Damage < 0 || m.StaminaCost < 0 || m.Range <= 0) errors.push(`Move ${m.Id}: invalid damage/stamina/range.`);
      if (m.CancelWindow < 0 || m.CancelWindow > m.Recovery) errors.push(`Move ${m.Id}: cancel window must fit inside recovery.`);
      if (!m.AnimationKey) errors.push(`Move ${m.Id}: missing animation key.`);
    }
    const st = s.Stance;
    if (st) {
      const mul = (v) => v >= 0.5 && v <= 1.5;
      if (![st.WalkSpeed, st.StrikeDamage, st.KickDamage, st.ThrowDamage, st.BlockChip, st.GuardDrain, st.DodgeDistance, st.DodgeCost, st.EscapeRate].every(mul))
        errors.push(`Style ${s.Id}: stance multipliers must stay within 0.5-1.5.`);
      if (st.ParryWindowBonus < 0 || st.ParryWindowBonus > 6 || st.CounterWindowBonus < 0 || st.CounterWindowBonus > 6) errors.push(`Style ${s.Id}: window bonuses 0-6.`);
      if (st.ClinchStrikesBonus < 0 || st.ClinchStrikesBonus > 3) errors.push(`Style ${s.Id}: clinch bonus 0-3.`);
      if (!st.IdleAnimation || !st.GuardAnimation) errors.push(`Style ${s.Id}: stance needs idle and guard animation keys.`);
    }
    const usable = new Map(s.Moves.map((m) => [m.Id, m]));
    const graph = [s];
    if (s.HybridOf && s.HybridOf.length) {
      if (s.HybridOf.length !== 2 || s.HybridOf[0] === s.HybridOf[1]) errors.push(`Hybrid ${s.Id}: needs two different parents.`);
      for (const pid of s.HybridOf) {
        const p = byId.get(pid);
        if (!p) { errors.push(`Hybrid ${s.Id}: unknown parent ${pid}`); continue; }
        if (p.HybridOf && p.HybridOf.length) errors.push(`Hybrid ${s.Id}: parent ${pid} is a hybrid.`);
        for (const m of p.Moves) usable.set(m.Id, m);
        graph.push(p);
      }
      if (s.HybridOf.length === 2) { const k = pairKey(s.HybridOf[0], s.HybridOf[1]); if (pairs.has(k)) errors.push(`Hybrid ${s.Id}: duplicate pair.`); pairs.add(k); }
    } else {
      for (const slot of REQUIRED) if (!s[slot]) errors.push(`Style ${s.Id}: base styles must fill the ${slot} slot.`);
    }
    for (const slot of SLOTS) if (s[slot] && !usable.has(s[slot])) errors.push(`Style ${s.Id}: slot references unknown move '${s[slot]}'.`);
    const keys = new Set();
    for (const l of s.Links) {
      if (!usable.has(l.From) || !usable.has(l.To)) { errors.push(`Style ${s.Id}: link ${l.From}->${l.To} references unknown move.`); continue; }
      if (keys.has(l.From + '|' + l.Command)) errors.push(`Style ${s.Id}: ambiguous link from ${l.From} on ${l.Command}.`); keys.add(l.From + '|' + l.Command);
      if (usable.get(l.From).CancelWindow <= 0) errors.push(`Style ${s.Id}: link from ${l.From} can never fire.`);
    }
    if (hasCycle(graph)) errors.push(`Style ${s.Id}: combo links contain a cycle (infinite combo risk).`);
  }
  return errors;
}

function hasCycle(styles) {
  const edges = new Map(); const taken = new Set();
  for (const s of styles) for (const l of s.Links) {
    const k = l.From + '|' + l.Command; if (taken.has(k)) continue; taken.add(k);
    if (!edges.has(l.From)) edges.set(l.From, []); edges.get(l.From).push(l.To);
  }
  const state = new Map();
  const visit = (n) => { if (state.has(n)) return state.get(n) === 1; state.set(n, 1); for (const x of edges.get(n) || []) if (visit(x)) return true; state.set(n, 2); return false; };
  for (const k of edges.keys()) if (visit(k)) return true;
  return false;
}

/** Longest combo chain (in hits) reachable from any entry slot of a resolved moveset. */
export function longestChain(moveset) {
  const memo = new Map();
  const depth = (id) => {
    if (memo.has(id)) return memo.get(id);
    memo.set(id, 1);
    let best = 1;
    for (const l of moveset.links) if (l.From === id && moveset.findLink(id, l.Command)?.Id === l.To) best = Math.max(best, 1 + depth(l.To));
    memo.set(id, best); return best;
  };
  return Math.max(...['Light', 'Heavy', 'Kick'].filter((s) => moveset[s]).map((s) => depth(moveset[s].Id)));
}

export const STARTER_STYLE = 'style.street';

/** Port of StyleLoadout (learn once through the bank, equip up to two, learned hybrid applies automatically). */
export class StyleLoadout {
  constructor(styles, bank) { this.styles = styles; this.byId = new Map(styles.map((s) => [s.Id, s])); this.bank = bank; this.reset(); }
  reset() { this.learned = [STARTER_STYLE]; this.equipped = [STARTER_STYLE]; }
  knows(id) { return this.learned.includes(id); }
  get activeHybrid() {
    if (this.equipped.length < 2) return null;
    const h = findHybrid(this.styles, this.equipped[0], this.equipped[1]);
    return h && this.knows(h.Id) ? h : null;
  }
  canLearn(id) {
    const s = this.byId.get(id);
    if (!s) return 'UnknownStyle';
    if (this.knows(id)) return 'AlreadyLearned';
    if (s.HybridOf && !(this.knows(s.HybridOf[0]) && this.knows(s.HybridOf[1]))) return 'ParentsNotLearned';
    return 'Ok';
  }
  learn(id) {
    const check = this.canLearn(id); if (check !== 'Ok') return check;
    const s = this.byId.get(id);
    if (s.UnlockCostCents > 0) {
      const st = this.bank ? this.bank.pay('style.learn.' + id, s.UnlockCostCents, 'Training: ' + s.DisplayName) : 'InsufficientFunds';
      if (st !== 'Posted' && st !== 'Duplicate') return 'InsufficientFunds';
    }
    this.learned.push(id); return 'Ok';
  }
  equip(primary, secondary = null) {
    const p = this.byId.get(primary);
    if (!p || p.HybridOf) return 'InvalidLoadout';
    if (!this.knows(primary)) return 'NotLearned';
    if (secondary != null) {
      const s = this.byId.get(secondary);
      if (!s || s.HybridOf || secondary === primary) return 'InvalidLoadout';
      if (!this.knows(secondary)) return 'NotLearned';
    }
    this.equipped = secondary ? [primary, secondary] : [primary]; return 'Ok';
  }
  buildMoveset() { return buildMoveset(this.styles, this.equipped, this.learned.filter((id) => this.byId.get(id)?.HybridOf)); }
  capture() { return { Learned: [...this.learned], Equipped: [...this.equipped] }; }
  restore(s) {
    this.reset(); if (!s) return;
    for (const id of s.Learned || []) if (this.byId.has(id) && !this.learned.includes(id)) this.learned.push(id);
    const eq = (s.Equipped || []).filter((id, i, a) => this.byId.has(id) && !this.byId.get(id).HybridOf && this.knows(id) && a.indexOf(id) === i).slice(0, 2);
    if (eq.length) this.equipped = eq;
  }
}
