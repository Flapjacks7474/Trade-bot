// Stand-alone tests for the FFTSCore story/session layer. Mirrors WebPrototype/tests/session.test.mjs and the C#
// CampaignTests so the three implementations agree. Build and run: Unreal/FightForTheSouth/CoreTests/run.sh
#include "FFTSSession.h"
#include <cstdio>
#include <fstream>
#include <functional>
#include <sstream>

using namespace ffts;

static int g_pass = 0, g_fail = 0;
static std::string g_current, g_data;
#define CHECK(cond, msg) do { if (!(cond)) { std::printf("    FAIL %s: %s (line %d)\n", g_current.c_str(), std::string(msg).c_str(), __LINE__); throw 1; } } while (0)

static void test(const char* name, const std::function<void()>& fn) {
    g_current = name;
    try { fn(); ++g_pass; std::printf("ok   %s\n", name); } catch (...) { ++g_fail; std::printf("FAIL %s\n", name); }
}

static std::string readFile(const std::string& p) { std::ifstream f(p, std::ios::binary); std::stringstream ss; ss << f.rdbuf(); return ss.str(); }

static const GameData& data() {
    static GameData d; static bool loaded = false;
    if (!loaded) {
        std::vector<std::string> errs;
        d.load([](const std::string& rel) { return readFile(g_data + "/" + rel); }, errs);
        for (auto& e : errs) std::printf("    content error: %s\n", e.c_str());
        loaded = true;
    }
    return d;
}

/** Plays the given options in order; returns the fight the dialogue starts (if any). */
static std::string talk(Game& g, const std::string& id, std::initializer_list<const char*> choices) {
    auto r = g.startDialogue(id);
    CHECK(r != nullptr, "no dialogue " + id);
    std::string fight;
    for (const char* c : choices) {
        DialogueChoice o = r->choose(c);
        CHECK(o.ok, id + ": option " + c + " not available");
        if (!o.fight.empty()) fight = o.fight;
    }
    return fight;
}

static void playChapterOne(Game& g) {
    g.trigger("location.southside.grandma_house");
    talk(g, "dialogue.grandma.welcome", { "respect", "photo", "ok" });
    talk(g, "dialogue.lou.barbershop", { "ask", "more", "sharp" });
    g.trigger("location.southside.corner_store");
    g.trigger("location.southside.tone_studio");
    talk(g, "dialogue.dre.reunion", { "challenge", "go" });
    g.trigger("location.southside.community_gym");
    const Json* rule = g.talkTo("npc.dre");
    CHECK(rule && (*rule)["FightId"].str() == "fighter.dre", "Dre's talk rule should start the spar");
    g.onFight("fighter.player", "Knockout", "fighter.dre", "Sanctioned", "location.southside.community_gym", {});
    g.trigger("location.southside.tone_studio");
    CHECK(talk(g, "dialogue.rell.confrontation", { "fight" }) == "fighter.rell", "Rell starts a fight");
    g.onFight("fighter.player", "Knockout", "fighter.rell", "Street", "location.southside.tone_studio", { "npc.tone" });
    talk(g, "dialogue.tone.studio", { "record", "thanks" });
    g.trigger("location.southside.underground_venue");
    talk(g, "dialogue.reece.venue", { "yes" });
    g.onFight("fighter.player", "Finisher", "fighter.brick", "Underground", "location.southside.underground_venue", {});
    talk(g, "dialogue.nia.invitation", { "yes" });
    talk(g, "dialogue.grandma.invitation", { "tell", "promise" });
}

int main(int argc, char** argv) {
    g_data = argc > 1 ? argv[1] : "../Content/FFTSData";

    test("json writer round-trips through the parser", [] {
        Json j = Json::makeObject().set("s", Json::makeString("a\"b\\c\n\x01" "é")).set("n", Json::makeNumber(-12.5)).set("i", Json::makeNumber(4000000000.0))
                     .set("b", Json::makeBool(true)).set("z", Json()).set("a", Json::makeArray().push(Json::makeNumber(1)).push(Json::makeObject()));
        j.set("n", Json::makeNumber(3));   // replace keeps one key
        std::string err; Json back = Json::parse(j.dump(), &err);
        CHECK(err.empty(), err);
        CHECK(back["s"].str() == "a\"b\\c\n\x01" "é", "string escapes");
        CHECK(back["n"].num() == 3 && back["i"].num() == 4000000000.0 && back.size() == 6, "numbers / replace");
        CHECK(back["b"].boolean() && back["z"].isNull() && back["a"].size() == 2 && back["a"][1].isObject(), "literals / nesting");
        CHECK(back.dump() == j.dump(), "stable output");
    });

    test("story content loads", [] {
        const GameData& d = data();
        CHECK(d.missions.size() >= 7 && d.dialogues.size() > 5 && d.rules.size() > 5 && d.npcs.size() >= 10, "content counts");
        CHECK(d.layout["Roads"].size() > 0 && d.layout["Locations"].size() > 0, "layout");
    });

    test("bank: idempotent posts, pay falls back to checking, formatting", [] {
        Bank b(4000);
        CHECK(b.post("k", "cash", 100, "x") == PostResult::Posted && b.post("k", "cash", 100, "x") == PostResult::Duplicate, "duplicate key");
        CHECK(b.post("z", "cash", 0, "x") == PostResult::InvalidAmount, "zero");
        CHECK(b.post("m", "checking", 5000, "move", "cash") == PostResult::InsufficientFunds, "overdraw");
        b.post("dep", "checking", 10000, "deposit");
        CHECK(b.pay("buy", 6000, "thing") == PostResult::Posted && b.wasPosted("buy.checking") && b.balance("checking") == 4000, "checking fallback");
        CHECK(b.pay("buy", 1, "again") == PostResult::Duplicate, "pay idempotent");
        CHECK(Bank::fmt(123456789) == "$1,234,567.89" && Bank::fmt(-500) == "-$5" && Bank::fmt(0) == "$0", Bank::fmt(123456789));
    });

    test("relationships: temper scales resentment, fear needs courage, attitude from memories", [] {
        Social s;
        s.personality["npc.hot"] = { { "Temper", 90 }, { "Patience", 20 }, { "Courage", 30 } };
        s.modify("npc.hot", { { "Resentment", 10 }, { "Fear", 10 } });
        CHECK(s.get("npc.hot").at("Resentment") == 17 && s.get("npc.hot").at("Fear") == 11, "scaled deltas");
        CHECK(s.attitude("npc.calm") == "Neutral", "default neutral");
        s.record({ "e1", "player.betrayed", "player", "npc.calm", "loc", { "npc.calm" } });
        s.record({ "e1", "player.betrayed", "player", "npc.calm", "loc", { "npc.calm" } });   // recorded once
        CHECK(s.memories["npc.calm"].size() == 1 && s.knows("npc.calm", "e1"), "memory once");
        CHECK(s.attitude("npc.calm") == "Hostile", "betrayal turns hostile, got " + s.attitude("npc.calm"));
        s.modify("npc.scared", { { "Fear", 50 } });   // x0.9 at average courage = 45 >= 40
        CHECK(s.attitude("npc.scared") == "Intimidated", "fear");
    });

    test("Chairman reveal flag is refused before Chapter 7", [] {
        Game g(data());
        CHECK(!g.story.set(RevealFlag) && !g.story.has(RevealFlag), "guarded in chapter 1");
        g.story.chapter = 7;
        CHECK(!g.story.set(RevealFlag), "still guarded until the Chapter 7 mission is reached");
    });

    test("full Chapter 1 reaches Chapter 2 with correct money", [] {
        Game g(data());
        CHECK(g.missions->status("mission.ch1.home_again") == "Active", "first mission active at start");
        playChapterOne(g);
        CHECK(g.missions->status("mission.ch1.the_invitation") == "Completed", "last mission " + g.missions->status("mission.ch1.the_invitation"));
        CHECK(g.story.chapter == 2, "chapter " + std::to_string(g.story.chapter));
        CHECK(g.bank.total() == 4000 + 20000 + 10000 + 50000, "money " + std::to_string(g.bank.total()));
        CHECK(!g.story.has(RevealFlag), "no reveal");
        CHECK(g.rep.face["Respect"] > 0 && g.social.get("npc.rell").at("Rivalry") > 0, "fights left a mark");
        CHECK(!g.takeToasts().empty(), "toasts");
    });

    test("rewards are paid once even if triggers repeat", [] {
        Game g(data());
        playChapterOne(g);
        const Cents money = g.bank.total();
        g.trigger("location.southside.grandma_house");
        g.onFight("fighter.player", "Knockout", "fighter.brick", "Underground", "location.southside.underground_venue", {});
        CHECK(g.bank.total() == money, "paid twice");
    });

    test("save round trip mid-chapter restores everything", [] {
        Game g(data());
        g.trigger("location.southside.grandma_house");
        talk(g, "dialogue.grandma.welcome", { "respect", "photo", "ok" });
        talk(g, "dialogue.lou.barbershop", { "ask", "more", "sharp" });
        g.minutes += 95;
        g.tick(0.1, 0, 0, false);
        const std::string text = g.capture(12, -3).dump();
        std::string err; Json save = Json::parse(text, &err);
        CHECK(err.empty(), err);
        Game h(data());
        CHECK(h.restore(save), "restore accepted");
        CHECK(h.capture(12, -3).dump() == text, "capture after restore is identical");
        CHECK(h.bank.total() == g.bank.total() && h.story.flags == g.story.flags && h.contacts == g.contacts, "state");
        CHECK(h.missions->tracked == g.missions->tracked && h.missions->status("mission.ch1.home_again") == g.missions->status("mission.ch1.home_again"), "missions");
        CHECK(h.savedX == 12 && h.savedZ == -3 && h.clockText() == g.clockText(), "position and clock");
        // finishing the chapter from the loaded game works and pays the same total
        g.trigger("location.southside.corner_store"); h.trigger("location.southside.corner_store");
        CHECK(h.bank.total() == g.bank.total(), "same progress after load");
        Json bad = Json::makeObject().set("v", Json::makeNumber(99));
        CHECK(!h.restore(bad), "wrong version refused");
    });

    test("NPCs follow schedules and travel along roads", [] {
        Game g(data());
        WorldNpc* lou = g.world->npc("npc.lou");
        CHECK(lou && lou->loc == "location.southside.apartments", "Lou starts at home");
        g.minutes += 60; g.tick(0.1, 0, 0, false);
        CHECK(lou->status == "Traveling", "Lou leaves at 9: " + lou->status);
        for (int i = 0; i < 400 && lou->status == "Traveling"; ++i) g.tick(1, lou->pos.x, lou->pos.z, false);
        CHECK(lou->loc == "location.southside.barbershop", "Lou reaches the barbershop: " + lou->loc);
    });

    test("sleep goes to the next 08:00 and the clock reads correctly", [] {
        Game g(data());
        CHECK(g.clockText() == "Mon 08:00" && g.segment() == "Morning", g.clockText());
        g.sleepUntilMorning();
        CHECK(g.clockText() == "Tue 08:00", g.clockText());
        g.minutes += 14 * 60; CHECK(g.segment() == "Night", g.segment());
    });

    std::printf("\n%d passed, %d failed\n", g_pass, g_fail);
    return g_fail ? 1 : 0;
}
