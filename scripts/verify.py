#!/usr/bin/env python3
"""Fast source checks that do not require Unity or the game DLLs."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / "Assets" / "Mods" / "Unit-8200"
LOCALES = MOD / "Locales"
SOURCE = MOD / "Scripts" / "Unit8200Mod.cs"
WORKSHOP = ROOT / "workshop"

STABLE_IDS = {
    "unit-8200:businesstype_osintservice",
    "unit-8200:skill_hacker",
    "unit-8200:itemname_hourlyosintfee",
    "unit-8200:businessrequirement_serverinfrastructure",
    "unit-8200:transaction_serverinfrastructure",
}


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def load_locale(name: str) -> dict[str, str]:
    path = LOCALES / name
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read {path.relative_to(ROOT)}: {error}")
    if not isinstance(data, dict) or not all(isinstance(k, str) and isinstance(v, str) for k, v in data.items()):
        fail(f"{path.relative_to(ROOT)} must be a string-to-string JSON object")
    if any(key != key.lower() for key in data):
        fail(f"{path.relative_to(ROOT)} contains a non-lowercase localization key")
    return data


def main() -> None:
    english = load_locale("en.json")
    japanese = load_locale("ja.json")
    if english.keys() != japanese.keys():
        missing_ja = sorted(english.keys() - japanese.keys())
        missing_en = sorted(japanese.keys() - english.keys())
        fail(f"locale key mismatch; missing ja={missing_ja}, missing en={missing_en}")

    source = SOURCE.read_text(encoding="utf-8")
    if re.search(r"^using\s+SkillKit\s*;", source, re.MULTILINE):
        fail("SkillKit must be loaded reflectively, not linked into the mod assembly")
    missing_ids = sorted(identifier for identifier in STABLE_IDS if identifier not in source or identifier not in english)
    if missing_ids:
        fail(f"stable IDs missing from source or locales: {missing_ids}")

    manifest = (MOD / "ModManifest.asset").read_text(encoding="utf-8")
    required_manifest_values = {
        "ModId": "Unit-8200",
        "Version": "0.1.1",
        "AssetBundleName": "",
    }
    for key, value in required_manifest_values.items():
        separator = r"\s*" if value == "" else " " + re.escape(value)
        if not re.search(rf"^  {re.escape(key)}:{separator}$", manifest, re.MULTILINE):
            fail(f"manifest must contain '{key}: {value}'")

    required_meta = [
        MOD.with_suffix(".meta"),
        MOD / "Unit-8200.asmdef.meta",
        MOD / "ModManifest.asset.meta",
        LOCALES.with_suffix(".meta"),
        SOURCE.with_suffix(SOURCE.suffix + ".meta"),
    ]
    missing_meta = [str(path.relative_to(ROOT)) for path in required_meta if not path.exists()]
    if missing_meta:
        fail(f"missing Unity metadata: {missing_meta}")

    title = (WORKSHOP / "title.txt").read_text(encoding="utf-8").strip()
    description = (WORKSHOP / "description.txt").read_text(encoding="utf-8")
    preview = WORKSHOP / "preview.jpg"
    if not title or "OSINT" not in title:
        fail("Workshop title must be non-empty and identify OSINT")
    for required_text in ("3741969623", "3795855100", "[h1]", "[list]", "[/list]"):
        if required_text not in description:
            fail(f"Workshop description is missing {required_text!r}")
    player_copy = description + "\n" + english["help_unit-8200:businesstype_osintservice_content"] + "\n" + japanese["help_unit-8200:businesstype_osintservice_content"]
    for out_of_world_disclaimer in ("実在人物", "架空組織", "real-person targeting", "fictional organization"):
        if out_of_world_disclaimer in player_copy:
            fail(f"player-facing copy contains an out-of-world disclaimer: {out_of_world_disclaimer!r}")
    if not preview.is_file() or preview.stat().st_size >= 1_000_000:
        fail("Workshop preview.jpg must exist and be smaller than 1 MB")

    print(f"OK: {len(english)} locale keys, stable IDs, manifest, Unity metadata, and Workshop assets")


if __name__ == "__main__":
    main()
