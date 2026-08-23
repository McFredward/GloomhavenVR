# Player-facing release notes

One file per released version, named after the version and nothing else:

```
packaging/release-highlights/0.2.0.md
```

`scripts/release-notes.sh` pastes that file **verbatim** into the `## What's new` section of
the GitHub release body, above the collapsed developer change log. No file, no section — the
release page then says so plainly and shows only the technical list.

## Why this file has to be written by hand

The commit subjects are written for developers:

> `feat(worldui,net): ModBuild 239 — a union that counted invisible ink, a host three levels too deep`

There is no filter, no prefix-stripping and no friendly grouping that turns that into a sentence
a player can use. Anything automatic would produce something that *looks* like release notes and
still says nothing. So the script does not try — it keeps the raw list, labels it honestly, folds
it away, and leaves this file as the one place the player's sentences come from.

## How to write it

The reader is somebody deciding whether to download an update. Write what **changed for them**:

- Say what they will notice, not what was changed to make them notice it. "The cards in your hand
  now update the moment you enhance one" — not "peer hand-fan sticker refresh".
- Lead with the fixes for things they complained about. Those are what the update is for.
- Name a thing that is still broken if this release does not fix it. The **Known rough edges**
  section of the README is the long form; a release note can point at it.
- Markdown works. Headings above `###` do not — the section already sits under `## What's new`.
- No version numbers, no build numbers, no file names, no `ModBuild`. If a player has to know a
  number, the game tells them.

## Checklist before releasing

1. Write `packaging/release-highlights/<version>.md` on `dev`.
2. `bash scripts/release-notes.sh <version> /tmp/notes.md && cat /tmp/notes.md` — read it as the
   player will, top to bottom.
3. Push `dev`, then `git push origin dev:main` to release.

Without step 1 the release still succeeds; it just has no plain-language notes, and the script
reminds you of that on stderr.
