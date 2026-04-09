#!/usr/bin/env python3
"""
Insert a new Keep-a-Changelog-style release section into CHANGELOG.md and mirror it in docs/changelog.html.

Previous tag for compare links: git describe --tags --abbrev=0 (must exist for accurate links).
"""

from __future__ import annotations

import argparse
import datetime as dt
import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CHANGELOG_PATH = REPO_ROOT / "CHANGELOG.md"
HTML_PATH = REPO_ROOT / "docs" / "changelog.html"

SEMVER_RE = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")
HEADER_RE = re.compile(r"^## \[(\d+\.\d+\.\d+)\]", re.MULTILINE)
FIRST_RELEASE_AFTER_SEPARATOR = re.compile(
    r"(\n---\n\n)(## \[\d+\.\d+\.\d+\])",
    re.MULTILINE,
)
FIRST_CL_ENTRY = re.compile(
    r'(\n    <article class="cl-entry">)',
    re.MULTILINE,
)


def die(msg: str, code: int = 1) -> None:
    print(msg, file=sys.stderr)
    raise SystemExit(code)


def normalize_version(raw: str) -> str:
    s = raw.strip().removeprefix("v").removeprefix("V")
    if not SEMVER_RE.match(s):
        die(f"Invalid version {raw!r}; expected x.y.z (optional leading v).")
    return s


def run_git(args: list[str], cwd: Path) -> str:
    try:
        out = subprocess.run(
            ["git", *args],
            cwd=cwd,
            capture_output=True,
            text=True,
            check=True,
        )
    except subprocess.CalledProcessError as e:
        die(f"git {' '.join(args)} failed: {e.stderr.strip() or e}")
    return out.stdout.strip()


def parse_github_slug(remote_url: str) -> tuple[str, str] | None:
    u = remote_url.strip()
    m = re.search(r"github\.com[:/]([^/]+)/([^/]+?)(?:\.git)?$", u)
    if not m:
        return None
    owner, repo = m.group(1), m.group(2)
    return owner, repo


def prev_tag_from_git(cwd: Path) -> str:
    try:
        tag = run_git(["describe", "--tags", "--abbrev=0"], cwd)
    except SystemExit:
        return ""
    if not tag.startswith("v"):
        return f"v{tag}"
    return tag


def prev_version_from_changelog(text: str) -> str | None:
    for m in HEADER_RE.finditer(text):
        ver = m.group(1)
        if ver != "Unreleased":
            return ver
    return None


def markdown_block(
    version: str,
    date_iso: str,
    owner: str,
    repo: str,
    prev_tag: str,
) -> str:
    base = f"https://github.com/{owner}/{repo}"
    vtag = f"v{version}"
    return (
        f"## [{version}] - {date_iso}\n\n"
        f"- [GitHub release {vtag}]({base}/releases/tag/{vtag}) — installers and platform artifacts (Windows, macOS, Linux).\n"
        f"- [Commits since {prev_tag}]({base}/compare/{prev_tag}...{vtag})\n\n"
    )


def html_block(
    version: str,
    date_iso: str,
    owner: str,
    repo: str,
    prev_tag: str,
) -> str:
    base = f"https://github.com/{owner}/{repo}"
    vtag = f"v{version}"
    prev_v = prev_tag.removeprefix("v")
    return (
        f'    <article class="cl-entry">\n'
        f'      <div class="cl-version">{version}</div>\n'
        f'      <div class="cl-date">{date_iso}</div>\n'
        f'      <div class="cl-links">\n'
        f'        <a href="{base}/releases/tag/{vtag}">GitHub release (downloads)</a>\n'
        f'        <a href="{base}/compare/{prev_tag}...{vtag}">Commits since v{prev_v}</a>\n'
        f"      </div>\n"
        f"    </article>\n"
    )


def main() -> None:
    parser = argparse.ArgumentParser(description="Add a release section to CHANGELOG.md and docs/changelog.html.")
    parser.add_argument("version", help="Release version x.y.z (optional v prefix)")
    parser.add_argument(
        "--date",
        dest="date_iso",
        help="Release date YYYY-MM-DD (default: today, local)",
    )
    args = parser.parse_args()
    version = normalize_version(args.version)

    date_iso = args.date_iso
    if date_iso:
        try:
            dt.date.fromisoformat(date_iso)
        except ValueError:
            die(f"Invalid --date {date_iso!r}; use YYYY-MM-DD.")
    else:
        date_iso = dt.date.today().isoformat()

    ch_text = CHANGELOG_PATH.read_text(encoding="utf-8")
    if re.search(rf"^## \[{re.escape(version)}\]\s*-", ch_text, re.MULTILINE):
        die(f"CHANGELOG.md already has ## [{version}].")

    remote = run_git(["remote", "get-url", "origin"], REPO_ROOT)
    slug = parse_github_slug(remote)
    if not slug:
        die(f"Could not parse owner/repo from git remote: {remote}")
    owner, repo = slug

    prev_tag = prev_tag_from_git(REPO_ROOT)
    if not prev_tag:
        pv = prev_version_from_changelog(ch_text)
        if pv:
            prev_tag = f"v{pv}"
        else:
            die("No git tag found (git describe failed) and no version header in CHANGELOG; cannot infer previous ref.")

    md = markdown_block(version, date_iso, owner, repo, prev_tag)
    if not FIRST_RELEASE_AFTER_SEPARATOR.search(ch_text):
        die("Could not find insertion point in CHANGELOG.md (expected --- then ## [semver]).")
    new_ch = FIRST_RELEASE_AFTER_SEPARATOR.sub(rf"\1{md}\2", ch_text, count=1)

    html_text = HTML_PATH.read_text(encoding="utf-8")
    if f'class="cl-version">{version}</div>' in html_text:
        die(f"docs/changelog.html already contains version {version}.")

    hb = html_block(version, date_iso, owner, repo, prev_tag)
    if not FIRST_CL_ENTRY.search(html_text):
        die('Could not find <article class="cl-entry"> in docs/changelog.html.')
    new_html = FIRST_CL_ENTRY.sub(rf"\n{hb}\1", html_text, count=1)

    CHANGELOG_PATH.write_text(new_ch, encoding="utf-8", newline="\n")
    HTML_PATH.write_text(new_html, encoding="utf-8", newline="\n")

    print(f"Updated CHANGELOG.md and docs/changelog.html for [{version}] - {date_iso}")
    print(f"Compare: {prev_tag}...v{version}")
    print("Next: review, git add, commit, push, git tag, push tag (see .cursor/commands/release.md).")


if __name__ == "__main__":
    main()
