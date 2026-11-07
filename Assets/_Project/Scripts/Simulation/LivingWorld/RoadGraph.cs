using System;
using System.Collections.Generic;
using System.Numerics;
using FFTS.World;

namespace FFTS.LivingWorld
{
    /// <summary>
    /// Navigation graph built from a district's road centrelines: nodes at road ends and intersections.
    /// NPCs walk or drive along it, so they never cut through buildings or appear out of nowhere.
    /// </summary>
    public sealed class RoadGraph
    {
        private readonly List<Vector2> _nodes = new List<Vector2>();
        private readonly List<List<int>> _adj = new List<List<int>>();
        private readonly List<(int a, int b)> _edges = new List<(int, int)>();

        public IReadOnlyList<Vector2> Nodes => _nodes;
        public int EdgeCount => _edges.Count;

        public static RoadGraph Build(DistrictLayout layout)
        {
            var g = new RoadGraph();
            var roads = layout.Roads;
            for (int i = 0; i < roads.Count; i++)
            {
                var a = new Vector2(roads[i].X1, roads[i].Z1); var b = new Vector2(roads[i].X2, roads[i].Z2);
                var ts = new List<float> { 0f, 1f };
                for (int j = 0; j < roads.Count; j++)
                {
                    if (i == j) continue;
                    var c = new Vector2(roads[j].X1, roads[j].Z1); var d = new Vector2(roads[j].X2, roads[j].Z2);
                    if (Intersect(a, b, c, d, out float t)) ts.Add(t);
                }
                ts.Sort();
                int prev = -1;
                foreach (var t in ts)
                {
                    int n = g.NodeAt(Vector2.Lerp(a, b, t));
                    if (prev >= 0 && prev != n) g.Connect(prev, n);
                    prev = n;
                }
            }
            return g;
        }

        private int NodeAt(Vector2 p)
        {
            for (int i = 0; i < _nodes.Count; i++) if (Vector2.DistanceSquared(_nodes[i], p) < 1f) return i;
            _nodes.Add(p); _adj.Add(new List<int>());
            return _nodes.Count - 1;
        }

        private void Connect(int a, int b)
        {
            if (_adj[a].Contains(b)) return;
            _adj[a].Add(b); _adj[b].Add(a); _edges.Add((a, b));
        }

        private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t)
        {
            t = 0;
            var r = b - a; var s = d - c;
            float denom = r.X * s.Y - r.Y * s.X;
            if (Math.Abs(denom) < 1e-6f) return false;
            var ac = c - a;
            t = (ac.X * s.Y - ac.Y * s.X) / denom;
            float u = (ac.X * r.Y - ac.Y * r.X) / denom;
            return t >= -1e-4f && t <= 1 + 1e-4f && u >= -1e-4f && u <= 1 + 1e-4f;
        }

        /// <summary>Closest point on any road and the edge it lies on.</summary>
        public Vector2 NearestRoadPoint(Vector2 p, out int edge)
        {
            edge = -1; var best = p; float bestD = float.MaxValue;
            for (int i = 0; i < _edges.Count; i++)
            {
                var a = _nodes[_edges[i].a]; var b = _nodes[_edges[i].b];
                var ab = b - a; float len2 = ab.LengthSquared();
                float t = len2 <= 0 ? 0 : Math.Max(0, Math.Min(1, Vector2.Dot(p - a, ab) / len2));
                var q = a + ab * t;
                float d = Vector2.DistanceSquared(p, q);
                if (d < bestD) { bestD = d; best = q; edge = i; }
            }
            return best;
        }

        /// <summary>
        /// Route from one point to another via the nearest road points. Returns waypoints including both ends.
        /// Falls back to a straight line if the map has no roads.
        /// </summary>
        public List<Vector2> FindPath(Vector2 from, Vector2 to)
        {
            var path = new List<Vector2> { from };
            if (_edges.Count == 0) { path.Add(to); return path; }
            var pa = NearestRoadPoint(from, out int ea);
            var pb = NearestRoadPoint(to, out int eb);
            path.Add(pa);
            if (ea != eb)
            {
                // Dijkstra over graph nodes from both endpoints of edge ea to both endpoints of edge eb.
                int n = _nodes.Count;
                var dist = new float[n]; var prev = new int[n]; var done = new bool[n];
                for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
                var (a0, a1) = _edges[ea];
                dist[a0] = Vector2.Distance(pa, _nodes[a0]); dist[a1] = Vector2.Distance(pa, _nodes[a1]);
                var (b0, b1) = _edges[eb];
                for (int iter = 0; iter < n; iter++)
                {
                    int u = -1; float best = float.MaxValue;
                    for (int i = 0; i < n; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                    if (u < 0) break;
                    done[u] = true;
                    foreach (var v in _adj[u])
                    {
                        float nd = dist[u] + Vector2.Distance(_nodes[u], _nodes[v]);
                        if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
                    }
                }
                float viaB0 = dist[b0] + Vector2.Distance(_nodes[b0], pb), viaB1 = dist[b1] + Vector2.Distance(_nodes[b1], pb);
                int end = viaB0 <= viaB1 ? b0 : b1;
                if (dist[end] < float.MaxValue)
                {
                    var chain = new List<Vector2>();
                    for (int c = end; c >= 0; c = prev[c]) chain.Add(_nodes[c]);
                    chain.Reverse();
                    path.AddRange(chain);
                }
            }
            path.Add(pb);
            path.Add(to);
            // Drop zero-length steps.
            for (int i = path.Count - 1; i > 0; i--) if (Vector2.DistanceSquared(path[i], path[i - 1]) < 0.01f) path.RemoveAt(i);
            return path;
        }

        public static float Length(List<Vector2> path)
        {
            float l = 0;
            for (int i = 1; i < path.Count; i++) l += Vector2.Distance(path[i - 1], path[i]);
            return l;
        }
    }
}
