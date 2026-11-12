#!/usr/bin/env bash
# Builds and runs the FFTSCore tests with the host C++ compiler (g++ or clang++), no engine needed.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
core="$here/../Source/FFTSCore"
out="${TMPDIR:-/tmp}/ffts_core_tests"
"${CXX:-g++}" -std=${CXXSTD:-c++20} -O2 -Wall -Wextra -Werror -Wno-array-bounds -fno-rtti -I"$core/Public" \
  "$core/Private/FFTSJson.cpp" "$core/Private/FFTSCombat.cpp" "$here/test_core.cpp" -o "$out"
"$out" "${1:-$here/../Content/FFTSData}"
