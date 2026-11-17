// FIGHT FOR THE SOUTH - FFTSCore
// Story-side simulation: FINESS BANK, Face Card / Public Image, relationships, story flags, missions.
// Line-for-line port of WebPrototype/src/sim/story.js (itself a port of the C# FFTS.Simulation), same keys and rules,
// so saves and tests agree across C#, JS and C++. Engine-free: no exceptions, no RTTI, no Unreal types.
#pragma once
#include "FFTSBank.h"
#include "FFTSJson.h"
#include <cstdint>
#include <deque>
#include <functional>
#include <map>
#include <set>
#include <string>
#include <vector>

#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable : 4251 4275)
#endif

namespace ffts {

/** JS Math.round (halves round up, toward +infinity), used wherever the reference rounds. */
FFTSCORE_API int jsRound(double v);

// ------------------------------------------------------------------------------------------------- reputation
using StatMap = std::map<std::string, int>;

struct FFTSCORE_API RepChange { StatMap face, image; };

class FFTSCORE_API Reputation {
public:
    Reputation();
    void apply(const RepChange& ch);
    Json capture() const;
    void restore(const Json& s);
    StatMap face, image;
};

/** Reputation change for a fight result at a venue type (Underground / Sanctioned / Street). */
FFTSCORE_API RepChange fightReputation(const std::string& venue, bool won, const std::string& method);

// ------------------------------------------------------------------------------------------------- social
struct FFTSCORE_API Memory {
    std::string id, type, actor, target, location, source = "Witnessed";
    double reliability = 1.0;
    bool resolved = false;
};

struct FFTSCORE_API SocialEvent { std::string id, type, actor, target, location; std::vector<std::string> witnesses; };

class FFTSCORE_API Social {
public:
    static const std::vector<std::string>& relationKeys();   // Trust, Respect, Loyalty, Affection, Fear, Resentment, Rivalry, Friendship
    static const std::vector<std::string>& attitudes();      // Hostile .. Friendly, in order
    StatMap get(const std::string& npc, const std::string& target = "player") const;
    void modify(const std::string& npc, const StatMap& delta, const std::string& target = "player");
    void record(const SocialEvent& e);
    bool knows(const std::string& npc, const std::string& eventId) const;
    std::string attitude(const std::string& npc) const;
    Json capture() const;
    void restore(const Json& s);

    std::map<std::string, StatMap> rel;              // "npc>target" -> values
    std::map<std::string, StatMap> personality;      // npc -> traits
    std::map<std::string, std::vector<Memory>> memories;
    std::set<std::string> events;
};

// ------------------------------------------------------------------------------------------------- story
constexpr const char* RevealFlag = "story.chairman.identity_revealed";

class FFTSCORE_API Story {
public:
    int chapter = 1;
    std::map<std::string, int> flags;
    std::function<bool(const std::string&)> missionReached;
    bool has(const std::string& f) const { auto it = flags.find(f); return it != flags.end() && it->second != 0; }
    int value(const std::string& f) const { auto it = flags.find(f); return it == flags.end() ? 0 : it->second; }
    /** The Chairman reveal is refused before Chapter 7's "The Man Behind the Table" is reached. */
    bool set(const std::string& f, int v = 1);
    void advance(int ch) { if (ch > chapter && ch <= 8) chapter = ch; }
};

// ------------------------------------------------------------------------------------------------- missions
struct FFTSCORE_API MissionProgress {
    std::string status = "Locked";   // Locked, Available, Active, Completed, Failed
    std::vector<std::string> done;
    std::string branch, lastFailure;
    int attempts = 0;
    bool hasDone(const std::string& o) const;
};

struct FFTSCORE_API MissionLogEntry { std::string type, mission, text; };   // objective / completed / failed

class FFTSCORE_API Missions {
public:
    using GrantFn = std::function<void(const std::string& key, const Json& reward)>;
    Missions(const Json& missionDefs, Story& story, GrantFn grant);
    Missions(const Missions&) = delete;
    Missions& operator=(const Missions&) = delete;

    const std::string& status(const std::string& id) const;
    const Json* def(const std::string& id) const;
    MissionProgress* progress(const std::string& id);
    const MissionProgress* progress(const std::string& id) const;
    bool objectiveDone(const std::string& ref) const;   // "mission.id/objective_id"
    void refresh();
    void pump();
    bool wants(const std::string& trigger) const;
    int report(const std::string& trigger);
    bool choose(const std::string& missionId, const std::string& branchId);
    void give(const std::string& key, const Json& reward);
    Json capture() const;
    void restore(const Json& s);

    std::vector<Json> defs;                              // sorted by chapter, order
    std::map<std::string, MissionProgress> prog;
    std::set<std::string> granted;
    std::string tracked;
    std::vector<MissionLogEntry> log;

private:
    bool prereqs(const Json& d) const;
    bool complete(const Json& d, const std::string& objectiveId);
    void fail(const Json& d, const std::string& why);
    void tryComplete(const Json& d);
    Story& story_;
    GrantFn grant_;
};

} // namespace ffts

#ifdef _MSC_VER
#pragma warning(pop)
#endif
