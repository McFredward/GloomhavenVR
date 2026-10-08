# Quest Windows retained-data qualification, 2026-10-08

## Observed failure and retained work

Input: `quest-build-support-20261008T142900Z-fa677485.zip`. The release and
current build log identify published Quest source `6cfd7cfb5`, ModBuild643.
All16 raw packages and six early derived phases reuse their receipts. Native
restoration now succeeds, and all14 derived phases finish. Preparation copies
191486 project files, completes post-effects, loading resources and startup
movies, then converts all3369 non-packed native Sprites. Its output-contract
consumer incorrectly treats `sourceContainers[].path` as a delivered Android
file and requires the original Windows AncientArtillery portrait bundle under
the generated project's StreamingAssets. Unity import and APK export have not
started.

The consumer now validates the exact `{path, sha256}` input-provenance role
without inventing a generated output. Sprite assets, manifests, scene bindings,
ordinary Texture2D outputs and delivered movie bytes remain required. Only an
explicit removed original movie importer source may be absent.

The failed Sprite boundary has no committed preparation success receipt and
must retry. All14 derived phases and the four closed preparation steps remain
eligible. In particular, the completed base-project writer must not repeat its
191486-file copy solely because this Builder repair changes its source key.

## Minimum work on continuation

Owned SQLite witnesses retain actual byte hashes together with file identity,
size, modification time and change time. Receipt hashes remain authoritative.
POSIX uses inode/ctime; Windows uses the opened file's native FileId and actual
ChangeTime, not Python's creation-time field. NTFS/ReFS metadata qualifies warm
unchanged files. Unknown metadata/filesystems and hardlinked content use actual
byte reads. Changed stamps, replaced files and corrupt witness rows trigger
targeted reads; they never bootstrap a proof from current stats alone.

Derived journals, stage receipts, preparation outputs/copies and retained input
snapshots use the same witness API. Producer reads seed later observers, avoiding
duplicate payload reads. Snapshot databases stay outside their immutable file
set. Mutations and pending rollback invalidate affected proofs before use.
Explicit project regeneration drops that owner's witnesses and binds the new
directory. Bounded log summaries distinguish metadata reuse from bytes rechecked.

Legacy receipts from the supplied Builder require one initial byte qualification
to establish these witnesses. Subsequent unchanged attempts use metadata. The
original installation still receives its existing source-immutability checks;
this repair does not claim a zero-read whole build or instantaneous startup.

An exact reviewed four-helper profile keeps the published derived recipe and
raw binding. Preparation additionally compares both immutable manifests, actual
Builder source bytes and the complete producer AST. Only the three exact new
identity-coordination nodes are omitted from that AST. Game, profile, template,
backend, mod assets and producer changes remain scoped. Compatible preparation
key migration first qualifies old output/hash/change-time proofs, retaining the
closed pre-archive prefix and Unity Library. Mutable content, settings and final
Player still belong to the current real input key. Unsupported later ownership
truncation fails explicitly while retaining the journal/project.

## Evidence and limits

Evidence: `/home/claw/quest3-local/build/evidence/B643-support-142900/`.
The reported3758.661-second build includes new native and preparation work;
rotated counters do not establish the initial retention-check duration. The
movie phase alone takes404.645 seconds, and the failed Sprite phase76.581 seconds.

The bounded Linux qualification sample contains2066 files /38172499 bytes,
including ten unchanged original UI prefabs. The first legacy pass reads that
payload once; two warm instances read zero payload bytes and qualify2066 metadata
hits. The small RAM-cached sample measures0.169 seconds for prior byte reads and
0.171 seconds warm, so it is evidence of read elimination, not a wall-clock
speedup claim. Larger snapshot checks and integrated continuation fixtures are
recorded separately. No Windows duration guarantee follows from Linux results.

The separate actual Stage-receipt qualifier benchmark uses one synthetic64-MiB
payload and the genuine preceding storage source. Prior qualification reads64MiB
in0.042232 seconds; migration reads it once in0.048417 seconds; a fresh warm
instance reads zero payload bytes in0.000641 seconds. A same-size mutation with
restored mtime reads the payload and refuses the old receipt. A10-MiB actual
snapshot fixture likewise migrates once and reads zero payload bytes warm.
These bounded Linux results distinguish large-payload savings from small-file
metadata overhead; they do not estimate the whole user's Windows build.

Focused validation covers source/output roles, true changed-file controls,
closed/pending derived phases, real preparation retries, current failure rows,
complete published-source compatibility and the packaged stdlib startup. Runtime
compilation/render/platform evidence is inherited only where its exact declared
inputs match. Current dev643 `29d4b6dd8` remains an ancestor; Quest stays on
`feature/quest3-standalone` under the maintainer's explicit exception.

The integrated focused gate passes318/318 tests in20 affected Builder/Wizard
modules. A bounded genuine original Sprite CAB/UnityPy producer fixture reproduces
the former false portrait-bundle output and commits only its real generated
Sprite/receipt; warm continuation reads zero payload bytes and refuses producer
replay. The full published-source comparison retains all33 recovery recipe files,
all14 derived transactions and16 raw packages. Actual Builder retry fixtures keep
the first four preparation writers and refuse changed original inputs before
rebind. All1060 declared runtime compilation inputs match the prior643 proof;
unchanged rendering/platform checks are inherited rather than repeated.

Publication requires the actual extracted Builder archive to preserve a retained
session across separate server launches and verify current source hashes plus
the preceding complete derived recipe. Unpack over the existing folder, keep
the selected work/state folders and continue the same session. No host APK,
exhaustive shader sweep, full Windows conversion or headset success is claimed.
Cleanup removes only this repair's completed private workers and duplicate ZIP.
