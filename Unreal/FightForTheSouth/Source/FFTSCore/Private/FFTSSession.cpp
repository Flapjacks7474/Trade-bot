#include "FFTSSession.h"
#include <algorithm>
#include <cstdio>

namespace ffts {

namespace {
bool contains(const Json& arr, const std::string& v) { for (const Json& x : arr.items()) if (x.str() == v) return true; return false; }
StatMap stats(const Json& o) { StatMap m; for (const auto& kv : o.members()) m[kv.first] = kv.second.integer(); return m; }
void split(const std::string& s, char c, std::string& a, std::string& b) {
    const size_t i = s.find(c); a = s.substr(0, i); b = i == std::string::npos ? std::string() : s.substr(i + 1);
}
}

// ================================================================================================= data
bool GameData::load(const std::function<std::string(const std::string&)>& read, std::vector<std::string>& errors) {
    struct Src { const char* path; const char* member; Json* out; };
    const Src srcs[] = { { "Missions/chapter1.json", "Missions", &missions }, { "Dialogue/chapter1_dialogue.json", "Dialogues", &dialogues },
                         { "Dialogue/npc_conversations.json", "Rules", &rules }, { "Characters/npc_profiles.json", "Npcs", &npcs },
                         { "World/southside_layout.json", nullptr, &layout } };
    const size_t before = errors.size();
    for (const Src& s : srcs) {
        const std::string text = read(s.path);
        if (text.empty()) { errors.push_back(std::string(s.path) + ": missing"); continue; }
        std::string err; Json root = Json::parse(text, &err);
        if (!err.empty()) { errors.push_back(std::string(s.path) + ": " + err); continue; }
        *s.out = s.member ? root[s.member] : root;
        if (s.member ? !s.out->isArray() : !s.out->isObject()) errors.push_back(std::string(s.path) + ": unexpected shape");
    }
    return errors.size() == before;
}

// ================================================================================================= dialogue
DialogueRunner::DialogueRunner(const Json& def, Game& game) : def_(def), game_(game) { current_ = node(def_["StartNode"].str()); }

const Json* DialogueRunner::node(const std::string& id) const {
    for (const Json& n : def_["Nodes"].items()) if (n["Id"].str() == id) return &n;
    return nullptr;
}
const std::string& DialogueRunner::id() const { return def_["Id"].str(); }
const std::string& DialogueRunner::speaker() const { return current_ ? (*current_)["Speaker"].str() : def_["NoSpeaker"].str(); }

bool DialogueRunner::matches(const Json& c) const {
    if (c.isNull()) return true;
    for (const Json& f : c["RequireFlags"].items()) if (!game_.story.has(f.str())) return false;
    for (const Json& f : c["ForbidFlags"].items()) if (game_.story.has(f.str())) return false;
    const std::string npc = def_["NpcId"].str();
    const StatMap r = game_.social.get(npc);
    if (c["MinTrust"].isNumber() && r.at("Trust") < c["MinTrust"].integer()) return false;
    if (c["MinRespect"].isNumber() && r.at("Respect") < c["MinRespect"].integer()) return false;
    if (!c["MinAttitude"].str().empty()) {
        const auto& a = Social::attitudes();
        const auto have = std::find(a.begin(), a.end(), game_.social.attitude(npc)) - a.begin();
        const auto need = std::find(a.begin(), a.end(), c["MinAttitude"].str()) - a.begin();
        if (have < need) return false;
    }
    if (c["Segments"].size() && !contains(c["Segments"], game_.segment())) return false;
    return true;
}

std::string DialogueRunner::line() const {
    if (!current_) return std::string();
    for (const Json& v : (*current_)["Variants"].items()) if (matches(v["When"])) return v["Text"].str();
    return (*current_)["Text"].str();
}

std::vector<const Json*> DialogueRunner::options() const {
    std::vector<const Json*> out;
    if (current_) for (const Json& o : (*current_)["Options"].items()) if (matches(o["When"])) out.push_back(&o);
    return out;
}

DialogueChoice DialogueRunner::choose(const std::string& optionId) {
    DialogueChoice out;
    const Json* o = nullptr;
    for (const Json* x : options()) if ((*x)["Id"].str() == optionId) { o = x; break; }
    if (!o) return out;
    out.ok = true;
    const auto& effects = (*o)["Effects"].items();
    for (size_t i = 0; i < effects.size(); ++i) game_.applyEffect("dialogue." + id() + "." + optionId + "." + std::to_string(i), effects[i]);
    if (!(*o)["MissionBranch"].str().empty()) { std::string m, b; split((*o)["MissionBranch"].str(), ':', m, b); game_.missions->choose(m, b); }
    if (!(*o)["Trigger"].str().empty()) game_.trigger((*o)["Trigger"].str());
    out.fight = (*o)["StartFight"].str();
    const std::string next = (*o)["Next"].str();
    current_ = next.empty() ? nullptr : node(next);
    if (!current_) { out.ended = true; if (!def_["EndTrigger"].str().empty()) game_.trigger(def_["EndTrigger"].str()); }
    return out;
}

// ================================================================================================= session
Game::Game(const GameData& data) : data_(data) {
    missions = std::make_unique<Missions>(data_.missions, story, [this](const std::string& k, const Json& r) { grant(k, r); });
    for (const Json& d : data_.dialogues.items()) dialogues_[d["Id"].str()] = &d;
    for (const Json& p : data_.npcs.items()) social.personality[p["NpcId"].str()] = stats(p["Personality"]);
    world = std::make_unique<LivingWorld>(data_.npcs, data_.layout, [this](const Json& e) { return pinMet(e); });
    world->init(day(), hour());
    lastHour_ = hour();
    missions->pump();
}

std::string Game::segment() const {
    const int h = hour();
    if (h >= 6 && h < 12) return "Morning";
    if (h >= 12 && h < 17) return "Afternoon";
    if (h >= 17 && h < 21) return "Evening";
    if (h >= 21 || h < 2) return "Night";
    return "LateNight";
}

std::string Game::clockText() const {
    static const char* days[] = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
    char b[32]; std::snprintf(b, sizeof b, "%s %02d:%02d", days[day() % 7], hour(), minute());
    return b;
}

bool Game::pinMet(const Json& e) const {
    if (!e["MissionActive"].str().empty() && missions->status(e["MissionActive"].str()) != "Active") return false;
    for (const Json& o : e["ObjectivesDone"].items()) if (!missions->objectiveDone(o.str())) return false;
    for (const Json& o : e["ObjectivesNotDone"].items()) if (missions->objectiveDone(o.str())) return false;
    for (const Json& f : e["RequireFlags"].items()) if (!story.has(f.str())) return false;
    return true;
}

void Game::tick(double sec, double px, double pz, bool clockRunning) {
    if (clockRunning) {
        clockAcc += sec;
        const long long m = static_cast<long long>(clockAcc);
        if (m > 0) { clockAcc -= static_cast<double>(m); minutes += m; }
    }
    if (hour() != lastHour_ || dirty_) { lastHour_ = hour(); dirty_ = false; world->plan(day(), hour()); }
    world->step(sec, px, pz);
}

void Game::sleepUntilMorning() {
    long long m = (24 * 60 - minutes % 1440 + 8 * 60) % 1440;
    if (m == 0) m = 1440;
    minutes += m;
}

void Game::trigger(const std::string& t) {
    if (t.empty()) return;
    const size_t before = missions->log.size();
    missions->report(t);
    for (size_t i = before; i < missions->log.size(); ++i) {
        const MissionLogEntry& e = missions->log[i];
        if (e.type == "completed") toasts.push_back("Mission complete: " + e.text);
        else if (e.type == "objective") toasts.push_back("Done: " + e.text);
        else if (e.type == "failed") toasts.push_back("Mission failed: " + e.text + " - try again");
    }
    dirty_ = true;
}

void Game::grant(const std::string& key, const Json& r) {
    const std::string& type = r["Type"].str();
    const std::string memo = r["Memo"].str();
    if (type == "Money") {
        const Cents amount = static_cast<Cents>(r["AmountCents"].num());
        if (amount > 0) { if (bank.post(key, "cash", amount, memo) == PostResult::Posted) toasts.push_back("+" + Bank::fmt(amount) + (memo.empty() ? "" : " - " + memo)); }
        else if (amount < 0 && bank.pay(key, -amount, memo) == PostResult::Posted) toasts.push_back("-" + Bank::fmt(-amount) + (memo.empty() ? "" : " - " + memo));
    } else if (type == "Reputation") {
        RepChange ch; ch.face = stats(r["FaceCard"]); ch.image = stats(r["PublicImage"]); rep.apply(ch);
    } else if (type == "Relationship") {
        social.modify(r["Target"].str(), stats(r["Relationship"]));
    } else if (type == "Flag") {
        story.set(r["Target"].str(), r["Value"].isNumber() ? r["Value"].integer() : 1);
        missions->refresh();
    } else if (type == "Contact") {
        const std::string& who = r["Target"].str();
        if (std::find(contacts.begin(), contacts.end(), who) == contacts.end()) { contacts.push_back(who); toasts.push_back("New contact: " + npcName(who)); }
    } else if (type == "Item") {
        const int n = r["Value"].integer(); inventory[r["Target"].str()] += n ? n : 1;
    }
}

void Game::applyEffect(const std::string& key, const Json& e) {
    if (missions->granted.count(key)) return;
    missions->granted.insert(key);
    grant(key, e);
}

std::string Game::npcName(const std::string& id) const {
    for (const Json& n : data_.npcs.items()) if (n["NpcId"].str() == id) return n["DisplayName"].str();
    return id.rfind("npc.", 0) == 0 ? id.substr(4) : id;
}

std::unique_ptr<DialogueRunner> Game::startDialogue(const std::string& id) {
    auto it = dialogues_.find(id);
    return it == dialogues_.end() ? nullptr : std::make_unique<DialogueRunner>(*it->second, *this);
}

const Json* Game::talkTo(const std::string& npcId) {
    for (const Json& r : data_.rules.items()) {
        if (r["NpcId"].str() != npcId) continue;
        if (!r["MissionActive"].str().empty() && missions->status(r["MissionActive"].str()) != "Active") continue;
        bool skip = false;
        for (const Json& o : r["ObjectivesDone"].items()) if (!missions->objectiveDone(o.str())) skip = true;
        for (const Json& o : r["ObjectivesNotDone"].items()) if (missions->objectiveDone(o.str())) skip = true;
        if (skip) continue;
        if (!r["Branch"].str().empty()) {
            std::string m, b; split(r["Branch"].str(), ':', m, b);
            const MissionProgress* p = missions->progress(m);
            if (!p || p->branch != b) continue;
        }
        for (const Json& f : r["RequireFlags"].items()) if (!story.has(f.str())) skip = true;
        for (const Json& f : r["ForbidFlags"].items()) if (story.has(f.str())) skip = true;
        if (skip) continue;
        if (r["Segments"].size() && !contains(r["Segments"], segment())) continue;
        if (!r["Activity"].str().empty()) {
            const WorldNpc* n = world->npc(npcId);
            if (!n || n->activity != r["Activity"].str() || n->status == "Traveling") continue;
        }
        return &r;
    }
    return nullptr;
}

bool Game::onFight(const std::string& winnerId, const std::string& method, const std::string& opponentId, const std::string& venue,
                   const std::string& locationId, const std::vector<std::string>& witnesses) {
    const bool won = !winnerId.empty() && winnerId != opponentId;
    const std::string key = "fight." + std::to_string(++fightCount);
    rep.apply(fightReputation(venue, won, method));
    std::string npc = opponentId;
    const size_t f = npc.find("fighter.");
    if (f != std::string::npos) npc.erase(f, 8);
    npc = "npc." + npc;
    social.modify(npc, won ? StatMap{ { "Respect", 8 }, { "Resentment", method == "Finisher" ? 15 : 8 }, { "Rivalry", 10 }, { "Fear", 5 } }
                           : StatMap{ { "Respect", -3 }, { "Rivalry", 4 } });
    SocialEvent e;
    e.id = "event." + key; e.type = won ? "player.won_fight" : "player.lost_fight";
    e.actor = won ? "player" : npc; e.target = won ? npc : "player"; e.location = locationId;
    e.witnesses.push_back(npc); e.witnesses.insert(e.witnesses.end(), witnesses.begin(), witnesses.end());
    social.record(e);
    trigger((won ? "fight.won." : "fight.lost.") + opponentId);
    return won;
}

Json Game::capture(double px, double pz) const {
    Json flags = Json::makeObject(); for (const auto& kv : story.flags) flags.set(kv.first, Json::makeNumber(kv.second));
    Json c = Json::makeArray(); for (const auto& x : contacts) c.push(Json::makeString(x));
    Json inv = Json::makeObject(); for (const auto& kv : inventory) inv.set(kv.first, Json::makeNumber(kv.second));
    return Json::makeObject()
        .set("v", Json::makeNumber(SaveVersion)).set("format", Json::makeString("ffts-cpp"))
        .set("minutes", Json::makeNumber(static_cast<double>(minutes)))
        .set("bank", bank.capture()).set("rep", rep.capture()).set("social", social.capture())
        .set("story", Json::makeObject().set("chapter", Json::makeNumber(story.chapter)).set("flags", flags))
        .set("missions", missions->capture())
        .set("player", Json::makeObject().set("contacts", c).set("inventory", inv))
        .set("fightCount", Json::makeNumber(fightCount))
        .set("world", world->capture())
        .set("pos", Json::makeObject().set("x", Json::makeNumber(px)).set("z", Json::makeNumber(pz)));
}

bool Game::restore(const Json& s) {
    if (!s.isObject() || s["v"].integer() != SaveVersion || s["format"].str() != "ffts-cpp") return false;
    minutes = static_cast<long long>(s["minutes"].num(8 * 60));
    bank.restore(s["bank"]); rep.restore(s["rep"]); social.restore(s["social"]);
    story.chapter = s["story"]["chapter"].integer(1);
    story.flags.clear(); for (const auto& kv : s["story"]["flags"].members()) story.flags[kv.first] = kv.second.integer();
    missions->restore(s["missions"]);
    contacts.clear(); for (const Json& x : s["player"]["contacts"].items()) contacts.push_back(x.str());
    inventory.clear(); for (const auto& kv : s["player"]["inventory"].members()) inventory[kv.first] = kv.second.integer();
    fightCount = s["fightCount"].integer();
    world->restore(s["world"]);
    savedX = s["pos"]["x"].num(); savedZ = s["pos"]["z"].num();
    lastHour_ = -1;
    return true;
}

} // namespace ffts
