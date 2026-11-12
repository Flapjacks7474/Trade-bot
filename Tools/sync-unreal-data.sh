#!/usr/bin/env bash
# Copies the game content JSON (single source of truth: Assets/_Project/Data) into the Unreal project,
# where it is staged as raw files (DirectoriesToAlwaysStageAsNonUFS). Run after editing any content JSON.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
dst="$root/Unreal/FightForTheSouth/Content/FFTSData"
rm -rf "$dst"; mkdir -p "$dst"
(cd "$root/Assets/_Project/Data" && find . -name '*.json' -print0 | while IFS= read -r -d '' f; do mkdir -p "$dst/$(dirname "$f")"; cp "$f" "$dst/$f"; done)
echo "synced $(find "$dst" -name '*.json' | wc -l) JSON files to $dst"
