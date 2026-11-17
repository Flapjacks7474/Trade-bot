// FIGHT FOR THE SOUTH - FFTSCore
// FINESS BANK: cash / checking / savings with an idempotency key on every movement (a key is never posted twice,
// including after a save). The one bank of the game: style training (FFTSCombat), mission rewards and purchases all use it.
// Port of the Bank in WebPrototype/src/sim/story.js.
#pragma once
#include "FFTSJson.h"
#include <deque>
#include <map>
#include <set>
#include <string>

#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable : 4251 4275)
#endif

namespace ffts {

using Cents = long long;

enum class PostResult { Posted, Duplicate, InvalidAmount, InsufficientFunds };
FFTSCORE_API const char* toString(PostResult r);

struct FFTSCORE_API LedgerEntry { std::string key, from, to; Cents cents = 0; std::string memo; };

/** Accounts cash / checking / savings. Every movement has an idempotency key and is recorded once. */
class FFTSCORE_API Bank {
public:
    explicit Bank(Cents startCash = 0);
    PostResult post(const std::string& key, const std::string& to, Cents cents, const std::string& memo, const std::string& from = "");
    /** Pays out of cash, else checking. The key is suffixed with the account used (".cash" / ".checking"). */
    PostResult pay(const std::string& key, Cents cents, const std::string& memo);
    Cents total() const;
    Cents balance(const std::string& account) const;
    bool wasPosted(const std::string& key) const { return keys.count(key) != 0; }
    static std::string fmt(Cents c);
    Json capture() const;
    void restore(const Json& s);

    std::map<std::string, Cents> accounts;
    std::set<std::string> keys;
    std::deque<LedgerEntry> ledger;
};

} // namespace ffts

#ifdef _MSC_VER
#pragma warning(pop)
#endif
