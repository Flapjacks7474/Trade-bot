// Builds the 3D Southside from the shared layout data: ground, roads, buildings with signs, props, people.
import * as THREE from 'three';

export function makeLabel(text, { size = 48, color = '#f4ead5', bg = 'rgba(14,16,20,0.72)', scale = 0.012 } = {}) {
  const c = document.createElement('canvas'), x = c.getContext('2d');
  x.font = `700 ${size}px "Barlow Condensed", "Arial Narrow", sans-serif`;
  const w = Math.ceil(x.measureText(text).width) + size, h = Math.ceil(size * 1.5);
  c.width = w; c.height = h;
  x.font = `700 ${size}px "Barlow Condensed", "Arial Narrow", sans-serif`;
  if (bg) { x.fillStyle = bg; roundRect(x, 0, 0, w, h, h / 3); x.fill(); }
  x.fillStyle = color; x.textBaseline = 'middle'; x.textAlign = 'center'; x.fillText(text, w / 2, h / 2 + 2);
  const tex = new THREE.CanvasTexture(c); tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = 4;
  const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthWrite: false, transparent: true }));
  s.scale.set(w * scale, h * scale, 1); s.userData.redraw = (t) => { s.material.map.dispose(); const n = makeLabel(t, { size, color, bg, scale }); s.material = n.material; s.scale.copy(n.scale); };
  return s;
}
function roundRect(x, a, b, w, h, r) { x.beginPath(); x.moveTo(a + r, b); x.arcTo(a + w, b, a + w, b + h, r); x.arcTo(a + w, b + h, a, b + h, r); x.arcTo(a, b + h, a, b, r); x.arcTo(a, b, a + w, b, r); x.closePath(); }

function groundTexture() {
  const c = document.createElement('canvas'); c.width = c.height = 256; const x = c.getContext('2d');
  x.fillStyle = '#3c4a34'; x.fillRect(0, 0, 256, 256);
  for (let i = 0; i < 2500; i++) { x.fillStyle = `rgba(${40 + Math.random() * 40},${60 + Math.random() * 40},${30 + Math.random() * 30},0.5)`; x.fillRect(Math.random() * 256, Math.random() * 256, 2, 2); }
  const t = new THREE.CanvasTexture(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(60, 60); t.colorSpace = THREE.SRGBColorSpace; return t;
}
function roadTexture() {
  const c = document.createElement('canvas'); c.width = 64; c.height = 256; const x = c.getContext('2d');
  x.fillStyle = '#26272b'; x.fillRect(0, 0, 64, 256);
  for (let i = 0; i < 400; i++) { x.fillStyle = `rgba(255,255,255,${Math.random() * 0.05})`; x.fillRect(Math.random() * 64, Math.random() * 256, 1, 1); }
  x.fillStyle = '#d9b44a'; x.fillRect(30, 0, 4, 120);
  const t = new THREE.CanvasTexture(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.colorSpace = THREE.SRGBColorSpace; return t;
}
function facadeTexture(base) {
  const c = document.createElement('canvas'); c.width = 128; c.height = 128; const x = c.getContext('2d');
  x.fillStyle = base; x.fillRect(0, 0, 128, 128);
  x.fillStyle = 'rgba(0,0,0,0.25)'; for (let i = 0; i < 128; i += 16) x.fillRect(0, i, 128, 1);
  x.fillStyle = 'rgba(255,214,140,0.55)'; for (let r = 0; r < 3; r++) for (let k = 0; k < 4; k++) x.fillRect(10 + k * 30, 14 + r * 38, 16, 20);
  const t = new THREE.CanvasTexture(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.colorSpace = THREE.SRGBColorSpace; return t;
}

export function buildWorld(scene, layout) {
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(layout.SizeX * 1.6, layout.SizeZ * 1.6), new THREE.MeshLambertMaterial({ map: groundTexture() }));
  ground.rotation.x = -Math.PI / 2; ground.receiveShadow = true; scene.add(ground);
  const rt = roadTexture();
  for (const r of layout.Roads) {
    const len = Math.hypot(r.X2 - r.X1, r.Z2 - r.Z1), tex = rt.clone(); tex.needsUpdate = true; tex.repeat.set(1, len / 10);
    const m = new THREE.Mesh(new THREE.PlaneGeometry(r.Width, len), new THREE.MeshLambertMaterial({ map: tex }));
    m.rotation.x = -Math.PI / 2; m.rotation.z = -Math.atan2(r.X2 - r.X1, r.Z2 - r.Z1);
    m.position.set((r.X1 + r.X2) / 2, 0.02, (r.Z1 + r.Z2) / 2); m.receiveShadow = true; scene.add(m);
    const curb = new THREE.Mesh(new THREE.PlaneGeometry(r.Width + 3, len + 3), new THREE.MeshLambertMaterial({ color: 0x6b6a66 }));
    curb.rotation.copy(m.rotation); curb.position.set(m.position.x, 0.01, m.position.z); scene.add(curb);
  }
  const solids = [];
  for (const l of layout.Locations) {
    const g = new THREE.Group(); g.position.set(l.X, 0, l.Z); scene.add(g);
    if (l.Height < 1) {
      const pad = new THREE.Mesh(new THREE.PlaneGeometry(l.Width, l.Depth), new THREE.MeshLambertMaterial({ color: l.Type === 'court' ? 0x3d6b8a : 0x34363b }));
      pad.rotation.x = -Math.PI / 2; pad.position.y = 0.03; g.add(pad);
      if (l.Type === 'court') for (const s of [-1, 1]) { const hoop = new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.1, 3.2), new THREE.MeshLambertMaterial({ color: 0xdddddd })); hoop.position.set(0, 1.6, s * (l.Depth / 2 - 1)); g.add(hoop); }
    } else {
      const tex = facadeTexture(l.Color); tex.repeat.set(Math.max(1, l.Width / 6), Math.max(1, l.Height / 6));
      const mat = new THREE.MeshLambertMaterial({ map: tex });
      const b = new THREE.Mesh(new THREE.BoxGeometry(l.Width, l.Height, l.Depth), mat);
      b.position.y = l.Height / 2; b.castShadow = true; b.receiveShadow = true; g.add(b);
      const roof = new THREE.Mesh(new THREE.BoxGeometry(l.Width + 0.6, 0.5, l.Depth + 0.6), new THREE.MeshLambertMaterial({ color: 0x1d1e22 }));
      roof.position.y = l.Height + 0.25; g.add(roof);
      solids.push({ x: l.X, z: l.Z, hw: l.Width / 2, hd: l.Depth / 2, id: l.Id });
    }
    const label = makeLabel(l.DisplayName, { size: 44 }); label.position.set(0, Math.max(l.Height, 2) + 2.2, 0); g.add(label);
  }
  // Street lights along roads for atmosphere.
  const lampMat = new THREE.MeshLambertMaterial({ color: 0x2b2d31 }), bulbMat = new THREE.MeshBasicMaterial({ color: 0xffd38a });
  for (const r of layout.Roads) {
    const len = Math.hypot(r.X2 - r.X1, r.Z2 - r.Z1), dx = (r.X2 - r.X1) / len, dz = (r.Z2 - r.Z1) / len;
    for (let s = 15; s < len; s += 40) {
      const px = r.X1 + dx * s + dz * (r.Width / 2 + 1.5), pz = r.Z1 + dz * s - dx * (r.Width / 2 + 1.5);
      const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.08, 0.1, 5), lampMat); pole.position.set(px, 2.5, pz); scene.add(pole);
      const bulb = new THREE.Mesh(new THREE.SphereGeometry(0.25), bulbMat); bulb.position.set(px, 5.1, pz); scene.add(bulb);
    }
  }
  return { solids };
}

/** Low-poly person with simple limbs we can pose for walking and fighting. */
export function makePerson(color, { skin = '#6b4a35', height = 1 } = {}) {
  const g = new THREE.Group();
  const cloth = new THREE.MeshLambertMaterial({ color }), skinM = new THREE.MeshLambertMaterial({ color: skin }), dark = new THREE.MeshLambertMaterial({ color: 0x1a1a1d });
  const torso = new THREE.Mesh(new THREE.CapsuleGeometry(0.28, 0.55, 4, 8), cloth); torso.position.y = 1.15; g.add(torso);
  const head = new THREE.Mesh(new THREE.SphereGeometry(0.2, 12, 10), skinM); head.position.y = 1.72; g.add(head);
  const limb = (w, h, m) => { const p = new THREE.Group(); const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, w), m); mesh.position.y = -h / 2; p.add(mesh); return p; };
  const armL = limb(0.13, 0.62, skinM), armR = limb(0.13, 0.62, skinM); armL.position.set(-0.36, 1.45, 0); armR.position.set(0.36, 1.45, 0);
  const legL = limb(0.17, 0.8, dark), legR = limb(0.17, 0.8, dark); legL.position.set(-0.14, 0.8, 0); legR.position.set(0.14, 0.8, 0);
  g.add(armL, armR, legL, legR);
  g.userData = { torso, head, armL, armR, legL, legR, phase: 0 };
  g.scale.setScalar(height);
  g.traverse((o) => { if (o.isMesh) o.castShadow = true; });
  return g;
}

export function poseWalk(p, speed, dt) {
  const u = p.userData; u.phase += speed * dt * 4;
  const s = Math.sin(u.phase) * Math.min(1, speed / 2) * 0.7;
  u.legL.rotation.x = s; u.legR.rotation.x = -s; u.armL.rotation.x = -s * 0.8; u.armR.rotation.x = s * 0.8;
  u.armL.rotation.z = 0; u.armR.rotation.z = 0; u.torso.rotation.x = 0; p.rotation.x = 0; p.position.y = 0;
}

export function makeCarMesh(color) {
  const g = new THREE.Group(), body = new THREE.MeshLambertMaterial({ color }), glass = new THREE.MeshLambertMaterial({ color: 0x1b2430 }), tire = new THREE.MeshLambertMaterial({ color: 0x111111 });
  const lower = new THREE.Mesh(new THREE.BoxGeometry(1.9, 0.7, 4.4), body); lower.position.y = 0.65; g.add(lower);
  const cabin = new THREE.Mesh(new THREE.BoxGeometry(1.6, 0.6, 2.1), glass); cabin.position.set(0, 1.28, -0.25); g.add(cabin);
  const lights = new THREE.Mesh(new THREE.BoxGeometry(1.6, 0.15, 0.05), new THREE.MeshBasicMaterial({ color: 0xfff1c1 })); lights.position.set(0, 0.75, 2.21); g.add(lights);
  const tail = new THREE.Mesh(new THREE.BoxGeometry(1.6, 0.15, 0.05), new THREE.MeshBasicMaterial({ color: 0xc1121f })); tail.position.set(0, 0.75, -2.21); g.add(tail);
  const wheels = [];
  for (const [x, z] of [[-0.95, 1.4], [0.95, 1.4], [-0.95, -1.4], [0.95, -1.4]]) {
    const w = new THREE.Mesh(new THREE.CylinderGeometry(0.36, 0.36, 0.28, 14), tire); w.rotation.z = Math.PI / 2; w.position.set(x, 0.36, z); g.add(w); wheels.push(w);
  }
  g.userData.wheels = wheels;
  g.traverse((o) => { if (o.isMesh) o.castShadow = true; });
  return g;
}

/** Pushes a circle out of building rectangles; returns the surface normal of the deepest hit (or null). */
export function collide(solids, pos, radius) {
  let normal = null;
  for (const s of solids) {
    const cx = Math.max(s.x - s.hw, Math.min(pos.x, s.x + s.hw)), cz = Math.max(s.z - s.hd, Math.min(pos.z, s.z + s.hd));
    const dx = pos.x - cx, dz = pos.z - cz, d = Math.hypot(dx, dz);
    if (d >= radius) continue;
    if (d > 1e-4) { pos.x = cx + dx / d * radius; pos.z = cz + dz / d * radius; normal = { x: dx / d, z: dz / d }; }
    else {
      const px = s.hw - Math.abs(pos.x - s.x), pz = s.hd - Math.abs(pos.z - s.z);
      if (px < pz) { const sx = Math.sign(pos.x - s.x) || 1; pos.x = s.x + sx * (s.hw + radius); normal = { x: sx, z: 0 }; }
      else { const sz = Math.sign(pos.z - s.z) || 1; pos.z = s.z + sz * (s.hd + radius); normal = { x: 0, z: sz }; }
    }
  }
  return normal;
}
