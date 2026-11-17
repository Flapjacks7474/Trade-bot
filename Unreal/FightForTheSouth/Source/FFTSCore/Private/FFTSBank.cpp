#include "FFTSBank.h"
#include <cmath>
#include <cstdio>

namespace ffts {

const char* toString(PostResult r) {
    switch (r) { case PostResult::Posted: return "Posted"; case PostResult::Duplicate: return "Duplicate";
                 case PostResult::InvalidAmount: return "InvalidAmount"; default: return "InsufficientFunds"; }
}

Bank::Bank(Cents startCash) {
    accounts = { { "cash", 0 }, { "checking", 0 }, { "savings", 0 } };
    if (startCash > 0) post("start.cash", "cash", startCash, "Pocket money");
}

PostResult Bank::post(const std::string& key, const std::string& to, Cents cents, const std::string& memo, const std::string& from) {
    if (key.empty() || keys.count(key)) return PostResult::Duplicate;
    if (cents <= 0) return PostResult::InvalidAmount;
    if (!from.empty() && accounts[from] < cents) return PostResult::InsufficientFunds;
    if (!from.empty()) accounts[from] -= cents;
    if (!to.empty()) accounts[to] += cents;
    keys.insert(key);
    ledger.push_back({ key, from, to, cents, memo });
    if (ledger.size() > 300) ledger.pop_front();
    return PostResult::Posted;
}

PostResult Bank::pay(const std::string& key, Cents cents, const std::string& memo) {
    if (keys.count(key + ".cash") || keys.count(key + ".checking")) return PostResult::Duplicate;
    if (balance("cash") >= cents) return post(key + ".cash", "", cents, memo, "cash");
    if (balance("checking") >= cents) return post(key + ".checking", "", cents, memo, "checking");
    return PostResult::InsufficientFunds;
}

Cents Bank::balance(const std::string& account) const { auto it = accounts.find(account); return it == accounts.end() ? 0 : it->second; }
Cents Bank::total() const { return balance("cash") + balance("checking") + balance("savings"); }

std::string Bank::fmt(Cents c) {
    const Cents a = c < 0 ? -c : c;
    std::string whole = std::to_string(a / 100), grouped;
    for (size_t i = 0; i < whole.size(); ++i) { if (i && (whole.size() - i) % 3 == 0) grouped += ','; grouped += whole[i]; }
    std::string out = (c < 0 ? "-$" : "$") + grouped;
    if (a % 100) { char b[8]; std::snprintf(b, sizeof b, ".%02lld", a % 100); out += b; }
    return out;
}

Json Bank::capture() const {
    Json s = Json::makeObject();
    Json acc = Json::makeObject(); for (const auto& kv : accounts) acc.set(kv.first, Json::makeNumber(static_cast<double>(kv.second)));
    s.set("accounts", acc);
    Json k = Json::makeArray(); for (const auto& key : keys) k.push(Json::makeString(key));
    s.set("keys", k);
    Json l = Json::makeArray();
    const size_t from = ledger.size() > 100 ? ledger.size() - 100 : 0;
    for (size_t i = from; i < ledger.size(); ++i) {
        const LedgerEntry& e = ledger[i];
        l.push(Json::makeObject().set("key", Json::makeString(e.key)).set("from", Json::makeString(e.from)).set("to", Json::makeString(e.to))
               .set("cents", Json::makeNumber(static_cast<double>(e.cents))).set("memo", Json::makeString(e.memo)));
    }
    s.set("ledger", l);
    return s;
}

void Bank::restore(const Json& s) {
    accounts = { { "cash", 0 }, { "checking", 0 }, { "savings", 0 } };
    for (const auto& kv : s["accounts"].members()) accounts[kv.first] = static_cast<Cents>(std::llround(kv.second.num()));
    keys.clear(); for (const Json& k : s["keys"].items()) keys.insert(k.str());
    ledger.clear();
    for (const Json& e : s["ledger"].items())
        ledger.push_back({ e["key"].str(), e["from"].str(), e["to"].str(), static_cast<Cents>(std::llround(e["cents"].num())), e["memo"].str() });
}

} // namespace ffts
