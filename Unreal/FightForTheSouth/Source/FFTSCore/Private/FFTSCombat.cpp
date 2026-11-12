#include "FFTSCombat.h"
#include <algorithm>
#include <cmath>
#include <functional>

namespace ffts {

// ================================================================================================ utilities
uint64_t Rng::next() {
    state += 0x9E3779B97F4A7C15ULL;
    uint64_t z = state;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    return z ^ (z >> 31);
}
int Rng::range(int minIncl, int maxExcl) { if (maxExcl <= minIncl) return minIncl; return minIncl + static_cast<int>(next() % static_cast<uint64_t>(maxExcl - minIncl)); }
float Rng::value() { return static_cast<float>(next() >> 40) / 16777216.0f; }
bool Rng::chance(float p) { if (p <= 0) return false; if (p >= 1) return true; return value() < p; }
int roundAway(float v) { return v < 0 ? -static_cast<int>(std::floor(-v + 0.5f)) : static_cast<int>(std::floor(v + 0.5f)); }

static const char* kStates[] = { "Idle", "Moving", "Attacking", "Combo", "Finisher", "Blocking", "Countering", "Grappling", "Throwing",
    "KnockedDown", "GettingUp", "Stunned", "Defeated", "HitStun", "BlockStun", "Dodging", "Grappled", "FinisherVictim" };
static const char* kCommands[] = { "None", "Light", "Heavy", "Kick", "Grapple", "Dodge", "Counter", "Finisher", "Taunt" };
static const char* kKinds[] = { "Light", "Heavy", "Kick", "Grapple", "Throw", "EnvironmentalThrow", "Counter", "Signature", "Finisher",
    "GroundAttack", "Taunt", "ClinchStrike" };
const char* toString(State s) { return kStates[static_cast<int>(s)]; }
const char* toString(Command c) { return kCommands[static_cast<int>(c)]; }
const char* toString(MoveKind k) { return kKinds[static_cast<int>(k)]; }
bool parseCommand(const std::string& s, Command& out) { for (int i = 0; i < 9; ++i) if (s == kCommands[i]) { out = static_cast<Command>(i); return true; } return false; }
bool parseMoveKind(const std::string& s, MoveKind& out) { for (int i = 0; i < 12; ++i) if (s == kKinds[i]) { out = static_cast<MoveKind>(i); return true; } return false; }

float StanceDef::damageFor(MoveKind k) const {
    switch (k) {
        case MoveKind::Kick: return kickDamage;
        case MoveKind::Throw: case MoveKind::EnvironmentalThrow: return throwDamage;
        case MoveKind::Finisher: case MoveKind::Taunt: case MoveKind::Grapple: return 1.0f;
        default: return strikeDamage;
    }
}
const StanceDef& StanceDef::neutral() { static const StanceDef n; return n; }

// ================================================================================================ moveset
std::shared_ptr<Moveset> Moveset::build(const std::vector<const StyleDef*>& styles, const StyleDef* hybrid) {
    if (styles.empty() || static_cast<int>(styles.size()) > MaxStyles) return nullptr;
    auto ms = std::make_shared<Moveset>();
    std::vector<const StyleDef*> ordered;
    if (hybrid) { ordered.push_back(hybrid); ms->hybridId_ = hybrid->id; }
    ordered.insert(ordered.end(), styles.begin(), styles.end());
    for (auto* s : ordered) if (s->hasStance) { ms->stance_ = &s->stance; break; }
    for (auto* s : ordered) {
        for (const auto& m : s->moves) if (!ms->moves_.count(m.id)) ms->moves_[m.id] = &m;
        ms->links_.insert(ms->links_.end(), s->links.begin(), s->links.end());
    }
    auto slot = [&](const MoveDef*& field, std::string StyleDef::*member) {
        for (auto* s : ordered) { if (!field && !(s->*member).empty()) field = ms->get(s->*member); }
    };
    slot(ms->light, &StyleDef::light); slot(ms->heavy, &StyleDef::heavy); slot(ms->kick, &StyleDef::kick); slot(ms->grapple, &StyleDef::grapple);
    slot(ms->counter, &StyleDef::counter); slot(ms->finisher, &StyleDef::finisher); slot(ms->groundAttack, &StyleDef::groundAttack);
    slot(ms->taunt, &StyleDef::taunt); slot(ms->clinchStrike, &StyleDef::clinchStrike); slot(ms->clinchThrow, &StyleDef::clinchThrow);
    slot(ms->environmentalThrow, &StyleDef::environmentalThrow); slot(ms->reversal, &StyleDef::reversal);
    return ms;
}
const MoveDef* Moveset::get(const std::string& id) const { auto it = moves_.find(id); return it == moves_.end() ? nullptr : it->second; }
const MoveDef* Moveset::findLink(const std::string& from, Command c) const {
    for (const auto& l : links_) if (l.from == from && l.command == c) return get(l.to);
    return nullptr;
}
const MoveDef* Moveset::entryFor(Command c) const {
    switch (c) { case Command::Light: return light; case Command::Heavy: return heavy; case Command::Kick: return kick;
                 case Command::Grapple: return grapple; case Command::Taunt: return taunt; default: return nullptr; }
}

// ================================================================================================ database
static StanceDef readStance(const Json& j) {
    StanceDef s;
    s.name = j["Name"].strOr("Neutral"); s.defense = j["Defense"].strOr("Block"); s.description = j["Description"].str();
    s.idleAnimation = j["IdleAnimation"].str(); s.guardAnimation = j["GuardAnimation"].str();
    s.walkSpeed = (float)j["WalkSpeed"].num(1); s.strikeDamage = (float)j["StrikeDamage"].num(1); s.kickDamage = (float)j["KickDamage"].num(1);
    s.throwDamage = (float)j["ThrowDamage"].num(1); s.blockChip = (float)j["BlockChip"].num(1); s.guardDrain = (float)j["GuardDrain"].num(1);
    s.dodgeDistance = (float)j["DodgeDistance"].num(1); s.dodgeCost = (float)j["DodgeCost"].num(1); s.escapeRate = (float)j["EscapeRate"].num(1);
    s.parryWindowBonus = j["ParryWindowBonus"].integer(0); s.counterWindowBonus = j["CounterWindowBonus"].integer(0);
    s.clinchStrikesBonus = j["ClinchStrikesBonus"].integer(0);
    return s;
}

bool CombatDatabase::load(const std::vector<std::string>& styleJson, const std::vector<std::string>& fighterJson, std::vector<std::string>& errors) {
    size_t before = errors.size();
    for (const auto& text : styleJson) {
        std::string err; Json root = Json::parse(text, &err);
        if (!err.empty()) { errors.push_back("Style file: " + err); continue; }
        for (const auto& js : root["Styles"].items()) {
            auto s = std::make_unique<StyleDef>();
            s->id = js["Id"].str(); s->displayName = js["DisplayName"].str(); s->description = js["Description"].str();
            s->discipline = js["Discipline"].str(); s->learnAt = js["LearnAt"].str(); s->strengths = js["Strengths"].str();
            s->weaknesses = js["Weaknesses"].str(); s->unlockCostCents = static_cast<long long>(js["UnlockCostCents"].num(0));
            if (js["Stance"].isObject()) { s->hasStance = true; s->stance = readStance(js["Stance"]); }
            for (const auto& h : js["HybridOf"].items()) s->hybridOf.push_back(h.str());
            for (const auto& jm : js["Moves"].items()) {
                MoveDef m;
                m.id = jm["Id"].str(); m.displayName = jm["DisplayName"].str(); m.animationKey = jm["AnimationKey"].str();
                if (!parseMoveKind(jm["Kind"].str(), m.kind)) errors.push_back("Move " + m.id + ": unknown kind '" + jm["Kind"].str() + "'.");
                m.startup = jm["Startup"].integer(1); m.active = jm["Active"].integer(1); m.recovery = jm["Recovery"].integer(0);
                m.hitStun = jm["HitStun"].integer(0); m.blockStun = jm["BlockStun"].integer(0); m.cancelWindow = jm["CancelWindow"].integer(0);
                m.damage = (float)jm["Damage"].num(0); m.staminaCost = (float)jm["StaminaCost"].num(0); m.staminaDamage = (float)jm["StaminaDamage"].num(0);
                m.momentumGain = (float)jm["MomentumGain"].num(0); m.range = (float)jm["Range"].num(1); m.pushback = (float)jm["Pushback"].num(0);
                m.knockdown = jm["Knockdown"].boolean(false);
                s->moves.push_back(m);
            }
            for (const auto& jl : js["Links"].items()) {
                MoveLink l; l.from = jl["From"].str(); l.to = jl["To"].str();
                if (!parseCommand(jl["Command"].str(), l.command)) errors.push_back("Style " + s->id + ": unknown link command '" + jl["Command"].str() + "'.");
                s->links.push_back(l);
            }
            s->light = js["Light"].str(); s->heavy = js["Heavy"].str(); s->kick = js["Kick"].str(); s->grapple = js["Grapple"].str();
            s->counter = js["Counter"].str(); s->finisher = js["Finisher"].str(); s->groundAttack = js["GroundAttack"].str();
            s->taunt = js["Taunt"].str(); s->clinchStrike = js["ClinchStrike"].str(); s->clinchThrow = js["ClinchThrow"].str();
            s->environmentalThrow = js["EnvironmentalThrow"].str(); s->reversal = js["Reversal"].str();
            styles_.push_back(std::move(s));
        }
    }
    for (const auto& text : fighterJson) {
        std::string err; Json root = Json::parse(text, &err);
        if (!err.empty()) { errors.push_back("Fighter file: " + err); continue; }
        for (const auto& jf : root["Fighters"].items()) {
            FighterDef f;
            f.id = jf["Id"].str(); f.displayName = jf["DisplayName"].str(); f.archetype = jf["Archetype"].str();
            f.aiStyle = jf["AiStyle"].strOr("Technical"); f.aiDifficulty = jf["AiDifficulty"].strOr("Normal");
            const Json& st = jf["Stats"];
            f.stats.strength = st["Strength"].integer(50); f.stats.speed = st["Speed"].integer(50); f.stats.durability = st["Durability"].integer(50);
            f.stats.stamina = st["Stamina"].integer(50); f.stats.technique = st["Technique"].integer(50); f.stats.aggression = st["Aggression"].integer(50);
            f.stats.confidence = st["Confidence"].integer(50); f.stats.composure = st["Composure"].integer(50);
            for (const auto& s : jf["Styles"].items()) f.styles.push_back(s.str());
            fighters_.push_back(f);
        }
    }
    validate(errors);
    return errors.size() == before;
}

const StyleDef* CombatDatabase::style(const std::string& id) const { for (const auto& s : styles_) if (s->id == id) return s.get(); return nullptr; }
const FighterDef* CombatDatabase::fighter(const std::string& id) const { for (const auto& f : fighters_) if (f.id == id) return &f; return nullptr; }

static std::string pairKey(const std::string& a, const std::string& b) { return a < b ? a + "+" + b : b + "+" + a; }

const StyleDef* CombatDatabase::findHybrid(const std::string& a, const std::string& b) const {
    if (a.empty() || b.empty() || a == b) return nullptr;
    std::string key = pairKey(a, b);
    for (const auto& s : styles_) if (s->hybridOf.size() == 2 && pairKey(s->hybridOf[0], s->hybridOf[1]) == key) return s.get();
    return nullptr;
}

std::shared_ptr<Moveset> CombatDatabase::buildMoveset(const std::vector<std::string>& ids, bool allowHybrid, const std::vector<std::string>* unlocked) const {
    std::vector<const StyleDef*> list;
    for (const auto& id : ids) { const StyleDef* s = style(id); if (!s) return nullptr; list.push_back(s); }
    const StyleDef* hybrid = nullptr;
    if (allowHybrid && ids.size() >= 2) {
        hybrid = findHybrid(ids[0], ids[1]);
        if (hybrid && unlocked && std::find(unlocked->begin(), unlocked->end(), hybrid->id) == unlocked->end()) hybrid = nullptr;
    }
    return Moveset::build(list, hybrid);
}

void CombatDatabase::validate(std::vector<std::string>& errors) const {
    std::set<std::string> moveIds, styleIds, pairs;
    for (const auto& s : styles_) if (!styleIds.insert(s->id).second) errors.push_back("Duplicate style id '" + s->id + "'.");
    for (const auto& sp : styles_) {
        const StyleDef& s = *sp;
        std::map<std::string, const MoveDef*> usable;
        for (const auto& m : s.moves) {
            if (!moveIds.insert(m.id).second) errors.push_back("Duplicate move id '" + m.id + "'.");
            if (m.startup < 1 || m.active < 1 || m.recovery < 0) errors.push_back("Move " + m.id + ": invalid frame data.");
            if (m.damage < 0 || m.staminaCost < 0 || m.range <= 0) errors.push_back("Move " + m.id + ": invalid damage/stamina/range.");
            if (m.cancelWindow < 0 || m.cancelWindow > m.recovery) errors.push_back("Move " + m.id + ": cancel window must fit inside recovery.");
            if (m.animationKey.empty()) errors.push_back("Move " + m.id + ": missing animation key.");
            usable[m.id] = &m;
        }
        if (s.hasStance) {
            const StanceDef& st = s.stance;
            auto mul = [](float v) { return v >= 0.5f && v <= 1.5f; };
            if (!(mul(st.walkSpeed) && mul(st.strikeDamage) && mul(st.kickDamage) && mul(st.throwDamage) && mul(st.blockChip) && mul(st.guardDrain)
                  && mul(st.dodgeDistance) && mul(st.dodgeCost) && mul(st.escapeRate)))
                errors.push_back("Style " + s.id + ": stance multipliers must stay within 0.5-1.5.");
            if (st.parryWindowBonus < 0 || st.parryWindowBonus > 6 || st.counterWindowBonus < 0 || st.counterWindowBonus > 6)
                errors.push_back("Style " + s.id + ": parry/counter window bonuses must be 0-6 ticks.");
            if (st.clinchStrikesBonus < 0 || st.clinchStrikesBonus > 3) errors.push_back("Style " + s.id + ": clinch strike bonus must be 0-3.");
            if (st.idleAnimation.empty() || st.guardAnimation.empty()) errors.push_back("Style " + s.id + ": stance needs idle and guard animation keys.");
        }
        std::vector<const StyleDef*> graph{ &s };
        if (s.isHybrid()) {
            if (s.hybridOf.size() != 2 || s.hybridOf[0] == s.hybridOf[1]) errors.push_back("Hybrid " + s.id + ": must name exactly two different parent styles.");
            for (const auto& pid : s.hybridOf) {
                const StyleDef* p = style(pid);
                if (!p) { errors.push_back("Hybrid " + s.id + ": unknown parent style '" + pid + "'."); continue; }
                if (p->isHybrid()) errors.push_back("Hybrid " + s.id + ": parent '" + pid + "' is itself a hybrid.");
                for (const auto& m : p->moves) usable[m.id] = &m;
                graph.push_back(p);
            }
            if (s.hybridOf.size() == 2 && !pairs.insert(pairKey(s.hybridOf[0], s.hybridOf[1])).second)
                errors.push_back("Hybrid " + s.id + ": another hybrid already uses this pair.");
        } else {
            const std::pair<const char*, const std::string*> req[] = { {"Light", &s.light}, {"Heavy", &s.heavy}, {"Kick", &s.kick}, {"Grapple", &s.grapple},
                {"Counter", &s.counter}, {"Finisher", &s.finisher}, {"ClinchStrike", &s.clinchStrike}, {"ClinchThrow", &s.clinchThrow} };
            for (const auto& r : req) if (r.second->empty()) errors.push_back("Style " + s.id + ": base styles must fill the " + r.first + " slot.");
        }
        for (const std::string* slot : { &s.light, &s.heavy, &s.kick, &s.grapple, &s.counter, &s.finisher, &s.groundAttack, &s.taunt,
                                         &s.clinchStrike, &s.clinchThrow, &s.environmentalThrow, &s.reversal })
            if (!slot->empty() && !usable.count(*slot)) errors.push_back("Style " + s.id + ": slot references unknown move '" + *slot + "'.");
        std::set<std::string> keys;
        for (const auto& l : s.links) {
            if (!usable.count(l.from) || !usable.count(l.to)) { errors.push_back("Style " + s.id + ": link " + l.from + "->" + l.to + " references unknown move."); continue; }
            if (!keys.insert(l.from + "|" + toString(l.command)).second) errors.push_back("Style " + s.id + ": ambiguous link from " + l.from + ".");
            if (usable[l.from]->cancelWindow <= 0) errors.push_back("Style " + s.id + ": link from " + l.from + " can never fire (no cancel window).");
        }
        // cycle check across the style (and its parents for a hybrid); hybrid links shadow parent links for the same key
        std::map<std::string, std::vector<std::string>> edges; std::set<std::string> taken;
        for (auto* g : graph) for (const auto& l : g->links) { if (!taken.insert(l.from + "|" + toString(l.command)).second) continue; edges[l.from].push_back(l.to); }
        std::map<std::string, int> state;
        std::function<bool(const std::string&)> visit = [&](const std::string& n) -> bool {
            auto it = state.find(n); if (it != state.end()) return it->second == 1;
            state[n] = 1;
            auto e = edges.find(n);
            if (e != edges.end()) for (const auto& x : e->second) if (visit(x)) return true;
            state[n] = 2; return false;
        };
        for (const auto& kv : edges) if (visit(kv.first)) { errors.push_back("Style " + s.id + ": combo links contain a cycle (infinite combo risk)."); break; }
    }
    for (const auto& f : fighters_) {
        if (f.styles.empty() || static_cast<int>(f.styles.size()) > Moveset::MaxStyles) errors.push_back("Fighter " + f.id + ": must have 1-3 styles.");
        for (const auto& sid : f.styles) {
            const StyleDef* st = style(sid);
            if (!st) errors.push_back("Fighter " + f.id + ": unknown style '" + sid + "'.");
            else if (st->isHybrid()) errors.push_back("Fighter " + f.id + ": list the two parent styles, not the hybrid '" + sid + "'.");
        }
        const int vals[] = { f.stats.strength, f.stats.speed, f.stats.durability, f.stats.stamina, f.stats.technique, f.stats.aggression, f.stats.confidence, f.stats.composure };
        for (int v : vals) if (v < 1 || v > 100) { errors.push_back("Fighter " + f.id + ": stat outside 1-100."); break; }
        if (f.displayName.empty()) errors.push_back("Fighter " + f.id + ": missing display name.");
    }
}

// ================================================================================================ fighter
Fighter::Fighter(const FighterDef& d, std::shared_ptr<Moveset> m, float xPos) : id(d.id), def(&d), moves(std::move(m)), stats(d.stats), x(xPos) {
    maxHealth = 100.0f + stats.durability; maxStamina = 60.0f + stats.stamina * 0.8f;
    health = maxHealth; stamina = maxStamina;
}
void Fighter::applyScenario(float h, float mo, float st) {
    if (h >= 0) health = clampf(h, 1, maxHealth);
    if (mo >= 0) momentum = clampf(mo, 0, 100);
    if (st >= 0) stamina = clampf(st, 0, maxStamina);
}
int Fighter::adjustStartup(int s) const { return std::max(1, roundAway(s * (1.15f - stats.speed / 400.0f))); }

// ================================================================================================ simulation
static bool isAny(State s, std::initializer_list<State> list) { for (State x : list) if (x == s) return true; return false; }

CombatSimulation::CombatSimulation(const CombatConfig& c, std::unique_ptr<Fighter> a, std::unique_ptr<Fighter> b, std::string id)
    : cfg(c), A(std::move(a)), B(std::move(b)), fightId(std::move(id)) { updateFacing(); }

float CombatSimulation::distance() const { return std::fabs(A->x - B->x); }

void CombatSimulation::step(const FighterInput& ia, const FighterInput& ib) {
    events.clear();
    if (over) return;
    tick++;
    buffer(*A, ia); buffer(*B, ib);
    update(*A, *B, ia); update(*B, *A, ib);
    resolveHits(); resolvePositions();
    if (!over && tick >= cfg.roundTicks) endOnTime();
}

void CombatSimulation::buffer(Fighter& f, const FighterInput& i) {
    if (i.command != Command::None) { f.buffered = i.command; f.bufferedAge = 0; }
    else if (f.buffered != Command::None && ++f.bufferedAge > cfg.inputBufferTicks) f.buffered = Command::None;
    if (i.block && !f.prevBlock) f.blockStartTick = tick;
    f.prevBlock = i.block;
}
Command CombatSimulation::take(Fighter& f) { Command c = f.buffered; f.buffered = Command::None; f.bufferedAge = 0; return c; }

void CombatSimulation::update(Fighter& f, Fighter& o, const FighterInput& input) {
    if (f.state == State::Defeated || over) return;
    f.stateTicks++;
    switch (f.state) {
        case State::Idle: case State::Moving: case State::Blocking: neutral(f, o, input); break;
        case State::Attacking: case State::Combo: case State::Throwing: case State::Finisher: advance(f, o); break;
        case State::Countering: if (f.stateTicks >= cfg.counterStanceTicks) set(f, State::Idle); break;
        case State::HitStun: if (breakaway(f, o)) break; if (f.stateTicks >= f.stateDuration) set(f, State::Idle); break;
        case State::BlockStun: case State::Stunned: if (f.stateTicks >= f.stateDuration) set(f, State::Idle); break;
        case State::Dodging:
            if (f.stateTicks <= 12) f.x -= f.facing * cfg.dodgeDistance * f.stance().dodgeDistance / 12.0f;
            if (f.stateTicks >= f.stateDuration) set(f, State::Idle);
            break;
        case State::KnockedDown:
            if (f.buffered == Command::Dodge && f.stateTicks >= cfg.quickRiseAfterTicks) { take(f); emit("QuickRise", &f, nullptr); set(f, State::GettingUp, cfg.getUpTicks / 2); }
            else if (f.stateTicks >= f.stateDuration) set(f, State::GettingUp, cfg.getUpTicks);
            break;
        case State::GettingUp: if (f.stateTicks >= f.stateDuration) { set(f, State::Idle); emit("GotUp", &f, nullptr); } break;
        case State::Grappling: clinch(f, o); break;
        case State::Grappled: clinchDefender(f, o); break;
        case State::FinisherVictim: if (o.state != State::Finisher) set(f, State::Idle); break;
        default: break;
    }
    regen(f);
}

void CombatSimulation::neutral(Fighter& f, Fighter& o, const FighterInput& input) {
    if (f.buffered != Command::None && tryCommand(f, o, take(f))) return;
    if (input.block) { if (f.state != State::Blocking) set(f, State::Blocking); return; }
    if (std::fabs(input.move) > 0.1f) {
        f.x += f.facing * clampf(input.move, -1, 1) * f.walkSpeed() * cfg.dt();
        if (f.state != State::Moving) set(f, State::Moving);
    } else if (f.state != State::Idle) set(f, State::Idle);
}

bool CombatSimulation::tryCommand(Fighter& f, Fighter& o, Command cmd) {
    switch (cmd) {
        case Command::Finisher: {
            std::string d = canFinish(f);
            if (d == "None") startFinisher(f, o); else emit("FinisherDenied", &f, &o, "", 0, d);
            return true;
        }
        case Command::Dodge: {
            float cost = cfg.dodgeStaminaCost * f.stance().dodgeCost;
            if (f.stamina < cost) { emit("NotEnoughStamina", &f, nullptr, "dodge"); return true; }
            f.stamina -= cost; set(f, State::Dodging, cfg.dodgeTicks); return true;
        }
        case Command::Counter:
            if (!f.moves->counter) return false;
            if (f.stamina < cfg.counterStaminaCost) { emit("NotEnoughStamina", &f, nullptr, "counter"); return true; }
            f.stamina -= cfg.counterStaminaCost; set(f, State::Countering, cfg.counterStanceTicks); return true;
        default: {
            if (cmd == Command::Heavy && o.state == State::KnockedDown && f.moves->groundAttack && distance() <= f.moves->groundAttack->range + cfg.bodyReach) {
                startMove(f, f.moves->groundAttack, State::Attacking); return true;
            }
            const MoveDef* m = f.moves->entryFor(cmd);
            if (!m) return false;
            startMove(f, m, State::Attacking); return true;
        }
    }
}

bool CombatSimulation::startMove(Fighter& f, const MoveDef* m, State s) {
    if (f.stamina < m->staminaCost) { emit("NotEnoughStamina", &f, nullptr, m->id, m->staminaCost); return false; }
    f.stamina -= m->staminaCost; set(f, s);
    f.move = m; f.moveTick = 0; f.moveStartup = f.adjustStartup(m->startup); f.moveConnected = false; f.moveWasDodged = false;
    emit("MoveStarted", &f, nullptr, m->id, 0, toString(m->kind));
    return true;
}

void CombatSimulation::advance(Fighter& f, Fighter& o) {
    const MoveDef* m = f.move;
    if (!m) { set(f, State::Idle); return; }
    f.moveTick++;
    if (!f.moveConnected && f.moveTick >= f.moveStartup) {
        if (f.state == State::Throwing) { applyThrow(f, o, m); return; }
        if (f.state == State::Finisher) { applyFinisher(f, o, m); return; }
    }
    int rs = f.moveStartup + m->active;
    if (f.moveConnected && f.moveTick >= rs && f.moveTick - rs < m->cancelWindow && f.buffered != Command::None && f.state != State::Throwing && f.state != State::Finisher) {
        const MoveDef* next = f.moves->findLink(m->id, f.buffered);
        if (next) { take(f); startMove(f, next, State::Combo); return; }
    }
    if (f.moveTick >= rs + m->recovery) {
        if (m->kind == MoveKind::Taunt) { f.momentum = std::min(cfg.momentumMax, f.momentum + cfg.tauntMomentum); emit("TauntCompleted", &f, nullptr, m->id, cfg.tauntMomentum); }
        set(f, State::Idle);
    }
}

bool CombatSimulation::breakaway(Fighter& f, Fighter& o) {
    if (f.buffered != Command::Dodge || f.combo < cfg.breakawayMinCombo) return false;
    if (f.momentum < cfg.breakawayMomentumCost || f.stamina < cfg.breakawayStaminaCost) return false;
    take(f); f.momentum -= cfg.breakawayMomentumCost; f.stamina -= cfg.breakawayStaminaCost;
    set(f, State::Dodging, cfg.dodgeTicks); f.stateTicks = cfg.dodgeInvulnStart;
    if (o.state == State::Attacking || o.state == State::Combo) set(o, State::BlockStun, 24);
    push(o, f, 0.8f); emit("Breakaway", &f, &o); return true;
}

void CombatSimulation::clinch(Fighter& a, Fighter& d) {
    a.clinchTicks++; if (a.clinchCooldown > 0) a.clinchCooldown--;
    if (d.state != State::Grappled) { set(a, State::Idle); return; }
    if (a.clinchTicks >= cfg.clinchMaxTicks) { set(a, State::Idle); set(d, State::Idle); push(d, a, 0.6f); emit("ClinchReleased", &a, &d); return; }
    Command cmd = a.buffered;
    if (cmd == Command::Light && a.clinchCooldown == 0 && a.clinchStrikes < cfg.clinchStrikesMax + a.stance().clinchStrikesBonus && a.moves->clinchStrike) {
        take(a); const MoveDef* m = a.moves->clinchStrike;
        if (a.stamina < m->staminaCost) { emit("NotEnoughStamina", &a, nullptr, m->id); return; }
        a.stamina -= m->staminaCost;
        float dmg = scaled(a, d, m); d.health -= dmg; d.combo++;
        addMomentum(a, m->momentumGain); a.clinchStrikes++; a.clinchCooldown = cfg.clinchStrikeCooldown;
        emit("ClinchStrike", &a, &d, m->id, dmg);
        if (d.health <= 0) { set(a, State::Idle); defeat(d, a, "Knockout"); }
    } else if (cmd == Command::Heavy || cmd == Command::Grapple) {
        take(a);
        bool near = cfg.arenaHalfWidth - std::fabs(d.x) <= cfg.wallThrowDistance;
        const MoveDef* tm = cmd == Command::Grapple && near && a.moves->environmentalThrow ? a.moves->environmentalThrow : a.moves->clinchThrow;
        if (tm) startMove(a, tm, State::Throwing);
    }
}

void CombatSimulation::clinchDefender(Fighter& d, Fighter& a) {
    if (a.state != State::Grappling && a.state != State::Throwing) { set(d, State::Idle); return; }
    if (a.state == State::Throwing) return;
    Command cmd = take(d);
    if (cmd == Command::Counter && a.clinchTicks <= cfg.reversalWindowTicks && d.momentum >= cfg.reversalMomentumCost && d.moves->reversal && d.stamina >= d.moves->reversal->staminaCost) {
        d.momentum -= cfg.reversalMomentumCost; set(a, State::Grappled); startMove(d, d.moves->reversal, State::Throwing);
        emit("Reversal", &d, &a, d.moves->reversal->id);
    } else if (cmd == Command::Grapple || cmd == Command::Dodge) {
        float gain = 16 + d.stats.technique * 0.2f + d.stats.strength * 0.1f - a.stats.strength * 0.08f;
        d.escape += std::max(6.0f, gain) * d.stance().escapeRate;
        if (d.escape >= cfg.escapeThreshold) {
            d.escape = 0; set(a, State::HitStun, 20); set(d, State::Idle); push(a, d, 0.5f); push(d, a, 0.5f);
            emit("GrappleEscaped", &d, &a);
        }
    }
}

void CombatSimulation::resolveHits() {
    char at = attempt(*A, *B), bt = attempt(*B, *A);
    if (at == 'G' && bt == 'G') {
        set(*A, State::BlockStun, 14); set(*B, State::BlockStun, 14); push(*A, *B, 0.4f); push(*B, *A, 0.4f);
        emit("Clash", A.get(), B.get()); return;
    }
    if (at == 'G' && bt == 'S') at = 0;
    if (bt == 'G' && at == 'S') bt = 0;
    const MoveDef* am = A->move; const MoveDef* bm = B->move;
    if (at == 'S') applyStrike(*A, *B, am); else if (at == 'G') applyGrapple(*A, *B);
    if (over) return;
    if (bt == 'S') applyStrike(*B, *A, bm); else if (bt == 'G' && B->state == State::Attacking) applyGrapple(*B, *A);
}

char CombatSimulation::attempt(Fighter& f, Fighter& o) {
    if (f.state != State::Attacking && f.state != State::Combo) return 0;
    const MoveDef* m = f.move;
    if (!m || f.moveConnected || !f.inActive()) return 0;
    if (m->kind == MoveKind::Taunt || m->kind == MoveKind::Throw || m->kind == MoveKind::EnvironmentalThrow || m->kind == MoveKind::Finisher || m->kind == MoveKind::ClinchStrike) return 0;
    if (distance() > m->range + cfg.bodyReach) return 0;
    if (isAny(o.state, { State::Defeated, State::GettingUp, State::FinisherVictim, State::Grappled, State::Grappling, State::Throwing, State::Finisher })) return 0;
    if (o.dodgeInvuln(cfg)) { if (!f.moveWasDodged) { f.moveWasDodged = true; emit("Dodged", &o, &f, m->id); } return 0; }
    bool down = o.state == State::KnockedDown;
    if (m->kind == MoveKind::GroundAttack) return down ? 'S' : 0;
    if (down) return 0;
    return m->kind == MoveKind::Grapple ? 'G' : 'S';
}

void CombatSimulation::applyStrike(Fighter& a, Fighter& d, const MoveDef* m) {
    a.moveConnected = true;
    bool parry = d.state == State::Blocking && tick - d.blockStartTick <= d.parryWindow();
    bool stance = d.state == State::Countering && d.stateTicks <= d.counterWindow();
    if (m->kind != MoveKind::GroundAttack && (parry || stance) && d.moves->counter) { applyCounter(d, a, parry ? "parry" : "stance"); return; }
    if (d.state == State::Blocking || d.state == State::BlockStun) { applyBlock(a, d, m); return; }
    applyHit(a, d, m);
}

void CombatSimulation::applyCounter(Fighter& c, Fighter& v, const char* how) {
    const MoveDef* cm = c.moves->counter; float dmg = scaled(c, v, cm);
    v.health -= dmg; v.combo++; addMomentum(c, cm->momentumGain); c.counters++;
    emit("Countered", &c, &v, cm->id, dmg, how);
    if (v.health <= 0) { defeat(v, c, "Knockout"); return; }
    set(v, State::HitStun, cm->hitStun);
    set(c, State::Attacking); c.move = cm; c.moveStartup = c.adjustStartup(cm->startup); c.moveTick = c.moveStartup + cm->active;
    c.moveConnected = true; c.moveWasDodged = false;
}

void CombatSimulation::applyBlock(Fighter& a, Fighter& d, const MoveDef* m) {
    float chip = m->damage * cfg.chipDamageFraction * a.strMul() * a.stance().damageFor(m->kind) * d.stance().blockChip;
    d.health = std::max(1.0f, d.health - chip); d.stamina -= m->staminaDamage * d.stance().guardDrain; addMomentum(a, m->momentumGain * 0.3f);
    push(d, a, m->pushback * 0.7f);
    if (d.stamina <= 0) { d.stamina = d.maxStamina * 0.25f; set(d, State::Stunned, cfg.guardBreakStunTicks); emit("GuardBreak", &a, &d, m->id, chip); return; }
    set(d, State::BlockStun, m->blockStun); emit("Blocked", &a, &d, m->id, chip);
}

void CombatSimulation::applyHit(Fighter& a, Fighter& d, const MoveDef* m) {
    float dmg = scaled(a, d, m);
    float stunScale = std::max(cfg.minHitStunScale, 1 - cfg.hitStunDecay * d.combo);
    d.health -= dmg; d.combo++; addMomentum(a, m->momentumGain); addMomentum(d, m->momentumGain * 0.25f);
    emit("Hit", &a, &d, m->id, dmg, "combo " + std::to_string(d.combo)); push(d, a, m->pushback);
    if (d.health <= 0) { defeat(d, a, "Knockout"); return; }
    if (m->knockdown) { knockdown(d); return; }
    set(d, State::HitStun, std::max(4, roundAway(m->hitStun * stunScale)));
}

void CombatSimulation::applyGrapple(Fighter& a, Fighter& d) {
    a.moveConnected = true; set(a, State::Grappling); a.clinchTicks = 0; a.clinchStrikes = 0; a.clinchCooldown = 10;
    set(d, State::Grappled); d.escape = 0;
    d.x = clampf(a.x + a.facing * cfg.minSeparation, -cfg.arenaHalfWidth, cfg.arenaHalfWidth);
    emit("GrappleStarted", &a, &d);
}

void CombatSimulation::applyThrow(Fighter& a, Fighter& d, const MoveDef* m) {
    a.moveConnected = true; bool env = m->kind == MoveKind::EnvironmentalThrow; float dmg = scaled(a, d, m);
    d.health -= dmg; d.combo++; addMomentum(a, m->momentumGain); a.throws++;
    emit("Thrown", &a, &d, m->id, dmg, env ? "environment" : "throw");
    float h = cfg.arenaHalfWidth;
    if (env) { float sx = d.x == 0 ? (float)a.facing : d.x; d.x = (sx > 0 ? 1.0f : -1.0f) * h; }
    else d.x = clampf(a.x + a.facing * 1.4f, -h, h);
    if (d.health <= 0) { defeat(d, a, "Knockout"); return; }
    knockdown(d);
}

void CombatSimulation::knockdown(Fighter& d) {
    int t = std::max(30, roundAway(cfg.knockdownTicks * (1.1f - d.stats.durability / 500.0f)));
    set(d, State::KnockedDown, t); emit("KnockedDown", &d, nullptr);
}

std::string CombatSimulation::canFinish(const Fighter& f) const {
    const Fighter& o = &f == A.get() ? *B : *A;
    if (!f.moves->finisher) return "NoFinisherMove";
    if (!isAny(f.state, { State::Idle, State::Moving, State::Blocking })) return "AttackerBusy";
    if (f.momentum < cfg.finisherMomentum) return "NotEnoughMomentum";
    if (!isAny(o.state, { State::Idle, State::Moving, State::Blocking, State::BlockStun, State::HitStun, State::Stunned, State::Countering })) return "OpponentStateInvalid";
    if (!(o.healthFraction() <= cfg.finisherHealthFraction || o.state == State::Stunned)) return "OpponentNotVulnerable";
    if (distance() > cfg.finisherRange) return "OutOfRange";
    return "None";
}

void CombatSimulation::startFinisher(Fighter& f, Fighter& o) {
    f.momentum = 0; const MoveDef* m = f.moves->finisher; f.stamina += m->staminaCost;
    startMove(f, m, State::Finisher); set(o, State::FinisherVictim); emit("FinisherStarted", &f, &o, m->id);
}

void CombatSimulation::applyFinisher(Fighter& a, Fighter& d, const MoveDef* m) {
    a.moveConnected = true; float dmg = m->damage * a.strMul() * d.durMul();
    d.health -= dmg; emit("Hit", &a, &d, m->id, dmg, "finisher");
    if (d.health <= 0) { defeat(d, a, "Finisher"); return; }
    knockdown(d);
}

float CombatSimulation::scaled(const Fighter& a, const Fighter& d, const MoveDef* m) const {
    return m->damage * a.strMul() * d.durMul() * std::max(cfg.minDamageScale, 1 - cfg.comboDamageDecay * d.combo) * a.stance().damageFor(m->kind);
}
void CombatSimulation::addMomentum(Fighter& f, float n) { f.momentum = clampf(f.momentum + n, 0, cfg.momentumMax); }

void CombatSimulation::set(Fighter& f, State s, int duration) {
    if (!isAny(s, { State::Attacking, State::Combo, State::Throwing, State::Finisher })) f.move = nullptr;
    f.state = s; f.stateTicks = 0; f.stateDuration = duration;
    if (isAny(s, { State::Idle, State::Moving, State::Blocking, State::GettingUp })) f.combo = 0;
}

void CombatSimulation::regen(Fighter& f) {
    float r = 0;
    switch (f.state) { case State::Idle: case State::Moving: r = 1; break; case State::Blocking: r = 0.25f; break;
                       case State::KnockedDown: case State::Grappled: r = 0.5f; break; default: break; }
    if (r <= 0) return;
    f.stamina = std::min(f.maxStamina, f.stamina + cfg.staminaRegenPerSecond * (0.5f + f.stats.stamina / 200.0f) * cfg.dt() * r);
}

void CombatSimulation::push(Fighter& t, const Fighter& from, float amt) {
    float d = t.x - from.x; int dir = d > 0 ? 1 : (d < 0 ? -1 : 0);
    if (dir == 0) dir = from.facing;
    t.x += dir * amt;
}
void CombatSimulation::updateFacing() {
    if (A->x < B->x) { A->facing = 1; B->facing = -1; } else if (A->x > B->x) { A->facing = -1; B->facing = 1; }
}

void CombatSimulation::resolvePositions() {
    float h = cfg.arenaHalfWidth;
    A->x = clampf(A->x, -h, h); B->x = clampf(B->x, -h, h);
    bool clinching = A->state == State::Grappling || B->state == State::Grappling;
    float gap = distance();
    if (!clinching && gap < cfg.minSeparation) {
        float p = (cfg.minSeparation - gap) / 2;
        Fighter& left = A->x <= B->x ? *A : *B; Fighter& right = &left == A.get() ? *B : *A;
        left.x -= p; right.x += p;
        if (left.x < -h) { right.x += -h - left.x; left.x = -h; }
        if (right.x > h) { left.x -= right.x - h; right.x = h; }
    }
    updateFacing();
}

void CombatSimulation::defeat(Fighter& loser, Fighter& winner, const char* method) {
    loser.health = 0; set(loser, State::Defeated); emit("Defeated", &winner, &loser, "", 0, method);
    over = true; result = { fightId, winner.id, loser.id, method, tick };
}
void CombatSimulation::endOnTime() {
    emit("TimeUp", A.get(), B.get());
    float a = A->healthFraction(), b = B->healthFraction();
    over = true;
    if (std::fabs(a - b) < 1e-4f) { result = { fightId, "", "", "Draw", tick }; return; }
    result = a > b ? FightResult{ fightId, A->id, B->id, "TimeDecision", tick } : FightResult{ fightId, B->id, A->id, "TimeDecision", tick };
}
void CombatSimulation::emit(const char* type, const Fighter* actor, const Fighter* target, const std::string& moveId, float amount, const std::string& detail) {
    CombatEvent e; e.type = type; e.actor = actor ? actor->id : ""; e.target = target ? target->id : ""; e.moveId = moveId;
    e.amount = amount; e.tick = tick; e.detail = detail;
    events.push_back(e);
}

// ================================================================================================ AI
FighterAI::FighterAI(Fighter& self, const std::string& style, const std::string& difficulty, uint64_t seed) : self_(self), style_(style), rng_(seed) {
    if (difficulty == "Easy") p_ = { 24, 40, 0.2f, 0.05f, 0.25f, 3, 0.3f, 0.1f };
    else if (difficulty == "Hard") p_ = { 10, 18, 0.6f, 0.3f, 0.8f, 7, 0.08f, 0.35f };
    else if (difficulty == "Expert") p_ = { 8, 14, 0.72f, 0.4f, 0.9f, 9, 0.04f, 0.45f };
    else if (difficulty == "Boss") p_ = { 6, 12, 0.8f, 0.5f, 0.95f, 10, 0.02f, 0.55f };
    else p_ = { 14, 26, 0.4f, 0.15f, 0.55f, 5, 0.15f, 0.25f };
}
float FighterAI::freq(const std::string& k) const { if (total_ <= 0) return 0; auto it = openers_.find(k); return it == openers_.end() ? 0 : it->second / total_; }
void FighterAI::observe(const std::string& k) { for (auto& kv : openers_) kv.second *= 0.93f; total_ = total_ * 0.93f + 1; openers_[k] += 1; }

FighterInput FighterAI::think(CombatSimulation& sim) {
    Fighter& me = self_; Fighter& opp = sim.other(me); const CombatConfig& c = sim.cfg;
    Seen now; now.tick = sim.tick; now.state = opp.state; now.hasKind = opp.move != nullptr; if (opp.move) { now.kind = opp.move->kind; now.range = opp.move->range; }
    now.startup = opp.inStartup(); now.recovery = opp.inRecovery() && !opp.moveConnected; now.x = opp.x;
    queue_.push_back(now);
    Seen seen; bool any = false;
    while (!queue_.empty() && sim.tick - queue_.front().tick >= p_.react) { seen = queue_.front(); queue_.erase(queue_.begin()); any = true; }
    if (any) {
        bool started = seen.state == State::Attacking && lastSeen_ != State::Attacking && lastSeen_ != State::Combo;
        if (started && seen.hasKind) observe(toString(seen.kind));
        blockTendency_ = blockTendency_ * 0.995f + (seen.state == State::Blocking ? 0.005f : 0);
        lastSeen_ = seen.state;
    } else { seen = Seen(); seen.x = opp.x; }
    float dist = std::fabs(me.x - seen.x);
    auto press = [](Command cmd) { return FighterInput::press(cmd); };
    FighterInput none;

    switch (me.state) {
        case State::Defeated: case State::FinisherVictim: case State::GettingUp: case State::Throwing: case State::Finisher: case State::Countering:
        case State::Dodging: case State::BlockStun: case State::Stunned: return none;
        case State::Grappled:
            if (opp.state == State::Grappling && opp.clinchTicks <= c.reversalWindowTicks && me.momentum >= c.reversalMomentumCost && rng_.chance(p_.counter * 0.15f)) return press(Command::Counter);
            return rng_.chance(p_.escape / c.tickRate) ? press(Command::Grapple) : none;
        case State::Grappling: {
            if (!rng_.chance(0.08f)) return none;
            bool wall = c.arenaHalfWidth - std::fabs(opp.x) <= c.wallThrowDistance;
            if (wall && me.moves->environmentalThrow) return press(Command::Grapple);
            if (me.clinchTicks < 40 && rng_.chance(0.6f)) return press(Command::Light);
            return press(Command::Heavy);
        }
        case State::HitStun:
            if (me.combo >= c.breakawayMinCombo && me.momentum >= c.breakawayMomentumCost && rng_.chance(p_.def * 0.3f)) return press(Command::Dodge);
            return none;
        case State::KnockedDown: return me.stateTicks >= c.quickRiseAfterTicks && rng_.chance(p_.def * 0.1f) ? press(Command::Dodge) : none;
        case State::Attacking: case State::Combo: {
            if (!me.moveConnected || !me.inRecovery() || !me.move) return none;
            std::vector<Command> opts;
            for (const auto& l : me.moves->links()) if (l.from == me.move->id) opts.push_back(l.command);
            if (opts.empty() || !rng_.chance(p_.combo)) return none;
            return press(opts[rng_.range(0, (int)opts.size())]);
        }
        default: break;
    }
    if (sim.canFinish(me) == "None") return press(Command::Finisher);

    if (seen.startup && seen.hasKind && seen.tick != lastDefended_ && dist <= seen.range + 0.8f) {
        lastDefended_ = seen.tick;
        float adapt = freq(toString(seen.kind)) * p_.adapt;
        if (rng_.chance(std::min(0.95f, p_.def + adapt))) {
            if (seen.kind == MoveKind::Grapple) return me.stamina >= 12 ? press(Command::Dodge) : press(Command::Light);
            bool tech = style_ == "Technical" || style_ == "Defensive";
            if (tech && me.moves->counter && rng_.chance(p_.counter + adapt)) return press(Command::Counter);
            if (style_ == "Showman" && rng_.chance(0.3f) && me.stamina >= 12) return press(Command::Dodge);
            blockHold_ = 18 + rng_.range(0, 10);
            return FighterInput::hold();
        }
    }
    if (blockHold_ > 0) { blockHold_--; return FighterInput::hold(); }

    if (dist <= 1.9f && (seen.state == State::Idle || seen.state == State::Moving)) {
        float strike = std::max(freq("Heavy"), std::max(freq("Light"), freq("Kick"))), grab = freq("Grapple");
        float perTick = p_.adapt * 0.08f;
        if (grab > 0.5f && me.stamina > 20 && rng_.chance(grab * perTick)) return press(Command::Dodge);
        if (strike > 0.5f && rng_.chance(strike * perTick)) { blockHold_ = 24; return FighterInput::hold(); }
    }

    if (sim.tick < nextDecision_) {
        if (style_ == "Defensive" && dist < 2.5f) return none;
        return dist > 1.4f ? FighterInput::walk(0.8f) : none;
    }
    nextDecision_ = sim.tick + p_.interval + rng_.range(0, p_.interval / 2 + 1);
    if (rng_.chance(p_.mistake)) return none;

    Command punish = style_ == "Wrestler" ? Command::Grapple : Command::Heavy;
    const MoveDef* pm = me.moves->entryFor(punish);
    if (seen.recovery && pm && dist <= pm->range + 0.35f && me.stamina >= pm->staminaCost) return press(punish);
    if (seen.state == State::KnockedDown && me.moves->groundAttack && dist <= me.moves->groundAttack->range + 0.35f) return press(Command::Heavy);
    if (me.stamina < me.maxStamina * 0.25f) return FighterInput::walk(-0.6f);

    float preferred = style_ == "Wrestler" ? 0.9f : 1.1f;
    if (dist > preferred + 0.3f) return FighterInput::walk(1);
    float wl, wh, wk, wg, wwait, wt;
    if (style_ == "Aggressive") { wl = .4f; wh = .3f; wk = .2f; wg = .1f; wwait = 0; wt = 0; }
    else if (style_ == "Technical") { wl = .45f; wh = .15f; wk = .25f; wg = .05f; wwait = .1f; wt = 0; }
    else if (style_ == "Wrestler") { wl = .2f; wh = .2f; wk = .1f; wg = .5f; wwait = 0; wt = 0; }
    else if (style_ == "Defensive") { wl = .3f; wh = .15f; wk = .1f; wg = .05f; wwait = .4f; wt = 0; }
    else { wl = .3f; wh = .25f; wk = .25f; wg = .05f; wwait = 0; wt = .15f; }
    float aff = style_ == "Wrestler" ? 1 : (style_ == "Aggressive" ? 0.5f : 0.25f);
    wg += std::min(0.5f, blockTendency_ * 2) * p_.adapt * 2 * aff;
    float r = rng_.value() * (wl + wh + wk + wg + wwait + wt);
    if ((r -= wl) < 0) return press(Command::Light);
    if ((r -= wh) < 0) return press(Command::Heavy);
    if ((r -= wk) < 0) return press(Command::Kick);
    if ((r -= wg) < 0) return press(Command::Grapple);
    if ((r -= wt) < 0 && dist > 1.5f) return press(Command::Taunt);
    blockHold_ = 20;
    return FighterInput::hold();
}

// ================================================================================================ progression
PostStatus Bank::pay(const std::string& key, long long cents) {
    if (key.empty() || cents <= 0) return PostStatus::Invalid;
    if (keys.count(key)) return PostStatus::Duplicate;
    if (cash < cents) return PostStatus::InsufficientFunds;
    cash -= cents; keys.insert(key); return PostStatus::Posted;
}
PostStatus Bank::receive(const std::string& key, long long cents) {
    if (key.empty() || cents <= 0) return PostStatus::Invalid;
    if (keys.count(key)) return PostStatus::Duplicate;
    cash += cents; keys.insert(key); return PostStatus::Posted;
}

StyleLoadout::StyleLoadout(const CombatDatabase& db, Bank* bank) : db_(db), bank_(bank) { reset(); }
void StyleLoadout::reset() { learned.clear(); equipped.clear(); if (db_.style(Starter)) { learned.push_back(Starter); equipped.push_back(Starter); } }
bool StyleLoadout::knows(const std::string& id) const { return std::find(learned.begin(), learned.end(), id) != learned.end(); }
const StyleDef* StyleLoadout::activeHybrid() const {
    if (equipped.size() < 2) return nullptr;
    const StyleDef* h = db_.findHybrid(equipped[0], equipped[1]);
    return h && knows(h->id) ? h : nullptr;
}
StyleResult StyleLoadout::canLearn(const std::string& id) const {
    const StyleDef* s = db_.style(id);
    if (!s) return StyleResult::UnknownStyle;
    if (knows(id)) return StyleResult::AlreadyLearned;
    if (s->isHybrid() && !(knows(s->hybridOf[0]) && knows(s->hybridOf[1]))) return StyleResult::ParentsNotLearned;
    return StyleResult::Ok;
}
StyleResult StyleLoadout::learn(const std::string& id) {
    StyleResult r = canLearn(id); if (r != StyleResult::Ok) return r;
    const StyleDef* s = db_.style(id);
    if (s->unlockCostCents > 0) {
        PostStatus st = bank_ ? bank_->pay("style.learn." + id, s->unlockCostCents) : PostStatus::InsufficientFunds;
        if (st != PostStatus::Posted && st != PostStatus::Duplicate) return StyleResult::InsufficientFunds;
    }
    learned.push_back(id); return StyleResult::Ok;
}
StyleResult StyleLoadout::equip(const std::string& primary, const std::string& secondary) {
    const StyleDef* p = db_.style(primary);
    if (!p || p->isHybrid()) return StyleResult::InvalidLoadout;
    if (!knows(primary)) return StyleResult::NotLearned;
    if (!secondary.empty()) {
        const StyleDef* s = db_.style(secondary);
        if (!s || s->isHybrid() || secondary == primary) return StyleResult::InvalidLoadout;
        if (!knows(secondary)) return StyleResult::NotLearned;
    }
    equipped.clear(); equipped.push_back(primary); if (!secondary.empty()) equipped.push_back(secondary);
    return StyleResult::Ok;
}
std::shared_ptr<Moveset> StyleLoadout::buildMoveset() const {
    std::vector<std::string> hybrids;
    for (const auto& id : learned) { const StyleDef* s = db_.style(id); if (s && s->isHybrid()) hybrids.push_back(id); }
    return db_.buildMoveset(equipped, true, &hybrids);
}
void StyleLoadout::restore(const std::vector<std::string>& l, const std::vector<std::string>& e) {
    reset();
    for (const auto& id : l) if (db_.style(id) && !knows(id)) learned.push_back(id);
    std::vector<std::string> eq;
    for (const auto& id : e) {
        const StyleDef* s = db_.style(id);
        if (s && !s->isHybrid() && knows(id) && std::find(eq.begin(), eq.end(), id) == eq.end() && eq.size() < 2) eq.push_back(id);
    }
    if (!eq.empty()) equipped = eq;
}

} // namespace ffts
