# Quest Builder ownership readiness and bounded update history

Runtime remains B691 /1.1.1. This is a host-side repair on
`feature/quest3-standalone`; no new APK or original game conversion is built here.

## Current evidence

Support174758 identifies source43ecd6d9a/B691 and stops during inspection because
Steam's native interface is unavailable. The maintainer confirms Steam was closed.
Support175746 identifies the same release, session
`de231bac22c649f8b05a72e5ebf12db6`. Its owned-PC inspection succeeds. The compact
ARM64 Player completes, then artwork delivery stops at467/468 bundles with
`Owned card pixel spool exceeds its compressed byte bound`. This is a separate,
post-compiler failure; historical Unity logs do not establish another failure.
Both captures are retained unchanged under the main checkout's ignored debug tree.

## Account, base game and DLCs

Open the installed matching client through a fixed provider-only API: Steam,
Epic Games Launcher and GOG Galaxy on Windows; installed Steam or Heroic/observed
installed Flatpak on Linux. No arbitrary executable/command reaches this API.
Missing installation/desktop or unfinished account setup becomes a bilingual
user action with an open-client button and explicit same-run continuation.
The launch itself never grants ownership or starts a build.

Steam initialization failure has a typed isolated result. Start its client once,
retry the same native query for a bounded90-second interval, then retain a guided
wait if it is still unavailable. Wrong account, missing base licence, inconsistent
DLC data and a native crash remain failures of their actual qualification; they
do not grant content. Explicit Windows Steam DLC declarations also qualify the
base-game licence and selected SteamID, without demanding DLC exports or running
a duplicate full DLC query. Offline/Linux/manual provider paths retain their
previous explicit policy; no Windows DLL is loaded on Linux.

An original installation, identity and account licence are different evidence.
All providers still qualify the original PC installation against the selected APK
before mod/name updates. Steam can additionally query its local native account
and base licence. GOG mini-manifests support offline installation; Galaxy login
is not mandatory. Epic installation markers identify installed products, not an
authenticated account licence. Their supported local declaration path remains
explicit. No credential/token extraction, Horizon registration or new store/cloud
runtime service is introduced. See official
[Steam ISteamApps](https://partner.steamgames.com/doc/api/ISteamApps?l=english),
[GOG DLC discovery](https://docs.gog.com/sdk-dlc-discovery/),
[GOG mini-manifests](https://docs.gog.com/bc-dlc/), and
[Epic Ecom prerequisites](https://dev.epicgames.com/docs/epic-games-store/services/ecom/ecom-overview?lang=en-US).

## Exact artwork and retained native output

Keep GHVP1 and the512MiB runtime limit. Normal lossless deflate replaces the fast
level1 search. Remove dead ambiguous-name spool bytes only when the spool budget
requires it, without keeping another whole spool or dropping valid images.
Ambiguous identities remain excluded; all decoded pixel hashes are unchanged.

Actual local468-bundle qualification with standard zlib1.3 produces310,650,293
bytes and1,049 entries; all1,487,709,920 decoded RGBA bytes match their previous
hashes. Cold47.76s; warm0.0503s without decoding or whole-file hashing. The local
level1 baseline is also below512MiB, so the exact Windows input/decoder reason for
its larger spool is unproven. The smaller lossless output and improved bound
error diagnostics must be tested on that machine; do not call it Windows E2E.

Pixel extraction happens after native compilation. Exclude this data producer
from compiler source rows for both historical and current inventories, retaining
its complete source in the update/data recipes. A finite complete controller-row
alias and exact previous/current compiler-recipe bridge admit the already completed
B691 native pair. Every other compiler function is unchanged; actual runtime,
package ABI, profile, original game, native pair and unknown recipe changes keep
their independent guards. No broader version/hash range is accepted.

## Storage behavior

Signed output versions previously accumulated under `build/updates/<key>`.
Shared code projects and compiler inputs already reuse their own game/ABI owners;
per-run verified source snapshots are separate, removable history. The supplied
report has no current storage census, so no claim quantifies the user's disk.

After successful completion, prune only obsolete final APK files proven by exact
owned update plans, successful build receipts, matching small reports and file
identities. Keep the current APK, original bank, pending runs' inputs, signing
keys, saves, native Players, compiler projects and shared source/game inputs.
Do not rehash APK payloads to clean history. Unknown, changed, pinned, linked or
external outputs remain intact. Optional cleanup cannot invalidate a successful
build; its bounded report is exported as `wizard/update-storage.json`.

Storage management adds an explicit old-update-APK preview and allows retiring
completed runs too. Metadata-only deletion is the default. An optional per-run
data cleanup removes only proved unused intermediates; compact compiler caches
remain shared. Every preview reports whether fast-update data is retained or
whether later updates may need preparation/compilation again, before the delete
confirmation. Inventory exposes fast-update bytes and correctly classifies code
project Library files as Unity caches. Disk use is bounded for proved obsolete
finals, not promised constant: useful compiler results, pending inputs, diagnostics
and unproved artifacts remain until a suitable explicit cleanup.

## Verification and limits

Focused provider, Wizard/action/API, browser, storage, native-pair resume and APK
update suites cover this repair. Actual468-bundle lossless production and warm
reuse are qualified separately. Reuse the previous B691 runtime/Weaver/desktop
proof by exact unchanged source inventory; no new complete wire gate, Windows
launcher login, Windows update or headset-picture result is claimed. Detailed
receipts live in `B691-update-174758-20261018` outside the public handoff directory.
