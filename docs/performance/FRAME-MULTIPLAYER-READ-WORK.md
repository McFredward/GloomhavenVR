# Exact multiplayer mirror source reads

Build 644 policy update (2026-10-08): pure work-removal controls documented here are now unbound and always enabled on every platform. Historical Off cases remain source-reference test evidence; quality, timing and diagnostics remain configurable. See [the current audit](FRAME-644-STEADY-CPU.md).

This lane changes `RemoteWidgetMirror.Pair.Apply`, independently of NPC transport,
protocol or remote-board refresh scheduling. `Optimize.SharedUiWindowReads` controls
both this lane and the local native-window read lane in the same PC/Frame binary.
The viewer selects repeated work, not another player's appearance or timing.

## Safe observation boundary

The original assignment commonly reads the source twice:

```csharp
if (target.sizeDelta != source.sizeDelta)
    target.sizeDelta = source.sizeDelta;
```

The new path retains target-then-source comparison order and reuses that source value
only in the immediately following assignment. There is no intervening destination
write or callback between these two original reads. The next property is still read
after every earlier write and its synchronous native callbacks.

Eligible getters are nonvirtual native properties or nonvirtual uGUI field readers:
RectTransform anchors/pivot/size/position, Transform position/rotation/scale,
Behaviour.enabled, Image sprite/override/type/fill, RawImage texture/UV and
CanvasGroup.alpha. The publisher's original `UnityEngine.UI.dll` was inspected:
Image and RawImage getters return their stored fields; overrideSprite returns the
current active sprite. The fixture checks the actual loaded getter metadata is
nonvirtual for every eligible property.

Virtual Graphic.color, TMP text/font/style/material and Text.text remain exactly on
the original path. A subclass getter can execute code or return a new value on each
observation. Sharing that observation would change the original behavior. Existing
native material ownership and renderer-tint application are untouched.

The mode boolean is read once for each visible `Apply`, after the native activation
write. Off executes the original second getter. A live mode change during a setter
callback takes effect at the next `Apply`, including another node in the same frame.
No source value, hierarchy snapshot, material snapshot or pose survives its own
compare/write. No frame/peer cache, structural refresh skip or animation cadence was
introduced. Source objects remain read-only.

## Source-bound runtime evidence

Run `python3 scripts/check-remote-mirror-read-runtime.py`. The checker extracts the
complete production Pair struct and compiles its actual Apply, constructors, branch
flags and material ownership. It runs against real Unity 2021.3.5f1 transforms,
CanvasRenderer, CanvasGroup, Image, RawImage and Text. Only config access and read/write
counters are fixture boundaries. The original Apply is pinned in
`Apply.pre-optimization.fixture` with a checked SHA256; it is not a rewritten reference
algorithm. TMP's untouched virtual branch is also compared directly to that original;
the separate existing board-rules suite exercises the complete current production DLL
with original native rules/TMP content.

Counter wrappers return the original evaluated value. The fixture records writes and
actual native dirty/layout/enable/dimension callbacks. Registered callbacks deliberately
change later original fields during a destination write: colour changes the later
sprite, the sprite write changes the later override, type changes the later fill,
texture changes later UV, and native activation changes the later fill. On preserves
the original final properties and the full write/callback sequence. Off additionally
preserves the full tracked source-read sequence.

Coverage includes:

- All 16 intermediate geometry/content/alpha/rotation/scale observations, including
  multiple Applies within one real frame and live mode switches.
- Forty unchanged passes for each Image/RawImage/Text/plain-transform case: no destination
  writes and exactly one mode read per visible Apply.
- Enabled/disabled Graphics; native activation; inactive sources; the independently
  visible root; suppressed and externally owned branches; owner-column geometry.
- Source and destination materials, owner material acquire/release, sprites/overrides,
  renderer colour, UV and exact source preservation for every observer.
- Hostile virtual Text and Graphic getters whose second result differs from their first.
- Twenty shared original Image nodes applied to three independent observer destinations.

The controlled changed-Image workload executes **1,800 -> 1,080 tracked source getters**
across 60 Applies: 30 -> 18 per node. This is a **40% reduction of that tracked source-read
subset**, not of the entire mirror, multiplayer frame or application CPU time.
Destination getters/writes, original material sharing and unrelated work are outside
that count. Unchanged-field comparison reads remain live; an active override also avoids
its redundant conditional second getter. Only method-local temporaries are added; the
instrumented harness itself allocates traces, so it is not an allocation benchmark.

Eight runtime causal controls must reach their specific assertion: repeating the native
getter, ignoring Off, reading an anchor before the previous dimension callback, reading a sprite before a previous callback, reading fill before
the type callback, reusing a virtual text getter, writing unchanged fill and dropping
alpha animation. Compilation failure never counts as a passing control.

`check-mirror-dials.py` previously missed null-conditional entry reads such as
`SharedUiWindowReads?.Value` inside the new PerfConfig partial. Its wrapper census now
recognizes that syntax. The remote reader is explicitly classified `not-1to1` in
`.planning/refactor/MIRROR-DIALS.allow` with the exact proof boundary. The focused checker
runs the real scanner on the actual partial/call-site files, verifies the classification,
and confirms the previous matcher misses this wrapper. Missing scanner coverage is
never treated as permission to alter a peer's picture.

## Validation and remaining hardware work

The final focused evidence is retained under
`.planning/debug/remote-mirror-read-runtime/`: source/dependency hashes, compiled variants,
Unity log, native assertions and `dial-census-proof.json`. The run reports **625 production
assertions, eight runtime causal controls and one scanner causal control**. The parent
must register `remote-mirror-read-runtime` as a local suite (command above; suggested
weight 40), run the complete final integration gate and retain the updated source hashes.

Compatibility checks on this production change passed:

- Strict Release build: zero errors and warnings.
- Existing native playback suite: 466 assertions and three runtime controls.
- Existing native board-rules suite, private current Debug DLL in real Unity: 229
  assertions and three controls; original serialized rules content and TMP presentation.
- Full mirror-dial scanner: eight readers, 411 ConfigEntry declarations and 100 wrappers;
  every reader classified, zero OPEN findings.

The offline three-room Steam Frame capture does not establish multiplayer timings.
Test two and four players on Frame as host and visitor and compare the existing
`Net.BoardMirrors`, `Mirror.NodesDriven`, `Mirror.NodesSkipped`, `Net.Fans`,
`Net.Presentation.NativeSend`, `Net.ApplyPending` and `Net.Send` scopes. The shared
config also changes local UI read work, so hardware A/B totals cannot isolate this
mirror lane without considering those scopes separately. These checks establish exact
read/write behavior and work counts; headset appearance and FPS still require hardware.

## Integrated Build627 review

Current production `Pair.Apply` was rerun against the pinned original and genuine
Unity/uGUI source getters and synchronous destination callbacks. Receipt
`remote-mirror-read-runtime/run-yznrkmed` passes 625 assertions, eight runtime
causal controls and the nullable-wrapper scanner control. Hostile virtual Text/
Graphic getter observations remain on the unchanged original path; native values
are reused only between their comparison and immediate setter. No network
scheduling, source visibility, geometry ownership or NPC transport changed in
this review. The parent retains hashes with its final common-tree evidence and
still runs the final gate; no headset picture or transport latency is established.
