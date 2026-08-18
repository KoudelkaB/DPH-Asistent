#!/usr/bin/env python3
"""Ensure that a release build advertises its actual version in AppStream."""

from __future__ import annotations

import argparse
import re
from datetime import date
from pathlib import Path


VERSION_PATTERN = re.compile(
    r"[0-9]+(?:\.[0-9]+){2}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?"
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("metainfo", type=Path)
    parser.add_argument("version")
    parser.add_argument("release_date")
    return parser.parse_args()


def main() -> None:
    args = parse_args()

    if VERSION_PATTERN.fullmatch(args.version) is None:
        raise SystemExit(f"Invalid release version: {args.version!r}")

    try:
        date.fromisoformat(args.release_date)
    except ValueError as error:
        raise SystemExit(
            f"Invalid release date {args.release_date!r}; expected YYYY-MM-DD"
        ) from error

    contents = args.metainfo.read_text(encoding="utf-8")
    release_pattern = re.compile(
        rf"<release\b[^>]*\bversion=(['\"]){re.escape(args.version)}\1"
    )
    if release_pattern.search(contents):
        print(f"AppStream release {args.version} is already present.")
        return

    releases_start = contents.find("<releases>")
    if releases_start < 0:
        raise SystemExit(f"Missing <releases> element in {args.metainfo}")

    line_start = contents.rfind("\n", 0, releases_start) + 1
    indent = contents[line_start:releases_start]
    newline = "\r\n" if "\r\n" in contents else "\n"
    child_indent = f"{indent}  "
    text_indent = f"{child_indent}  "
    entry = newline.join(
        (
            "",
            f'{child_indent}<release version="{args.version}" date="{args.release_date}">',
            f"{text_indent}<description>",
            f"{text_indent}  <p>Release {args.version}.</p>",
            f'{text_indent}  <p xml:lang="cs">Vydání {args.version}.</p>',
            f"{text_indent}</description>",
            f"{child_indent}</release>",
        )
    )
    insert_at = releases_start + len("<releases>")
    args.metainfo.write_text(
        f"{contents[:insert_at]}{entry}{contents[insert_at:]}", encoding="utf-8"
    )
    print(f"Added AppStream release {args.version} ({args.release_date}).")


if __name__ == "__main__":
    main()
