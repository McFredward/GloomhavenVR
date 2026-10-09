# NPC658 offered-card overlay review

The supplied `zauberin_overlay_problem.mp4` shows the enhancement border disappearing
while the physical card and outer cyan ring remain. This lane repairs the offered
print's ordering relationship; flight continuity and first-picture latency are
separate NPC658 lanes. The worker starts at published656 `bf3444cb502e1eff52bcf0e5194a255109879d36`
and is rebased onto published657 `a9af3c330f25ffb3f74ab32342f57604e0f7f890`.

## Hardware evidence and causal scope

The frozen host Debug and remote Info logs both identify ModBuild656 and commit
`bf3444cb5`. Their banners were checked separately. Inputs remain in the main
checkout's `.planning/debug/npc658/inputs/`; the already extracted video contact
sheet is `.planning/debug/npc658/videos/overlay-contact-1.jpg`. At approximately
1.0/2.5 seconds the inner frames disappear; at 1.5/2.0 seconds they are visible.

The host's peer2/session3 `enchant.highlight` modules59/60 remain active and enabled,
without a cull indication. Their canvas paint orders alternate128/144/160. A
zero-alpha native hover fill can be legitimate. These logs and pictures constrain
the cause but do not uniquely attribute every hardware frame to one branch.

The production order helper previously measured each detached native partition's
own finite Rect. The highlighter includes a large aura/host Rect, while the
physical print has the smaller card Rect. The production panel/furniture ladder
can therefore place a native effect at96 and its opaque paper at112.

The actual serialized `GUI/GUI_FlexFrame_Shd` shader is
`GH_Data/sharedassets1.assets:path874`; its enhancement material is
`sharedassets4.assets:path18`, `GUI_New_Enhancement_Frame_Highlight`. Its real pass
uses Transparent queue, `Blend SrcAlpha One`, `ZTest LEqual`, `ZWrite Off`,
`Cull Off`, and zero depth offsets. A later opaque print can erase additive ink
that wrote no depth. The ring extends outside the opaque paper, so its continued
visibility does not establish that the inner frame was painted correctly.

## Production change

The existing exact109 original-to-physical-print relationship now supplies the
offered original's furniture distance. After native pose application and again
at canvas submission, its native canvas takes the print's tier plus one. The
actual imported highlighter has no nested Canvas: holder branches, aura and
ability-area ink inherit one original tier above their paper. Internal native
hierarchy and sibling order are retained. The helper does not modify alpha,
graphic enablement, material/depth state, pose, native callbacks or paper child
canvases. A different authored sorting layer retains its original priority and
does not receive an override.

Relations cache the native and paper canvases. Submission only visits active
relations; it does not search canvas hierarchies each render. Multiple registered
roots sharing one native canvas retain one original baseline and a deterministic
whole-plate owner: the nearest registered root to that canvas, then stable
instance identity. A delayed descendant association cannot steal the parent's
plate while a card is replaced. Removing either shared root first preserves the
survivor, and the last removal restores the first original canvas order.

Withdrawn/dead relations stop following their prior print. Destroyed or inactive
roots retire on the next canvas submission. Existing network/room reset releases
all relationships and the callback immediately, including live pooled originals
that might never render again. The registry is bounded by the existing 4096 native
modules per peer, eight retained peer presentations and one local holder. No new
normal or Debug log stream is introduced.

The separate one-token `ResetMotionNetwork` seam calls `ClearOfferedFrames()`;
the flight lane owns the rest of that file. Apply this seam after the flight
implementation when integrating the lanes.

## Native production and rendering proof

The new `scripts/npc658-overlay-runtime/run.py` binds real capture, packed numeric
motion, ordered avatar reception, the original-template path, original property
application, offered109 fitting and the production furniture ladder. It imports
the actual serialized eleven-node highlighter and its pooled ability area, and
extracts the unchanged native `UIEnhancementButtonHighlight.OnHovered` method from
`GH.Runtime.dll`. Its four states and both hover inputs must produce the native
0/.2/.7 fill outputs. All original `Frame` nodes remain enabled; the previous655
frame exclusion is absent. Reordered original arrival, late old headers, 15Hz
continuous vertical native hover and independent numeric application are exercised.
Area-to-print world corners and enabled/color outputs are checked throughout.

Rendering is deliberately bounded. The actual D3D11 shader has no GL subprogram.
The fixture records its exact pass flags, native material properties and asset
hashes, then uses `NativePass658.shader` as a declared GL fragment adapter with
those same depth/blend flags. It preserves native geometry/vertex color and draws
an edge so occlusion can be observed. This does not reproduce the original
angular/noise fragment pixels. The physical print is an opaque Image boundary
with the actual 325.1 x 449.5 Rect and 0.001 m/px; it is not the game's complete physical
CardBody/CardMesh. Neither limit is evidence of a verified headset picture.

Twenty-four perspective picture pairs cover four native states, both hover inputs
and three eye positions crossing actual panel ranks. Source and observer must
each include meaningful paper and border paint. Settled readbacks retain their
actual world poses; the camera does not normalize away a source/observer pose
disagreement. Native movement/corner checks happen before the settled picture
comparison. The final run records 20,075 assertions and 24 picture pairs, with 0–1
differing pixels per pair (one total; acceptance below 150), and native/print
orders 113/112 or 97/96 in both views.

Eighteen explicit lifetime behaviors cover withdraw/reopen, replacement, old
print retirement, inactive/destroyed objects, valid native hide/zero fill,
shared-canvas removal in both orders, deterministic ownership during replacement,
distinct native sorting layers, and immediate reset with a live original bank.
An additional prerequisite verifies that the distinct sorting layer exists.

The causal negative compiles exact published656 DepthOrder and Offerings in the
same current fixture, with the new reset API token reversed for compatibility.
Compilation must succeed; a missing API or unrelated exception cannot count.
Its sole expected failure is:
`original native frame remains above its physical print across actual panel ranks`.
Both readbacks and the actual ranks are persisted before that assertion, so the
failing border paint remains reviewable. Its result is a source-bound causal
control, not a reproduction of the complete headset D3D11 fragment.
The final negative records both native canvases at 96 behind their papers at 112:
the ring outside the print remains visible while the inner border disappears.
The observer's first perspective readback contains zero frame-adapter pixels in
that negative, versus 3,030 with the current native tier113 above paper112.

## Commands, receipts and integration

Register two local scopes with a common `unity-editor` resource lock and a 360s
suite budget. Each scope compiles independently and has a 240s internal Unity
timeout. The primary agent owns the shared suite registry.

```sh
python3 scripts/npc658-overlay-runtime/run.py
python3 scripts/npc658-overlay-runtime/run.py --old-depth-source --expect-incomplete
```

Private worker receipts initially live under
`/home/claw/worktrees/npc658-overlays/.planning/debug/npc658-overlay/`:

| Check | Receipt | Scope |
| --- | --- | --- |
| Final original/state/order render | `frozen-production/run-l6p1i0a5` | 20,075 assertions; 24 picture pairs; 18 lifetime behaviors |
| Exact published656 negative | `frozen-negative/run-2nuavhmp` | Successful compile and the designated rank/paint failure; native 96 behind paper 112 |
| Existing original offered geometry | `native635-focused/run-f6vciyjy` | 1,081 assertions; five causal controls; unchanged by the later depth-only refinement |
| Production ordinary depth ladder | `depth-final/run-i6izmyin` |Eight assertions; three causal controls |
| Source group on rebased implementation | `source-rebased/results.json` | 16/16 source checks |
| Strict Debug on rebased implementation | `strict-debug-rebased.log` |Zero errors and warnings |

The two final native runs retain `source-hashes.json`, native hover/pass provenance,
zero-warning fixture build logs, Unity results, world-pose records, native rank
records and PNG readbacks. Assertion totals vary with editor coroutine scheduling;
the state/picture/lifetime coverage is fixed. Earlier exploratory runs are not
promoted to passing evidence. The primary integration retains compact receipts
before deleting worker projects/caches and runs its required combined gate.
This worker result is focused evidence, not a new complete local/wire gate.

The rebased checkpoints are `aebe799a7` (unrelated transport fixture API boundary),
`07cd37d68` (separate Motion reset seam), and `88686d704` (production affinity and
native overlay proof). The fixture-only final receipt persistence and this report
are a subsequent checkpoint. Headset acceptance still requires the actual native
D3D11 frames to remain stable during hover, selection, print replacement and
town/scenario teardown. The new proof supports the ordering fix without claiming
that untested hardware result.
