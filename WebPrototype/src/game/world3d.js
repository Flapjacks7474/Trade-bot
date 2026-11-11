// Builds a detailed Southside from the shared layout data. All geometry and textures are generated here.
import * as THREE from 'three';
import { asphalt, concrete, grass, roadMarkings, parkingLines, facade } from './textures.js';

export const nightMaterials = []; // { mat, base } emissive intensities scaled by time of day

export function makeLabel(text, { size = 48, color = '#f4ead5', bg = 'rgba(14,16,20,0.72)', scale = 0.012 } = {}) {
  const c = document.createElement('canvas'), x = c.getContext('2d');
  x.font = `700 ${size}px "Barlow Condensed", "Arial Narrow", sans-serif`;
  const w = Math.ceil(x.measureText(text).width) + size, h = Math.ceil(size * 1.5);
  c.width = w; c.height = h;
  x.font = `700 ${size}px "Barlow Condensed", "Arial Narrow", sans-serif`;
  if (bg) { x.fillStyle = bg; roundRect(x, 0, 0, w, h, h / 3); x.fill(); }
  x.fillStyle = color; x.textBaseline = 'middle'; x.textAlign = 'center'; x.fillText(text, w / 2, h / 2 + 2);
  const tex = new THREE.CanvasTexture(c); tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = 4;
  const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthWrite: false, transparent: true, toneMapped: false }));
  s.scale.set(w * scale, h * scale, 1); s.renderOrder = 10;
  s.userData.redraw = (t) => { s.material.map.dispose(); const n = makeLabel(t, { size, color, bg, scale }); s.material = n.material; s.scale.copy(n.scale); };
  return s;
}
function roundRect(x, a, b, w, h, r) { x.beginPath(); x.moveTo(a + r, b); x.arcTo(a + w, b, a + w, b + h, r); x.arcTo(a + w, b + h, a, b + h, r); x.arcTo(a, b + h, a, b, r); x.arcTo(a, b, a + w, b, r); x.closePath(); }

/** Scales a geometry's UVs so a shared texture tiles every `metres` metres. */
function uvMetres(geo, sx, sy, metres) { const uv = geo.attributes.uv; for (let i = 0; i < uv.count; i++) uv.setXY(i, uv.getX(i) * sx / metres, uv.getY(i) * sy / metres); uv.needsUpdate = true; return geo; }
function boxUvMetres(geo, w, h, d, metres) {
  const uv = geo.attributes.uv, dims = [[d, h], [d, h], [w, d], [w, d], [w, h], [w, h]];
  for (let f = 0; f < 6; f++) for (let k = 0; k < 4; k++) { const i = f * 4 + k; uv.setXY(i, uv.getX(i) * dims[f][0] / metres, uv.getY(i) * dims[f][1] / metres); }
  uv.needsUpdate = true; return geo;
}
let rs = 99; const rnd = () => { rs = (rs * 16807) % 2147483647; return (rs - 1) / 2147483646; };

const STYLE = {
  residence: { wall: 'siding', storefront: false, residential: true },
  shop: { wall: 'brick', storefront: true }, studio: { wall: 'stucco', storefront: true }, gym: { wall: 'brick', storefront: true },
  venue: { wall: 'metal', storefront: false }, garage: { wall: 'metal', storefront: true }, restaurant: { wall: 'stucco', storefront: true },
  hq: { wall: 'brick', storefront: false }
};
const SIGN_COLORS = ['#ffcf6b', '#ff5d73', '#7ce0ff', '#b8ff8a', '#ffffff'];

export function buildWorld(scene, layout, quality = 'high') {
  rs = 99;
  const solids = [], props = new THREE.Group(); scene.add(props);
  const G = grass(), A = asphalt(), Cn = concrete();
  const grassMat = new THREE.MeshStandardMaterial({ map: G.map, normalMap: G.normalMap, roughness: 1 });
  const asphaltMat = new THREE.MeshStandardMaterial({ map: A.map, normalMap: A.normalMap, roughness: 0.92, normalScale: new THREE.Vector2(0.6, 0.6) });
  const concreteMat = new THREE.MeshStandardMaterial({ map: Cn.map, normalMap: Cn.normalMap, roughness: 0.85 });
  const markMat = new THREE.MeshStandardMaterial({ map: roadMarkings(), transparent: true, roughness: 0.7, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2 });

  const ground = new THREE.Mesh(uvMetres(new THREE.PlaneGeometry(layout.SizeX * 1.6, layout.SizeZ * 1.6), layout.SizeX * 1.6, layout.SizeZ * 1.6, 6), grassMat);
  ground.rotation.x = -Math.PI / 2; ground.receiveShadow = true; scene.add(ground);

  // ---------------------------------------------------------------- roads, markings, sidewalks
  const roads = layout.Roads;
  const crossT = (i) => {
    const r = roads[i], a = [r.X1, r.Z1], b = [r.X2, r.Z2], out = [];
    roads.forEach((o, j) => {
      if (i === j) return;
      const s = [o.X2 - o.X1, o.Z2 - o.Z1], d = [b[0] - a[0], b[1] - a[1]], den = d[0] * s[1] - d[1] * s[0];
      if (Math.abs(den) < 1e-6) return;
      const ac = [o.X1 - a[0], o.Z1 - a[1]], t = (ac[0] * s[1] - ac[1] * s[0]) / den, u = (ac[0] * d[1] - ac[1] * d[0]) / den;
      if (t >= -0.01 && t <= 1.01 && u >= -0.01 && u <= 1.01) out.push({ t, w: o.Width });
    });
    return out;
  };
  roads.forEach((r, i) => {
    const len = Math.hypot(r.X2 - r.X1, r.Z2 - r.Z1), ang = Math.atan2(r.X2 - r.X1, r.Z2 - r.Z1);
    const cx = (r.X1 + r.X2) / 2, cz = (r.Z1 + r.Z2) / 2, dir = [(r.X2 - r.X1) / len, (r.Z2 - r.Z1) / len], nrm = [dir[1], -dir[0]];
    const road = new THREE.Mesh(uvMetres(new THREE.PlaneGeometry(r.Width, len), r.Width, len, 8), asphaltMat);
    road.rotation.set(-Math.PI / 2, 0, ang); road.position.set(cx, 0.02 + i * 0.002, cz); road.receiveShadow = true; scene.add(road);
    // Markings, interrupted at intersections.
    const crosses = crossT(i).map((c) => ({ from: c.t * len - c.w / 2 - 2, to: c.t * len + c.w / 2 + 2 }));
    const spans = []; let start = 0;
    for (const c of crosses.sort((p, q) => p.from - q.from)) { if (c.from > start) spans.push([start, c.from]); start = Math.max(start, c.to); }
    if (start < len) spans.push([start, len]);
    for (const [s0, s1] of spans) {
      const l = s1 - s0; if (l < 2) continue;
      const geo = new THREE.PlaneGeometry(r.Width - 0.4, l); const uv = geo.attributes.uv;
      for (let k = 0; k < uv.count; k++) uv.setY(k, uv.getY(k) * l / 12);
      const m = new THREE.Mesh(geo, markMat); const mid = (s0 + s1) / 2 - len / 2;
      m.rotation.set(-Math.PI / 2, 0, ang); m.position.set(cx + dir[0] * mid, 0.035 + i * 0.002, cz + dir[1] * mid); m.receiveShadow = true; scene.add(m);
    }
    // Sidewalks with curbs on both sides, gapped where other roads cross.
    const sw = 2.6, curbH = 0.14;
    for (const side of [-1, 1]) {
      const off = side * (r.Width / 2 + sw / 2);
      const gaps = crossT(i).map((c) => ({ from: c.t * len - c.w / 2 - sw, to: c.t * len + c.w / 2 + sw })).sort((p, q) => p.from - q.from);
      let s = 0; const segs = [];
      for (const g of gaps) { if (g.from > s) segs.push([s, g.from]); s = Math.max(s, g.to); }
      if (s < len) segs.push([s, len]);
      for (const [s0, s1] of segs) {
        const l = s1 - s0; if (l < 1) continue;
        const geo = boxUvMetres(new THREE.BoxGeometry(sw, curbH, l), sw, curbH, l, 3);
        const m = new THREE.Mesh(geo, concreteMat); const mid = (s0 + s1) / 2 - len / 2;
        m.rotation.y = ang; m.position.set(cx + dir[0] * mid + nrm[0] * off, curbH / 2, cz + dir[1] * mid + nrm[1] * off); m.receiveShadow = true; scene.add(m);
      }
    }
  });

  // ---------------------------------------------------------------- buildings
  const nearestRoadDir = (x, z) => {
    let best = null, bd = Infinity;
    for (const r of roads) {
      const dx = r.X2 - r.X1, dz = r.Z2 - r.Z1, l2 = dx * dx + dz * dz, t = Math.max(0, Math.min(1, ((x - r.X1) * dx + (z - r.Z1) * dz) / l2));
      const px = r.X1 + t * dx, pz = r.Z1 + t * dz, d = Math.hypot(px - x, pz - z);
      if (d < bd) { bd = d; best = [px - x, pz - z]; }
    }
    return Math.abs(best[0]) > Math.abs(best[1]) ? [Math.sign(best[0]), 0] : [0, Math.sign(best[1])];
  };
  layout.Locations.forEach((l, idx) => {
    const g = new THREE.Group(); g.position.set(l.X, 0, l.Z); scene.add(g);
    if (l.Height < 1) { buildPad(g, l, asphaltMat, concreteMat); return; }
    const st = STYLE[l.Type] || STYLE.shop, front = nearestRoadDir(l.X, l.Z);
    const sign = st.storefront ? l.DisplayName.replace(/\s*\(.*\)/, '') : null, signColor = SIGN_COLORS[idx % SIGN_COLORS.length];
    const faceW = front[0] !== 0 ? l.Depth : l.Width, sideW = front[0] !== 0 ? l.Width : l.Depth;
    const fF = facade({ widthM: faceW, heightM: l.Height, wall: st.wall, color: l.Color, storefront: st.storefront, sign, signColor, seed: idx + 1, residential: st.residential });
    const fS = facade({ widthM: sideW, heightM: l.Height, wall: st.wall, color: l.Color, storefront: false, seed: idx + 31, residential: st.residential });
    const mat = (f) => { const m = new THREE.MeshStandardMaterial({ map: f.map, normalMap: f.normalMap, roughnessMap: f.roughnessMap, roughness: 1, metalness: 0.05, emissive: 0xffffff, emissiveMap: f.emissiveMap, emissiveIntensity: 0 }); nightMaterials.push({ mat: m, base: 1.4 }); return m; };
    const mFront = mat(fF), mSide = mat(fS), roofMat = new THREE.MeshStandardMaterial({ color: 0x2a2b2f, roughness: 0.95 });
    // Box faces order: +x, -x, +y, -y, +z, -z
    const faces = [mSide, mSide, roofMat, roofMat, mSide, mSide];
    const fi = front[0] === 1 ? 0 : front[0] === -1 ? 1 : front[1] === 1 ? 4 : 5; faces[fi] = mFront;
    const body = new THREE.Mesh(new THREE.BoxGeometry(l.Width, l.Height, l.Depth), faces);
    body.position.y = l.Height / 2; body.castShadow = body.receiveShadow = true; g.add(body);
    // Faces whose texture must not mirror: flip U on -x and +z faces so text reads correctly.
    const uv = body.geometry.attributes.uv; for (const f of [1, 4]) for (let k = 0; k < 4; k++) { const i = f * 4 + k; uv.setX(i, 1 - uv.getX(i)); } uv.needsUpdate = true;
    // Parapet + rooftop clutter
    const parapet = new THREE.Mesh(new THREE.BoxGeometry(l.Width + 0.4, 0.6, l.Depth + 0.4), new THREE.MeshStandardMaterial({ color: 0x3a3936, roughness: 0.9 }));
    parapet.position.y = l.Height + 0.3; parapet.castShadow = true; g.add(parapet);
    const roofTop = new THREE.Mesh(new THREE.BoxGeometry(l.Width - 0.2, 0.62, l.Depth - 0.2), roofMat); roofTop.position.y = l.Height + 0.32; g.add(roofTop);
    const units = Math.max(1, Math.floor(l.Width * l.Depth / 120));
    for (let k = 0; k < units; k++) {
      const u = new THREE.Mesh(new THREE.BoxGeometry(1.4, 0.9, 1.1), new THREE.MeshStandardMaterial({ color: 0x9a9c9e, roughness: 0.5, metalness: 0.6 }));
      u.position.set((rnd() - 0.5) * (l.Width - 3), l.Height + 1.05, (rnd() - 0.5) * (l.Depth - 3)); u.castShadow = true; g.add(u);
    }
    if (l.Type === 'residence' && l.Height < 9) addPorchAndRoof(g, l, front);
    if (l.Height >= 12) { // water tank on taller blocks
      const t = new THREE.Mesh(new THREE.CylinderGeometry(1.2, 1.2, 2, 14), new THREE.MeshStandardMaterial({ color: 0x6b5848, roughness: 0.8 }));
      t.position.set(l.Width / 4, l.Height + 2.6, -l.Depth / 4); t.castShadow = true; g.add(t);
    }
    if (st.storefront) addAwning(g, l, front, signColor);
    solids.push({ x: l.X, z: l.Z, hw: l.Width / 2, hd: l.Depth / 2, id: l.Id });
    const label = makeLabel(l.DisplayName, { size: 40 }); label.position.set(0, l.Height + 3.2, 0); g.add(label);
  });

  // ---------------------------------------------------------------- street lamps (instanced)
  const lampSpots = [];
  roads.forEach((r) => {
    const len = Math.hypot(r.X2 - r.X1, r.Z2 - r.Z1), dx = (r.X2 - r.X1) / len, dz = (r.Z2 - r.Z1) / len;
    for (let s = 12, side = 1; s < len - 6; s += 30, side = -side) lampSpots.push({ x: r.X1 + dx * s + dz * side * (r.Width / 2 + 0.6), z: r.Z1 + dz * s - dx * side * (r.Width / 2 + 0.6), ang: Math.atan2(-dz * side, dx * side) });
  });
  const poleGeo = new THREE.CylinderGeometry(0.07, 0.11, 6.5, 8); poleGeo.translate(0, 3.25, 0);
  const armGeo = new THREE.BoxGeometry(1.6, 0.08, 0.08); armGeo.translate(0.8, 6.45, 0);
  const headGeo = new THREE.BoxGeometry(0.55, 0.12, 0.28); headGeo.translate(1.5, 6.35, 0);
  const poleMat = new THREE.MeshStandardMaterial({ color: 0x2f3236, roughness: 0.6, metalness: 0.5 });
  const headMat = new THREE.MeshStandardMaterial({ color: 0xfff1d0, emissive: 0xffc77a, emissiveIntensity: 0.2 }); nightMaterials.push({ mat: headMat, base: 6, min: 0.2 });
  for (const [geo, mt] of [[poleGeo, poleMat], [armGeo, poleMat], [headGeo, headMat]]) {
    const im = new THREE.InstancedMesh(geo, mt, lampSpots.length); const m4 = new THREE.Matrix4();
    lampSpots.forEach((p, k) => { m4.makeRotationY(p.ang); m4.setPosition(p.x, 0, p.z); im.setMatrixAt(k, m4); });
    im.castShadow = mt === poleMat; props.add(im);
  }
  // Light pools under lamps (cheap fake lighting for night).
  const poolTex = (() => { const c = document.createElement('canvas'); c.width = c.height = 64; const x = c.getContext('2d'); const gr = x.createRadialGradient(32, 32, 0, 32, 32, 32); gr.addColorStop(0, 'rgba(255,200,120,0.9)'); gr.addColorStop(1, 'rgba(255,200,120,0)'); x.fillStyle = gr; x.fillRect(0, 0, 64, 64); return new THREE.CanvasTexture(c); })();
  const poolMat = new THREE.MeshBasicMaterial({ map: poolTex, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, opacity: 0 });
  nightMaterials.push({ mat: poolMat, base: 0.55, opacityOnly: true });
  const pools = new THREE.InstancedMesh(new THREE.PlaneGeometry(9, 9).rotateX(-Math.PI / 2), poolMat, lampSpots.length); const m4 = new THREE.Matrix4();
  lampSpots.forEach((p, k) => { const ox = Math.cos(p.ang) * 1.5, oz = -Math.sin(p.ang) * 1.5; m4.makeTranslation(p.x + ox, 0.06, p.z + oz); pools.setMatrixAt(k, m4); });
  props.add(pools);

  // ---------------------------------------------------------------- trees (instanced), avoiding roads and buildings
  const onRoad = (x, z, pad) => roads.some((r) => { const dx = r.X2 - r.X1, dz = r.Z2 - r.Z1, l2 = dx * dx + dz * dz, t = Math.max(0, Math.min(1, ((x - r.X1) * dx + (z - r.Z1) * dz) / l2)); return Math.hypot(r.X1 + t * dx - x, r.Z1 + t * dz - z) < r.Width / 2 + pad; });
  const inBuilding = (x, z, pad) => layout.Locations.some((l) => Math.abs(x - l.X) < l.Width / 2 + pad && Math.abs(z - l.Z) < l.Depth / 2 + pad);
  const trees = [];
  for (let k = 0; k < (quality === 'low' ? 60 : 140); k++) {
    const x = (rnd() - 0.5) * layout.SizeX, z = (rnd() - 0.5) * layout.SizeZ;
    if (onRoad(x, z, 4.5) || inBuilding(x, z, 4)) continue;
    trees.push({ x, z, s: 0.8 + rnd() * 0.6, r: rnd() * Math.PI });
  }
  const trunkGeo = new THREE.CylinderGeometry(0.18, 0.28, 3, 7); trunkGeo.translate(0, 1.5, 0);
  const crown = new THREE.IcosahedronGeometry(1.9, 1); crown.translate(0, 4.2, 0);
  const crown2 = new THREE.IcosahedronGeometry(1.4, 1); crown2.translate(0.9, 3.6, 0.4);
  const crown3 = new THREE.IcosahedronGeometry(1.3, 1); crown3.translate(-0.8, 3.7, -0.5);
  const leaf = new THREE.MeshStandardMaterial({ color: 0x3f5f2a, roughness: 0.9, flatShading: true }), bark = new THREE.MeshStandardMaterial({ color: 0x4a3b2c, roughness: 1 });
  for (const [geo, mt] of [[trunkGeo, bark], [crown, leaf], [crown2, leaf], [crown3, leaf]]) {
    const im = new THREE.InstancedMesh(geo, mt, trees.length); const mm = new THREE.Matrix4(), q = new THREE.Quaternion(), sc = new THREE.Vector3();
    trees.forEach((t, k) => { q.setFromEuler(new THREE.Euler(0, t.r, 0)); sc.setScalar(t.s); mm.compose(new THREE.Vector3(t.x, 0, t.z), q, sc); im.setMatrixAt(k, mm); });
    im.castShadow = true; im.receiveShadow = true; props.add(im);
  }
  for (const t of trees) solids.push({ x: t.x, z: t.z, hw: 0.3, hd: 0.3, id: 'tree' });

  // ---------------------------------------------------------------- parked cars along the curb
  const palette = ['#7a1f1f', '#1f3a5f', '#d8d4cc', '#2b2b2b', '#5e6b3a', '#8c6d2b', '#3d2f4f', '#b0b3b8'];
  let pc = 0;
  roads.forEach((r, i) => {
    if (r.Width < 9) return;
    const len = Math.hypot(r.X2 - r.X1, r.Z2 - r.Z1), dx = (r.X2 - r.X1) / len, dz = (r.Z2 - r.Z1) / len, cr = crossT(i);
    for (let s = 20; s < len - 15; s += 26 + rnd() * 30) {
      if (cr.some((c) => Math.abs(c.t * len - s) < c.w / 2 + 8)) continue;
      const side = rnd() < 0.5 ? -1 : 1, off = side * (r.Width / 2 - 1.3), x = r.X1 + dx * s + dz * off, z = r.Z1 + dz * s - dx * off;
      if (inBuilding(x, z, 1)) continue;
      const car = makeCarMesh(palette[pc++ % palette.length]); car.position.set(x, 0, z); car.rotation.y = Math.atan2(dx, dz) + (side < 0 ? Math.PI : 0);
      props.add(car); solids.push({ x, z, hw: Math.abs(dx) > 0.5 ? 2.2 : 1.0, hd: Math.abs(dx) > 0.5 ? 1.0 : 2.2, id: 'parked' });
    }
  });
  return { solids };
}

function buildPad(g, l, asphaltMat, concreteMat) {
  if (l.Type === 'court') {
    const c = document.createElement('canvas'); c.width = 256; c.height = 160; const x = c.getContext('2d');
    x.fillStyle = '#2f5d7c'; x.fillRect(0, 0, 256, 160); x.fillStyle = '#b4523a'; x.fillRect(16, 12, 224, 136);
    x.strokeStyle = '#f2efe6'; x.lineWidth = 2.5; x.strokeRect(16, 12, 224, 136); x.beginPath(); x.moveTo(128, 12); x.lineTo(128, 148); x.stroke();
    x.beginPath(); x.arc(128, 80, 18, 0, Math.PI * 2); x.stroke(); x.strokeRect(16, 52, 40, 56); x.strokeRect(200, 52, 40, 56);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
    const pad = new THREE.Mesh(new THREE.PlaneGeometry(l.Width, l.Depth), new THREE.MeshStandardMaterial({ map: t, roughness: 0.8 }));
    pad.rotation.x = -Math.PI / 2; pad.rotation.z = Math.PI / 2; pad.position.y = 0.04; pad.receiveShadow = true; g.add(pad);
    const steel = new THREE.MeshStandardMaterial({ color: 0xcfd2d6, metalness: 0.8, roughness: 0.35 }), board = new THREE.MeshStandardMaterial({ color: 0xf5f5f0, roughness: 0.4 });
    for (const s of [-1, 1]) {
      const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.09, 0.09, 3.6), steel); pole.position.set(0, 1.8, s * (l.Depth / 2 + 0.6)); g.add(pole);
      const b = new THREE.Mesh(new THREE.BoxGeometry(1.8, 1.05, 0.06), board); b.position.set(0, 3.5, s * (l.Depth / 2 - 0.1)); g.add(b);
      const rim = new THREE.Mesh(new THREE.TorusGeometry(0.23, 0.02, 6, 16), new THREE.MeshStandardMaterial({ color: 0xd2541f })); rim.rotation.x = Math.PI / 2; rim.position.set(0, 3.05, s * (l.Depth / 2 - 0.4)); g.add(rim);
      pole.castShadow = b.castShadow = true;
    }
    const fence = new THREE.Mesh(new THREE.BoxGeometry(l.Width + 2, 3, l.Depth + 2), new THREE.MeshStandardMaterial({ color: 0x8d9196, wireframe: true, transparent: true, opacity: 0.35 }));
    fence.position.y = 1.5; g.add(fence);
    return;
  }
  const pad = new THREE.Mesh(uvMetres(new THREE.PlaneGeometry(l.Width, l.Depth), l.Width, l.Depth, 8), asphaltMat);
  pad.rotation.x = -Math.PI / 2; pad.position.y = 0.03; pad.receiveShadow = true; g.add(pad);
  const lines = new THREE.Mesh(new THREE.PlaneGeometry(l.Width - 2, l.Depth - 4), new THREE.MeshStandardMaterial({ map: parkingLines(), transparent: true, depthWrite: false }));
  lines.rotation.x = -Math.PI / 2; lines.position.y = 0.045; g.add(lines);
}

function addAwning(g, l, front, color) {
  const along = front[0] !== 0 ? l.Depth : l.Width, out = 1.6;
  const geo = new THREE.BoxGeometry(along * 0.86, 0.08, out); const m = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ color: new THREE.Color(color).multiplyScalar(0.55), roughness: 0.8 }));
  const half = front[0] !== 0 ? l.Width / 2 : l.Depth / 2;
  m.position.set(front[0] * (half + out / 2), 3.9, front[1] * (half + out / 2));
  if (front[0] !== 0) m.rotation.y = Math.PI / 2;
  m.rotation.x = front[1] !== 0 ? -front[1] * 0.25 : 0; if (front[0] !== 0) m.rotation.z = front[0] * 0.25;
  m.castShadow = true; g.add(m);
}

function addPorchAndRoof(g, l, front) {
  const wood = new THREE.MeshStandardMaterial({ color: 0xe8e1d2, roughness: 0.8 }), shingle = new THREE.MeshStandardMaterial({ color: 0x3b3431, roughness: 0.95, flatShading: true });
  const along = front[0] !== 0 ? l.Depth : l.Width, half = front[0] !== 0 ? l.Width / 2 : l.Depth / 2, deep = 2.6;
  const porch = new THREE.Group(); porch.position.set(front[0] * (half + deep / 2), 0, front[1] * (half + deep / 2)); if (front[0] !== 0) porch.rotation.y = Math.PI / 2; g.add(porch);
  const floor = new THREE.Mesh(new THREE.BoxGeometry(along * 0.8, 0.4, deep), wood); floor.position.y = 0.2; floor.receiveShadow = true; porch.add(floor);
  const roof = new THREE.Mesh(new THREE.BoxGeometry(along * 0.84, 0.15, deep + 0.3), shingle); roof.position.y = 3.1; roof.rotation.x = 0.12 * (front[1] || front[0]); roof.castShadow = true; porch.add(roof);
  for (const s of [-0.38, -0.13, 0.13, 0.38]) { const col = new THREE.Mesh(new THREE.CylinderGeometry(0.08, 0.08, 2.8, 8), wood); col.position.set(along * s, 1.6, deep / 2 - 0.2); col.castShadow = true; porch.add(col); }
  const rail = new THREE.Mesh(new THREE.BoxGeometry(along * 0.8, 0.08, 0.06), wood); rail.position.set(0, 1.2, deep / 2 - 0.2); porch.add(rail);
  // Pitched roof over the house
  const w = l.Width + 0.6, d = l.Depth + 0.6, h = 2.4, shape = new THREE.Shape(); shape.moveTo(-w / 2, 0); shape.lineTo(w / 2, 0); shape.lineTo(0, h); shape.closePath();
  const gable = new THREE.Mesh(new THREE.ExtrudeGeometry(shape, { depth: d, bevelEnabled: false }), shingle);
  gable.position.set(0, l.Height + 0.6, -d / 2); gable.castShadow = true; g.add(gable);
}

/** A sedan built from extruded side profiles: painted body, glass greenhouse, rims, lights. */
export function makeCarMesh(color, { brakeLights = null } = {}) {
  const g = new THREE.Group();
  const paint = new THREE.MeshPhysicalMaterial({ color, metalness: 0.55, roughness: 0.32, clearcoat: 1, clearcoatRoughness: 0.08 });
  const glass = new THREE.MeshPhysicalMaterial({ color: 0x11171f, metalness: 0.9, roughness: 0.06, transparent: true, opacity: 0.9 });
  const trim = new THREE.MeshStandardMaterial({ color: 0x15161a, roughness: 0.6 });
  const body = new THREE.Shape(); [[-2.25, 0.32], [-2.3, 0.78], [-2.0, 0.98], [2.0, 0.92], [2.28, 0.72], [2.25, 0.32]].forEach(([z, y], i) => (i ? body.lineTo(z, y) : body.moveTo(z, y)));
  const bodyGeo = new THREE.ExtrudeGeometry(body, { depth: 1.86, bevelEnabled: true, bevelSize: 0.06, bevelThickness: 0.06, bevelSegments: 2, curveSegments: 4 });
  bodyGeo.translate(0, 0, -0.93); bodyGeo.rotateY(Math.PI / 2);
  const bm = new THREE.Mesh(bodyGeo, paint); g.add(bm);
  const cab = new THREE.Shape(); [[-1.55, 0.96], [-1.0, 1.46], [0.55, 1.48], [1.2, 0.96]].forEach(([z, y], i) => (i ? cab.lineTo(z, y) : cab.moveTo(z, y)));
  const cabGeo = new THREE.ExtrudeGeometry(cab, { depth: 1.56, bevelEnabled: true, bevelSize: 0.04, bevelThickness: 0.04, bevelSegments: 1 });
  cabGeo.translate(0, 0, -0.78); cabGeo.rotateY(Math.PI / 2); g.add(new THREE.Mesh(cabGeo, glass));
  const roof = new THREE.Mesh(new THREE.BoxGeometry(1.5, 0.05, 1.45), paint); roof.position.set(0, 1.5, -0.22); g.add(roof);
  const bumperF = new THREE.Mesh(new THREE.BoxGeometry(1.9, 0.22, 0.14), trim); bumperF.position.set(0, 0.42, 2.3); g.add(bumperF);
  const bumperR = bumperF.clone(); bumperR.position.z = -2.33; g.add(bumperR);
  const head = new THREE.MeshStandardMaterial({ color: 0xffffff, emissive: 0xfff3d6, emissiveIntensity: 1.5 });
  const tail = new THREE.MeshStandardMaterial({ color: 0x550000, emissive: 0xff1a1a, emissiveIntensity: 0.8 });
  for (const s of [-1, 1]) {
    const hl = new THREE.Mesh(new THREE.BoxGeometry(0.42, 0.13, 0.05), head); hl.position.set(s * 0.68, 0.74, 2.3); g.add(hl);
    const tl = new THREE.Mesh(new THREE.BoxGeometry(0.46, 0.14, 0.05), tail); tl.position.set(s * 0.66, 0.76, -2.33); g.add(tl);
  }
  const tireGeo = new THREE.CylinderGeometry(0.36, 0.36, 0.26, 18); tireGeo.rotateZ(Math.PI / 2);
  const rimGeo = new THREE.CylinderGeometry(0.22, 0.22, 0.27, 12); rimGeo.rotateZ(Math.PI / 2);
  const tireMat = new THREE.MeshStandardMaterial({ color: 0x111111, roughness: 0.9 }), rimMat = new THREE.MeshStandardMaterial({ color: 0xc0c4c8, metalness: 0.95, roughness: 0.25 });
  const wheels = [];
  for (const [x, z] of [[-0.9, 1.42], [0.9, 1.42], [-0.9, -1.42], [0.9, -1.42]]) {
    const w = new THREE.Group(); w.position.set(x, 0.36, z); w.add(new THREE.Mesh(tireGeo, tireMat), new THREE.Mesh(rimGeo, rimMat)); g.add(w); wheels.push(w);
  }
  g.userData = { wheels, tail };
  g.traverse((o) => { if (o.isMesh) { o.castShadow = true; } });
  return g;
}

/** Fallback low-poly person (used only if the character models fail to load). */
export function makePerson(color, { skin = '#6b4a35' } = {}) {
  const g = new THREE.Group();
  const cloth = new THREE.MeshStandardMaterial({ color }), skinM = new THREE.MeshStandardMaterial({ color: skin }), dark = new THREE.MeshStandardMaterial({ color: 0x1a1a1d });
  const torso = new THREE.Mesh(new THREE.CapsuleGeometry(0.28, 0.55, 4, 8), cloth); torso.position.y = 1.15; g.add(torso);
  const head = new THREE.Mesh(new THREE.SphereGeometry(0.2, 12, 10), skinM); head.position.y = 1.72; g.add(head);
  const legs = new THREE.Mesh(new THREE.BoxGeometry(0.4, 0.8, 0.2), dark); legs.position.y = 0.4; g.add(legs);
  g.traverse((o) => { if (o.isMesh) o.castShadow = true; });
  return g;
}

/** Pushes a circle out of rectangles; returns the surface normal of the last hit (or null). */
export function collide(solids, pos, radius) {
  let normal = null;
  for (const s of solids) {
    if (Math.abs(pos.x - s.x) > s.hw + radius + 0.5 || Math.abs(pos.z - s.z) > s.hd + radius + 0.5) continue;
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
