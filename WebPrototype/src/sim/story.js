// Port of the story-side simulation: FinessBank, Reputation, Social, Story flags, Missions, Dialogue,
// NPC conversation rules and fight consequences. Same keys and rules as the C# version.
import { clamp } from './rng.js';

export class Bank {
  constructor(startCents) {
    this.accounts = { cash: 0, checking: 0, savings: 0 }; this.keys = new Set(); this.ledger = [];
    if (startCents > 0) this.post('start.cash', 'cash', startCents, 'Pocket money');
  }
  post(key, to, cents, memo, from = null) {
    if (!key || this.keys.has(key)) return 'Duplicate';
    if (cents <= 0) return 'InvalidAmount';
    if (from && this.accounts[from] < cents) return 'InsufficientFunds';
    if (from) this.accounts[from] -= cents;
    if (to) this.accounts[to] += cents;
    this.keys.add(key); this.ledger.push({ key, from, to, cents, memo });
    if (this.ledger.length > 300) this.ledger.shift();
    return 'Posted';
  }
  pay(key, cents, memo) {
    if (this.keys.has(key + '.cash') || this.keys.has(key + '.checking')) return 'Duplicate';
    if (this.accounts.cash >= cents) return this.post(key + '.cash', null, cents, memo, 'cash');
    if (this.accounts.checking >= cents) return this.post(key + '.checking', null, cents, memo, 'checking');
    return 'InsufficientFunds';
  }
  get total() { return this.accounts.cash + this.accounts.checking + this.accounts.savings; }
  wasPosted(k) { return this.keys.has(k); }
  capture() { return { accounts: { ...this.accounts }, keys: [...this.keys], ledger: this.ledger.slice(-100) }; }
  restore(s) { this.accounts = { ...s.accounts }; this.keys = new Set(s.keys); this.ledger = s.ledger || []; }
  static fmt(c) { return (c < 0 ? '-$' : '$') + Math.floor(Math.abs(c) / 100).toLocaleString('en-US') + (Math.abs(c) % 100 ? '.' + String(Math.abs(c) % 100).padStart(2, '0') : ''); }
}

export class Reputation {
  constructor() {
    this.face = { Respect: 0, Fear: 0, Loyalty: 0, Influence: 0, Notoriety: 0, ThreatLevel: 0 };
    this.image = { Popularity: 0, Credibility: 20, Controversy: 0, Professionalism: 50, FanLoyalty: 0, IndustryRelationships: 0 };
  }
  apply(ch) {
    for (const k in ch.face || {}) this.face[k] = clamp((this.face[k] || 0) + ch.face[k], 0, 100);
    for (const k in ch.image || {}) this.image[k] = clamp((this.image[k] || 0) + ch.image[k], 0, 100);
  }
  capture() { return { face: { ...this.face }, image: { ...this.image } }; }
  restore(s) { Object.assign(this.face, s.face); Object.assign(this.image, s.image); }
}

const VALENCE = { 'player.helped': 20, 'player.protected': 30, 'player.won_fight': 0, 'player.humiliated': -30, 'player.attacked': -40, 'player.betrayed': -60 };
const REL_KEYS = ['Trust', 'Respect', 'Loyalty', 'Affection', 'Fear', 'Resentment', 'Rivalry', 'Friendship'];

export class Social {
  constructor() { this.rel = {}; this.personality = {}; this.memories = {}; this.events = new Set(); }
  get(npc, t = 'player') { return { ...Object.fromEntries(REL_KEYS.map((k) => [k, 0])), ...(this.rel[npc + '>' + t] || {}) }; }
  modify(npc, d, t = 'player') {
    const p = this.personality[npc] || {}; const temper = p.Temper ?? 50, patience = p.Patience ?? 50, courage = p.Courage ?? 50;
    const v = this.get(npc, t);
    for (const k of REL_KEYS) {
      let x = d[k] || 0;
      if (k === 'Resentment') x = Math.round(x * (0.6 + temper / 100 - (patience - 50) / 200));
      if (k === 'Fear') x = Math.round(x * (1.4 - courage / 100));
      v[k] = clamp(v[k] + x, -100, 100);
    }
    this.rel[npc + '>' + t] = v;
  }
  record(evt) {
    if (this.events.has(evt.id)) return; this.events.add(evt.id);
    for (const w of evt.witnesses) (this.memories[w] ||= []).push({ ...evt, source: 'Witnessed', reliability: 1 });
  }
  knows(npc, id) { return (this.memories[npc] || []).some((m) => m.id === id); }
  attitude(npc) {
    const v = this.get(npc), p = this.personality[npc] || {};
    let s = v.Trust + v.Friendship + v.Respect * 0.5 + v.Affection * 0.5 + v.Loyalty * 0.3 - v.Resentment - v.Rivalry * 0.5;
    const temper = 0.5 + (p.Temper ?? 50) / 100;
    for (const m of this.memories[npc] || []) {
      if (m.resolved || m.actor !== 'player' || VALENCE[m.type] == null) continue;
      const val = VALENCE[m.type], w = (m.target === npc ? 1 : 0.4) * m.reliability;
      s += val < 0 ? val * w * temper : val * w;
    }
    if (v.Fear >= 40) return (p.Courage ?? 50) >= 60 ? 'Hostile' : 'Intimidated';
    if (s <= -60) return 'Hostile'; if (s <= -15) return 'Wary'; if (s < 20) return 'Neutral'; if (s < 60) return 'Warm'; return 'Friendly';
  }
  capture() { return { rel: this.rel, memories: this.memories, events: [...this.events] }; }
  restore(s) { this.rel = s.rel || {}; this.memories = s.memories || {}; this.events = new Set(s.events || []); }
}
const ATTITUDES = ['Hostile', 'Intimidated', 'Wary', 'Neutral', 'Warm', 'Friendly'];

export const REVEAL_FLAG = 'story.chairman.identity_revealed';

export class Story {
  constructor() { this.chapter = 1; this.flags = {}; this.missionReached = () => false; }
  has(f) { return !!this.flags[f]; }
  set(f, v = 1) {
    if (f === REVEAL_FLAG && (this.chapter < 7 || !this.missionReached('mission.ch7.man_behind_table'))) return false;
    this.flags[f] = v; return true;
  }
  advance(ch) { if (ch > this.chapter && ch <= 8) this.chapter = ch; }
}

export class Missions {
  constructor(defs, story, grant) {
    this.defs = [...defs].sort((a, b) => a.Chapter - b.Chapter || a.Order - b.Order);
    this.byId = new Map(this.defs.map((d) => [d.Id, d]));
    this.prog = new Map(this.defs.map((d) => [d.Id, { status: 'Locked', done: [], branch: null, attempts: 0, lastFailure: null }]));
    this.granted = new Set(); this.story = story; this.grant = grant; this.tracked = null; this.log = [];
    story.missionReached = (id) => ['Active', 'Completed'].includes(this.status(id));
    this.refresh();
  }
  status(id) { return this.prog.get(id)?.status || 'Locked'; }
  def(id) { return this.byId.get(id); }
  objectiveDone(ref) { const [m, o] = ref.split('/'); const p = this.prog.get(m); return !!p && (p.status === 'Completed' || p.done.includes(o)); }
  prereqs(d) {
    const pre = d.Prerequisites || {};
    if (this.story.chapter < Math.max(pre.MinChapter || 1, d.Category === 'Main' ? d.Chapter : 0)) return false;
    for (const m of pre.MissionsCompleted || []) if (this.status(m) !== 'Completed') return false;
    for (const [f, v] of Object.entries(pre.FlagsAtLeast || {})) if ((this.story.flags[f] || 0) < v) return false;
    for (const f of pre.FlagsAbsent || []) if (this.story.has(f)) return false;
    return true;
  }
  refresh() { for (const d of this.defs) { const p = this.prog.get(d.Id); if (p.status === 'Locked' && this.prereqs(d)) p.status = 'Available'; } }
  pump() {
    for (let g = 0; g < 8; g++) {
      let changed = false;
      for (const d of this.defs) {
        const p = this.prog.get(d.Id);
        if ((p.status === 'Available' && (d.Category === 'Main' || d.Category === 'Side')) || (p.status === 'Failed' && d.Recovery !== 'FailPermanently')) {
          if (p.status === 'Failed' && d.Recovery === 'RestartMission') p.done = [];
          p.status = 'Active'; p.attempts++; changed = true;
          if (d.Category === 'Main') this.tracked = d.Id;
        }
      }
      if (!changed) break;
    }
    if (!this.tracked || this.status(this.tracked) !== 'Active') this.tracked = this.defs.find((d) => d.Category === 'Main' && this.status(d.Id) === 'Active')?.Id || null;
  }
  wants(trigger) {
    for (const d of this.defs) {
      const p = this.prog.get(d.Id); if (p.status !== 'Active') continue;
      if (d.Objectives.some((o) => o.Trigger === trigger && !p.done.includes(o.Id) && (!o.Branch || o.Branch === p.branch))) return true;
    }
    return false;
  }
  report(trigger) {
    let n = 0;
    for (const d of this.defs) {
      const p = this.prog.get(d.Id); if (p.status !== 'Active') continue;
      if ((d.FailureConditions || []).includes(trigger)) { this.fail(d, trigger); continue; }
      for (const o of d.Objectives) if (o.Trigger === trigger && this.complete(d, o.Id)) n++;
    }
    this.pump(); return n;
  }
  complete(d, oid) {
    const p = this.prog.get(d.Id), o = d.Objectives.find((x) => x.Id === oid);
    if (!o || p.done.includes(oid) || (o.Branch && o.Branch !== p.branch)) return false;
    p.done.push(oid); this.log.push({ type: 'objective', mission: d.Id, objective: o.Text });
    this.tryComplete(d); return true;
  }
  choose(mid, bid) {
    const d = this.def(mid), p = this.prog.get(mid);
    if (!d || p.status !== 'Active' || p.branch) return false;
    const b = (d.Branches || []).find((x) => x.Id === bid); if (!b) return false;
    p.branch = bid; b.Effects.forEach((e, i) => this.give(`branch.${mid}.${bid}.${i}`, e));
    this.tryComplete(d); this.pump(); return true;
  }
  fail(d, why) {
    const p = this.prog.get(d.Id); p.lastFailure = why;
    if (d.Recovery === 'RestartFromCheckpoint') p.done = p.done.filter((id) => d.Objectives.find((o) => o.Id === id)?.Checkpoint);
    p.status = 'Failed'; this.log.push({ type: 'failed', mission: d.Id, title: d.Title });
  }
  tryComplete(d) {
    const p = this.prog.get(d.Id);
    if (d.BranchRequired && !p.branch) return;
    for (const o of d.Objectives) { if (o.Optional || (o.Branch && o.Branch !== p.branch)) continue; if (!p.done.includes(o.Id)) return; }
    p.status = 'Completed';
    d.Rewards.forEach((r, i) => this.give(`reward.${d.Id}.${i}`, r));
    if (d.AdvanceToChapter) this.story.advance(d.AdvanceToChapter);
    if (this.tracked === d.Id) this.tracked = null;
    this.log.push({ type: 'completed', mission: d.Id, title: d.Title });
    this.refresh();
  }
  give(key, r) { if (this.granted.has(key)) return; this.granted.add(key); this.grant(key, r); }
  capture() { return { prog: [...this.prog.entries()], granted: [...this.granted], tracked: this.tracked }; }
  restore(s) { this.prog = new Map(s.prog); this.granted = new Set(s.granted); this.tracked = s.tracked; for (const d of this.defs) if (!this.prog.has(d.Id)) this.prog.set(d.Id, { status: 'Locked', done: [], branch: null, attempts: 0 }); this.refresh(); this.pump(); }
}

export class DialogueRunner {
  constructor(def, game) { this.def = def; this.game = game; this.nodes = new Map(def.Nodes.map((n) => [n.Id, n])); this.current = this.nodes.get(def.StartNode); }
  matches(c) {
    if (!c) return true; const g = this.game;
    for (const f of c.RequireFlags || []) if (!g.story.has(f)) return false;
    for (const f of c.ForbidFlags || []) if (g.story.has(f)) return false;
    const r = g.social.get(this.def.NpcId);
    if (c.MinTrust != null && r.Trust < c.MinTrust) return false;
    if (c.MinRespect != null && r.Respect < c.MinRespect) return false;
    if (c.MinAttitude && ATTITUDES.indexOf(g.social.attitude(this.def.NpcId)) < ATTITUDES.indexOf(c.MinAttitude)) return false;
    if ((c.Segments || []).length && !c.Segments.includes(g.segment)) return false;
    return true;
  }
  get speaker() { return this.current?.Speaker; }
  get line() { if (!this.current) return null; const v = (this.current.Variants || []).find((x) => this.matches(x.When)); return v ? v.Text : this.current.Text; }
  get options() { return this.current ? this.current.Options.filter((o) => this.matches(o.When)) : []; }
  choose(id) {
    const o = this.options.find((x) => x.Id === id); if (!o) return null;
    o.Effects.forEach((e, i) => this.game.applyEffect(`dialogue.${this.def.Id}.${o.Id}.${i}`, e));
    if (o.MissionBranch) { const [m, b] = o.MissionBranch.split(':'); this.game.missions.choose(m, b); }
    if (o.Trigger) this.game.trigger(o.Trigger);
    const out = { fight: o.StartFight || null, ended: false };
    this.current = o.Next ? this.nodes.get(o.Next) : null;
    if (!this.current) { out.ended = true; if (this.def.EndTrigger) this.game.trigger(this.def.EndTrigger); }
    return out;
  }
}

export function fightReputation(venue, won, method) {
  const ch = { face: {}, image: {} }; const fin = method === 'Finisher';
  if (venue === 'Underground') { if (won) Object.assign(ch.face, { Respect: 4, Fear: fin ? 4 : 2, Notoriety: 2 }), (ch.image.Professionalism = -1); else ch.face.Respect = -2; }
  else if (venue === 'Sanctioned') { if (won) (ch.face.Respect = 2), Object.assign(ch.image, { Popularity: 3, Professionalism: 1 }); else ch.image.Popularity = -1; }
  else { if (won) Object.assign(ch.face, { Notoriety: 3, Fear: 2 }), Object.assign(ch.image, { Credibility: -2, Professionalism: -3, Controversy: 2 }); else (ch.face.Respect = -3), (ch.image.Controversy = 1); }
  return ch;
}
