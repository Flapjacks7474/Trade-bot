// FIGHT FOR THE SOUTH - FFTSCore
// Minimal JSON reader used by the portable simulation core. No exceptions, no RTTI, no engine types, so the same
// code compiles in the Unreal module and in the stand-alone test harness (Unreal/FightForTheSouth/CoreTests).
#pragma once
#include <map>
#include <memory>
#include <string>
#include <vector>

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

class FFTSCORE_API Json {
public:
    enum class Type { Null, Bool, Number, String, Array, Object };

    Json() = default;
    static Json parse(const std::string& text, std::string* error = nullptr);

    Type type() const { return type_; }
    bool isNull() const { return type_ == Type::Null; }
    bool isObject() const { return type_ == Type::Object; }
    bool isArray() const { return type_ == Type::Array; }
    bool isString() const { return type_ == Type::String; }
    bool isNumber() const { return type_ == Type::Number; }

    // Lookups never fail: a missing key or wrong type yields a shared null value.
    const Json& operator[](const std::string& key) const;
    const Json& operator[](size_t index) const;
    bool has(const std::string& key) const;
    size_t size() const;

    double num(double fallback = 0.0) const { return type_ == Type::Number ? number_ : fallback; }
    int integer(int fallback = 0) const { return type_ == Type::Number ? static_cast<int>(number_ < 0 ? number_ - 0.5 : number_ + 0.5) : fallback; }
    bool boolean(bool fallback = false) const { return type_ == Type::Bool ? bool_ : fallback; }
    const std::string& str() const;                       // "" unless a string
    std::string strOr(const std::string& fallback) const { return type_ == Type::String ? string_ : fallback; }
    const std::vector<Json>& items() const;               // empty unless an array
    const std::vector<std::pair<std::string, Json>>& members() const; // insertion order; empty unless an object

private:
    friend class JsonParser;
    Type type_ = Type::Null;
    bool bool_ = false;
    double number_ = 0.0;
    std::string string_;
    std::vector<Json> array_;
    std::vector<std::pair<std::string, Json>> object_;
};

} // namespace ffts

#ifdef _MSC_VER
#pragma warning(pop)
#endif
