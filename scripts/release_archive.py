#!/usr/bin/env python3
"""Create and safely validate the Steam Workshop release ZIP."""

from __future__ import annotations

import argparse
import json
import shutil
import stat
import sys
import zipfile
from pathlib import Path, PurePosixPath


PREFIX = "Unit-8200"
REQUIRED = {
    f"{PREFIX}/Unit-8200.dll",
    f"{PREFIX}/Locales/en.json",
    f"{PREFIX}/Locales/ja.json",
}
MAX_UNCOMPRESSED_BYTES = 50 * 1024 * 1024


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def validate(archive: Path) -> list[zipfile.ZipInfo]:
    try:
        handle = zipfile.ZipFile(archive)
    except (OSError, zipfile.BadZipFile) as error:
        fail(f"invalid ZIP: {error}")

    with handle:
        infos = handle.infolist()
        names = {info.filename.rstrip("/") for info in infos if not info.is_dir()}
        missing = sorted(REQUIRED - names)
        if missing:
            fail(f"release ZIP is missing: {missing}")
        if sum(info.file_size for info in infos) > MAX_UNCOMPRESSED_BYTES:
            fail("release ZIP exceeds the 50 MiB uncompressed safety limit")

        for info in infos:
            path = PurePosixPath(info.filename)
            if path.is_absolute() or ".." in path.parts or not path.parts or path.parts[0] != PREFIX:
                fail(f"unsafe or unexpected archive path: {info.filename}")
            mode = info.external_attr >> 16
            if stat.S_ISLNK(mode):
                fail(f"symbolic links are not allowed: {info.filename}")
            if path.name.lower() == "skillkit.dll":
                fail("SkillKit.dll must remain a separate Workshop dependency")

        for locale in ("en.json", "ja.json"):
            with handle.open(f"{PREFIX}/Locales/{locale}") as stream:
                json.load(stream)
    return infos


def pack(source: Path, archive: Path) -> None:
    if not source.is_dir():
        fail(f"build output does not exist: {source}")
    archive.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as handle:
        for path in sorted(source.rglob("*")):
            if not path.is_file() or path.name == ".DS_Store" or path.suffix.lower() == ".pdb":
                continue
            relative = PurePosixPath(PREFIX) / PurePosixPath(path.relative_to(source).as_posix())
            info = zipfile.ZipInfo(str(relative), date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            handle.writestr(info, path.read_bytes())
    validate(archive)
    print(f"OK: created {archive}")


def extract(archive: Path, destination: Path) -> None:
    infos = validate(archive)
    resolved_destination = destination.resolve()
    if resolved_destination in {Path("/"), Path.home().resolve(), Path(__file__).resolve().parents[1]}:
        fail(f"refusing to replace broad extraction target: {resolved_destination}")
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    with zipfile.ZipFile(archive) as handle:
        for info in infos:
            if info.is_dir():
                continue
            relative = PurePosixPath(info.filename).relative_to(PREFIX)
            target = destination.joinpath(*relative.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            with handle.open(info) as source, target.open("wb") as output:
                shutil.copyfileobj(source, output)
    print(f"OK: extracted verified Workshop content to {destination}")


def main() -> None:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    pack_parser = subparsers.add_parser("pack")
    pack_parser.add_argument("source", type=Path)
    pack_parser.add_argument("archive", type=Path)
    check_parser = subparsers.add_parser("check")
    check_parser.add_argument("archive", type=Path)
    extract_parser = subparsers.add_parser("extract")
    extract_parser.add_argument("archive", type=Path)
    extract_parser.add_argument("destination", type=Path)
    args = parser.parse_args()

    if args.command == "pack":
        pack(args.source, args.archive)
    elif args.command == "check":
        validate(args.archive)
        print(f"OK: verified {args.archive}")
    else:
        extract(args.archive, args.destination)


if __name__ == "__main__":
    main()
