# Quest Windows CRLF retry, 2026-10-08

## Observed failure

Input: `quest-build-support-20261008T065008Z-a3cf3b9e.zip`. Its release manifest
identifies published Quest commit `24ea5928b884bd353981a68866d097b748176edb`,
ModBuild640. The current `wizard/build.log`, rather than older rotated recovery
logs, records the failure. All16 original packages were reused. Catalog was
reused; canonical, copy, runtime, GUID and layout stages completed. Native
reference auditing failed at `Assets/GameObject/UI Quest Marker.prefab` with
`ParserError: found undefined tag handle`, line3/column5. Unity import and APK
export had not begun. This is a different failure from the previous missing
UnityPy launcher dependency.

The support ZIP does not contain the offending prefab bytes. Against the retained
read-only reference export of that same prefab, LF parses six pointers and CRLF
reproduces the exact error signature. The production audit decodes raw bytes,
retaining Windows CRLF; its former header pattern accepted only LF. It blanked
the `%TAG` directive but left the unrecognized Unity `!u!` document tag. A bounded
set of ten original UI Quest prefabs reproduces this with the former parser.

## Repair and continuation

`serialized_pointer_tokens` recognizes Unity document headers with LF, CRLF,
mixed endings, signed object IDs and the optional stripped-prefab suffix. Header
and directive cleanup preserves character positions and line endings. The exact
null-managed-registry C-parser fallback also accepts CRLF. Quoted scalar pointer
lookalikes remain excluded; unrelated malformed YAML still fails. The audit wraps
recognized YAML failures with the asset path and original exception cause, so
`last-failure.json` and the Wizard's builder-error detail identify the file.
Unexpected I/O exceptions retain their original identity.

The observer repair changes no original export identity or completed derived
asset transformation. Nevertheless, a plain source hash change would previously
select a new derived project and repeat completed copying/restoration. Only the
following exact reviewed pair aliases the recipe identity:

| Source | SHA256 | Bytes |
|---|---|---|
| Published640 observer | `ac15d03682290541f6271926b989eec084b5b35746e7915fe1e77ec75ef5c45a` | 47202 |
| Fixed observer | `c0e738efa56b4c6c29cea965eb94a6e274e0ed19bd3802a42206cf343339365f` | 47667 |

Actual immutable source verification continues to use the new hash/size. Every
other recovery/transformation row, game identity and recipe version remains in
the workspace key. A changed transform selects its own derived workspace. The
exact observer pair also keeps existing qualified raw bindings eligible. No
journal, owner or original export is rewritten or moved to manufacture reuse.
Retained byte/owner qualification remains necessary; reuse does not promise an
instant restart on a large existing project.

Unpack the new public Builder over its existing folder, keep the selected work
folder and continue the same Wizard session. The six committed staging phases
are retained; native work resumes from its last uncommitted boundary. Old failed
attempts remain in diagnostic history while the current run owns live rows.
The new release manifest correctly updates the source snapshot; this does not
require re-exporting the unchanged game.

## Current mod integration

The feature remains `feature/quest3-standalone`. Reviewed merge
`1e0d5c78039864302341e04d2b84dcdd88e63242` has current dev642
`cf0da0bb8bd2defc7c12bed71ca0ed79acfc1fe7` and published Quest24ea as parents.
Repair commits are based on that integration; published history remains intact
and can be pushed normally. All29 runtime paths introduced by dev641/642 match
current dev; all41 prior Quest runtime paths match the previous release. The
four-bank owned-content pipeline and Quest camera ownership are retained.
The platform fixture gains only the two new explicit CPU dependency signatures;
production runtime needs no compatibility edit.

## Verification and limits

Evidence root, outside disposable worktrees:
`/home/claw/quest3-local/build/evidence/B640-support-065008/`.

- Recovery worker:162 cases,160 passed and two existing optional skips. Both
  Python and C YAML loaders, raw Windows-byte auditing, Unicode offsets and
  malformed-input controls pass. Ten unchanged original prefabs preserve131
  reference tokens, including the six in UI Quest Marker.
- Integrated repair gate:180/180 cases across13 affected parser, raw-reuse,
  staging, release and Wizard error/retry/session modules. A real Builder to
  raw-selection to Journal test retains16 small package receipts and six closed
  phases, preserves GUID/layout file contents/inodes/mtime and retries only
  native work. Existing bindings to an older raw recipe are exercised without
  using the current-workspace shortcut. Corrupt source/staged bytes and missing
  or changed owners are rejected.
- Reviewed dev642 composition: strict Release/Debug with zero warnings/errors,
  source16/16, Quest platform3081 assertions and17 rejected defect controls.
  Final runtime inputs match the worker qualification. Suite inventory runs18
  cases with two existing optional skips; local196/ci111/source16 compose both
  branches. This is not a new complete local/CI/wire gate.
- Previous authored-bank/startup, current dev CPU and Frame camera evidence is
  inherited only where the complete declared input hash sets match. No new
  native pixels, transport session or FPS acceptance is inferred.
- Publication requires the actual extracted source ZIP to pass isolated
  standard-library discovery, retained-session startup on two server ports,
  offline artwork/branding delivery and exact current-source/old-staging identity
  checks. Its manifest carries ModBuild642 and the committed feature source.

No Unity import, exhaustive shader audit, host APK, complete Windows conversion
or headset run was performed for this bounded repair. The next owner-PC attempt
must establish further full-pipeline progress. Public packaging continues to
exclude original/decompiled game code, DLLs, original-derived environment/figure
geometry, APKs, credentials and local build/state caches. After successful ZIP
qualification, only completed workers and intermediate archives owned by this
repair are eligible for cleanup; supplied evidence and shared worktrees remain.
