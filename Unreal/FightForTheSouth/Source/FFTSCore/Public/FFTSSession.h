// FIGHT FOR THE SOUTH - FFTSCore
// The game session: owns story, missions, bank, reputation, relationships and the living world, wires the cross-system
// rules (triggers, rewards, talk rules, fight consequences) and saves/loads everything as JSON.
// Port of WebPrototype/src/sim/session.js (vehicles and racing are not ported yet).
#pragma once
#include "FFTSJson.h"
#include "FFTSStory.h"
#include "FFTSWorld.h"
#include <functional>
#include <map>
#include <memory>
#include <string>
#include <vector>

#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable : 4251 4275)
#endif

namespace ffts {

/** The story content the session needs (same files the browser build reads). */
struct FFTSCORE_API GameData {
    Json missions;    // Missions/chapter1.json  -> Missions
    Json dialogues;   // Dialogue/chapter1_dialogue.json -> Dialogues
    Json rules;       // Dialogue/npc_conversations.json -> Rules
    Json npcs;        // Characters/npc_profiles.json -> Npcs
    Json layout;      // World/southside_layout.json
    /** read(relativePath) returns the file text ("" if missing). Problems are appended to errors. */
    bool load(const std::function<std::string(const std::string&)>& read, std::vector<std::string>& errors);
};

class Game;

struct FFTSCORE_API DialogueChoice { bool ok = false; bool ended = false; std::string fight; };

/** Walks one dialogue tree. Options and line variants are filtered by flags, relationship, attitude and time of day. */
class FFTSCORE_API DialogueRunner {
public:
    DialogueRunner(const Json& def, Game& game);
    const std::string& id() const;
    const std::string& speaker() const;
    std::string line() const;
    std::vector<const Json*> options() const;
    DialogueChoice choose(const std::string& optionId);
    bool finished() const { return current_ == nullptr; }
private:
    bool matches(const Json& when) const;
    const Json* node(const std::string& id) const;
    const Json& def_;
    Game& game_;
    const Json* current_ = nullptr;
};

class FFTSCORE_API Game {
public:
    static constexpr int SaveVersion = 1;
    explicit Game(const GameData& data);
    Game(const Game&) = delete;
    Game& operator=(const Game&) = delete;

    // ---- clock (minutes since Monday 00:00 of week 0; starts Monday 08:00)
    int day() const { return static_cast<int>(minutes / 1440); }
    int hour() const { return static_cast<int>(minutes % 1440 / 60); }
    int minute() const { return static_cast<int>(minutes % 60); }
    std::string segment() const;   // Morning, Afternoon, Evening, Night, LateNight
    std::string clockText() const;
    /** Advances the clock one game minute per real second when clockRunning, replans NPCs on the hour, moves them. */
    void tick(double sec, double playerX, double playerZ, bool clockRunning);
    void sleepUntilMorning();

    // ---- story
    void trigger(const std::string& t);
    void grant(const std::string& key, const Json& reward);
    void applyEffect(const std::string& key, const Json& effect);
    std::string npcName(const std::string& id) const;
    std::unique_ptr<DialogueRunner> startDialogue(const std::string& dialogueId);
    /** What happens when the player talks to an NPC right now: the first matching rule (dialogue, bark or fight), or null. */
    const Json* talkTo(const std::string& npcId);
    /** Applies reputation, relationship and memory consequences and reports the fight trigger. Returns true if the player won. */
    bool onFight(const std::string& winnerId, const std::string& method, const std::string& opponentId, const std::string& venue,
                 const std::string& locationId, const std::vector<std::string>& witnesses);
    std::vector<std::string> takeToasts() { std::vector<std::string> t; t.swap(toasts); return t; }

    // ---- saves
    Json capture(double playerX, double playerZ) const;
    bool restore(const Json& save);

    // ---- state (public like the reference implementation, for tests and the UI)
    long long minutes = 8 * 60;
    double clockAcc = 0;
    Bank bank{ 4000 };
    Reputation rep;
    Social social;
    Story story;
    std::unique_ptr<Missions> missions;
    std::unique_ptr<LivingWorld> world;
    std::vector<std::string> contacts;
    std::map<std::string, int> inventory;
    std::vector<std::string> toasts;
    int fightCount = 0;
    double savedX = 0, savedZ = 0;   // player position from the last restore

private:
    bool pinMet(const Json& e) const;
    GameData data_;   // own copy, so the caller's GameData can go away
    std::map<std::string, const Json*> dialogues_;
    int lastHour_ = -1;
    bool dirty_ = false;
};

} // namespace ffts

#ifdef _MSC_VER
#pragma warning(pop)
#endif
