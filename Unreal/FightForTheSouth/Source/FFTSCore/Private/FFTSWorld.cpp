#include "FFTSWorld.h"
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <limits>

namespace ffts {

namespace {
double dist(Vec2 a, Vec2 b) { return std::hypot(a.x - b.x, a.z - b.z); }
bool intersect(Vec2 a, Vec2 b, Vec2 c, Vec2 d, double& t) {
    const double rx = b.x - a.x, rz = b.z - a.z, sx = d.x - c.x, sz = d.z - c.z, den = rx * sz - rz * sx;
    if (std::fabs(den) < 1e-6) return false;
    const double acx = c.x - a.x, acz = c.z - a.z;
    t = (acx * sz - acz * sx) / den;
    const double u = (acx * rz - acz * rx) / den;
    return t >= -1e-4 && t <= 1 + 1e-4 && u >= -1e-4 && u <= 1 + 1e-4;
}
bool covers(const Json& e, int h) {
    const int s = e["StartHour"].integer(), en = e["EndHour"].integer();
    return s <= en ? (h >= s && h < en) : (h >= s || h < en);
}
bool isPin(const Json& e) {
    return !e["MissionActive"].str().empty() || e["ObjectivesDone"].size() || e["ObjectivesNotDone"].size() || e["RequireFlags"].size();
}
}

// ================================================================================================= roads
RoadGraph::RoadGraph(const Json& layout) {
    const auto& roads = layout["Roads"].items();
    for (size_t i = 0; i < roads.size(); ++i) {
        const Vec2 a{ roads[i]["X1"].num(), roads[i]["Z1"].num() }, b{ roads[i]["X2"].num(), roads[i]["Z2"].num() };
        std::vector<double> ts = { 0.0, 1.0 };
        for (size_t j = 0; j < roads.size(); ++j) {
            if (i == j) continue;
            double t = 0;
            if (intersect(a, b, { roads[j]["X1"].num(), roads[j]["Z1"].num() }, { roads[j]["X2"].num(), roads[j]["Z2"].num() }, t)) ts.push_back(t);
        }
        std::sort(ts.begin(), ts.end());
        int prev = -1;
        for (double t : ts) {
            const int n = node({ a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t });
            if (prev >= 0 && prev != n) connect(prev, n);
            prev = n;
        }
    }
}

int RoadGraph::node(Vec2 p) {
    for (size_t i = 0; i < nodes.size(); ++i)
        if ((nodes[i].x - p.x) * (nodes[i].x - p.x) + (nodes[i].z - p.z) * (nodes[i].z - p.z) < 1) return static_cast<int>(i);
    nodes.push_back(p); adj.emplace_back();
    return static_cast<int>(nodes.size()) - 1;
}

void RoadGraph::connect(int a, int b) {
    if (std::find(adj[a].begin(), adj[a].end(), b) != adj[a].end()) return;
    adj[a].push_back(b); adj[b].push_back(a); edges.emplace_back(a, b);
}

RoadGraph::Hit RoadGraph::nearest(Vec2 p) const {
    Hit best{ p, -1 }; double bd = std::numeric_limits<double>::infinity();
    for (size_t i = 0; i < edges.size(); ++i) {
        const Vec2 a = nodes[edges[i].first], b = nodes[edges[i].second];
        const double abx = b.x - a.x, abz = b.z - a.z, l2 = abx * abx + abz * abz;
        const double t = l2 <= 0 ? 0 : std::max(0.0, std::min(1.0, ((p.x - a.x) * abx + (p.z - a.z) * abz) / l2));
        const Vec2 q{ a.x + abx * t, a.z + abz * t };
        const double d = (p.x - q.x) * (p.x - q.x) + (p.z - q.z) * (p.z - q.z);
        if (d < bd) { bd = d; best = { q, static_cast<int>(i) }; }
    }
    return best;
}

std::vector<Vec2> RoadGraph::path(Vec2 from, Vec2 to) const {
    std::vector<Vec2> out = { from };
    if (edges.empty()) { out.push_back(to); return out; }
    const Hit A = nearest(from), B = nearest(to);
    out.push_back(A.point);
    if (A.edge != B.edge) {
        const size_t n = nodes.size();
        const double inf = std::numeric_limits<double>::infinity();
        std::vector<double> d(n, inf); std::vector<int> prev(n, -1); std::vector<bool> done(n, false);
        const int a0 = edges[A.edge].first, a1 = edges[A.edge].second, b0 = edges[B.edge].first, b1 = edges[B.edge].second;
        d[a0] = dist(A.point, nodes[a0]); d[a1] = dist(A.point, nodes[a1]);
        for (size_t it = 0; it < n; ++it) {
            int u = -1; double best = inf;
            for (size_t i = 0; i < n; ++i) if (!done[i] && d[i] < best) { best = d[i]; u = static_cast<int>(i); }
            if (u < 0) break;
            done[u] = true;
            for (int v : adj[u]) { const double nd = d[u] + dist(nodes[u], nodes[v]); if (nd < d[v]) { d[v] = nd; prev[v] = u; } }
        }
        const int end = d[b0] + dist(nodes[b0], B.point) <= d[b1] + dist(nodes[b1], B.point) ? b0 : b1;
        std::vector<Vec2> chain;
        for (int c = end; c >= 0; c = prev[c]) chain.push_back(nodes[c]);
        out.insert(out.end(), chain.rbegin(), chain.rend());
    }
    out.push_back(B.point); out.push_back(to);
    for (size_t i = out.size() - 1; i > 0; --i) if (dist(out[i], out[i - 1]) < 0.1) out.erase(out.begin() + static_cast<std::ptrdiff_t>(i));
    return out;
}

double surfaceAt(const Json& layout, double x, double z) {
    for (const Json& r : layout["Roads"].items()) {
        const double dx = r["X2"].num() - r["X1"].num(), dz = r["Z2"].num() - r["Z1"].num(), l2 = dx * dx + dz * dz;
        const double t = l2 <= 0 ? 0 : std::max(0.0, std::min(1.0, ((x - r["X1"].num()) * dx + (z - r["Z1"].num()) * dz) / l2));
        const double ex = r["X1"].num() + t * dx - x, ez = r["Z1"].num() + t * dz - z, hw = r["Width"].num() / 2;
        if (ex * ex + ez * ez <= hw * hw) return 1;
    }
    for (const Json& l : layout["Locations"].items())
        if (l["Type"].str() == "street" && std::fabs(x - l["X"].num()) <= l["Width"].num() / 2 && std::fabs(z - l["Z"].num()) <= l["Depth"].num() / 2) return 1;
    return 0.7;
}

// ================================================================================================= NPCs
LivingWorld::LivingWorld(const Json& profiles, const Json& layout, PinFn pinMet)
    : layout_(layout), graph_(layout), profiles_(profiles.items()), pinMet_(std::move(pinMet)) {
    for (const Json& p : profiles_) { WorldNpc n; n.id = p["NpcId"].str(); npcs[n.id] = n; }
}

const Json* LivingWorld::profile(const std::string& id) const {
    for (const Json& p : profiles_) if (p["NpcId"].str() == id) return &p;
    return nullptr;
}

const Json* LivingWorld::desired(const std::string& id, int day, int hour) const {
    const Json* prof = profile(id); if (!prof) return nullptr;
    const Json* best = nullptr; double bp = -std::numeric_limits<double>::infinity();
    for (const Json& e : (*prof)["Schedule"].items()) {
        if (e["Days"].size()) {
            bool today = false; for (const Json& d : e["Days"].items()) if (d.integer() == day % 7) today = true;
            if (!today) continue;
        }
        if (!covers(e, hour)) continue;
        const bool pin = isPin(e);
        if (pin && !(pinMet_ && pinMet_(e))) continue;
        const double pr = e["Priority"].num() + (pin ? 1000 : 0);
        if (pr > bp) { bp = pr; best = &e; }
    }
    return best;
}

Vec2 LivingWorld::spot(const std::string& locId, const std::string& npcId) const {
    const Json* l = nullptr;
    for (const Json& x : layout_["Locations"].items()) if (x["Id"].str() == locId) { l = &x; break; }
    if (!l) return {};
    const Vec2 c{ (*l)["X"].num(), (*l)["Z"].num() }, road = graph_.nearest(c).point;
    double dx = road.x - c.x, dz = road.z - c.z; double len = std::hypot(dx, dz); if (len == 0) len = 1;
    dx /= len; dz /= len;
    const double half = std::fabs(dx) * (*l)["Width"].num() / 2 + std::fabs(dz) * (*l)["Depth"].num() / 2;
    const Vec2 door{ c.x + dx * (half + 2), c.z + dz * (half + 2) };
    const double sx = -dz, sz = dx;
    uint32_t h = 2166136261u;
    for (unsigned char ch : npcId) { h ^= ch; h *= 16777619u; }   // FNV-1a, matches the JS Math.imul version for ASCII ids
    const int slot = static_cast<int>(h % 5) - 2;
    return { door.x + sx * slot * 1.4, door.z + sz * slot * 1.4 };
}

void LivingWorld::init(int day, int hour) {
    for (auto& kv : npcs) {
        WorldNpc& n = kv.second;
        const Json* e = desired(kv.first, day, hour);
        const std::string loc = e ? (*e)["LocationId"].str() : (*profile(kv.first))["HomeLocation"].str();
        n.pos = spot(loc, kv.first); n.loc = loc; n.target = loc;
        n.activity = e ? (*e)["Activity"].str() : "home"; n.status = "At"; n.path.clear(); n.pi = 0;
    }
}

void LivingWorld::plan(int day, int hour) {
    for (auto& kv : npcs) {
        WorldNpc& n = kv.second;
        const Json* e = desired(kv.first, day, hour); if (!e) continue;
        const std::string& loc = (*e)["LocationId"].str();
        if (loc == n.target) { if (n.status == "At") n.activity = (*e)["Activity"].str(); continue; }
        n.target = loc; n.activity = (*e)["Activity"].str(); n.loc.clear();
        n.path = graph_.path(n.pos, spot(loc, kv.first)); n.pi = 1;
        if (!n.held) n.status = "Traveling";
    }
}

double LivingWorld::speed(const std::string& id) const { const Json* p = profile(id); return p && (*p)["Travel"].str() == "Drive" ? 11 : 1.6; }

void LivingWorld::step(double sec, double px, double pz) {
    for (auto& kv : npcs) {
        WorldNpc& n = kv.second;
        if (n.status != "Traveling") { n.acc = 0; continue; }
        const double d = std::hypot(n.pos.x - px, n.pos.z - pz), interval = d <= 60 ? 0 : d <= 250 ? 1 : 5;
        n.acc += sec; if (n.acc < interval) continue;
        double budget = speed(kv.first) * n.acc; n.acc = 0;
        while (budget > 0 && n.pi < n.path.size()) {
            const Vec2 t = n.path[n.pi]; const double dx = t.x - n.pos.x, dz = t.z - n.pos.z, dd = std::hypot(dx, dz);
            if (dd > 0.001) n.heading = std::atan2(dx, dz) * 180 / 3.14159265358979323846;
            if (dd <= budget) { n.pos = t; budget -= dd; ++n.pi; }
            else { n.pos.x += dx / dd * budget; n.pos.z += dz / dd * budget; budget = 0; }
        }
        if (n.pi >= n.path.size()) { n.status = "At"; n.loc = n.target; n.path.clear(); }
    }
}

void LivingWorld::hold(const std::string& id, bool on) {
    WorldNpc* n = npc(id); if (!n) return;
    n->held = on;
    n->status = on ? "Held" : (!n->loc.empty() ? "At" : "Traveling");
}

std::vector<std::string> LivingWorld::nearby(double x, double z, double r) const {
    std::vector<std::string> out;
    for (const auto& kv : npcs) if (std::hypot(kv.second.pos.x - x, kv.second.pos.z - z) <= r) out.push_back(kv.first);
    return out;
}

Json LivingWorld::capture() const {
    Json list = Json::makeArray();
    for (const auto& kv : npcs) {
        const WorldNpc& n = kv.second;
        Json path = Json::makeArray();
        for (const Vec2& p : n.path) path.push(Json::makeArray().push(Json::makeNumber(p.x)).push(Json::makeNumber(p.z)));
        const std::string status = n.status == "Held" ? (!n.loc.empty() ? "At" : "Traveling") : n.status;
        list.push(Json::makeObject().set("id", Json::makeString(n.id)).set("x", Json::makeNumber(n.pos.x)).set("z", Json::makeNumber(n.pos.z))
                  .set("loc", Json::makeString(n.loc)).set("target", Json::makeString(n.target)).set("activity", Json::makeString(n.activity))
                  .set("status", Json::makeString(status)).set("path", path).set("pi", Json::makeNumber(static_cast<double>(n.pi)))
                  .set("heading", Json::makeNumber(n.heading)));
    }
    return list;
}

void LivingWorld::restore(const Json& list) {
    for (const Json& s : list.items()) {
        WorldNpc* n = npc(s["id"].str()); if (!n) continue;
        n->pos = { s["x"].num(), s["z"].num() }; n->loc = s["loc"].str(); n->target = s["target"].str();
        n->activity = s["activity"].strOr("home"); n->status = s["status"].strOr("At"); n->heading = s["heading"].num();
        n->path.clear(); for (const Json& p : s["path"].items()) n->path.push_back({ p[0].num(), p[1].num() });
        n->pi = static_cast<size_t>(s["pi"].integer()); n->held = false; n->acc = 0;
    }
}

} // namespace ffts
