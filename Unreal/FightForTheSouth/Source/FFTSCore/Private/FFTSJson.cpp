#include "FFTSJson.h"
#include <cstdlib>

namespace ffts {

namespace {
const Json& nullJson() { static const Json n; return n; }
const std::string& emptyString() { static const std::string s; return s; }
const std::vector<Json>& emptyArray() { static const std::vector<Json> a; return a; }
const std::vector<std::pair<std::string, Json>>& emptyObject() { static const std::vector<std::pair<std::string, Json>> o; return o; }
}

class JsonParser {
public:
    JsonParser(const std::string& t) : s(t) {}
    const std::string& s;
    size_t i = 0;
    std::string err;

    void ws() { while (i < s.size() && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) ++i; }
    bool fail(const char* m) { if (err.empty()) err = std::string(m) + " at offset " + std::to_string(i); return false; }

    bool value(Json& out) {
        ws();
        if (i >= s.size()) return fail("unexpected end");
        char c = s[i];
        if (c == '{') return object(out);
        if (c == '[') return array(out);
        if (c == '"') { out.type_ = Json::Type::String; return string(out.string_); }
        if (c == 't' && s.compare(i, 4, "true") == 0) { i += 4; out.type_ = Json::Type::Bool; out.bool_ = true; return true; }
        if (c == 'f' && s.compare(i, 5, "false") == 0) { i += 5; out.type_ = Json::Type::Bool; out.bool_ = false; return true; }
        if (c == 'n' && s.compare(i, 4, "null") == 0) { i += 4; out.type_ = Json::Type::Null; return true; }
        if (c == '-' || (c >= '0' && c <= '9')) {
            const char* begin = s.c_str() + i; char* end = nullptr;
            double v = std::strtod(begin, &end);
            if (end == begin) return fail("bad number");
            i += static_cast<size_t>(end - begin);
            out.type_ = Json::Type::Number; out.number_ = v; return true;
        }
        return fail("unexpected character");
    }

    static void utf8(std::string& o, unsigned cp) {
        if (cp < 0x80) o += static_cast<char>(cp);
        else if (cp < 0x800) { o += static_cast<char>(0xC0 | (cp >> 6)); o += static_cast<char>(0x80 | (cp & 0x3F)); }
        else if (cp < 0x10000) { o += static_cast<char>(0xE0 | (cp >> 12)); o += static_cast<char>(0x80 | ((cp >> 6) & 0x3F)); o += static_cast<char>(0x80 | (cp & 0x3F)); }
        else { o += static_cast<char>(0xF0 | (cp >> 18)); o += static_cast<char>(0x80 | ((cp >> 12) & 0x3F)); o += static_cast<char>(0x80 | ((cp >> 6) & 0x3F)); o += static_cast<char>(0x80 | (cp & 0x3F)); }
    }

    bool hex4(unsigned& v) {
        if (i + 4 > s.size()) return fail("bad escape");
        v = 0;
        for (int k = 0; k < 4; ++k) {
            char h = s[i++]; v <<= 4;
            if (h >= '0' && h <= '9') v |= h - '0'; else if (h >= 'a' && h <= 'f') v |= h - 'a' + 10; else if (h >= 'A' && h <= 'F') v |= h - 'A' + 10; else return fail("bad hex");
        }
        return true;
    }

    bool string(std::string& out) {
        ++i; // opening quote
        while (i < s.size()) {
            char c = s[i++];
            if (c == '"') return true;
            if (c != '\\') { out += c; continue; }
            if (i >= s.size()) break;
            char e = s[i++];
            switch (e) {
                case '"': out += '"'; break; case '\\': out += '\\'; break; case '/': out += '/'; break;
                case 'b': out += '\b'; break; case 'f': out += '\f'; break; case 'n': out += '\n'; break;
                case 'r': out += '\r'; break; case 't': out += '\t'; break;
                case 'u': {
                    unsigned cp = 0; if (!hex4(cp)) return false;
                    if (cp >= 0xD800 && cp <= 0xDBFF && i + 6 <= s.size() && s[i] == '\\' && s[i + 1] == 'u') {
                        i += 2; unsigned lo = 0; if (!hex4(lo)) return false;
                        cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00);
                    }
                    utf8(out, cp); break;
                }
                default: return fail("bad escape");
            }
        }
        return fail("unterminated string");
    }

    bool array(Json& out) {
        out.type_ = Json::Type::Array; ++i; ws();
        if (i < s.size() && s[i] == ']') { ++i; return true; }
        while (true) {
            Json v; if (!value(v)) return false;
            out.array_.push_back(std::move(v));
            ws();
            if (i < s.size() && s[i] == ',') { ++i; continue; }
            if (i < s.size() && s[i] == ']') { ++i; return true; }
            return fail("expected , or ]");
        }
    }

    bool object(Json& out) {
        out.type_ = Json::Type::Object; ++i; ws();
        if (i < s.size() && s[i] == '}') { ++i; return true; }
        while (true) {
            ws();
            if (i >= s.size() || s[i] != '"') return fail("expected key");
            std::string key; if (!string(key)) return false;
            ws();
            if (i >= s.size() || s[i] != ':') return fail("expected :");
            ++i;
            Json v; if (!value(v)) return false;
            out.object_.emplace_back(std::move(key), std::move(v));
            ws();
            if (i < s.size() && s[i] == ',') { ++i; continue; }
            if (i < s.size() && s[i] == '}') { ++i; return true; }
            return fail("expected , or }");
        }
    }
};

Json Json::parse(const std::string& text, std::string* error) {
    JsonParser p(text);
    Json root;
    // Skip a UTF-8 byte-order mark if present.
    if (text.size() >= 3 && static_cast<unsigned char>(text[0]) == 0xEF && static_cast<unsigned char>(text[1]) == 0xBB && static_cast<unsigned char>(text[2]) == 0xBF) p.i = 3;
    if (!p.value(root)) { if (error) *error = p.err; return Json(); }
    p.ws();
    if (p.i != text.size()) { if (error) *error = "trailing characters at offset " + std::to_string(p.i); return Json(); }
    if (error) error->clear();
    return root;
}

const Json& Json::operator[](const std::string& key) const {
    if (type_ != Type::Object) return nullJson();
    for (const auto& kv : object_) if (kv.first == key) return kv.second;
    return nullJson();
}
const Json& Json::operator[](size_t index) const { return type_ == Type::Array && index < array_.size() ? array_[index] : nullJson(); }
bool Json::has(const std::string& key) const { if (type_ != Type::Object) return false; for (const auto& kv : object_) if (kv.first == key) return true; return false; }
size_t Json::size() const { return type_ == Type::Array ? array_.size() : type_ == Type::Object ? object_.size() : 0; }
const std::string& Json::str() const { return type_ == Type::String ? string_ : emptyString(); }
const std::vector<Json>& Json::items() const { return type_ == Type::Array ? array_ : emptyArray(); }
const std::vector<std::pair<std::string, Json>>& Json::members() const { return type_ == Type::Object ? object_ : emptyObject(); }

} // namespace ffts
