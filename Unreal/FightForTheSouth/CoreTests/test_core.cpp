// Stand-alone tests for FFTSCore (the portable C++ simulation used by the Unreal build).
// Build and run:  Unreal/FightForTheSouth/CoreTests/run.sh
// Mirrors WebPrototype/tests/styles.test.mjs and the C# StyleTests, so all three implementations agree.
#include "FFTSCombat.h"
#include <cmath>
#include <cstdio>
#include <dirent.h>
#include <fstream>
#include <functional>
#include <sstream>

using namespace ffts;

static int g_pass = 0, g_fail = 0;
static std::string g_current;
#define CHECK(cond, msg) do { if (!(cond)) { std::printf("    FAIL %s: %s (line %d)\n", g_current.c_str(), std::string(msg).c_str(), __LINE__); throw 1; } } while (0)

static void test(const char* name, const std::function<void()>& fn) {
    g_current = name;
    try { fn(); ++g_pass; std::printf("ok   %s\n", name); } catch (...) { ++g_fail; std::printf("FAIL %s\n", name); }
}

static std::string readFile(const std::string& p) { std::ifstream f(p, std::ios::binary); std::stringstream ss; ss << f.rdbuf(); return ss.str(); }
static std::vector<std::string> jsonFiles(const std::string& dir) {
    std::vector<std::string> names;
    if (DIR* d = opendir(dir.c_str())) { while (dirent* e = readdir(d)) { std::string n = e->d_name; if (n.size() > 5 && n.substr(n.size() - 5) == ".json") names.push_back(n); } closedir(d); }
    std::sort(names.begin(), names.end(), [](const std::string& a, const std::string& b) {
        bool sa = a == "street_fighting.json", sb = b == "street_fighting.json"; return sa != sb ? sa : a < b; });
    std::vector<std::string> out; for (auto& n : names) out.push_back(readFile(dir + "/" + n));
    return out;
}

static std::string g_data;
static CombatDatabase& db() {
    static CombatDatabase d; static bool loaded = false;
    if (!loaded) {
        std::vector<std::string> errs;
        d.load(jsonFiles(g_data + "/Moves"), { readFile(g_data + "/Characters/fighters.json") }, errs);
        for (auto& e : errs) std::printf("    content error: %s\n", e.c_str());
        loaded = true;
    }
    return d;
}

static std::unique_ptr<CombatSimulation> fightWith(const std::vector<std::string>& a, const std::vector<std::string>& b = { "style.street" }, float gap = 1.0f) {
    auto& d = db();
    return std::make_unique<CombatSimulation>(CombatConfig(), std::make_unique<Fighter>(*d.fighter("fighter.player"), d.buildMoveset(a), -gap / 2),
                                              std::make_unique<Fighter>(*d.fighter("fighter.rell"), d.buildMoveset(b), gap / 2), "t");
}

static std::vector<CombatEvent> run(CombatSimulation& sim, int ticks, const std::function<FighterInput(int)>& fa, const std::function<FighterInput(int)>& fb = nullptr) {
    std::vector<CombatEvent> all;
    for (int i = 0; i < ticks && !sim.isOver(); ++i) {
        sim.step(fa(i), fb ? fb(i) : FighterInput::none());
        all.insert(all.end(), sim.events.begin(), sim.events.end());
    }
    return all;
}
static int count(const std::vector<CombatEvent>& ev, const char* type) { int n = 0; for (auto& e : ev) if (e.type == type) ++n; return n; }

int main(int argc, char** argv) {
    g_data = argc > 1 ? argv[1] : "../../../Assets/_Project/Data";

    test("json parser handles nesting, escapes, unicode and errors", [] {
        std::string err;
        Json j = Json::parse(R"({"a":[1,2.5,-3e2],"b":{"c":"x\"yé"},"t":true,"n":null})", &err);
        CHECK(err.empty(), err);
        CHECK(j["a"].size() == 3 && j["a"][2].num() == -300, "array");
        CHECK(j["b"]["c"].str() == "x\"y\xC3\xA9", "string escapes");
        CHECK(j["t"].boolean() && j["n"].isNull() && j["missing"].isNull(), "literals");
        Json bad = Json::parse("{\"a\":}", &err);
        CHECK(!err.empty() && bad.isNull(), "error reported");
    });

    test("all shipped styles and fighters validate (10 base + 6 hybrids)", [] {
        std::vector<std::string> errs; db().validate(errs);
        CHECK(errs.empty(), errs.empty() ? "" : errs[0]);
        int base = 0, hyb = 0; for (auto& s : db().styles()) (s->isHybrid() ? hyb : base)++;
        CHECK(base == 10, "base styles " + std::to_string(base));
        CHECK(hyb >= 6, "hybrids " + std::to_string(hyb));
    });

    test("validation catches cycles, hybrid misuse and dead links", [] {
        CombatDatabase bad; std::vector<std::string> errs;
        std::string styles = R"({"Styles":[{"Id":"style.x","DisplayName":"X","Moves":[
            {"Id":"x.a","Kind":"Light","Startup":3,"Active":2,"Recovery":8,"Damage":3,"StaminaCost":2,"Range":1,"CancelWindow":6,"AnimationKey":"anim.x.a"},
            {"Id":"x.b","Kind":"Light","Startup":3,"Active":2,"Recovery":8,"Damage":3,"StaminaCost":2,"Range":1,"CancelWindow":0,"AnimationKey":"anim.x.b"}],
            "Light":"x.a","Links":[{"From":"x.a","Command":"Light","To":"x.b"},{"From":"x.b","Command":"Light","To":"x.a"}]}]})";
        std::string fighters = R"({"Fighters":[{"Id":"f","DisplayName":"F","Styles":["style.x"],"Stats":{}}]})";
        bad.load({ styles }, { fighters }, errs);
        auto has = [&](const char* frag) { for (auto& e : errs) if (e.find(frag) != std::string::npos) return true; return false; };
        CHECK(has("cycle"), "cycle"); CHECK(has("can never fire"), "dead link"); CHECK(has("must fill the Heavy"), "incomplete base style");
    });

    test("street stance is neutral, so the original numbers hold", [] {
        const StanceDef& st = db().style("style.street")->stance;
        CHECK(st.strikeDamage == 1 && st.kickDamage == 1 && st.walkSpeed == 1 && st.parryWindowBonus == 0 && st.clinchStrikesBonus == 0, "neutral");
    });

    test("every base style is a complete kit with its own finisher", [] {
        for (auto& s : db().styles()) {
            if (s->isHybrid()) continue;
            auto ms = Moveset::build({ s.get() });
            CHECK(ms->light && ms->heavy && ms->kick && ms->grapple && ms->counter && ms->finisher && ms->clinchStrike && ms->clinchThrow, s->id);
            CHECK(ms->finisher->id.find(s->moves[0].id.substr(0, s->moves[0].id.find('.') + 1)) == 0, s->id + " finisher must be its own");
        }
    });

    test("hybrid applies for its pair in either order, own finisher and stance first", [] {
        CHECK(db().findHybrid("style.street", "style.boxing")->id == "hybrid.southside_hands", "pair");
        CHECK(db().findHybrid("style.boxing", "style.street")->id == "hybrid.southside_hands", "either order");
        CHECK(!db().findHybrid("style.boxing", "style.karate"), "no hybrid");
        auto ms = db().buildMoveset({ "style.street", "style.boxing" });
        CHECK(ms->hybridId() == "hybrid.southside_hands", "hybrid id");
        CHECK(ms->finisher->id == "southside_hands.last_call", "finisher");
        CHECK(ms->stance().name == "Low Shell", "stance");
        CHECK(ms->findLink("boxing.jab", Command::Heavy)->id == "southside_hands.double_hook", "cross-style link");
        std::vector<std::string> none;
        CHECK(db().buildMoveset({ "style.street", "style.boxing" }, true, &none)->hybridId().empty(), "locked hybrid");
    });

    test("Muay Thai stance allows 5 clinch knees, Street 3", [] {
        auto knees = [](const char* style) {
            auto sim = fightWith({ style });
            auto ev = run(*sim, 200, [](int i) { return i == 0 ? FighterInput::press(Command::Grapple) : (i % 21 == 15 ? FighterInput::press(Command::Light) : FighterInput::none()); },
                          [](int) { return FighterInput::hold(); });
            return count(ev, "ClinchStrike");
        };
        CHECK(knees("style.street") == 3, "street " + std::to_string(knees("style.street")));
        CHECK(knees("style.muay_thai") == 5, "muay thai " + std::to_string(knees("style.muay_thai")));
    });

    test("Taekwondo kick outdamages Street kick by stance", [] {
        auto first = [](const char* style) {
            auto sim = fightWith({ style }, { "style.street" }, 1.2f);
            for (auto& e : run(*sim, 60, [](int i) { return i == 0 ? FighterInput::press(Command::Kick) : FighterInput::none(); })) if (e.type == "Hit") return e.amount;
            return 0.0f;
        };
        float tkd = first("style.taekwondo"), street = first("style.street");
        CHECK(tkd > street * 1.4f && street > 0, std::to_string(tkd) + " vs " + std::to_string(street));
    });

    test("street jab into cross into hook combo lands three hits", [] {
        auto sim = fightWith({ "style.street" });
        int step = 0; Command route[] = { Command::Light, Command::Light, Command::Heavy };
        int hits = 0;
        for (int i = 0; i < 120 && !sim->isOver(); ++i) {
            Command cmd = Command::None;
            if (step < 3 && (i == 0 || (sim->A->moveConnected && sim->A->inRecovery()))) cmd = route[step++];
            sim->step(FighterInput::press(cmd), FighterInput::none());
            for (auto& e : sim->events) if (e.type == "Hit" && e.actor == "fighter.player") ++hits;
        }
        CHECK(hits >= 3, "hits " + std::to_string(hits));
    });

    test("simulation is deterministic for the same inputs and seeds", [] {
        auto play = [] {
            auto& d = db();
            CombatSimulation sim(CombatConfig(), std::make_unique<Fighter>(*d.fighter("fighter.player"), d.buildMoveset({ "style.boxing" }), -1.2f),
                                 std::make_unique<Fighter>(*d.fighter("fighter.brick"), d.buildMoveset({ "style.wrestling" }), 1.2f), "det");
            FighterAI a(*sim.A, "Aggressive", "Hard", 11), b(*sim.B, "Wrestler", "Hard", 23);
            for (int t = 0; t < 60 * 120 && !sim.isOver(); ++t) sim.step(a.think(sim), b.think(sim));
            return sim.result.winnerId + "|" + sim.result.method + "|" + std::to_string(sim.result.durationTicks);
        };
        CHECK(play() == play(), "same result");
    });

    test("AI defeats a passive opponent", [] {
        auto& d = db();
        CombatSimulation sim(CombatConfig(), std::make_unique<Fighter>(*d.fighter("fighter.rell"), d.buildMoveset({ "style.street" }), -1.2f),
                             std::make_unique<Fighter>(*d.fighter("fighter.player"), d.buildMoveset({ "style.street" }), 1.2f), "p");
        FighterAI ai(*sim.A, "Aggressive", "Normal", 5);
        for (int t = 0; t < 60 * 180 && !sim.isOver(); ++t) sim.step(ai.think(sim), FighterInput::none());
        CHECK(sim.isOver() && sim.result.winnerId == "fighter.rell", "winner " + sim.result.winnerId);
    });

    test("learning styles charges once, hybrids need both parents", [] {
        Bank bank(100000); StyleLoadout lo(db(), &bank);
        CHECK(lo.learn("hybrid.southside_hands") == StyleResult::ParentsNotLearned, "parents first");
        CHECK(lo.learn("style.boxing") == StyleResult::Ok, "learn");
        long long after = bank.cash;
        CHECK(lo.learn("style.boxing") == StyleResult::AlreadyLearned && bank.cash == after, "no double charge");
        CHECK(bank.pay("style.learn.style.boxing", 25000) == PostStatus::Duplicate, "key used");
        CHECK(lo.equip("style.street", "style.boxing") == StyleResult::Ok && !lo.activeHybrid(), "equip");
        CHECK(lo.learn("hybrid.southside_hands") == StyleResult::Ok && lo.activeHybrid()->id == "hybrid.southside_hands", "hybrid on");
        CHECK(lo.buildMoveset()->finisher->id == "southside_hands.last_call", "hybrid finisher");
        CHECK(lo.equip("hybrid.southside_hands") == StyleResult::InvalidLoadout, "cannot equip hybrid");
        Bank poor(1000); StyleLoadout p(db(), &poor);
        CHECK(p.learn("style.mma") == StyleResult::InsufficientFunds, "funds");
        StyleLoadout copy(db(), &bank); copy.restore(lo.learned, lo.equipped);
        CHECK(copy.equipped == lo.equipped && copy.learned == lo.learned, "restore");
        copy.restore({ "style.gone" }, { "style.gone" });
        CHECK(copy.equipped.size() == 1 && copy.equipped[0] == "style.street", "unknown dropped");
    });

    test("AI round robin: every base style wins 25-75% (equal stats)", [] {
        auto& d = db();
        std::vector<std::string> ids; for (auto& s : d.styles()) if (!s->isHybrid()) ids.push_back(s->id);
        std::map<std::string, int> wins;
        FighterDef def = *d.fighter("fighter.rell");
        FighterDef da = def, dbb = def; da.id = "A"; dbb.id = "B";
        for (auto& a : ids) for (auto& b : ids) {
            if (a == b) continue;
            for (int seed = 1; seed <= 3; ++seed) {
                CombatConfig cfg; cfg.roundTicks = 60 * 90;
                CombatSimulation sim(cfg, std::make_unique<Fighter>(da, d.buildMoveset({ a }), -1.2f), std::make_unique<Fighter>(dbb, d.buildMoveset({ b }), 1.2f), "rr");
                FighterAI ai1(*sim.A, "Technical", "Normal", seed * 7 + 1), ai2(*sim.B, "Aggressive", "Normal", seed * 13 + 5);
                for (int t = 0; t < cfg.roundTicks + 5 && !sim.isOver(); ++t) sim.step(ai1.think(sim), ai2.think(sim));
                if (sim.result.winnerId == "A") wins[a]++; else if (sim.result.winnerId == "B") wins[b]++;
            }
        }
        float per = (ids.size() - 1) * 2 * 3.0f;
        std::string line;
        for (auto& id : ids) { char buf[64]; std::snprintf(buf, sizeof buf, "%s %.2f  ", id.c_str() + 6, wins[id] / per); line += buf; }
        std::printf("    win rates: %s\n", line.c_str());
        for (auto& id : ids) { float r = wins[id] / per; CHECK(r >= 0.25f && r <= 0.75f, id + " " + std::to_string(r)); }
    });

    std::printf("\n%d passed, %d failed\n", g_pass, g_fail);
    return g_fail == 0 ? 0 : 1;
}
