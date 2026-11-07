import * as THREE from 'three';
import { Game } from '../sim/session.js';
import { Bank } from '../sim/story.js';
import { createFight, C, S } from '../sim/combat.js';
import { surfaceAt } from '../sim/world.js';
import { RANKS } from '../sim/vehicles.js';
import { Input } from './input.js';
import { buildWorld, makePerson, poseWalk, makeCarMesh, makeLabel, collide } from './world3d.js';

const DATA = window.FFTS_DATA;
const SAVE_KEY = 'ffts.save.v1';
const $ = (id) => document.getElementById(id);
const store = {
  get() { try { return JSON.parse(localStorage.getItem(SAVE_KEY)); } catch { return null; } },
  set(v) { try { localStorage.setItem(SAVE_KEY, JSON.stringify(v)); return true; } catch { return false; } },
  clear() { try { localStorage.removeItem(SAVE_KEY); } catch {} }
};

// ------------------------------------------------------------------ renderer & scene
const view = $('view');
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2)); renderer.shadowMap.enabled = true; renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
view.appendChild(renderer.domElement);
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(60, 1, 0.1, 900);
const hemi = new THREE.HemisphereLight(0xcfe3ff, 0x3a3326, 0.9); scene.add(hemi);
const sun = new THREE.DirectionalLight(0xfff0d6, 1.6); sun.castShadow = true; sun.shadow.mapSize.set(1024, 1024);
Object.assign(sun.shadow.camera, { left: -40, right: 40, top: 40, bottom: -40, near: 1, far: 200 }); scene.add(sun, sun.target);
scene.fog = new THREE.Fog(0x9fb7c9, 120, 420);
const { solids } = buildWorld(scene, DATA.layout);
function resize() { const w = view.clientWidth, h = view.clientHeight; renderer.setSize(w, h); camera.aspect = w / h; camera.updateProjectionMatrix(); }
addEventListener('resize', resize); resize();

const input = new Input(renderer.domElement);
let game = null;

// ------------------------------------------------------------------ actors
const player = makePerson('#c8102e'); scene.add(player);
const playerState = { x: DATA.layout.SpawnX, z: DATA.layout.SpawnZ, heading: 0, yaw: 0, pitch: 0.32, inside: new Set() };
const npcViews = new Map();
for (const p of DATA.npcs) {
  const g = new THREE.Group(); const body = makePerson(p.Color); g.add(body);
  const car = makeCarMesh(p.Color); car.visible = false; g.add(car);
  const label = makeLabel(p.DisplayName, { size: 40 }); label.position.y = 2.4; g.add(label);
  scene.add(g); npcViews.set(p.NpcId, { g, body, car, label, last: '', hidden: false, speed: 0 });
}

// Interaction markers for layout interactions, garages, race starts.
const markers = [];
const markerMat = new THREE.MeshBasicMaterial({ color: 0xf2c14e });
for (const l of DATA.layout.Locations) {
  for (const ia of l.Interactions || []) {
    const m = new THREE.Mesh(new THREE.OctahedronGeometry(0.35), markerMat); m.position.set(l.X + ia.OffsetX, 1, l.Z + ia.OffsetZ); scene.add(m);
    markers.push({ kind: 'interaction', ia, mesh: m, x: m.position.x, z: m.position.z, prompt: ia.Prompt });
  }
}
for (const g of DATA.vehicles.Garages) {
  const l = DATA.layout.Locations.find((x) => x.Id === g.LocationId);
  const m = new THREE.Mesh(new THREE.CylinderGeometry(0.4, 0.4, 1.2, 12), new THREE.MeshBasicMaterial({ color: 0x4cc9f0 }));
  m.position.set(g.SpawnX + 4, 0.6, g.SpawnZ); scene.add(m);
  const lab = makeLabel(g.DisplayName, { size: 34, color: '#bfefff' }); lab.position.set(g.SpawnX + 4, 2.2, g.SpawnZ); scene.add(lab);
  const pad = new THREE.Mesh(new THREE.PlaneGeometry(3.2, 5.6), new THREE.MeshBasicMaterial({ color: 0x4cc9f0, transparent: true, opacity: 0.25 }));
  pad.rotation.x = -Math.PI / 2; pad.rotation.z = -g.SpawnHeadingDeg * Math.PI / 180; pad.position.set(g.SpawnX, 0.04, g.SpawnZ); scene.add(pad);
  markers.push({ kind: 'garage', garage: g, mesh: m, x: m.position.x, z: m.position.z, prompt: 'Open ' + g.DisplayName });
}
for (const r of DATA.races) {
  const l = DATA.layout.Locations.find((x) => x.Id === r.LocationId);
  const flag = makeLabel('RACE · ' + r.DisplayName + ' · drive here', { size: 40, color: '#ffd6dc', bg: 'rgba(120,10,30,0.8)' });
  flag.position.set(l.X, 4, l.Z); scene.add(flag);
}
// Signs fade out when the camera is very close or far away, so they never block the view.
const labels = [];
const npcLabels = new Set([...npcViews.values()].map((v) => v.label));
scene.traverse((o) => { if (o.isSprite && !npcLabels.has(o)) labels.push({ s: o, near: 14, far: 170 }); });
for (const v of npcViews.values()) labels.push({ s: v.label, near: 3.5, far: 45 });
function updateLabels() {
  const p = new THREE.Vector3();
  for (const l of labels) {
    l.s.getWorldPosition(p); const d = p.distanceTo(camera.position);
    const a = Math.min(1, (d - l.near) / 3) * Math.min(1, (l.far - d) / 20);
    l.s.material.opacity = Math.max(0, a); l.s.visible = a > 0.02;
  }
}
const objectiveArrow = new THREE.Mesh(new THREE.ConeGeometry(0.6, 1.4, 4), new THREE.MeshBasicMaterial({ color: 0xffd166 }));
objectiveArrow.rotation.x = Math.PI; scene.add(objectiveArrow);
const checkpointRing = new THREE.Mesh(new THREE.TorusGeometry(6, 0.35, 8, 40), new THREE.MeshBasicMaterial({ color: 0xffd166 }));
checkpointRing.visible = false; scene.add(checkpointRing);

// ------------------------------------------------------------------ modes
let mode = 'title';         // title | explore | fight | drive
let overlay = null;         // dialogue | garage | menu | null
let fight = null, drive = null, race = null, parked = null;
let banner = { text: '', until: 0 };
let current = null;         // current interaction candidate
let lastLog = 0, autosaveAt = 0, hudAt = 0, wantSave = false;
const clockRunning = () => !overlay && (mode === 'explore' || mode === 'drive') && !race;

function toast(text) {
  const el = document.createElement('div'); el.className = 'toast'; el.textContent = text; $('toasts').prepend(el);
  setTimeout(() => el.classList.add('out'), 3200); setTimeout(() => el.remove(), 3800);
  while ($('toasts').children.length > 4) $('toasts').lastChild.remove();
}
function showBanner(text, seconds = 1.2) { banner = { text, until: performance.now() + seconds * 1000 }; $('banner').textContent = text; $('banner').hidden = false; }

// ------------------------------------------------------------------ start / save
function startGame(fromSave) {
  game = new Game(DATA);
  const s = fromSave ? store.get() : null;
  if (s && game.restore(s.game)) {
    Object.assign(playerState, { x: s.pos.x, z: s.pos.z, heading: s.pos.heading || 0, yaw: s.pos.heading || 0 });
  } else { Object.assign(playerState, { x: DATA.layout.SpawnX, z: DATA.layout.SpawnZ, heading: 0, yaw: 0 }); }
  playerState.inside.clear();
  lastLog = game.missions.log.length;
  spawnParkedCar();
  mode = 'explore'; overlay = null; $('title').hidden = true; setPads();
  for (const [, v] of npcViews) { const n = game.world.npcs.get(v.g.userData.id); }
  for (const [id, v] of npcViews) { const n = game.world.npcs.get(id); v.g.position.set(n.x, 0, n.z); }
  if (!fromSave) toast('Welcome home. Walk up Magnolia Ave to Grandma\'s house.');
  autosaveAt = performance.now() + 60000;
}
function save(silent) {
  if (!game || fight || race) { if (!silent) toast('Finish what you\'re doing first.'); return; }
  if (drive) game.recordDrive(drive.rec, drive.car, drive.car.distance - drive.recorded), (drive.recorded = drive.car.distance);
  const ok = store.set({ game: game.capture(), pos: { x: playerState.x, z: playerState.z, heading: playerState.heading } });
  if (!silent) toast(ok ? 'Game saved on this device.' : 'Couldn\'t save: this browser is blocking storage.');
}
addEventListener('visibilitychange', () => { if (document.hidden && mode !== 'title') save(true); });

// ------------------------------------------------------------------ objective helper
function objectiveTarget() {
  const id = game.missions.tracked; if (!id) return null;
  const d = game.missions.def(id), p = game.missions.prog.get(id);
  for (const o of d.Objectives) {
    if (o.Optional || p.done.includes(o.Id) || (o.Branch && o.Branch !== p.branch)) continue;
    const t = o.Trigger;
    if (t.startsWith('location.')) { const l = DATA.layout.Locations.find((x) => x.Id === t); if (l) return { x: l.X, z: l.Z, text: o.Text }; }
    if (t.startsWith('interact.')) { for (const m of markers) if (m.kind === 'interaction' && m.ia.Trigger === t) return { x: m.x, z: m.z, text: o.Text }; }
    if (t.startsWith('fight.won.')) { const n = game.world.npcs.get('npc.' + t.split('.').pop()); if (n) return { x: n.x, z: n.z, text: o.Text }; }
    if (t.startsWith('race.')) { const r = DATA.races.find((x) => t.endsWith(x.Id)); const l = r && DATA.layout.Locations.find((x) => x.Id === r.LocationId); if (l) return { x: l.X, z: l.Z, text: o.Text }; }
    if (t.startsWith('dialogue.')) {
      const dl = DATA.dialogues.find((x) => x.EndTrigger === t || x.Nodes.some((n) => n.Options.some((op) => op.Trigger === t)));
      const n = dl && game.world.npcs.get(dl.NpcId); if (n) return { x: n.x, z: n.z, text: o.Text };
    }
    return { x: null, z: null, text: o.Text };
  }
  return null;
}

// ------------------------------------------------------------------ explore
function markerAvailable(m) {
  if (m.kind === 'garage') return true;
  if (m.kind === 'interaction') return m.ia.Verb === 'look' || m.ia.Verb === 'rest' || game.missions.wants(m.ia.Trigger);
  return true;
}
function updateExplore(dt) {
  const ax = input.axis();
  const moving = Math.hypot(ax.x, ax.y) > 0.08;
  if (!overlay && moving) {
    const sprint = input.down('shift') || Math.hypot(ax.x, ax.y) > 0.97;
    const speed = sprint ? 7 : 4.5, yaw = playerState.yaw;
    const fx = Math.sin(yaw), fz = Math.cos(yaw);
    const mx = fx * ax.y - fz * ax.x, mz = fz * ax.y + fx * ax.x;
    playerState.x += mx * speed * dt; playerState.z += mz * speed * dt;
    playerState.heading = Math.atan2(mx, mz);
    collide(solids, playerState, 0.45);
    if (parked) collide([{ x: parked.mesh.position.x, z: parked.mesh.position.z, hw: 1.0, hd: 1.0 }], playerState, 1.6);
    poseWalk(player, sprint ? 7 : 4.5, dt);
    if (input.look.dx === 0) playerState.yaw += angleDiff(playerState.heading, playerState.yaw) * Math.min(1, dt * 1.2) * Math.max(0, ax.y);
  } else poseWalk(player, 0, dt);
  player.position.set(playerState.x, 0, playerState.z); player.rotation.y = playerState.heading; player.visible = true;

  // Location enter triggers
  for (const l of DATA.layout.Locations) {
    const inside = Math.abs(playerState.x - l.X) <= l.Width / 2 + 4 && Math.abs(playerState.z - l.Z) <= l.Depth / 2 + 4;
    if (inside && !playerState.inside.has(l.Id)) { playerState.inside.add(l.Id); if (l.EnterTrigger) game.trigger(l.EnterTrigger); $('place').textContent = l.DisplayName; }
    else if (!inside && playerState.inside.has(l.Id)) playerState.inside.delete(l.Id);
  }

  // Interaction candidate
  current = null; let best = Infinity;
  const consider = (c, x, z, r) => { const d = Math.hypot(playerState.x - x, playerState.z - z); if (d <= r && d < best) { best = d; current = c; } };
  for (const [id, n] of game.world.npcs) {
    const prof = game.world.profiles.get(id);
    if (n.status === 'Traveling' && prof.Travel === 'Drive') continue;
    if (npcViews.get(id).indoors) continue;
    consider({ kind: 'npc', id, prompt: 'Talk to ' + prof.DisplayName }, n.x, n.z, 2.6);
  }
  for (const m of markers) if (markerAvailable(m)) consider(m, m.x, m.z, 2.6);
  if (parked && parked.rec.state === 'Out') consider({ kind: 'car', prompt: 'Drive ' + game.vdef(parked.rec.def).DisplayName }, parked.mesh.position.x, parked.mesh.position.z, 3.6);
  if (!overlay && current && input.hit('e', 'act')) activate(current);
}
const angleDiff = (a, b) => { let d = a - b; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return d; };

function activate(c) {
  if (c.kind === 'npc') return talkTo(c.id);
  if (c.kind === 'garage') return openGarage(c.garage);
  if (c.kind === 'car') return enterCar(parked.rec);
  if (c.kind === 'interaction') {
    const ia = c.ia;
    if (ia.Verb === 'rest') { game.sleepUntilMorning(); toast('You slept until morning. ' + game.clockText); wantSave = true; }
    else if (ia.Verb === 'look') toast('An old photo: two boys on a porch on Pecan Street. The bigger one\'s face has worn away.');
    game.trigger(ia.Trigger);
  }
}

// ------------------------------------------------------------------ dialogue
let runner = null, talkingTo = null;
function talkTo(npcId) {
  const r = game.talkTo(npcId), name = game.npcName(npcId);
  if (!r) return toast(name + ' has nothing to say.');
  if (r.Bark) return toast(r.Bark);
  if (r.FightId) return startFight(r.FightId, r.Venue);
  runner = game.startDialogue(r.DialogueId); talkingTo = npcId; game.world.hold(npcId, true);
  overlay = 'dialogue'; renderDialogue();
}
function renderDialogue() {
  if (!runner || !runner.current) return closeDialogue();
  $('dlg-speaker').textContent = runner.speaker; $('dlg-line').textContent = runner.line;
  const box = $('dlg-options'); box.innerHTML = '';
  runner.options.forEach((o, i) => {
    const b = document.createElement('button'); b.className = 'opt';
    b.innerHTML = `<span class="n">${i + 1}</span><span class="t"></span>${o.Tone && o.Tone !== 'Neutral' ? `<span class="tone">${o.Tone}</span>` : ''}`;
    b.querySelector('.t').textContent = o.Text; b.onclick = () => chooseOption(o.Id); box.appendChild(b);
  });
  $('dialogue').hidden = false;
}
function chooseOption(id) {
  const out = runner.choose(id); if (!out) return;
  if (out.ended || !runner.current) { closeDialogue(); if (out.fight) startFight(out.fight, null); }
  else renderDialogue();
}
function closeDialogue() { $('dialogue').hidden = true; if (talkingTo) game.world.hold(talkingTo, false); runner = null; talkingTo = null; overlay = null; }

// ------------------------------------------------------------------ fights
const FIGHT_KEYS = { j: C.Light, k: C.Heavy, i: C.Kick, l: C.Grapple, shift: C.Dodge, o: C.Counter, u: C.Finisher, t: C.Taunt };
function venueAt(x, z) {
  for (const l of DATA.layout.Locations) if (Math.abs(x - l.X) <= l.Width / 2 + 8 && Math.abs(z - l.Z) <= l.Depth / 2 + 8) return l.Type === 'venue' ? 'Underground' : l.Type === 'gym' ? 'Sanctioned' : 'Street';
  return 'Street';
}
function startFight(fighterId, venue) {
  const npcId = 'npc.' + fighterId.replace('fighter.', ''), n = game.world.npcs.get(npcId);
  const seed = game.minutes * 31 + game.fightCount;
  const { sim, ai } = createFight(DATA, 'fighter.player', fighterId, seed);
  let ax = Math.sin(playerState.heading), az = Math.cos(playerState.heading);
  if (n && Math.hypot(n.x - playerState.x, n.z - playerState.z) < 8) { const dx = n.x - playerState.x, dz = n.z - playerState.z, d = Math.hypot(dx, dz) || 1; ax = dx / d; az = dz / d; }
  const origin = { x: playerState.x + ax * 1.1, z: playerState.z + az * 1.1 };
  const opp = makePerson(game.world.profiles.get(npcId)?.Color || '#888'); scene.add(opp);
  const loc = [...playerState.inside][0] || null;
  fight = { sim, ai, origin, axis: { x: ax, z: az }, opp, npcId, fighterId, venue: venue || venueAt(playerState.x, playerState.z), loc, acc: 0, latched: new Set(), endAt: 0,
    witnesses: game.world.near(playerState.x, playerState.z, 25).filter((id) => id !== npcId) };
  if (npcViews.get(npcId)) npcViews.get(npcId).hidden = true;
  mode = 'fight'; setPads(); showBanner('FIGHT!', 1); toast(`${game.npcName(npcId)} · ${fight.venue === 'Underground' ? 'underground fight' : fight.venue === 'Sanctioned' ? 'sparring' : 'street fight'}`);
}
function updateFight(dt) {
  const f = fight, sim = f.sim;
  for (const k of Object.keys(FIGHT_KEYS)) if (input.hit(k)) f.latched.add(FIGHT_KEYS[k]);
  if (sim.isOver) {
    if (!f.endAt) { f.endAt = performance.now() + 2600; const won = sim.result.winnerId === 'fighter.player'; showBanner(won ? 'YOU WIN' : sim.result.method === 'Draw' ? 'DRAW' : 'DEFEATED', 2.4); }
    else if (performance.now() > f.endAt) return endFight();
  } else {
    f.acc += Math.min(dt, 0.1);
    while (f.acc >= 1 / 60 && !sim.isOver) {
      f.acc -= 1 / 60;
      const order = [C.Finisher, C.Counter, C.Dodge, C.Grapple, C.Heavy, C.Kick, C.Light, C.Taunt];
      const cmd = order.find((c) => f.latched.has(c)) || C.None; f.latched.clear();
      const toward = Math.sign(sim.B.x - sim.A.x) || 1;
      const ax = input.axis();
      sim.step({ move: ax.x * toward, block: input.down(' ', 'block'), command: cmd }, f.ai.think(sim));
      for (const e of sim.events) fightEvent(e);
    }
  }
  const pa = { x: f.origin.x + f.axis.x * sim.A.x, z: f.origin.z + f.axis.z * sim.A.x }, pb = { x: f.origin.x + f.axis.x * sim.B.x, z: f.origin.z + f.axis.z * sim.B.x };
  poseFighter(player, sim.A, pa, pb, dt); poseFighter(f.opp, sim.B, pb, pa, dt);
  playerState.x = pa.x; playerState.z = pa.z;
}
function fightEvent(e) {
  const me = e.actor === 'fighter.player';
  if (e.type === 'Countered') showBanner(e.detail === 'parry' ? (me ? 'PARRY!' : 'PARRIED') : (me ? 'COUNTER!' : 'COUNTERED'), 0.8);
  if (e.type === 'Thrown') showBanner(e.detail === 'environment' ? 'WALL SLAM!' : 'THROWN', 0.8);
  if (e.type === 'GuardBreak') showBanner('GUARD BROKEN', 0.8);
  if (e.type === 'FinisherStarted') showBanner('SOUTHSIDE SEND-OFF', 1.4);
  if (e.type === 'Breakaway') showBanner('BREAKAWAY', 0.8);
  if (e.type === 'Reversal') showBanner('REVERSAL!', 0.8);
  if (e.type === 'FinisherDenied' && me) $('fight-tip').textContent = { NotEnoughMomentum: 'Finisher needs a full meter', OpponentNotVulnerable: 'Wear them down first (under 35% or stunned)', OutOfRange: 'Get closer', AttackerBusy: 'Recover first', OpponentStateInvalid: 'Wait for an opening' }[e.detail] || '';
  if (e.type === 'Blocked' && e.target === 'fighter.player') game.trigger('combat.tutorial.blocked');
  if (e.type === 'Hit' && me && e.detail && e.detail.startsWith('combo ') && +e.detail.slice(6) >= 3) game.trigger('combat.tutorial.combo');
  if (e.type === 'Thrown' && me) game.trigger('combat.tutorial.thrown');
  if ((e.type === 'Hit' || e.type === 'Countered' || e.type === 'ClinchStrike') && e.target) { const m = e.target === 'fighter.player' ? player : fight.opp; m.userData.flash = 0.12; }
}
function poseFighter(m, f, at, toward, dt) {
  const u = m.userData; m.position.set(at.x, 0, at.z); m.rotation.y = Math.atan2(toward.x - at.x, toward.z - at.z);
  for (const k of ['armL', 'armR', 'legL', 'legR']) { u[k].rotation.set(0, 0, 0); }
  m.rotation.x = 0; m.position.y = 0; u.torso.rotation.x = 0;
  const st = f.state, mv = f.move;
  if (st === S.Moving) poseWalk(m, 3, dt);
  if (st === S.Idle || st === S.Moving) { u.armL.rotation.x = -1.1; u.armR.rotation.x = -1.1; u.armL.rotation.z = 0.35; u.armR.rotation.z = -0.35; }
  if (st === S.Blocking || st === S.BlockStun) { u.armL.rotation.x = -2.6; u.armR.rotation.x = -2.6; u.armL.rotation.z = 0.5; u.armR.rotation.z = -0.5; }
  if (st === S.Countering) { u.armL.rotation.x = -2.2; u.armR.rotation.x = -1.4; u.torso.rotation.x = -0.2; }
  if ((st === S.Attacking || st === S.Combo || st === S.Finisher) && mv) {
    const active = f.inActive || (f.inRecovery && f.moveConnected && f.moveTick < f.moveStartup + mv.Active + 4);
    const reach = active ? 1 : f.inStartup ? 0.35 : 0.2;
    if (mv.Kind === 'Kick') { u.legR.rotation.x = -1.6 * reach; u.armL.rotation.x = -1.1; }
    else if (mv.Kind === 'Grapple') { u.armL.rotation.x = -1.6 * reach - 0.2; u.armR.rotation.x = -1.6 * reach - 0.2; }
    else if (mv.Kind === 'Taunt') { u.armL.rotation.x = -3; u.armR.rotation.x = -3; }
    else if (mv.Kind === 'GroundAttack') { u.legR.rotation.x = -0.9 * reach; }
    else { const arm = mv.Id.includes('jab') || mv.Id.includes('uppercut') ? u.armL : u.armR; arm.rotation.x = -1.57 * (0.6 + reach * 0.5); u.torso.rotation.x = 0.15 * reach; }
    if (st === S.Finisher) { u.armR.rotation.x = -2.9; u.torso.rotation.x = 0.35; }
  }
  if (st === S.Grappling || st === S.Throwing) { u.armL.rotation.x = -1.5; u.armR.rotation.x = -1.5; if (st === S.Throwing) m.rotation.z = Math.sin(f.moveTick * 0.2) * 0.4; }
  else m.rotation.z = 0;
  if (st === S.Grappled || st === S.FinisherVictim) { u.torso.rotation.x = -0.3; u.armL.rotation.x = -0.4; u.armR.rotation.x = -0.4; }
  if (st === S.HitStun || st === S.Stunned) { u.torso.rotation.x = -0.35; m.position.y = 0; }
  if (st === S.Dodging) m.position.y = 0, (u.torso.rotation.x = -0.5);
  if (st === S.KnockedDown || st === S.Defeated) { m.rotation.x = -Math.PI / 2; m.position.y = 0.3; }
  if (st === S.GettingUp) { m.rotation.x = -Math.PI / 2 * (1 - f.stateTicks / Math.max(1, f.stateDuration)); }
  if (u.flash > 0) { u.flash -= dt; m.traverse((o) => { if (o.isMesh) o.material.emissive?.setHex(0x661111); }); }
  else m.traverse((o) => { if (o.isMesh) o.material.emissive?.setHex(0x000000); });
}
function endFight() {
  const f = fight, won = game.onFight(f.sim.result, f.fighterId, f.venue, f.loc, f.witnesses);
  toast(won ? `You beat ${game.npcName(f.npcId)}.` : `${game.npcName(f.npcId)} got the better of you.`);
  scene.remove(f.opp); if (npcViews.get(f.npcId)) npcViews.get(f.npcId).hidden = false;
  player.rotation.set(0, playerState.heading, 0); player.position.y = 0;
  fight = null; mode = 'explore'; setPads(); $('fight-tip').textContent = '';
}

// ------------------------------------------------------------------ driving & racing
function spawnParkedCar() {
  if (parked) { scene.remove(parked.mesh); parked = null; }
  const id = game.garage.active, rec = id && game.garage.cars.find((c) => c.id === id);
  if (!rec || (rec.state !== 'Out' && rec.state !== 'Wrecked')) return;
  const mesh = makeCarMesh(rec.color); mesh.position.set(rec.x, 0, rec.z); mesh.rotation.y = rec.heading * Math.PI / 180; scene.add(mesh);
  parked = { rec, mesh };
}
function enterCar(rec) {
  if (rec.state === 'Wrecked') return toast('It\'s wrecked. Tow it from a garage and repair it.');
  if (!parked || parked.rec !== rec) { spawnParkedCar(); }
  const car = game.makeCar(rec);
  drive = { rec, car, mesh: parked.mesh, acc: 0, recorded: 0, view: 0 };
  parked = null; mode = 'drive'; player.visible = false; setPads();
  toast(`Driving the ${game.vdef(rec.def).DisplayName}. ${input.touch ? 'Use the pedals.' : 'W/S, A/D, Space handbrake, Shift nitrous, C camera, E exit.'}`);
}
function exitCar() {
  const d = drive; game.recordDrive(d.rec, d.car, d.car.distance - d.recorded);
  const h = d.car.heading * Math.PI / 180;
  playerState.x = d.car.x - Math.cos(h) * 2.4; playerState.z = d.car.z + Math.sin(h) * 2.4; collide(solids, playerState, 0.45);
  parked = { rec: d.rec, mesh: d.mesh }; drive = null; mode = 'explore'; setPads();
}
function nearGarageSpot() {
  for (const g of DATA.vehicles.Garages) if (game.ownsGarage(g.Id) && Math.hypot(drive.car.x - g.SpawnX, drive.car.z - g.SpawnZ) < 14) return g;
  return null;
}
function raceStartNear() {
  for (const r of DATA.races) { const l = DATA.layout.Locations.find((x) => x.Id === r.LocationId); if (Math.hypot(drive.car.x - l.X, drive.car.z - l.Z) < Math.max(l.Width, l.Depth) / 2 + 12) return r; }
  return null;
}
function driveInput() {
  const ax = input.axis();
  return {
    throttle: input.down('gas') ? 1 : Math.max(0, ax.y), brake: input.down('brake') ? 1 : Math.max(0, -ax.y),
    steer: (input.down('left') ? -1 : 0) + (input.down('right') ? 1 : 0) + ax.x,
    handbrake: input.down(' ', 'hand'), nitrous: input.down('shift', 'nos'), surface: surfaceAt(DATA.layout, drive.car.x, drive.car.z)
  };
}
function carCollisions(car, mesh) {
  const pos = { x: car.x, z: car.z }; const n = collide(solids, pos, 1.8);
  if (n) {
    const t = (car.heading - car.drift) * Math.PI / 180, vx = Math.sin(t) * car.speed, vz = Math.cos(t) * car.speed;
    const closing = Math.max(0, -(vx * n.x + vz * n.z));
    const dmg = car.impact(closing); car.x = pos.x; car.z = pos.z;
    if (dmg > 8) toast(`Crash! Damage ${Math.round(car.damage)}%`);
    return dmg;
  }
  return 0;
}
function updateDrive(dt) {
  const d = drive, car = d.car;
  if (input.hit('c', 'cam')) d.view = (d.view + 1) % 2;
  if (race) return updateRace(dt);
  d.acc += Math.min(dt, 0.1);
  const inp = driveInput();
  while (d.acc >= 1 / 60) { d.acc -= 1 / 60; car.step(inp, 1 / 60); carCollisions(car, d.mesh); }
  placeCar(d.mesh, car, dt);
  playerState.x = car.x; playerState.z = car.z;
  if (car.wrecked) { toast('Your car is wrecked. Tow it from a garage and repair it.'); exitCar(); return; }
  // Context action
  const slow = Math.abs(car.speed) < 2, garage = nearGarageSpot(), r = raceStartNear();
  d.action = r ? { label: `Start ${r.DisplayName} (${Bank.fmt(r.EntryFeeCents)})`, run: () => startRace(r) }
    : garage ? { label: 'Park at ' + garage.DisplayName, run: () => { game.recordDrive(d.rec, car, car.distance - d.recorded); game.store(d.rec, garage.Id); scene.remove(d.mesh); drive = null; mode = 'explore'; playerState.x = garage.SpawnX + 4; playerState.z = garage.SpawnZ + 3; setPads(); toast('Parked.'); wantSave = true; } }
    : slow ? { label: 'Get out', run: exitCar } : null;
  if (d.action && slow && input.hit('e', 'act')) d.action.run();
  for (const l of DATA.layout.Locations) {
    const inside = Math.abs(car.x - l.X) <= l.Width / 2 + 4 && Math.abs(car.z - l.Z) <= l.Depth / 2 + 4;
    if (inside && !playerState.inside.has(l.Id)) { playerState.inside.add(l.Id); if (l.EnterTrigger) game.trigger(l.EnterTrigger); $('place').textContent = l.DisplayName; }
    else if (!inside) playerState.inside.delete(l.Id);
  }
}
function placeCar(mesh, car, dt) {
  mesh.position.set(car.x, 0, car.z); mesh.rotation.y = car.heading * Math.PI / 180;
  for (const w of mesh.userData.wheels) w.rotation.x += car.speed * dt / 0.36;
}
function startRace(def) {
  const r = game.beginRace(def.Id, drive.car);
  if (r.error) return toast(r.error);
  race = { r: r.race, acc: 0, meshes: new Map(), doneAt: 0 };
  for (const x of r.race.racers) {
    if (x.isPlayer) continue;
    const mesh = makeCarMesh(x.id === 'racer.rell' ? '#2ec4b6' : '#8d99ae'); const lab = makeLabel(x.name, { size: 36 }); lab.position.y = 2.6; mesh.add(lab); labels.push({ s: lab, near: 4, far: 120 });
    scene.add(mesh); race.meshes.set(x.id, mesh);
  }
  checkpointRing.visible = true; toast(def.DisplayName + ' — get ready');
}
function updateRace(dt) {
  const R = race.r, car = drive.car;
  race.acc += Math.min(dt, 0.1);
  const inp = driveInput();
  while (race.acc >= 1 / 60 && !R.over) {
    race.acc -= 1 / 60; R.step(inp, 1 / 60);
    if (carCollisions(car, drive.mesh) > 0) R.player.impacts++;
    for (const e of R.events) {
      if (e.type === 'Countdown') showBanner(String(e.value), 0.9);
      if (e.type === 'Go') showBanner('GO!', 0.8);
      if (e.type === 'Checkpoint' && e.racer === 'racer.player') toast(`Checkpoint ${e.value + 1}/${R.def.Checkpoints.length}`);
      if (e.type === 'Finished' && e.racer === 'racer.player') showBanner(ordinal(e.value) + ' PLACE', 2.5);
      if (e.type === 'TimeExpired') showBanner('TIME UP', 2);
    }
  }
  placeCar(drive.mesh, car, dt);
  for (const x of R.racers) if (!x.isPlayer) placeCar(race.meshes.get(x.id), x.car, dt);
  playerState.x = car.x; playerState.z = car.z;
  const cp = R.def.Checkpoints[Math.min(R.player.next, R.def.Checkpoints.length - 1)];
  checkpointRing.position.set(cp.X, 3, cp.Z); checkpointRing.lookAt(car.x, 3, car.z);
  if (R.over && !race.doneAt) {
    race.doneAt = performance.now() + 3000;
    const o = game.resolveRace(R);
    toast(o.dnf ? 'Did not finish.' : `Finished ${ordinal(o.place)}${o.purse + o.bonus ? ' · won ' + Bank.fmt(o.purse + o.bonus) : ''} · +${o.points} racing points`);
    if (o.rankAfter !== o.rankBefore) toast('Racing rank: ' + RANKS[o.rankAfter]);
    wantSave = true;
  }
  if (race.doneAt && performance.now() > race.doneAt) { for (const m of race.meshes.values()) scene.remove(m); checkpointRing.visible = false; race = null; }
}
const ordinal = (n) => n + (n % 100 >= 11 && n % 100 <= 13 ? 'th' : ['th', 'st', 'nd', 'rd'][n % 10] || 'th');

// ------------------------------------------------------------------ garage sheet
let garageOpen = null, garageVisit = 0;
function openGarage(g) { garageOpen = g; garageVisit++; overlay = 'garage'; renderGarage(); $('garage').hidden = false; }
function closeGarage() { $('garage').hidden = true; garageOpen = null; overlay = null; }
function renderGarage() {
  const g = garageOpen, box = $('garage-body'); box.innerHTML = '';
  $('garage-title').textContent = g.DisplayName;
  $('garage-money').textContent = 'Cash ' + Bank.fmt(game.bank.accounts.cash);
  const row = (html, buttons) => {
    const r = document.createElement('div'); r.className = 'grow'; r.innerHTML = `<div class="ginfo">${html}</div><div class="gbtns"></div>`;
    for (const [label, fn, disabled] of buttons) { const b = document.createElement('button'); b.textContent = label; b.disabled = !!disabled; b.onclick = () => { const res = fn(); if (res && res !== 'Ok') toast(res); renderGarage(); }; r.querySelector('.gbtns').appendChild(b); }
    box.appendChild(r);
  };
  if (g.ForSale.length) {
    box.insertAdjacentHTML('beforeend', '<h3>For sale</h3>');
    for (const id of g.ForSale) {
      const d = game.vdef(id);
      row(`<b>${d.Make} ${d.DisplayName}</b><span>${d.Class.replace('LuxurySedan', 'Luxury sedan')} · ${d.MaxSpeedKph} km/h · ${d.Drivetrain}</span>`, [[`Buy ${Bank.fmt(d.BaseValueCents)}`, () => game.buyCar(id, g.Id, `visit${garageVisit}.${id}`)]]);
    }
  }
  box.insertAdjacentHTML('beforeend', '<h3>Your cars</h3>');
  if (!game.garage.cars.length) box.insertAdjacentHTML('beforeend', '<p class="muted">No cars yet. The Magnolia Cruiser is $750 at Southside Motors.</p>');
  for (const c of game.garage.cars) {
    const d = game.vdef(c.def), here = c.garage === g.Id, where = c.state === 'Stored' ? 'at ' + game.gdef(c.garage).DisplayName : c.state.toLowerCase();
    const btns = [];
    if (c.state === 'Stored' && here) btns.push(['Drive', () => { closeGarage(); const rec = game.retrieve(c.id); spawnParkedCar(); enterCar(rec); return 'Ok'; }]);
    if (c.damage > 0.01 && c.state !== 'Out') btns.push([`Repair ${Bank.fmt(game.repairCost(c))}`, () => game.repair(c)]);
    if ((c.state === 'Out' || c.state === 'Wrecked') && !(drive && drive.rec === c)) btns.push(['Tow here $150', () => { const r = game.tow(c); if (r === 'Ok') spawnParkedCar(); return r; }]);
    row(`<b>${d.DisplayName}</b><span>${where} · damage ${Math.round(c.damage)}% · ${c.km.toFixed(1)} km</span>`, btns);
    if (c.state === 'Stored' && here) {
      for (const u of DATA.vehicles.Upgrades.filter((u) => d.UpgradeSlots.includes(u.Slot)))
        row(`<span class="up">${u.DisplayName}</span>`, [[c.upgrades.includes(u.Id) ? 'Installed' : Bank.fmt(u.PriceCents), () => game.upgrade(c, u.Id), c.upgrades.includes(u.Id)]]);
    }
  }
  box.insertAdjacentHTML('beforeend', '<h3>Handling</h3>');
  row(`<span>Current: <b>${game.garage.preset}</b></span>`, ['Arcade', 'Street', 'Expert'].map((p) => [p, () => { game.garage.preset = p; return 'Ok'; }, game.garage.preset === p]));
}

// ------------------------------------------------------------------ menu
function openMenu() {
  if (mode === 'title') return;
  overlay = 'menu'; $('menu').hidden = false; $('confirm-new').hidden = true;
  const r = game.rep;
  $('menu-stats').innerHTML = `<div><b>${Bank.fmt(game.bank.total)}</b><span>money</span></div><div><b>${r.face.Respect}</b><span>respect</span></div><div><b>${r.face.Fear}</b><span>fear</span></div><div><b>${r.image.Popularity}</b><span>popularity</span></div><div><b>${RANKS[game.racingRank]}</b><span>racing rank</span></div><div><b>Ch. ${game.story.chapter}</b><span>story</span></div>`;
}
function closeMenu() { $('menu').hidden = true; overlay = null; }

// ------------------------------------------------------------------ HUD
function setPads() {
  $('fight-pad').hidden = mode !== 'fight'; $('drive-pad').hidden = mode !== 'drive'; $('act').hidden = mode !== 'explore' && mode !== 'drive';
  $('fight-hud').hidden = mode !== 'fight'; $('drive-hud').hidden = mode !== 'drive'; $('mission').hidden = mode === 'fight';
  document.body.classList.toggle('touch', input.touch);
}
const minimap = $('minimap').getContext('2d');
function drawMinimap(target) {
  const c = minimap, W = 132, s = 0.42, px = playerState.x, pz = playerState.z;
  c.clearRect(0, 0, W, W); c.save(); c.beginPath(); c.arc(W / 2, W / 2, W / 2 - 2, 0, Math.PI * 2); c.clip();
  c.fillStyle = 'rgba(16,18,22,0.85)'; c.fillRect(0, 0, W, W);
  const tx = (x) => W / 2 + (x - px) * s, tz = (z) => W / 2 - (z - pz) * s;
  c.strokeStyle = '#5b5e66'; c.lineCap = 'round';
  for (const r of DATA.layout.Roads) { c.lineWidth = r.Width * s; c.beginPath(); c.moveTo(tx(r.X1), tz(r.Z1)); c.lineTo(tx(r.X2), tz(r.Z2)); c.stroke(); }
  c.fillStyle = '#3a3d44'; for (const l of DATA.layout.Locations) c.fillRect(tx(l.X - l.Width / 2), tz(l.Z + l.Depth / 2), l.Width * s, l.Depth * s);
  for (const n of game.world.npcs.values()) { c.fillStyle = '#9aa0aa'; c.fillRect(tx(n.x) - 1.5, tz(n.z) - 1.5, 3, 3); }
  if (target && target.x != null) {
    let ox = tx(target.x), oz = tz(target.z); const dx = ox - W / 2, dz = oz - W / 2, d = Math.hypot(dx, dz), lim = W / 2 - 8;
    if (d > lim) { ox = W / 2 + dx / d * lim; oz = W / 2 + dz / d * lim; }
    c.fillStyle = '#ffd166'; c.beginPath(); c.arc(ox, oz, 5, 0, Math.PI * 2); c.fill();
  }
  c.translate(W / 2, W / 2); c.rotate(-(drive ? drive.car.heading * Math.PI / 180 : playerState.heading) + Math.PI);
  c.fillStyle = '#ef233c'; c.beginPath(); c.moveTo(0, -7); c.lineTo(5, 5); c.lineTo(-5, 5); c.closePath(); c.fill();
  c.restore();
}
function updateHud(target) {
  $('clock').textContent = game.clockText; $('cash').textContent = Bank.fmt(game.bank.accounts.cash + game.bank.accounts.checking);
  $('respect').textContent = game.rep.face.Respect; $('chapter').textContent = 'Ch. ' + game.story.chapter;
  const id = game.missions.tracked;
  if (id) {
    const d = game.missions.def(id), p = game.missions.prog.get(id);
    $('mission-title').textContent = d.Title;
    const list = $('mission-list'); list.innerHTML = '';
    for (const o of d.Objectives) {
      if (o.Branch && o.Branch !== p.branch) continue;
      const li = document.createElement('li'); li.textContent = o.Text + (o.Optional ? ' (optional)' : '');
      if (p.done.includes(o.Id)) li.className = 'done'; list.appendChild(li);
    }
    const dist = target && target.x != null ? Math.round(Math.hypot(target.x - playerState.x, target.z - playerState.z)) : null;
    $('mission-dist').textContent = dist != null ? dist + ' m' : '';
  } else { $('mission-title').textContent = game.story.chapter > 1 ? 'Chapter 2 is coming' : 'Free roam'; $('mission-list').innerHTML = '<li>Explore Southside, race, fight, earn.</li>'; $('mission-dist').textContent = ''; }
  if (mode === 'fight') {
    const s = fight.sim;
    for (const [k, f] of [['a', s.A], ['b', s.B]]) {
      $('hp-' + k).style.width = (f.healthFraction * 100).toFixed(1) + '%'; $('st-' + k).style.width = (f.stamina / f.maxStamina * 100).toFixed(1) + '%';
      $('mo-' + k).style.width = f.momentum.toFixed(1) + '%';
    }
    $('name-b').textContent = game.npcName(fight.npcId);
    $('fin').classList.toggle('ready', s.canFinish(s.A) === 'None');
  }
  if (mode === 'drive') {
    const car = drive.car;
    $('speed').textContent = Math.round(Math.abs(car.kph)); $('dmg').textContent = Math.round(car.damage) + '%';
    $('nos-row').hidden = car.s.nitrous <= 0; $('nos').textContent = car.s.nitrous > 0 ? Math.round(car.nos / car.s.nitrous * 100) + '%' : '';
    $('race-row').hidden = !race;
    if (race) { const R = race.r; $('race-info').textContent = `P${R.position(R.player)}/${R.racers.length} · CP ${Math.min(R.player.next + 1, R.def.Checkpoints.length)}/${R.def.Checkpoints.length} · ${R.elapsed.toFixed(1)}s`; }
  }
  const actLabel = mode === 'drive' ? (drive && drive.action ? drive.action.label : null) : !overlay && current ? current.prompt : null;
  $('act').hidden = !actLabel; if (actLabel) $('act-label').textContent = actLabel;
}

// ------------------------------------------------------------------ camera & lighting
function updateCamera(dt) {
  if (mode === 'fight') {
    const f = fight, s = f.sim;
    const mid = { x: f.origin.x + f.axis.x * (s.A.x + s.B.x) / 2, z: f.origin.z + f.axis.z * (s.A.x + s.B.x) / 2 };
    const side = { x: f.axis.z, z: -f.axis.x }, span = Math.abs(s.A.x - s.B.x), back = Math.min(10, 4.8 + span * 0.8);
    const finisher = s.A.state === S.Finisher || s.B.state === S.Finisher;
    const want = new THREE.Vector3(mid.x - side.x * (finisher ? 2.8 : back), finisher ? 1.0 : 2.2, mid.z - side.z * (finisher ? 2.8 : back));
    camera.position.lerp(want, 1 - Math.exp(-8 * dt)); camera.lookAt(mid.x, 1.2, mid.z); return;
  }
  if (mode === 'drive') {
    const car = drive.car, h = car.heading * Math.PI / 180, fx = Math.sin(h), fz = Math.cos(h);
    if (drive.view === 1) { camera.position.set(car.x + fx * 0.4, 1.3, car.z + fz * 0.4); camera.lookAt(car.x + fx * 10, 1.1, car.z + fz * 10); return; }
    const want = new THREE.Vector3(car.x - fx * 7.5, 3, car.z - fz * 7.5);
    camera.position.lerp(want, 1 - Math.exp(-5 * dt)); camera.lookAt(car.x + fx * 3, 1, car.z + fz * 3); return;
  }
  playerState.yaw -= input.look.dx * 0.006; playerState.pitch = Math.max(0.05, Math.min(1.1, playerState.pitch + input.look.dy * 0.004));
  const dist = 6.5, y = playerState.yaw, p = playerState.pitch;
  const want = new THREE.Vector3(playerState.x - Math.sin(y) * Math.cos(p) * dist, 1.6 + Math.sin(p) * dist, playerState.z - Math.cos(y) * Math.cos(p) * dist);
  camera.position.lerp(want, 1 - Math.exp(-10 * dt)); camera.lookAt(playerState.x, 1.5, playerState.z);
}
const dayColor = new THREE.Color(0x9fc4e0), duskColor = new THREE.Color(0xe08a5b), nightColor = new THREE.Color(0x0d1424);
function updateLighting() {
  const h = game.minutes % 1440 / 60;
  const day = h >= 7 && h < 18 ? 1 : h >= 5 && h < 7 ? (h - 5) / 2 : h >= 18 && h < 20 ? 1 - (h - 18) / 2 : 0;
  const dusk = (h >= 5.5 && h < 7.5) || (h >= 17.5 && h < 19.5) ? 0.6 : 0;
  const sky = nightColor.clone().lerp(dayColor, day).lerp(duskColor, dusk * (1 - Math.abs(day - 0.5) * 2) * 0.8);
  scene.background = sky; scene.fog.color.copy(sky);
  sun.intensity = 0.25 + day * 1.4; hemi.intensity = 0.35 + day * 0.6;
  sun.position.set(playerState.x + 40, 80, playerState.z + 20); sun.target.position.set(playerState.x, 0, playerState.z);
}

// ------------------------------------------------------------------ NPC views
function updateNpcs(dt) {
  for (const [id, v] of npcViews) {
    const n = game.world.npcs.get(id), prof = game.world.profiles.get(id);
    const indoors = n.status !== 'Traveling' && n.activity === 'sleep' && id !== 'npc.grandma';
    v.g.visible = !v.hidden && !indoors; v.indoors = indoors;
    const target = new THREE.Vector3(n.x, 0, n.z), d = v.g.position.distanceTo(target);
    const sp = game.world.speed(id) * 1.5 + 0.5;
    if (d > 30) v.g.position.copy(target); else v.g.position.lerp(target, Math.min(1, sp * dt / Math.max(d, 1e-3)));
    const traveling = n.status === 'Traveling', driving = traveling && prof.Travel === 'Drive';
    v.car.visible = driving; v.body.visible = !driving;
    if (traveling) v.g.rotation.y = n.heading * Math.PI / 180;
    else if (!fight && Math.hypot(playerState.x - n.x, playerState.z - n.z) < 4) v.g.rotation.y = Math.atan2(playerState.x - n.x, playerState.z - n.z);
    poseWalk(v.body, traveling && !driving ? 1.6 : 0, dt);
    const text = prof.DisplayName + (traveling ? '' : n.activity === 'sleep' ? ' · asleep' : '');
    if (text !== v.last) { v.label.userData.redraw(text); v.last = text; }
  }
}

// ------------------------------------------------------------------ main loop
let last = performance.now();
function frame(now) {
  const dt = Math.min(0.1, (now - last) / 1000); last = now;
  if (game) {
    if (input.hit('escape', 'tab', 'menu')) overlay === 'menu' ? closeMenu() : !overlay && openMenu();
    if (overlay === 'dialogue') { for (let i = 1; i <= 9; i++) if (input.hit(String(i))) { const o = runner?.options[i - 1]; if (o) chooseOption(o.Id); } }
    game.tick(dt, playerState.x, playerState.z, clockRunning());
    if (mode === 'explore') updateExplore(dt);
    else if (mode === 'fight') updateFight(dt);
    else if (mode === 'drive') updateDrive(dt);
    updateNpcs(dt); updateLighting();
    while (game.toasts.length) toast(game.toasts.shift());
    if (game.missions.log.length !== lastLog) { const fresh = game.missions.log.slice(lastLog); lastLog = game.missions.log.length; if (fresh.some((e) => e.type === 'completed')) { wantSave = true; showBanner('MISSION COMPLETE', 1.8); } }
    if (wantSave && !fight && !race && !overlay) { wantSave = false; save(true); }
    if (now > autosaveAt) { autosaveAt = now + 60000; wantSave = true; }
    const target = mode === 'explore' || mode === 'drive' ? objectiveTarget() : null;
    objectiveArrow.visible = !!(target && target.x != null) && mode === 'explore';
    if (objectiveArrow.visible) objectiveArrow.position.set(target.x, 6 + Math.sin(now / 300) * 0.4, target.z), (objectiveArrow.rotation.y += dt);
    if (now > hudAt) { hudAt = now + 120; updateHud(target); drawMinimap(target); }
  }
  if (banner.until && now > banner.until) { $('banner').hidden = true; banner.until = 0; }
  for (const m of markers) { m.mesh.rotation.y += dt * 1.5; m.mesh.visible = !game || markerAvailable(m); }
  updateCamera(dt);
  updateLabels();
  renderer.render(scene, camera);
  input.endFrame();
  requestAnimationFrame(frame);
}

// ------------------------------------------------------------------ UI wiring
for (const b of document.querySelectorAll('[data-key]')) input.bindButton(b, b.dataset.key);
$('act').addEventListener('click', () => { if (mode === 'drive' && drive?.action) drive.action.run(); else if (current && !overlay) activate(current); });
$('menu-btn').onclick = () => (overlay === 'menu' ? closeMenu() : openMenu());
$('menu-close').onclick = closeMenu;
$('menu-save').onclick = () => { closeMenu(); save(false); };
$('menu-load').onclick = () => { if (fight || race) return toast('Finish what you\'re doing first.'); closeMenu(); if (drive) { scene.remove(drive.mesh); drive = null; } startGame(true); toast('Save loaded.'); };
$('menu-new').onclick = () => ($('confirm-new').hidden = false);
$('confirm-yes').onclick = () => { store.clear(); closeMenu(); if (drive) { scene.remove(drive.mesh); drive = null; } if (fight) { scene.remove(fight.opp); fight = null; } race = null; startGame(false); };
$('confirm-no').onclick = () => ($('confirm-new').hidden = true);
$('garage-close').onclick = closeGarage;
$('dlg-leave').onclick = closeDialogue;
const hasSave = !!store.get();
$('btn-continue').hidden = !hasSave;
$('btn-continue').onclick = () => startGame(true);
$('btn-new').onclick = () => { if (hasSave && !confirmNewFromTitle) { confirmNewFromTitle = true; $('btn-new').textContent = 'Tap again to start over'; return; } store.clear(); startGame(false); };
let confirmNewFromTitle = false;
$('controls-touch').hidden = !input.touch; $('controls-keys').hidden = input.touch;
setPads();
requestAnimationFrame(frame);

// Test/debug hook (used by the automated smoke test).
window.__ffts = { get game() { return game; }, get mode() { return mode; }, get overlay() { return overlay; }, playerState, startGame, talkTo, chooseOption, get runner() { return runner; }, get fight() { return fight; }, activate, get current() { return current; }, openGarage, enterCar, get drive() { return drive; }, startRace, get race() { return race; } };
