#!/usr/bin/env python3
"""Write the minimal SteamCMD workshop_build_item VDF."""

from __future__ import annotations

import argparse
from pathlib import Path


APP_ID = "1331550"


def escaped(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"').replace("\n", " ")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--vdf", required=True, type=Path)
    parser.add_argument("--published-file-id", required=True)
    parser.add_argument("--content-folder", required=True, type=Path)
    parser.add_argument("--change-note", required=True)
    args = parser.parse_args()

    if not args.published_file_id.isdigit() or args.published_file_id.startswith("0"):
        parser.error("published file id must be a positive integer")
    if not args.content_folder.is_dir():
        parser.error(f"content folder does not exist: {args.content_folder}")

    values = {
        "appid": APP_ID,
        "publishedfileid": args.published_file_id,
        "contentfolder": str(args.content_folder.resolve()),
        "changenote": args.change_note,
    }
    body = ["\"workshopitem\"", "{"]
    body.extend(f'    "{key}"    "{escaped(value)}"' for key, value in values.items())
    body.append("}")
    args.vdf.write_text("\n".join(body) + "\n", encoding="utf-8")
    print(f"OK: wrote {args.vdf}")


if __name__ == "__main__":
    main()
