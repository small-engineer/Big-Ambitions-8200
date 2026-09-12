#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

release_tag="${1:-local}"
steam_username="${STEAM_USERNAME:-yumezi_626}"
workshop_item_id="${STEAM_WORKSHOP_ITEM_ID:-3800064885}"
steamcmd_executable="${STEAMCMD_EXECUTABLE:-$(command -v steamcmd || true)}"

[[ -n "$steamcmd_executable" ]] || {
  echo "ERROR: steamcmd was not found." >&2
  exit 1
}

temporary="$(mktemp -d)"
trap 'rm -rf "$temporary"' EXIT

python3 -B scripts/release_archive.py extract \
  dist/Unit-8200.zip \
  "$temporary/workshop-content"
python3 -B scripts/build_workshop.py \
  --vdf "$temporary/workshop.vdf" \
  --published-file-id "$workshop_item_id" \
  --content-folder "$temporary/workshop-content" \
  --change-note "Release $release_tag"

"$steamcmd_executable" \
  +@ShutdownOnFailedCommand 1 \
  +login "$steam_username" \
  +workshop_build_item "$temporary/workshop.vdf" \
  +quit | tee "$temporary/steamcmd.log"

grep -q 'Success\.' "$temporary/steamcmd.log" || {
  echo "ERROR: SteamCMD did not report a successful Workshop update." >&2
  exit 1
}

echo "OK: published $release_tag to Workshop item $workshop_item_id"
