# Quest cold-start FEX acquisition repair — 2026-10-12

## Captured failure

`quest-build-support-20261012T181854Z-600d0045.zip` identifies the delivered
Builder commit `135a5508f`, ModBuild672, Windows AMD64 and Python3.14.8.
The fresh workspace has no conversion receipts. Capacity passes, then the
mandatory dependency preflight blocks before any asset conversion: the FEX
`2609.1-1~n` PPA pool URL returns HTTP404. The other23 dependencies are local or
reachable. This is an upstream artifact-location failure, not a Unity import,
RAM exhaustion or a new game conversion failure.

The older October9 preflight review already recorded that pool URL's removal;
existing native caches had hidden the cold-build consequence. The maintainer's
intentional deletion of the large workspace exposes the missing acquisition.

## Same artifact, available official archive

The official Launchpad API lists Noble ARM64 publication250198350 and build
33620553 for this exact package. The superseded publication was removed from
the PPA on October9. The [original build file](https://launchpad.net/~fex-emu/+archive/ubuntu/fex/+build/33620553/+files/fex-emu-wine_2609.1-1~n_arm64.deb)
still resolves to the [official librarian artifact](https://launchpadlibrarian.net/877920617/fex-emu-wine_2609.1-1~n_arm64.deb).
Both URLs were downloaded fully on October12 and match the existing lock:

- Archive:1,693,160bytes, SHA256
  `c62210a501153885dede187f433e9a107d8d7a91b6959ba17e8a35a05c62e7fa`.
- Staged ARM64EC DLL:5,931,008bytes, SHA256
  `89240b8cbc70703c7dc87739910d40fdc5c7ea004b2df5c8c47f607bd87a0c89`.

These are two routes to the same official stored file, not independent mirrors
or a newer runtime. `proton.lock.json`, all seven Proton native producer files,
the FEX revision, license/source references and runtime provenance stay unchanged.
No public game content, owned DLL or new APK enters the Builder archive.

## Implementation and reuse

`runtime.py` declares a finite mapping from the old exact archive URL to the
librarian URL and original build URL, followed by the old pool URL. The Wizard
reads this same literal declaration without executing converter code. The real
downloader uses the same order, streams SHA256 during transfer and publishes
only bytes matching the existing pin. Correct cached archives need no network;
corrupt ones are replaced atomically after successful qualification. Interrupted,
404 and wrong-byte attempts close their handles and remove their own partials
before trying the next declared route. All failures remain bounded and named.

Proton's native input key excludes the generic acquisition helper, so existing
compiled bridge/runtime caches retain their original keys. Exact reviewed helper
rows preserve already completed preparation and compact updates only when the
original manifest explicitly records `proton-arm64ec-fex`. Box64 embeds this
helper's source hash and receives no runtime alias. Missing backend provenance,
unknown helper edits and changed native sources retain normal migration rules.
The late classifier's exact reviewed source row produces no IL2CPP input change;
host-only repair updates need no Unity compilation. Raw/derived asset recipe
identities are unchanged. The complete prior3337-file release inventory was
compared against the actual four changed production files: incomplete/complete
preparation scopes, raw recovery key and compact payload/code scopes agree.

Retain the Windows workspace, replace the Builder archive and resume the stopped
session. This captured run had not entered conversion, so it has no completed
asset work to recover; any previously qualified tool/download cache remains.

## Evidence boundary

Focused checks cover the real downloader's fallback, checksum rejection, atomic
repair, no-network cache reuse, interruption cleanup, early preflight and actual
native-cache/preparation/update qualification. Unknown backends, files and pins
are negative controls. Existing preparation, completed preparation, update,
release and TLS checks run with the repair. The full archived release compatibility
comparison uses actual source records, not a synthetic pair alone.

The actual producer fetched and statically staged the official file, and both
official routes passed full byte verification. Evidence and per-run central
cleanup receipts live outside the handoff folder at
`/home/claw/quest3-local/build/evidence/B672-fex-download-20261012/`.
This is a Builder-only repair with inherited B672 mod/runtime checks. It does
not claim a new Windows end-to-end export, Unity/game import, Android execution
or headset result. The handoff receipt records final focused counts and the
extracted archive validation. Remove this round's integrated worker and temporary
archive copies after their processes stop; preserve captures and current output.
