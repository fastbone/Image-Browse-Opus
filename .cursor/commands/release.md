# Release (tag + changelog)

The user invoked this command with a **version** in their message (e.g. `/release 1.4.1` or `/release v1.4.1`). Perform a full release prep: update both changelog files, commit, push branch, create and push the tag.

## 1. Parse version

- Take the first semver-like token from the user message: `x.y.z` (three dot-separated integers).
- Strip a leading `v` if present (`v1.4.1` → `1.4.1`).
- Reject if missing or invalid. Tag name must be `v` + that string (e.g. `v1.4.1`).

## 2. Preconditions

- Working tree: either clean or only intentional edits; do not mix unrelated changes into the release commit.
- Confirm current branch is the release branch (typically `main`). If unclear, ask.
- Ensure tag `v<version>` does not already exist locally (`git tag -l "v<version>"`) or on `origin` (`git ls-remote --tags origin "refs/tags/v<version>"`). If it exists, stop and explain.

## 3. Insert changelog entries (use the script)

From the **repository root**, run:

```bash
python scripts/add_release_changelog.py <version>
```

Optional: `python scripts/add_release_changelog.py <version> --date YYYY-MM-DD` if not using today (UTC/local same-day is fine).

- The script edits `CHANGELOG.md` and `docs/changelog.html` in one shot (GitHub release link + compare link since `git describe --tags --abbrev=0`).
- **Review** the diff. Expand bullets under `### Added` / `### Changed` / `### Fixed` in `CHANGELOG.md` if the release warrants it (Unreleased items can be moved); mirror any extra bullets in `docs/changelog.html` if you add substantive notes. The script’s default block matches existing “release + compare” style.

If the script errors (duplicate version, parse failure), fix the issue or ask the user—do not proceed to tag.

## 4. Git: commit, push, tag

```bash
git add CHANGELOG.md docs/changelog.html
git commit -m "chore: release v<version>"
git push
git tag -a "v<version>" -m "v<version>"
git push origin "v<version>"
```

Use the same `<version>` as in the changelog heading (no `v` in the commit message body except as written: `chore: release v1.4.1`).

## 5. Reminder for the user

- CI runs on the new tag and uses `scripts/extract_release_notes.py`; the tagged commit **must** contain `## [<version>] - YYYY-MM-DD` in `CHANGELOG.md` or the workflow will fail.
- Do not edit the plan file in `.cursor/plans/`.
