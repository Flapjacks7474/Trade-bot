// FIGHT FOR THE SOUTH - FFTSCore
// Living world: road graph built from southside_layout.json, NPC weekly schedules (with mission "pins"), and travel
// along roads (no teleporting; distance tiers only change how often far NPCs update). Port of WebPrototype/src/sim/world.js.
#pragma once
#include "FFTSJson.h"
#include <functional>
#include <map>
#include <string>
#include <vector>

#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable : 4251 4275)
#endif

namespace ffts {

struct FFTSCORE_API Vec2 { double x = 0, z = 0; };

class FFTSCORE_API RoadGraph {
public:
    explicit RoadGraph(const Json& layout);
    struct Hit { Vec2 point; int edge = -1; };
    Hit nearest(Vec2 p) const;
    std::vector<Vec2> path(Vec2 from, Vec2 to) const;
    std::vector<Vec2> nodes;
    std::vector<std::vector<int>> adj;
    std::vector<std::pair<int, int>> edges;
private:
    int node(Vec2 p);
    void connect(int a, int b);
};

/** 1 on a road or street lot, 0.7 elsewhere (grip multiplier for the driving model). */
FFTSCORE_API double surfaceAt(const Json& layout, double x, double z);

struct FFTSCORE_API WorldNpc {
    std::string id, loc, target, activity = "home", status = "At";   // status: At, Traveling, Held
    Vec2 pos;
    std::vector<Vec2> path;
    size_t pi = 0;
    double heading = 0, acc = 0;
    bool held = false;
};

class FFTSCORE_API LivingWorld {
public:
    using PinFn = std::function<bool(const Json& scheduleEntry)>;
    LivingWorld(const Json& profiles, const Json& layout, PinFn pinMet);
    const Json* desired(const std::string& id, int day, int hour) const;
    Vec2 spot(const std::string& locationId, const std::string& npcId) const;
    void init(int day, int hour);
    void plan(int day, int hour);
    void step(double sec, double px, double pz);
    void hold(const std::string& id, bool on);
    std::vector<std::string> nearby(double x, double z, double r) const;   // not "near": a Windows header macro
    WorldNpc* npc(const std::string& id) { auto it = npcs.find(id); return it == npcs.end() ? nullptr : &it->second; }
    Json capture() const;
    void restore(const Json& list);

    std::map<std::string, WorldNpc> npcs;
private:
    double speed(const std::string& id) const;
    const Json* profile(const std::string& id) const;
    Json layout_;
    RoadGraph graph_;
    std::vector<Json> profiles_;
    PinFn pinMet_;
};

} // namespace ffts

#ifdef _MSC_VER
#pragma warning(pop)
#endif
