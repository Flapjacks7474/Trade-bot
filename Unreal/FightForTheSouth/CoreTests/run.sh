#!/usr/bin/env bash
# Builds and runs the FFTSCore tests with the host C++ compiler (g++ or clang++), no engine needed.
#   run.sh            combat + session tests against Content/FFTSData
#   run.sh asan       same, built with AddressSanitizer + UndefinedBehaviorSanitizer
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
core="$here/../Source/FFTSCore"
data="$here/../Content/FFTSData"
out="${TMPDIR:-/tmp}/ffts_core_tests.d"
flags=(-std=${CXXSTD:-c++20} -O2 -Wall -Wextra -Wshadow -Werror -Wno-array-bounds -fno-rtti)
if [[ "${1:-}" == "asan" ]]; then flags+=(-O1 -g -fsanitize=address,undefined -fno-omit-frame-pointer -fno-sanitize-recover=all); fi
# Every engine-free source (FFTSCoreModule.cpp needs Unreal headers and is left out).
srcs=()
for f in "$core"/Private/*.cpp; do [[ "$(basename "$f")" == FFTSCoreModule.cpp ]] || srcs+=("$f"); done
mkdir -p "$out"
"${CXX:-g++}" "${flags[@]}" -I"$core/Public" "${srcs[@]}" "$here/test_core.cpp" -o "$out/combat"
"${CXX:-g++}" "${flags[@]}" -I"$core/Public" "${srcs[@]}" "$here/test_session.cpp" -o "$out/session"
status=0
echo "== combat"; "$out/combat" "$data" || status=1
echo "== session"; "$out/session" "$data" || status=1
exit $status
