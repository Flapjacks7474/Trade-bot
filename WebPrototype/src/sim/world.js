// Port of FFTS.LivingWorld: road graph + NPC schedules and travel (no teleporting; tiers only change update rate).
export class RoadGraph {
  constructor(layout) {
    this.nodes = []; this.adj = []; this.edges = [];
    const roads = layout.Roads;
    for (let i = 0; i < roads.length; i++) {
      const a = [roads[i].X1, roads[i].Z1], b = [roads[i].X2, roads[i].Z2], ts = [0, 1];
      for (let j = 0; j < roads.length; j++) {
        if (i === j) continue;
        const t = inter(a, b, [roads[j].X1, roads[j].Z1], [roads[j].X2, roads[j].Z2]);
        if (t != null) ts.push(t);
      }
      ts.sort((x, y) => x - y);
      let prev = -1;
      for (const t of ts) { const n = this.node([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t]); if (prev >= 0 && prev !== n) this.connect(prev, n); prev = n; }
    }
  }
  node(p) { const i = this.nodes.findIndex((q) => (q[0] - p[0]) ** 2 + (q[1] - p[1]) ** 2 < 1); if (i >= 0) return i; this.nodes.push(p); this.adj.push([]); return this.nodes.length - 1; }
  connect(a, b) { if (this.adj[a].includes(b)) return; this.adj[a].push(b); this.adj[b].push(a); this.edges.push([a, b]); }
  nearest(p) {
    let best = p, bd = Infinity, edge = -1;
    this.edges.forEach(([ia, ib], i) => {
      const a = this.nodes[ia], b = this.nodes[ib], ab = [b[0] - a[0], b[1] - a[1]], l2 = ab[0] ** 2 + ab[1] ** 2;
      const t = l2 <= 0 ? 0 : Math.max(0, Math.min(1, ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / l2));
      const q = [a[0] + ab[0] * t, a[1] + ab[1] * t], d = (p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2;
      if (d < bd) { bd = d; best = q; edge = i; }
    });
    return { point: best, edge };
  }
  path(from, to) {
    const out = [from];
    if (!this.edges.length) { out.push(to); return out; }
    const A = this.nearest(from), B = this.nearest(to);
    out.push(A.point);
    if (A.edge !== B.edge) {
      const n = this.nodes.length, dist = Array(n).fill(Infinity), prev = Array(n).fill(-1), done = Array(n).fill(false);
      const [a0, a1] = this.edges[A.edge], [b0, b1] = this.edges[B.edge];
      dist[a0] = d2(A.point, this.nodes[a0]); dist[a1] = d2(A.point, this.nodes[a1]);
      for (let it = 0; it < n; it++) {
        let u = -1, best = Infinity;
        for (let i = 0; i < n; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
        if (u < 0) break; done[u] = true;
        for (const v of this.adj[u]) { const nd = dist[u] + d2(this.nodes[u], this.nodes[v]); if (nd < dist[v]) { dist[v] = nd; prev[v] = u; } }
      }
      const end = dist[b0] + d2(this.nodes[b0], B.point) <= dist[b1] + d2(this.nodes[b1], B.point) ? b0 : b1;
      const chain = []; for (let c = end; c >= 0; c = prev[c]) chain.push(this.nodes[c]);
      out.push(...chain.reverse());
    }
    out.push(B.point, to);
    for (let i = out.length - 1; i > 0; i--) if (d2(out[i], out[i - 1]) < 0.1) out.splice(i, 1);
    return out;
  }
}
const d2 = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1]);
function inter(a, b, c, d) {
  const r = [b[0] - a[0], b[1] - a[1]], s = [d[0] - c[0], d[1] - c[1]], den = r[0] * s[1] - r[1] * s[0];
  if (Math.abs(den) < 1e-6) return null;
  const ac = [c[0] - a[0], c[1] - a[1]], t = (ac[0] * s[1] - ac[1] * s[0]) / den, u = (ac[0] * r[1] - ac[1] * r[0]) / den;
  return t >= -1e-4 && t <= 1 + 1e-4 && u >= -1e-4 && u <= 1 + 1e-4 ? t : null;
}

export function surfaceAt(layout, x, z) {
  for (const r of layout.Roads) {
    const dx = r.X2 - r.X1, dz = r.Z2 - r.Z1, l2 = dx * dx + dz * dz;
    const t = l2 <= 0 ? 0 : Math.max(0, Math.min(1, ((x - r.X1) * dx + (z - r.Z1) * dz) / l2));
    if ((r.X1 + t * dx - x) ** 2 + (r.Z1 + t * dz - z) ** 2 <= (r.Width / 2) ** 2) return 1;
  }
  for (const l of layout.Locations) if (l.Type === 'street' && Math.abs(x - l.X) <= l.Width / 2 && Math.abs(z - l.Z) <= l.Depth / 2) return 1;
  return 0.7;
}

const covers = (e, h) => (e.StartHour <= e.EndHour ? h >= e.StartHour && h < e.EndHour : h >= e.StartHour || h < e.EndHour);
const isPin = (e) => !!(e.MissionActive || (e.ObjectivesDone || []).length || (e.ObjectivesNotDone || []).length || (e.RequireFlags || []).length);

export class LivingWorld {
  constructor(profiles, layout, pinMet) {
    this.layout = layout; this.graph = new RoadGraph(layout); this.pinMet = pinMet;
    this.profiles = new Map(profiles.map((p) => [p.NpcId, p]));
    this.npcs = new Map(profiles.map((p) => [p.NpcId, { id: p.NpcId, x: 0, z: 0, loc: null, target: null, activity: 'home', status: 'At', path: [], pi: 0, heading: 0, held: false, acc: 0 }]));
  }
  desired(id, day, hour) {
    let best = null, bp = -Infinity;
    for (const e of this.profiles.get(id).Schedule) {
      if ((e.Days || []).length && !e.Days.includes(day % 7)) continue;
      if (!covers(e, hour)) continue;
      const pin = isPin(e);
      if (pin && !this.pinMet(e)) continue;
      const pr = (e.Priority || 0) + (pin ? 1000 : 0);
      if (pr > bp) { bp = pr; best = e; }
    }
    return best;
  }
  spot(locId, npcId) {
    const l = this.layout.Locations.find((x) => x.Id === locId); if (!l) return [0, 0];
    const c = [l.X, l.Z], road = this.graph.nearest(c).point;
    let dir = [road[0] - c[0], road[1] - c[1]]; const len = Math.hypot(...dir) || 1; dir = [dir[0] / len, dir[1] / len];
    const half = Math.abs(dir[0]) * l.Width / 2 + Math.abs(dir[1]) * l.Depth / 2;
    const door = [c[0] + dir[0] * (half + 2), c[1] + dir[1] * (half + 2)], side = [-dir[1], dir[0]];
    let h = 2166136261; for (const ch of npcId) { h ^= ch.charCodeAt(0); h = Math.imul(h, 16777619) >>> 0; }
    const slot = (h % 5) - 2;
    return [door[0] + side[0] * slot * 1.4, door[1] + side[1] * slot * 1.4];
  }
  init(day, hour) {
    for (const [id, n] of this.npcs) {
      const e = this.desired(id, day, hour), loc = e ? e.LocationId : this.profiles.get(id).HomeLocation, s = this.spot(loc, id);
      Object.assign(n, { x: s[0], z: s[1], loc, target: loc, activity: e ? e.Activity : 'home', status: 'At', path: [], pi: 0 });
    }
  }
  plan(day, hour) {
    for (const [id, n] of this.npcs) {
      const e = this.desired(id, day, hour); if (!e) continue;
      if (e.LocationId === n.target) { if (n.status === 'At') n.activity = e.Activity; continue; }
      n.target = e.LocationId; n.activity = e.Activity; n.loc = null;
      n.path = this.graph.path([n.x, n.z], this.spot(e.LocationId, id)); n.pi = 1;
      if (!n.held) n.status = 'Traveling';
    }
  }
  speed(id) { return this.profiles.get(id).Travel === 'Drive' ? 11 : 1.6; }
  step(sec, px, pz) {
    for (const [id, n] of this.npcs) {
      if (n.status !== 'Traveling') { n.acc = 0; continue; }
      const d = Math.hypot(n.x - px, n.z - pz), interval = d <= 60 ? 0 : d <= 250 ? 1 : 5;
      n.acc += sec; if (n.acc < interval) continue;
      let budget = this.speed(id) * n.acc; n.acc = 0;
      while (budget > 0 && n.pi < n.path.length) {
        const t = n.path[n.pi], dx = t[0] - n.x, dz = t[1] - n.z, dd = Math.hypot(dx, dz);
        if (dd > 0.001) n.heading = Math.atan2(dx, dz) * 180 / Math.PI;
        if (dd <= budget) { n.x = t[0]; n.z = t[1]; budget -= dd; n.pi++; } else { n.x += dx / dd * budget; n.z += dz / dd * budget; budget = 0; }
      }
      if (n.pi >= n.path.length) { n.status = 'At'; n.loc = n.target; n.path = []; }
    }
  }
  hold(id, on) { const n = this.npcs.get(id); if (!n) return; n.held = on; n.status = on ? 'Held' : n.loc ? 'At' : 'Traveling'; }
  near(x, z, r) { return [...this.npcs.values()].filter((n) => Math.hypot(n.x - x, n.z - z) <= r).map((n) => n.id); }
  capture() { return [...this.npcs.values()].map((n) => ({ ...n, held: false, status: n.status === 'Held' ? (n.loc ? 'At' : 'Traveling') : n.status })); }
  restore(list) { for (const s of list || []) if (this.npcs.has(s.id)) this.npcs.set(s.id, { ...s, acc: 0 }); }
}
