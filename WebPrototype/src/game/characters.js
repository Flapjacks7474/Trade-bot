// Animated people: KayKit's CC0 mannequin + CC0 animation clips, dressed per character with skin/shirt/pants colours.
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { clone as cloneSkinned } from 'three/examples/jsm/utils/SkeletonUtils.js';

const FILES = {
  body: 'assets/characters/Mannequin_Medium.glb',
  anims: ['assets/animations/Rig_Medium_General.glb', 'assets/animations/Rig_Medium_MovementBasic.glb',
    'assets/animations/Rig_Medium_MovementAdvanced.glb', 'assets/animations/Rig_Medium_CombatMelee.glb']
};
const HEIGHT = 1.8, SOURCE_HEIGHT = 2.2;
let lib = null;

export async function loadCharacters(onProgress) {
  const loader = new GLTFLoader();
  const all = [FILES.body, ...FILES.anims];
  let done = 0;
  const results = await Promise.all(all.map((u) => loader.loadAsync(u).then((r) => { done++; onProgress?.(done / all.length); return r; })));
  const clips = new Map();
  for (const r of results.slice(1)) for (const c of r.animations) {
    if (c.name === 'T-Pose') continue;
    c.tracks = c.tracks.filter((t) => !t.name.endsWith('.scale')); // keep our head proportion fix from being animated away
    clips.set(c.name, c);
  }
  lib = { body: results[0].scene, clips };
  return lib;
}
export const charactersReady = () => !!lib;

/** Appearance: shirt/pants/skin colours, optional hat colour and build (height multiplier). */
export function createCharacter({ shirt = '#c8102e', pants = '#1c1d22', skin = '#6b4a35', shoes = '#121212', hat = null, height = 1 } = {}) {
  const root = new THREE.Group();
  const model = cloneSkinned(lib.body);
  model.scale.setScalar(HEIGHT / SOURCE_HEIGHT * height);
  const mats = {
    skin: new THREE.MeshStandardMaterial({ color: skin, roughness: 0.6 }),
    shirt: new THREE.MeshStandardMaterial({ color: shirt, roughness: 0.85 }),
    pants: new THREE.MeshStandardMaterial({ color: pants, roughness: 0.9 })
  };
  model.traverse((o) => {
    if (!o.isMesh) return;
    o.castShadow = true; o.receiveShadow = false; o.frustumCulled = false;
    const n = o.name.toLowerCase();
    o.material = n.includes('head') ? mats.skin : n.includes('leg') ? mats.pants : n.includes('arm') ? mats.skin : mats.shirt;
  });
  // KayKit's mannequin is stylised with a large head; shrink it toward realistic proportions.
  let headBone = null; model.traverse((o) => { if (o.isBone && o.name === 'head') headBone = o; });
  if (headBone) headBone.scale.setScalar(0.74);
  if (hat && headBone) {
    const cap = new THREE.Mesh(new THREE.SphereGeometry(0.46, 18, 10, 0, Math.PI * 2, 0, Math.PI / 2), new THREE.MeshStandardMaterial({ color: hat, roughness: 0.85 }));
    cap.position.set(0, 0.52, 0); cap.scale.set(1.0, 0.62, 1.06); headBone.add(cap);
    const brim = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.3, 0.04, 16, 1, false, -Math.PI / 2, Math.PI), cap.material);
    brim.position.set(0, 0.52, 0.32); brim.scale.set(1.2, 1, 0.9); headBone.add(brim);
  }
  root.add(model);
  const mixer = new THREE.AnimationMixer(model);
  let current = null, currentName = '';
  const play = (name, { loop = true, fade = 0.15, timeScale = 1, restart = false } = {}) => {
    const clip = lib.clips.get(name);
    if (!clip) return;
    if (name === currentName && !restart) { if (current) current.timeScale = timeScale; return; }
    const next = mixer.clipAction(clip);
    next.reset(); next.setLoop(loop ? THREE.LoopRepeat : THREE.LoopOnce, Infinity); next.clampWhenFinished = !loop; next.timeScale = timeScale;
    next.enabled = true; next.setEffectiveWeight(1); next.play();
    if (current && current !== next) current.crossFadeTo(next, fade, false);
    current = next; currentName = name;
  };
  const duration = (name) => lib.clips.get(name)?.duration || 1;
  play('Idle_A');
  return { root, model, mixer, mats, play, duration, get anim() { return currentName; }, update: (dt) => mixer.update(dt) };
}

/** Picks a locomotion clip for a ground speed in m/s. */
export function locomotion(c, speed) {
  if (speed > 5.2) c.play('Running_A', { timeScale: speed / 6.2 });
  else if (speed > 0.25) c.play('Walking_A', { timeScale: Math.max(0.6, speed / 1.7) });
  else c.play('Idle_A');
}

/** Maps a fighter's simulation state to a clip, timing attacks to the move's real frame data. */
export function fightAnimation(c, f) {
  const S = f.state, m = f.move;
  const timed = (name, ticks) => c.play(name, { loop: false, fade: 0.06, timeScale: Math.min(3, Math.max(0.5, c.duration(name) / Math.max(0.15, ticks / 60))), restart: c.anim !== name || f.moveTick <= 1 });
  switch (S) {
    case 'Idle': return c.play('Melee_Unarmed_Idle');
    case 'Moving': return c.play('Walking_B', { timeScale: 1.1 });
    case 'Blocking': return c.play('Melee_Blocking');
    case 'BlockStun': return c.play('Melee_Block_Hit', { loop: false, fade: 0.05 });
    case 'Countering': return c.play('Melee_Block', { loop: false, fade: 0.05 });
    case 'Attacking': case 'Combo': case 'Finisher': {
      if (!m) return;
      const total = m.Startup + m.Active + m.Recovery;
      if (S === 'Finisher') return timed('Melee_1H_Attack_Jump_Chop', total);
      if (m.Kind === 'Kick' || m.Kind === 'GroundAttack') return timed('Melee_Unarmed_Attack_Kick', total);
      if (m.Kind === 'Grapple') return timed('Interact', total);
      if (m.Kind === 'Taunt') return c.play('Interact', { loop: false });
      if (m.Kind === 'Heavy' || m.Kind === 'Signature') return timed('Melee_1H_Attack_Chop', total);
      return timed('Melee_Unarmed_Attack_Punch_A', total);
    }
    case 'Throwing': return timed('Throw', f.move ? f.move.Startup + f.move.Active + f.move.Recovery : 30);
    case 'Grappling': return c.play('Melee_Block_Attack', { timeScale: 0.4 });
    case 'Grappled': case 'FinisherVictim': return c.play('Hit_B', { timeScale: 0.5 });
    case 'HitStun': return c.play(f.combo % 2 ? 'Hit_A' : 'Hit_B', { loop: false, fade: 0.04, restart: f.stateTicks <= 1 });
    case 'Stunned': return c.play('Hit_B', { timeScale: 0.35 });
    case 'Dodging': return c.play('Dodge_Backward', { loop: false, fade: 0.05, timeScale: 1.4 });
    case 'KnockedDown': return c.play('Death_A', { loop: false, fade: 0.08 });
    case 'Defeated': return c.play('Death_B', { loop: false, fade: 0.08 });
    case 'GettingUp': return c.play('Spawn_Ground', { loop: false, fade: 0.08, timeScale: 1.6 });
  }
}

/**
 * The player's own hero model (Meshy export: one static mesh, no rig, no textures). We colour it by body region
 * so it reads as the character: skin, white tank top, gold chain, olive cargo pants, white sneakers.
 * Faces +Z; returned group is 1.8 m tall with feet at y = 0.
 */
export async function loadHeroModel(url, colors = {}) {
  const { FBXLoader } = await import('three/examples/jsm/loaders/FBXLoader.js');
  const obj = await new FBXLoader().loadAsync(url);
  const C = { skin: '#4a2f20', shirt: '#eeeae2', chain: '#d4a83a', pants: '#5b5a3e', shoes: '#f2f2f0', soles: '#2a2a2a', ...colors };
  const col = Object.fromEntries(Object.entries(C).map(([k, v]) => [k, new THREE.Color(v)]));
  let mesh = null; obj.traverse((o) => { if (o.isMesh && !mesh) mesh = o; });
  obj.updateMatrixWorld(true);
  // Measure in the model's own world space: FBX nodes can carry scale/rotation (e.g. centimetres, Z-up).
  const bb = new THREE.Box3().setFromObject(obj), H = bb.max.y - bb.min.y, W = bb.max.x - bb.min.x, cx = (bb.max.x + bb.min.x) / 2;
  const zFront = bb.max.z, D = bb.max.z - bb.min.z;
  const geo = mesh.geometry, pos = geo.attributes.position, n = pos.count, colors3 = new Float32Array(n * 3), v = new THREE.Vector3();
  for (let i = 0; i < n; i++) {
    v.fromBufferAttribute(pos, i).applyMatrix4(mesh.matrixWorld);
    const y = (v.y - bb.min.y) / H, x = Math.abs(v.x - cx) / W, z = (zFront - v.z) / D;
    let c;
    if (y < 0.022) c = col.soles;
    else if (y < 0.075) c = col.shoes;
    else if (y < 0.47) c = x > 0.27 && y > 0.4 ? col.skin : col.pants;          // hands tucked in the pockets
    else if (y < 0.8) {
      const torso = x < 0.2 - Math.max(0, y - 0.7) * 0.6;                        // narrower near the straps
      const chain = y > 0.69 && y < 0.79 && x < 0.07 && z < 0.25;
      c = chain ? col.chain : torso ? col.shirt : col.skin;
    } else c = col.skin;
    colors3[i * 3] = c.r; colors3[i * 3 + 1] = c.g; colors3[i * 3 + 2] = c.b;
  }
  geo.setAttribute('color', new THREE.BufferAttribute(colors3, 3));
  mesh.material = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.72, metalness: 0.02 });
  mesh.castShadow = true; mesh.receiveShadow = true;
  const g = new THREE.Group(), s = 1.8 / H, inner = new THREE.Group();
  inner.add(obj); obj.position.set(-cx, -bb.min.y, -(bb.max.z + bb.min.z) / 2); inner.scale.setScalar(s);
  g.add(inner);
  return g;
}
