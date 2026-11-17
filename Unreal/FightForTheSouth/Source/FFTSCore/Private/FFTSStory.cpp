#include "FFTSStory.h"
#include <algorithm>
#include <cmath>
#include <cstdio>

namespace ffts {

int jsRound(double v) { return static_cast<int>(std::floor(v + 0.5)); }

namespace {
int clampi(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }
std::vector<std::string> strings(const Json& arr) { std::vector<std::string> out; for (const Json& v : arr.items()) out.push_back(v.str()); return out; }
Json stringArray(const std::vector<std::string>& v) { Json a = Json::makeArray(); for (const auto& s : v) a.push(Json::makeString(s)); return a; }
Json statObject(const StatMap& m) { Json o = Json::makeObject(); for (const auto& kv : m) o.set(kv.first, Json::makeNumber(kv.second)); return o; }
void readStats(StatMap& m, const Json& o) { for (const auto& kv : o.members()) m[kv.first] = kv.second.integer(); }
int trait(const std::map<std::string, StatMap>& personality, const std::string& npc, const char* name) {
    auto p = personality.find(npc); if (p == personality.end()) return 50;
    auto t = p->second.find(name); return t == p->second.end() ? 50 : t->second;
}
}

// ================================================================================================= reputation
Reputation::Reputation() {
    face = { { "Respect", 0 }, { "Fear", 0 }, { "Loyalty", 0 }, { "Influence", 0 }, { "Notoriety", 0 }, { "ThreatLevel", 0 } };
    image = { { "Popularity", 0 }, { "Credibility", 20 }, { "Controversy", 0 }, { "Professionalism", 50 }, { "FanLoyalty", 0 }, { "IndustryRelationships", 0 } };
}

void Reputation::apply(const RepChange& ch) {
    for (const auto& kv : ch.face) face[kv.first] = clampi(face[kv.first] + kv.second, 0, 100);
    for (const auto& kv : ch.image) image[kv.first] = clampi(image[kv.first] + kv.second, 0, 100);
}

Json Reputation::capture() const { return Json::makeObject().set("face", statObject(face)).set("image", statObject(image)); }
void Reputation::restore(const Json& s) { readStats(face, s["face"]); readStats(image, s["image"]); }

RepChange fightReputation(const std::string& venue, bool won, const std::string& method) {
    RepChange ch; const bool fin = method == "Finisher";
    if (venue == "Underground") {
        if (won) { ch.face = { { "Respect", 4 }, { "Fear", fin ? 4 : 2 }, { "Notoriety", 2 } }; ch.image["Professionalism"] = -1; }
        else ch.face["Respect"] = -2;
    } else if (venue == "Sanctioned") {
        if (won) { ch.face["Respect"] = 2; ch.image = { { "Popularity", 3 }, { "Professionalism", 1 } }; }
        else ch.image["Popularity"] = -1;
    } else {
        if (won) { ch.face = { { "Notoriety", 3 }, { "Fear", 2 } }; ch.image = { { "Credibility", -2 }, { "Professionalism", -3 }, { "Controversy", 2 } }; }
        else { ch.face["Respect"] = -3; ch.image["Controversy"] = 1; }
    }
    return ch;
}

// ================================================================================================= social
const std::vector<std::string>& Social::relationKeys() {
    static const std::vector<std::string> k = { "Trust", "Respect", "Loyalty", "Affection", "Fear", "Resentment", "Rivalry", "Friendship" };
    return k;
}
const std::vector<std::string>& Social::attitudes() {
    static const std::vector<std::string> a = { "Hostile", "Intimidated", "Wary", "Neutral", "Warm", "Friendly" };
    return a;
}

StatMap Social::get(const std::string& npc, const std::string& target) const {
    StatMap v; for (const auto& k : relationKeys()) v[k] = 0;
    auto it = rel.find(npc + ">" + target);
    if (it != rel.end()) for (const auto& kv : it->second) v[kv.first] = kv.second;
    return v;
}

void Social::modify(const std::string& npc, const StatMap& d, const std::string& target) {
    const int temper = trait(personality, npc, "Temper"), patience = trait(personality, npc, "Patience"), courage = trait(personality, npc, "Courage");
    StatMap v = get(npc, target);
    for (const auto& k : relationKeys()) {
        auto it = d.find(k); int x = it == d.end() ? 0 : it->second;
        if (k == "Resentment") x = jsRound(x * (0.6 + temper / 100.0 - (patience - 50) / 200.0));
        if (k == "Fear") x = jsRound(x * (1.4 - courage / 100.0));
        v[k] = clampi(v[k] + x, -100, 100);
    }
    rel[npc + ">" + target] = v;
}

void Social::record(const SocialEvent& e) {
    if (events.count(e.id)) return;
    events.insert(e.id);
    for (const auto& w : e.witnesses) {
        Memory m; m.id = e.id; m.type = e.type; m.actor = e.actor; m.target = e.target; m.location = e.location;
        memories[w].push_back(m);
    }
}

bool Social::knows(const std::string& npc, const std::string& eventId) const {
    auto it = memories.find(npc); if (it == memories.end()) return false;
    for (const Memory& m : it->second) if (m.id == eventId) return true;
    return false;
}

std::string Social::attitude(const std::string& npc) const {
    static const std::map<std::string, int> valence = { { "player.helped", 20 }, { "player.protected", 30 }, { "player.won_fight", 0 },
                                                        { "player.humiliated", -30 }, { "player.attacked", -40 }, { "player.betrayed", -60 } };
    const StatMap v = get(npc);
    double s = v.at("Trust") + v.at("Friendship") + v.at("Respect") * 0.5 + v.at("Affection") * 0.5 + v.at("Loyalty") * 0.3 - v.at("Resentment") - v.at("Rivalry") * 0.5;
    const double temper = 0.5 + trait(personality, npc, "Temper") / 100.0;
    auto mem = memories.find(npc);
    if (mem != memories.end())
        for (const Memory& m : mem->second) {
            auto val = valence.find(m.type);
            if (m.resolved || m.actor != "player" || val == valence.end()) continue;
            const double w = (m.target == npc ? 1.0 : 0.4) * m.reliability;
            s += val->second < 0 ? val->second * w * temper : val->second * w;
        }
    if (v.at("Fear") >= 40) return trait(personality, npc, "Courage") >= 60 ? "Hostile" : "Intimidated";
    if (s <= -60) return "Hostile";
    if (s <= -15) return "Wary";
    if (s < 20) return "Neutral";
    if (s < 60) return "Warm";
    return "Friendly";
}

Json Social::capture() const {
    Json r = Json::makeObject(); for (const auto& kv : rel) r.set(kv.first, statObject(kv.second));
    Json mem = Json::makeObject();
    for (const auto& kv : memories) {
        Json list = Json::makeArray();
        for (const Memory& m : kv.second)
            list.push(Json::makeObject().set("id", Json::makeString(m.id)).set("type", Json::makeString(m.type)).set("actor", Json::makeString(m.actor))
                      .set("target", Json::makeString(m.target)).set("location", Json::makeString(m.location)).set("source", Json::makeString(m.source))
                      .set("reliability", Json::makeNumber(m.reliability)).set("resolved", Json::makeBool(m.resolved)));
        mem.set(kv.first, list);
    }
    Json ev = Json::makeArray(); for (const auto& e : events) ev.push(Json::makeString(e));
    return Json::makeObject().set("rel", r).set("memories", mem).set("events", ev);
}

void Social::restore(const Json& s) {
    rel.clear(); for (const auto& kv : s["rel"].members()) readStats(rel[kv.first], kv.second);
    memories.clear();
    for (const auto& kv : s["memories"].members())
        for (const Json& j : kv.second.items()) {
            Memory m; m.id = j["id"].str(); m.type = j["type"].str(); m.actor = j["actor"].str(); m.target = j["target"].str();
            m.location = j["location"].str(); m.source = j["source"].strOr("Witnessed"); m.reliability = j["reliability"].num(1.0); m.resolved = j["resolved"].boolean();
            memories[kv.first].push_back(m);
        }
    events.clear(); for (const Json& e : s["events"].items()) events.insert(e.str());
}

// ================================================================================================= story
bool Story::set(const std::string& f, int v) {
    if (f == RevealFlag && (chapter < 7 || !missionReached || !missionReached("mission.ch7.man_behind_table"))) return false;
    flags[f] = v;
    return true;
}

// ================================================================================================= missions
bool MissionProgress::hasDone(const std::string& o) const { return std::find(done.begin(), done.end(), o) != done.end(); }

Missions::Missions(const Json& missionDefs, Story& story, GrantFn grant) : story_(story), grant_(std::move(grant)) {
    defs = missionDefs.items();
    std::stable_sort(defs.begin(), defs.end(), [](const Json& a, const Json& b) {
        if (a["Chapter"].integer() != b["Chapter"].integer()) return a["Chapter"].integer() < b["Chapter"].integer();
        return a["Order"].integer() < b["Order"].integer(); });
    for (const Json& d : defs) prog[d["Id"].str()] = MissionProgress();
    story_.missionReached = [this](const std::string& id) { const std::string& s = status(id); return s == "Active" || s == "Completed"; };
    refresh();
}

const std::string& Missions::status(const std::string& id) const {
    static const std::string locked = "Locked";
    auto it = prog.find(id); return it == prog.end() ? locked : it->second.status;
}
const Json* Missions::def(const std::string& id) const { for (const Json& d : defs) if (d["Id"].str() == id) return &d; return nullptr; }
MissionProgress* Missions::progress(const std::string& id) { auto it = prog.find(id); return it == prog.end() ? nullptr : &it->second; }
const MissionProgress* Missions::progress(const std::string& id) const { auto it = prog.find(id); return it == prog.end() ? nullptr : &it->second; }

bool Missions::objectiveDone(const std::string& ref) const {
    const size_t slash = ref.find('/');
    const MissionProgress* p = progress(ref.substr(0, slash));
    if (!p) return false;
    return p->status == "Completed" || (slash != std::string::npos && p->hasDone(ref.substr(slash + 1)));
}

bool Missions::prereqs(const Json& d) const {
    const Json& pre = d["Prerequisites"];
    const int need = std::max(pre["MinChapter"].integer(1) ? pre["MinChapter"].integer(1) : 1, d["Category"].str() == "Main" ? d["Chapter"].integer() : 0);
    if (story_.chapter < need) return false;
    for (const Json& m : pre["MissionsCompleted"].items()) if (status(m.str()) != "Completed") return false;
    for (const auto& kv : pre["FlagsAtLeast"].members()) if (story_.value(kv.first) < kv.second.integer()) return false;
    for (const Json& f : pre["FlagsAbsent"].items()) if (story_.has(f.str())) return false;
    return true;
}

void Missions::refresh() {
    for (const Json& d : defs) { MissionProgress& p = prog[d["Id"].str()]; if (p.status == "Locked" && prereqs(d)) p.status = "Available"; }
}

void Missions::pump() {
    for (int g = 0; g < 8; ++g) {
        bool changed = false;
        for (const Json& d : defs) {
            MissionProgress& p = prog[d["Id"].str()];
            const std::string& cat = d["Category"].str(), rec = d["Recovery"].str();
            if ((p.status == "Available" && (cat == "Main" || cat == "Side")) || (p.status == "Failed" && rec != "FailPermanently")) {
                if (p.status == "Failed" && rec == "RestartMission") p.done.clear();
                p.status = "Active"; ++p.attempts; changed = true;
                if (cat == "Main") tracked = d["Id"].str();
            }
        }
        if (!changed) break;
    }
    if (tracked.empty() || status(tracked) != "Active") {
        tracked.clear();
        for (const Json& d : defs) if (d["Category"].str() == "Main" && status(d["Id"].str()) == "Active") { tracked = d["Id"].str(); break; }
    }
}

bool Missions::wants(const std::string& trigger) const {
    for (const Json& d : defs) {
        const MissionProgress* p = progress(d["Id"].str());
        if (!p || p->status != "Active") continue;
        for (const Json& o : d["Objectives"].items())
            if (o["Trigger"].str() == trigger && !p->hasDone(o["Id"].str()) && (o["Branch"].str().empty() || o["Branch"].str() == p->branch)) return true;
    }
    return false;
}

int Missions::report(const std::string& trigger) {
    int n = 0;
    for (const Json& d : defs) {
        if (status(d["Id"].str()) != "Active") continue;
        bool failed = false;
        for (const Json& f : d["FailureConditions"].items()) if (f.str() == trigger) { fail(d, trigger); failed = true; break; }
        if (failed) continue;
        for (const Json& o : d["Objectives"].items())
            if (o["Trigger"].str() == trigger && complete(d, o["Id"].str())) ++n;
    }
    pump();
    return n;
}

bool Missions::complete(const Json& d, const std::string& oid) {
    MissionProgress& p = prog[d["Id"].str()];
    const Json* o = nullptr;
    for (const Json& x : d["Objectives"].items()) if (x["Id"].str() == oid) { o = &x; break; }
    if (!o || p.hasDone(oid) || (!(*o)["Branch"].str().empty() && (*o)["Branch"].str() != p.branch)) return false;
    p.done.push_back(oid);
    log.push_back({ "objective", d["Id"].str(), (*o)["Text"].str() });
    tryComplete(d);
    return true;
}

bool Missions::choose(const std::string& mid, const std::string& bid) {
    const Json* d = def(mid); MissionProgress* p = progress(mid);
    if (!d || !p || p->status != "Active" || !p->branch.empty()) return false;
    const Json* b = nullptr;
    for (const Json& x : (*d)["Branches"].items()) if (x["Id"].str() == bid) { b = &x; break; }
    if (!b) return false;
    p->branch = bid;
    const auto& effects = (*b)["Effects"].items();
    for (size_t i = 0; i < effects.size(); ++i) give("branch." + mid + "." + bid + "." + std::to_string(i), effects[i]);
    tryComplete(*d);
    pump();
    return true;
}

void Missions::fail(const Json& d, const std::string& why) {
    MissionProgress& p = prog[d["Id"].str()];
    p.lastFailure = why;
    if (d["Recovery"].str() == "RestartFromCheckpoint") {
        std::vector<std::string> keep;
        for (const auto& id : p.done)
            for (const Json& o : d["Objectives"].items()) if (o["Id"].str() == id && o["Checkpoint"].boolean()) { keep.push_back(id); break; }
        p.done = keep;
    }
    p.status = "Failed";
    log.push_back({ "failed", d["Id"].str(), d["Title"].str() });
}

void Missions::tryComplete(const Json& d) {
    MissionProgress& p = prog[d["Id"].str()];
    if (p.status == "Completed") return;
    if (d["BranchRequired"].boolean() && p.branch.empty()) return;
    for (const Json& o : d["Objectives"].items()) {
        if (o["Optional"].boolean() || (!o["Branch"].str().empty() && o["Branch"].str() != p.branch)) continue;
        if (!p.hasDone(o["Id"].str())) return;
    }
    p.status = "Completed";
    const auto& rewards = d["Rewards"].items();
    for (size_t i = 0; i < rewards.size(); ++i) give("reward." + d["Id"].str() + "." + std::to_string(i), rewards[i]);
    if (d["AdvanceToChapter"].integer() > 0) story_.advance(d["AdvanceToChapter"].integer());
    if (tracked == d["Id"].str()) tracked.clear();
    log.push_back({ "completed", d["Id"].str(), d["Title"].str() });
    refresh();
}

void Missions::give(const std::string& key, const Json& r) {
    if (granted.count(key)) return;
    granted.insert(key);
    if (grant_) grant_(key, r);
}

Json Missions::capture() const {
    Json p = Json::makeObject();
    for (const auto& kv : prog)
        p.set(kv.first, Json::makeObject().set("status", Json::makeString(kv.second.status)).set("done", stringArray(kv.second.done))
                         .set("branch", Json::makeString(kv.second.branch)).set("attempts", Json::makeNumber(kv.second.attempts))
                         .set("lastFailure", Json::makeString(kv.second.lastFailure)));
    Json g = Json::makeArray(); for (const auto& k : granted) g.push(Json::makeString(k));
    return Json::makeObject().set("prog", p).set("granted", g).set("tracked", Json::makeString(tracked));
}

void Missions::restore(const Json& s) {
    prog.clear();
    for (const auto& kv : s["prog"].members()) {
        MissionProgress mp; mp.status = kv.second["status"].strOr("Locked"); mp.done = strings(kv.second["done"]);
        mp.branch = kv.second["branch"].str(); mp.attempts = kv.second["attempts"].integer(); mp.lastFailure = kv.second["lastFailure"].str();
        prog[kv.first] = mp;
    }
    for (const Json& d : defs) if (!prog.count(d["Id"].str())) prog[d["Id"].str()] = MissionProgress();
    granted.clear(); for (const Json& k : s["granted"].items()) granted.insert(k.str());
    tracked = s["tracked"].str();
    refresh();
    pump();
}

} // namespace ffts
