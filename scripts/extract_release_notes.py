#!/usr/bin/env python3
"""Write release_notes.md from CHANGELOG.md for the version in GITHUB_REF (refs/tags/vX.Y.Z)."""
from __future__ import annotations

import os
import pathlib
import re
import sys


def main() -> int:
    ref = os.environ.get("GITHUB_REF", "")
    if not ref.startswith("refs/tags/v"):
        print("Expected GITHUB_REF refs/tags/v*", file=sys.stderr)
        return 1
    version = ref.removeprefix("refs/tags/v")
    changelog = pathlib.Path("CHANGELOG.md")
    if not changelog.is_file():
        print("CHANGELOG.md not found", file=sys.stderr)
        return 1
    text = changelog.read_text(encoding="utf-8")
    pat = re.compile(
        rf"^## \[{re.escape(version)}\] - \d{{4}}-\d{{2}}-\d{{2}}\s*\n(.*?)(?=^## \[|\Z)",
        re.MULTILINE | re.DOTALL,
    )
    m = pat.search(text)
    if not m:
        print(f"No ## [{version}] - YYYY-MM-DD section in CHANGELOG.md", file=sys.stderr)
        return 1
    body = m.group(1).strip()
    if len(body) < 8:
        print("Changelog section is empty or too short", file=sys.stderr)
        return 1
    out = pathlib.Path("release_notes.md")
    out.write_text(body + "\n", encoding="utf-8")
    print(f"Wrote {out} ({len(body)} chars) for v{version}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
