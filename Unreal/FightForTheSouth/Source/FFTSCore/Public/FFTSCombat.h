// FIGHT FOR THE SOUTH - FFTSCore combat simulation.
// C++ port of FFTS.Combat (Unity C#) and WebPrototype/src/sim/combat.js: same rules, same numbers, same JSON content.
// Engine-agnostic: no Unreal types, no exceptions, no RTTI. The Unreal game module presents and drives it.
#pragma once
#include <cstdint>
#include <map>
#include <memory>
#include <set>
#include <string>
#include <vector>
#include "FFTSJson.h"

// FFTSCORE_API is defined by Unreal Build Tool (dllexport/dllimport, the editor builds each module as a DLL);
// the stand-alone test build leaves it empty.
#if defined(_MSC_VER) && !defined(DLLEXPORT)   // UBT sets FFTSCORE_API=DLLEXPORT; these .cpp files skip engine headers
#define DLLEXPORT __declspec(dllexport)
#define DLLIMPORT __declspec(dllimport)
#endif
#ifndef FFTSCORE_API
#define FFTSCORE_API
#endif
#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable : 4251 4275)  // std:: members of exported classes
#endif

namespace ffts {

// ------------------------------------------------------------------------------------------------ utilities
class FFTSCORE_API Rng {  // SplitMix64, identical to FFTS.Core.DeterministicRandom
public:
    explicit Rng(uint64_t seed = 1) : state(seed) {}
    uint64_t state;
    uint64_t next();
    int range(int minIncl, int maxExcl);
    float value();               // [0,1) with 24-bit resolution
    bool chance(float p);
};
inline float clampf(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }
FFTSCORE_API int roundAway(float v);

// ------------------------------------------------------------------------------------------------ enums
enum class State { Idle, Moving, Attacking, Combo, Finisher, Blocking, Countering, Grappling, Throwing, KnockedDown, GettingUp,
                   Stunned, Defeated, HitStun, BlockStun, Dodging, Grappled, FinisherVictim };
enum class Command { None, Light, Heavy, Kick, Grapple, Dodge, Counter, Finisher, Taunt };
enum class MoveKind { Light, Heavy, Kick, Grapple, Throw, EnvironmentalThrow, Counter, Signature, Finisher, GroundAttack, Taunt, ClinchStrike };

FFTSCORE_API const char* toString(State s);
FFTSCORE_API const char* toString(Command c);
FFTSCORE_API const char* toString(MoveKind k);
FFTSCORE_API bool parseCommand(const std::string& s, Command& out);
FFTSCORE_API bool parseMoveKind(const std::string& s, MoveKind& out);

// ------------------------------------------------------------------------------------------------ content
struct FFTSCORE_API StanceDef {
    std::string name = "Neutral", defense = "Block", description, idleAnimation, guardAnimation;
    float walkSpeed = 1, strikeDamage = 1, kickDamage = 1, throwDamage = 1, blockChip = 1, guardDrain = 1;
    float dodgeDistance = 1, dodgeCost = 1, escapeRate = 1;
    int parryWindowBonus = 0, counterWindowBonus = 0, clinchStrikesBonus = 0;
    float damageFor(MoveKind k) const;
    static const StanceDef& neutral();
};

struct MoveDef {
    std::string id, displayName, animationKey;
    MoveKind kind = MoveKind::Light;
    int startup = 1, active = 1, recovery = 0, hitStun = 0, blockStun = 0, cancelWindow = 0;
    float damage = 0, staminaCost = 0, staminaDamage = 0, momentumGain = 0, range = 1, pushback = 0;
    bool knockdown = false;
};

struct MoveLink { std::string from, to; Command command = Command::None; };

struct StyleDef {
    std::string id, displayName, description, discipline, learnAt, strengths, weaknesses;
    long long unlockCostCents = 0;
    bool hasStance = false;
    StanceDef stance;
    std::vector<std::string> hybridOf;
    bool isHybrid() const { return !hybridOf.empty(); }
    std::vector<MoveDef> moves;
    std::vector<MoveLink> links;
    // slots
    std::string light, heavy, kick, grapple, counter, finisher, groundAttack, taunt, clinchStrike, clinchThrow, environmentalThrow, reversal;
};

struct FighterStats { int strength = 50, speed = 50, durability = 50, stamina = 50, technique = 50, aggression = 50, confidence = 50, composure = 50; };

struct FighterDef {
    std::string id, displayName, archetype, aiStyle = "Technical", aiDifficulty = "Normal";
    FighterStats stats;
    std::vector<std::string> styles;
};

// ------------------------------------------------------------------------------------------------ moveset
class FFTSCORE_API Moveset {
public:
    static const int MaxStyles = 3;
    // Builds the merged moveset. A hybrid (if given) goes first: its stance, slots and links take priority.
    static std::shared_ptr<Moveset> build(const std::vector<const StyleDef*>& styles, const StyleDef* hybrid = nullptr);

    const MoveDef* get(const std::string& id) const;
    const MoveDef* findLink(const std::string& from, Command c) const;
    const MoveDef* entryFor(Command c) const;
    const StanceDef& stance() const { return stance_ ? *stance_ : StanceDef::neutral(); }
    const std::string& hybridId() const { return hybridId_; }
    const std::vector<MoveLink>& links() const { return links_; }

    const MoveDef *light = nullptr, *heavy = nullptr, *kick = nullptr, *grapple = nullptr, *counter = nullptr, *finisher = nullptr,
                  *groundAttack = nullptr, *taunt = nullptr, *clinchStrike = nullptr, *clinchThrow = nullptr,
                  *environmentalThrow = nullptr, *reversal = nullptr;
private:
    std::map<std::string, const MoveDef*> moves_;
    std::vector<MoveLink> links_;
    const StanceDef* stance_ = nullptr;
    std::string hybridId_;
};

// ------------------------------------------------------------------------------------------------ database
class FFTSCORE_API CombatDatabase {
public:
    CombatDatabase() = default;
    CombatDatabase(const CombatDatabase&) = delete;             // owns its styles; exported classes must not
    CombatDatabase& operator=(const CombatDatabase&) = delete;  // try to generate a copy of vector<unique_ptr>
    // Loads style files ({"Styles":[...]}) and fighter files ({"Fighters":[...]}); appends errors to `errors`.
    bool load(const std::vector<std::string>& styleJson, const std::vector<std::string>& fighterJson, std::vector<std::string>& errors);
    void validate(std::vector<std::string>& errors) const;

    const StyleDef* style(const std::string& id) const;
    const FighterDef* fighter(const std::string& id) const;
    const std::vector<std::unique_ptr<StyleDef>>& styles() const { return styles_; }
    const std::vector<FighterDef>& fighters() const { return fighters_; }

    const StyleDef* findHybrid(const std::string& a, const std::string& b) const;
    // Fighter's moveset; when its first two styles form a hybrid, the hybrid applies (if unlocked, when a list is given).
    std::shared_ptr<Moveset> buildMoveset(const std::vector<std::string>& styleIds, bool allowHybrid = true,
                                          const std::vector<std::string>* unlockedHybrids = nullptr) const;
private:
    std::vector<std::unique_ptr<StyleDef>> styles_;
    std::vector<FighterDef> fighters_;
};

// ------------------------------------------------------------------------------------------------ simulation
struct CombatConfig {
    int tickRate = 60;
    float arenaHalfWidth = 6, minSeparation = 0.7f, bodyReach = 0.35f, wallThrowDistance = 1.3f;
    int inputBufferTicks = 8, dodgeTicks = 20, dodgeInvulnStart = 2, dodgeInvulnEnd = 14;
    float dodgeDistance = 1.6f, dodgeStaminaCost = 12;
    int counterStanceTicks = 26; float counterStaminaCost = 6;
    int knockdownTicks = 70, getUpTicks = 24, quickRiseAfterTicks = 20, stunTicks = 90, guardBreakStunTicks = 75;
    int clinchMaxTicks = 110, clinchStrikeCooldown = 20, clinchStrikesMax = 3;
    float escapeThreshold = 100; int reversalWindowTicks = 14; float reversalMomentumCost = 25;
    int breakawayMinCombo = 3; float breakawayMomentumCost = 30, breakawayStaminaCost = 15;
    float momentumMax = 100, finisherMomentum = 100, finisherHealthFraction = 0.35f, finisherRange = 1.8f;
    float comboDamageDecay = 0.15f, minDamageScale = 0.35f, hitStunDecay = 0.12f, minHitStunScale = 0.4f, chipDamageFraction = 0.1f;
    float staminaRegenPerSecond = 14, tauntMomentum = 15;
    int roundTicks = 60 * 180;
    float dt() const { return 1.0f / tickRate; }
};

struct FighterInput {
    float move = 0; bool block = false; Command command = Command::None;
    static FighterInput none() { return {}; }
    static FighterInput press(Command c) { FighterInput i; i.command = c; return i; }
    static FighterInput hold() { FighterInput i; i.block = true; return i; }
    static FighterInput walk(float m) { FighterInput i; i.move = m; return i; }
};

struct CombatEvent {
    std::string type, actor, target, moveId, detail;
    float amount = 0; int tick = 0;
};

struct FightResult { std::string fightId, winnerId, loserId, method; int durationTicks = 0; };

class FFTSCORE_API Fighter {
public:
    Fighter(const FighterDef& def, std::shared_ptr<Moveset> moves, float x);
    std::string id; const FighterDef* def; std::shared_ptr<Moveset> moves; FighterStats stats;
    float maxHealth, maxStamina, health, stamina, momentum = 0, x, escape = 0;
    int facing = 1;
    State state = State::Idle; int stateTicks = 0, stateDuration = 0;
    const MoveDef* move = nullptr; int moveTick = 0, moveStartup = 0; bool moveConnected = false, moveWasDodged = false;
    int combo = 0, blockStartTick = 0; Command buffered = Command::None; int bufferedAge = 0; bool prevBlock = false;
    int clinchTicks = 0, clinchStrikes = 0, clinchCooldown = 0, counters = 0, throws = 0;

    void applyScenario(float healthV, float momentumV, float staminaV);  // negative = unchanged
    float healthFraction() const { return health / maxHealth; }
    const StanceDef& stance() const { return moves->stance(); }
    float walkSpeed() const { return (2.2f + stats.speed / 50.0f) * stance().walkSpeed; }
    float strMul() const { return 0.7f + stats.strength / 166.0f; }
    float durMul() const { return 1.15f - stats.durability / 333.0f; }
    int counterWindow() const { return 6 + stats.technique / 15 + stance().counterWindowBonus; }
    int parryWindow() const { return 3 + stats.technique / 25 + stance().parryWindowBonus; }
    int adjustStartup(int s) const;
    bool inActive() const { return move && moveTick >= moveStartup && moveTick < moveStartup + move->active; }
    bool inStartup() const { return move && moveTick < moveStartup; }
    bool inRecovery() const { return move && moveTick >= moveStartup + move->active; }
    bool dodgeInvuln(const CombatConfig& c) const { return state == State::Dodging && stateTicks >= c.dodgeInvulnStart && stateTicks < c.dodgeInvulnEnd; }
};

class FFTSCORE_API CombatSimulation {
public:
    CombatSimulation(const CombatConfig& cfg, std::unique_ptr<Fighter> a, std::unique_ptr<Fighter> b, std::string fightId);
    CombatConfig cfg;
    std::unique_ptr<Fighter> A, B;
    std::string fightId;
    int tick = 0;
    bool over = false;
    FightResult result;
    std::vector<CombatEvent> events;   // events of the last Step only

    void step(const FighterInput& ia, const FighterInput& ib);
    bool isOver() const { return over; }
    Fighter& other(const Fighter& f) { return &f == A.get() ? *B : *A; }
    float distance() const;
    std::string canFinish(const Fighter& f) const;   // "None" when allowed, otherwise the reason

private:
    void buffer(Fighter& f, const FighterInput& i);
    Command take(Fighter& f);
    void update(Fighter& f, Fighter& o, const FighterInput& input);
    void neutral(Fighter& f, Fighter& o, const FighterInput& input);
    bool tryCommand(Fighter& f, Fighter& o, Command cmd);
    bool startMove(Fighter& f, const MoveDef* m, State s);
    void advance(Fighter& f, Fighter& o);
    bool breakaway(Fighter& f, Fighter& o);
    void clinch(Fighter& a, Fighter& d);
    void clinchDefender(Fighter& d, Fighter& a);
    void resolveHits();
    char attempt(Fighter& f, Fighter& o);
    void applyStrike(Fighter& a, Fighter& d, const MoveDef* m);
    void applyCounter(Fighter& c, Fighter& v, const char* how);
    void applyBlock(Fighter& a, Fighter& d, const MoveDef* m);
    void applyHit(Fighter& a, Fighter& d, const MoveDef* m);
    void applyGrapple(Fighter& a, Fighter& d);
    void applyThrow(Fighter& a, Fighter& d, const MoveDef* m);
    void knockdown(Fighter& d);
    void startFinisher(Fighter& f, Fighter& o);
    void applyFinisher(Fighter& a, Fighter& d, const MoveDef* m);
    float scaled(const Fighter& a, const Fighter& d, const MoveDef* m) const;
    void addMomentum(Fighter& f, float n);
    void set(Fighter& f, State s, int duration = 0);
    void regen(Fighter& f);
    void push(Fighter& t, const Fighter& from, float amt);
    void updateFacing();
    void resolvePositions();
    void defeat(Fighter& loser, Fighter& winner, const char* method);
    void endOnTime();
    void emit(const char* type, const Fighter* actor, const Fighter* target, const std::string& moveId = "", float amount = 0, const std::string& detail = "");
};

class FFTSCORE_API FighterAI {
public:
    FighterAI(Fighter& self, const std::string& style, const std::string& difficulty, uint64_t seed);
    FighterInput think(CombatSimulation& sim);
private:
    struct Params { int react, interval; float def, counter, combo, escape, mistake, adapt; };
    struct Seen { int tick = -1; State state = State::Idle; bool hasKind = false; MoveKind kind = MoveKind::Light; bool startup = false, recovery = false; float range = 0, x = 0; };
    float freq(const std::string& k) const;
    void observe(const std::string& k);
    Fighter& self_; std::string style_; Params p_; Rng rng_;
    std::vector<Seen> queue_; State lastSeen_ = State::Idle; int blockHold_ = 0, nextDecision_ = 0, lastDefended_ = -1000;
    std::map<std::string, float> openers_; float total_ = 0, blockTendency_ = 0;
};

// ------------------------------------------------------------------------------------------------ progression
enum class PostStatus { Posted, Duplicate, InsufficientFunds, Invalid };

/// Minimal FINESS BANK: personal cash with idempotent payments (a key is never charged twice, including after a save).
class FFTSCORE_API Bank {
public:
    explicit Bank(long long cashCents = 0) : cash(cashCents) {}
    long long cash;
    std::set<std::string> keys;
    PostStatus pay(const std::string& key, long long cents);
    PostStatus receive(const std::string& key, long long cents);
};

enum class StyleResult { Ok, AlreadyLearned, UnknownStyle, ParentsNotLearned, InsufficientFunds, NotLearned, InvalidLoadout };

class FFTSCORE_API StyleLoadout {
public:
    static constexpr const char* Starter = "style.street";
    StyleLoadout(const CombatDatabase& db, Bank* bank);
    std::vector<std::string> learned, equipped;
    bool knows(const std::string& id) const;
    const StyleDef* activeHybrid() const;
    StyleResult canLearn(const std::string& id) const;
    StyleResult learn(const std::string& id);
    StyleResult equip(const std::string& primary, const std::string& secondary = "");
    std::shared_ptr<Moveset> buildMoveset() const;
    void restore(const std::vector<std::string>& learnedIds, const std::vector<std::string>& equippedIds);
private:
    void reset();
    const CombatDatabase& db_; Bank* bank_;
};

} // namespace ffts

#ifdef _MSC_VER
#pragma warning(pop)
#endif
