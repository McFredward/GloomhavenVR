# Build760 combined window/input integration

Status: implementation and final qualification in progress; not delivered.

## Authorized scope and evidence

The maximum-zoom Build759 confirmation report remains part of this delivery.
Its scale-free plane-normal correction and actual GL/Vulkan consumer proof are
recorded in POPUP-760-MAX-ZOOM.md and POPUP760-CONSUMER.md.

During its integration checkpoint, the maintainer supplied a new paired759
multiplayer run. The remote player is the host. Inputs and the inspected
`ziel_wird_rechts_abgeschnitten.png` are pinned in ignored
`debug/mp760/hardware759/`. The maintainer clarified that the card-overview issue
means **scrolling**, and subsequently reproduced the Options laser failure in
**windowed mode**, with fullscreen restoring it.

This expanded union is class C: native capture layout, converted-window design
frames and synthetic UI input share infrastructure. The previous unfinished
checkpoint was stopped before further implementation. Retain qualified proof
only where actual runtime/test/tool/native inputs and consumer boundaries remain
unchanged. Changed or uncertain dependencies must run again. No complete-gate,
wire-golden or headset acceptance is inferred from the stopped invocation.

## Causal findings

- The original ability-card prefabs contain pointer-only EventTriggers that also
  implement IScrollHandler. Unity's nearest-handler dispatch stops there and
  never reaches the actual ancestor ScrollRect. Empty ordinary adapters must
  yield to the nearest real handler; explicit/custom wheel behavior stays native.
- The host's native party frame starts2580x1080 on3440x1440, while the client
  starts1920x1080. A later1988x1080 outer fit cannot repair earlier child anchor
  placement. Use the original authored native frame before conversion and keep
  the existing shared authority law, fixed-popup geometry and native restoration.
- A16:9 physical screen requires a matching native capture domain. Actual Unity
  proof showed that CanvasScaler already reads its target metric correctly;
  the proposed extra scaler patch was therefore removed. Capture quality must
  preserve both prior raster axes rather than force a low fixed resolution.
- Synthetic render-target coordinates are not OS mouse coordinates. The original
  windowed display conversion can remap that domain. Exact owned pointer/caster
  and captured-camera/target provenance is required; genuine mouse, other
  displays, nested calls and native permission filters retain native behavior.
- World-space pointer coordinates also require a physical interaction frame,
  rather than the desktop-derived mono viewport. Any such correction must
  validate the admitted host's committed bounds with a finite forward ray,
  preserve raw native ray/graphic filters, and retain native fallback for
  unsupported spatial remapping. Do not clamp or fabricate an inside result.

The RT-domain replay does not itself establish the exact cause of the reported
backbuffer-head/windowed failure. Bounded Debug receipts and an independent
backbuffer-aspect control distinguish OS remapping from mono/XR bounds coupling.
Windows/OpenXR headset behavior remains a separate acceptance boundary.

## Preserved checkpoint and storage

The earlier class-C attempt retained100 successful local IDs and16 source gates.
It also contains an unsuccessful native level-up editor exit137, with no timeout
or completed containing-suite pass; do not relabel it successful. An initial
scheduler-inventory failure was repaired before local execution. A concurrency
adjustment and the subsequent new-scope interruption are recorded independently.

All179312 evidence files, source/input manifests, results, captures, build
artifacts and failure records were verified before the old worktree was removed.
Identical contents were then stored once in a content-addressed archive; each
original path and its metadata remain recorded. Every blob and original file
mapping was verified, and independent-copy restoration was checked for manifests,
logs, a PNG, C# source and the actual primary DLL before retiring the redundant
container. No successful proof or failure evidence was discarded.

Current archive: `debug/popup760/checkpoint-pre-mp/evidence-cas.tar.zst`, SHA256
`faf10de3da30017531162abde10993113eea0bc43242579f2c8e7b607f1c016f`.
The adjacent `restore-cas.py` restores independent files; it does not follow
external symlinks. The independent reuse audit consumes the retained manifests.
Only these owned worktrees/caches are retired; shared references, hardware logs,
pins and other agents' work remain untouched.

## Final qualification

Pending final input-lane commit and review, strict Release/Debug builds, fresh
source gates, complete unique local coverage with justified input-bound reuse,
actual byte-exact wire goldens, bundle/surface/compiled review, final source pins,
dev push and owned cleanup. No local release archive is requested.
