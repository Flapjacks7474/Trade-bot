// Procedural PBR textures drawn at runtime (no third-party images). Each returns { map, normalMap, roughnessMap?, emissiveMap? }.
import * as THREE from 'three';

let seed = 1337;
const rnd = () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
const canvas = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };
function tex(c, { repeat = [1, 1], color = true } = {}) {
  const t = new THREE.CanvasTexture(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(...repeat); t.anisotropy = 8;
  if (color) t.colorSpace = THREE.SRGBColorSpace; return t;
}
/** Builds a tangent-space normal map from a grayscale height canvas (Sobel). */
function normalFrom(height, strength = 2) {
  const w = height.width, h = height.height, src = height.getContext('2d').getImageData(0, 0, w, h).data;
  const out = canvas(w, h), ctx = out.getContext('2d'), img = ctx.createImageData(w, h), d = img.data;
  const H = (x, y) => src[(((y + h) % h) * w + ((x + w) % w)) * 4] / 255;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const dx = (H(x + 1, y) - H(x - 1, y)) * strength, dy = (H(x, y + 1) - H(x, y - 1)) * strength;
    const l = Math.hypot(dx, dy, 1), i = (y * w + x) * 4;
    d[i] = (-dx / l * 0.5 + 0.5) * 255; d[i + 1] = (dy / l * 0.5 + 0.5) * 255; d[i + 2] = (1 / l * 0.5 + 0.5) * 255; d[i + 3] = 255;
  }
  ctx.putImageData(img, 0, 0); return out;
}
function speckle(ctx, w, h, n, colors, size = [1, 2]) {
  for (let i = 0; i < n; i++) { ctx.fillStyle = colors[Math.floor(rnd() * colors.length)]; const s = size[0] + rnd() * (size[1] - size[0]); ctx.fillRect(rnd() * w, rnd() * h, s, s); }
}
function blotches(ctx, w, h, n, color, r = [10, 60]) {
  for (let i = 0; i < n; i++) {
    const x = rnd() * w, y = rnd() * h, rad = r[0] + rnd() * (r[1] - r[0]);
    const g = ctx.createRadialGradient(x, y, 0, x, y, rad); g.addColorStop(0, color); g.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.fillStyle = g; ctx.fillRect(x - rad, y - rad, rad * 2, rad * 2);
  }
}

const cache = new Map();
const once = (k, f) => (cache.has(k) ? cache.get(k) : cache.set(k, f()).get(k));

export const asphalt = () => once('asphalt', () => {
  const S = 512, c = canvas(S, S), x = c.getContext('2d'), hgt = canvas(S, S), hx = hgt.getContext('2d');
  x.fillStyle = '#2c2d30'; x.fillRect(0, 0, S, S); hx.fillStyle = '#808080'; hx.fillRect(0, 0, S, S);
  blotches(x, S, S, 40, 'rgba(70,70,74,0.35)', [20, 90]); blotches(x, S, S, 25, 'rgba(15,15,18,0.4)', [15, 70]);
  speckle(x, S, S, 9000, ['#3d3e42', '#1d1e20', '#4a4a4c', '#28292b'], [1, 2.4]);
  speckle(hx, S, S, 9000, ['#9a9a9a', '#5c5c5c', '#a8a8a8'], [1, 2.4]);
  x.strokeStyle = 'rgba(10,10,12,0.7)'; hx.strokeStyle = '#3a3a3a';
  for (let i = 0; i < 7; i++) { // cracks
    let px = rnd() * S, py = rnd() * S; x.lineWidth = hx.lineWidth = 1 + rnd(); x.beginPath(); hx.beginPath(); x.moveTo(px, py); hx.moveTo(px, py);
    for (let k = 0; k < 12; k++) { px += (rnd() - 0.5) * 30; py += (rnd() - 0.5) * 30; x.lineTo(px, py); hx.lineTo(px, py); }
    x.stroke(); hx.stroke();
  }
  return { map: tex(c), normalMap: tex(normalFrom(hgt, 3), { color: false }) };
});

export const concrete = () => once('concrete', () => {
  const S = 512, c = canvas(S, S), x = c.getContext('2d'), hgt = canvas(S, S), hx = hgt.getContext('2d');
  x.fillStyle = '#9d9a93'; x.fillRect(0, 0, S, S); hx.fillStyle = '#909090'; hx.fillRect(0, 0, S, S);
  blotches(x, S, S, 30, 'rgba(120,115,105,0.35)'); blotches(x, S, S, 20, 'rgba(60,58,55,0.18)', [10, 40]);
  speckle(x, S, S, 5000, ['#aaa79f', '#8b8880', '#b5b2aa'], [1, 2]);
  x.strokeStyle = '#6f6c66'; hx.strokeStyle = '#2a2a2a'; x.lineWidth = hx.lineWidth = 3;
  for (let i = 0; i <= 2; i++) { const p = i * S / 2; x.strokeRect(p, -2, 0, S + 4); hx.strokeRect(p, -2, 0, S + 4); x.strokeRect(-2, p, S + 4, 0); hx.strokeRect(-2, p, S + 4, 0); }
  return { map: tex(c), normalMap: tex(normalFrom(hgt, 2.5), { color: false }) };
});

export const grass = () => once('grass', () => {
  const S = 512, c = canvas(S, S), x = c.getContext('2d'), hgt = canvas(S, S), hx = hgt.getContext('2d');
  x.fillStyle = '#4a5e33'; x.fillRect(0, 0, S, S); hx.fillStyle = '#707070'; hx.fillRect(0, 0, S, S);
  blotches(x, S, S, 50, 'rgba(110,120,60,0.35)', [20, 80]); blotches(x, S, S, 40, 'rgba(40,55,25,0.4)', [20, 70]);
  blotches(x, S, S, 12, 'rgba(120,100,60,0.3)', [10, 40]);
  for (let i = 0; i < 16000; i++) {
    const px = rnd() * S, py = rnd() * S, l = 2 + rnd() * 5, a = -Math.PI / 2 + (rnd() - 0.5) * 0.8;
    const g = 70 + rnd() * 70; x.strokeStyle = `rgba(${g * 0.6},${g},${g * 0.35},0.8)`; x.lineWidth = 1;
    x.beginPath(); x.moveTo(px, py); x.lineTo(px + Math.cos(a) * l, py + Math.sin(a) * l); x.stroke();
    hx.strokeStyle = `rgba(200,200,200,0.6)`; hx.beginPath(); hx.moveTo(px, py); hx.lineTo(px + Math.cos(a) * l, py + Math.sin(a) * l); hx.stroke();
  }
  return { map: tex(c), normalMap: tex(normalFrom(hgt, 1.5), { color: false }) };
});

/** Road markings strip: dashed yellow centre line and white edge lines, tiled along the road (v = length). */
export const roadMarkings = () => once('markings', () => {
  const c = canvas(128, 512), x = c.getContext('2d');
  x.clearRect(0, 0, 128, 512);
  x.fillStyle = 'rgba(232,188,62,0.92)'; x.fillRect(60, 0, 3, 300); x.fillRect(65, 0, 3, 300);
  x.fillStyle = 'rgba(235,235,230,0.85)'; x.fillRect(5, 0, 3, 512); x.fillRect(120, 0, 3, 512);
  speckle(x, 128, 512, 600, ['rgba(0,0,0,0.25)'], [1, 3]);
  return tex(c);
});

export const parkingLines = () => once('parking', () => {
  const c = canvas(256, 256), x = c.getContext('2d');
  x.fillStyle = 'rgba(235,235,230,0.85)'; for (let i = 0; i < 256; i += 42) x.fillRect(i, 0, 3, 110), x.fillRect(i, 146, 3, 110);
  return tex(c);
});

const WALLS = {
  brick: (x, w, h, base) => {
    x.fillStyle = '#5b4a40'; x.fillRect(0, 0, w, h);
    const bw = 16, bh = 6;
    for (let r = 0; r * bh < h; r++) for (let k = -1; k * bw < w; k++) {
      const off = r % 2 ? bw / 2 : 0, v = 0.82 + rnd() * 0.3, c = new THREE.Color(base).multiplyScalar(v);
      x.fillStyle = `#${c.getHexString()}`; x.fillRect(k * bw + off + 1, r * bh + 1, bw - 2, bh - 2);
    }
  },
  siding: (x, w, h, base) => {
    x.fillStyle = base; x.fillRect(0, 0, w, h);
    for (let y = 0; y < h; y += 7) { x.fillStyle = 'rgba(0,0,0,0.22)'; x.fillRect(0, y, w, 1); x.fillStyle = 'rgba(255,255,255,0.08)'; x.fillRect(0, y + 1, w, 1); }
  },
  stucco: (x, w, h, base) => {
    x.fillStyle = base; x.fillRect(0, 0, w, h);
    blotches(x, w, h, Math.ceil(w * h / 4000), 'rgba(0,0,0,0.08)', [8, 30]); speckle(x, w, h, w * h / 30, ['rgba(255,255,255,0.08)', 'rgba(0,0,0,0.08)'], [1, 2]);
  },
  metal: (x, w, h, base) => {
    x.fillStyle = base; x.fillRect(0, 0, w, h);
    for (let i = 0; i < w; i += 8) { x.fillStyle = 'rgba(0,0,0,0.25)'; x.fillRect(i, 0, 2, h); x.fillStyle = 'rgba(255,255,255,0.1)'; x.fillRect(i + 2, 0, 1, h); }
  }
};

/**
 * One facade texture set for a building: wall material, window grid on upper floors, optional storefront with
 * a lit sign. Pixels per metre = 16. Returns maps plus the pixel size so UVs can be set in metres.
 */
export function facade({ widthM, heightM, wall = 'brick', color = '#8a5a44', storefront = false, sign = null, signColor = '#ffcf6b', seed: s = 7, residential = false }) {
  seed = s * 7919 + 13;
  const P = 16, w = Math.min(1024, Math.ceil(widthM * P)), h = Math.min(512, Math.ceil(heightM * P));
  const c = canvas(w, h), x = c.getContext('2d'), e = canvas(w, h), ex = e.getContext('2d'), r = canvas(w, h), rx = r.getContext('2d');
  WALLS[wall](x, w, h, color);
  ex.fillStyle = '#000'; ex.fillRect(0, 0, w, h); rx.fillStyle = '#e0e0e0'; rx.fillRect(0, 0, w, h);
  const floorPx = 3.2 * P, groundPx = storefront ? 4.2 * P : 0;
  // Upper-floor windows
  for (let fy = h - groundPx - floorPx; fy > -1; fy -= floorPx) {
    const winW = (residential ? 1.3 : 1.5) * P, winH = 1.7 * P, gap = (residential ? 2.6 : 2.2) * P;
    for (let wx = gap / 2; wx + winW < w; wx += winW + gap) {
      const y = fy + 0.7 * P;
      x.fillStyle = '#d8d2c4'; x.fillRect(wx - 3, y - 3, winW + 6, winH + 6); // frame
      const glass = x.createLinearGradient(wx, y, wx + winW, y + winH); glass.addColorStop(0, '#273648'); glass.addColorStop(1, '#4e6a84');
      x.fillStyle = glass; x.fillRect(wx, y, winW, winH);
      x.fillStyle = 'rgba(255,255,255,0.15)'; x.fillRect(wx + 2, y + 2, winW * 0.35, winH - 4);
      x.fillStyle = '#c9c3b5'; x.fillRect(wx + winW / 2 - 1, y, 2, winH); x.fillRect(wx - 4, y + winH + 3, winW + 8, 3); // mullion + sill
      rx.fillStyle = '#202020'; rx.fillRect(wx, y, winW, winH);
      if (rnd() < 0.55) { ex.fillStyle = rnd() < 0.7 ? '#ffcf8a' : '#cfe3ff'; ex.globalAlpha = 0.6 + rnd() * 0.4; ex.fillRect(wx + 1, y + 1, winW - 2, winH - 2); ex.globalAlpha = 1; }
    }
    x.fillStyle = 'rgba(0,0,0,0.25)'; x.fillRect(0, fy, w, 2); // floor line
  }
  if (storefront) {
    const top = h - groundPx;
    x.fillStyle = '#1f2125'; x.fillRect(0, top, w, groundPx);
    const glassTop = top + 1.4 * P, glassH = groundPx - 1.6 * P;
    for (let gx = P * 0.6; gx < w - P; gx += 3.2 * P) {
      const gw = Math.min(2.8 * P, w - gx - P * 0.6);
      const g = x.createLinearGradient(0, glassTop, 0, glassTop + glassH); g.addColorStop(0, '#5a7088'); g.addColorStop(1, '#1d2834');
      x.fillStyle = g; x.fillRect(gx, glassTop, gw, glassH);
      x.fillStyle = 'rgba(255,255,255,0.12)'; x.fillRect(gx + 3, glassTop + 3, gw * 0.3, glassH - 6);
      rx.fillStyle = '#151515'; rx.fillRect(gx, glassTop, gw, glassH);
      ex.fillStyle = '#ffd9a0'; ex.globalAlpha = 0.85; ex.fillRect(gx + 2, glassTop + 2, gw - 4, glassH - 4); ex.globalAlpha = 1;
    }
    // Sign board
    if (sign) {
      const sh = 1.1 * P, sy = top + 0.15 * P;
      x.fillStyle = '#16171a'; x.fillRect(P * 0.5, sy, w - P, sh);
      x.font = `700 ${Math.floor(sh * 0.72)}px "Anton", "Arial Narrow", sans-serif`; x.textAlign = 'center'; x.textBaseline = 'middle';
      x.fillStyle = signColor; x.fillText(sign.toUpperCase(), w / 2, sy + sh / 2 + 1, w - P * 1.6);
      ex.font = x.font; ex.textAlign = 'center'; ex.textBaseline = 'middle'; ex.fillStyle = signColor; ex.fillText(sign.toUpperCase(), w / 2, sy + sh / 2 + 1, w - P * 1.6);
    }
  }
  // Grime at the base and under windows
  const grime = x.createLinearGradient(0, h - 1.2 * P, 0, h); grime.addColorStop(0, 'rgba(0,0,0,0)'); grime.addColorStop(1, 'rgba(20,15,10,0.35)');
  x.fillStyle = grime; x.fillRect(0, h - 1.2 * P, w, 1.2 * P);
  // Height map for normals: mortar/siding/frames from luminance.
  const hgt = canvas(w, h), hx = hgt.getContext('2d'); hx.filter = 'grayscale(1) contrast(1.4)'; hx.drawImage(c, 0, 0);
  return { map: tex(c), emissiveMap: tex(e), roughnessMap: tex(r, { color: false }), normalMap: tex(normalFrom(hgt, 1.6), { color: false }), w, h, P };
}
