# Build766 — Results footer and shared picture lane

Test class: **B, bounded Results geometry/presentation**. Base is `dev` at
`1c61941fd` / Build765. The parent owns strict builds, all source gates, compiled
review and integration. No complete gate, wire/golden repetition, ZIP, release,
device write or worker push was performed.

Runtime commits: `54cf1c739` and the reviewed one-line ownership repair
`e0e2e3dfb`. The parent's Build766 reservation was cherry-picked separately as
`a756cf70a`; integrate the two runtime commits, not that duplicate reservation.

## Evidence and cause

Both immutable supplied logs are Build765 / `1c61941fd`: host SteamVR/OpenXR,
visitor VirtualDesktopXR. They are retained by the parent under
`.planning/validation/build766/hardware765`. No screenshots were supplied.

The host's native viewport spans approximately y=-427..225, while the fitted
1458x878 frame ends at -439. The old dock placed the 60px Ready face at
[-419,-359], wholly inside that viewport. The host fit also alternated between
878 and912 high as its visible-ink union moved by17px. Source inspection shows
that the original Return/Retry row already sits outside the viewport; the extra
Ready dock had been based on the fitted frame instead of that native footer.

The new complete producer test found a second defect hidden by the old fixture's
manual Ready seating. The actual dock owns Ready at child index0. Publishing that
index under the virtual native root, then replaying the Results root's sibling
index, moves Background in front of Results. This produced a white common body.
The retained failure includes actual original-root/mask/Ready pixels. It was
repaired by preserving the detached dock's real after-window draw order.

The input lane independently reproduced a third defect: Unity appends `(Clone)`
to every separately instantiated module root, but scroll metadata uses exact
original paths. Restoring `clone.name = original.name` preserves those paths,
including legitimate original names already containing `(Clone)`. The input
lane owns the complete laser/stick/drag proof for that consumer.

## Implementation

- Derive the Results frame from the original native viewport, header, scrollbar
  and footer geometry, in the original root's coordinates. This includes the
  entire scrollbar and never depends on scrolled ink, private row count, local
  zoom or the previous fitted frame.
- Seat the actual Ready control in the native footer lane, below the viewport.
  If original options and Ready really coexist, use a separate lower Ready row.
  The original Return/Retry faces, native layout, visibility and callbacks remain
  original. No mask/content reparenting, arbitrary depth or hidden mandatory
  control is used.
- Suspend the ordinary fit during this Results phase. Restore only exact owned
  frame/position receipts; preserve successor FitContentRoot/frame/pose and
  manual shared world movement/resize. Release on map claims, missing/inactive
  native scroll, native closure and common-picture takeover. The common owner
  records its restoration receipt after the native footer lease has retired.
- Map detached Ready into the existing snapshot's root-relative pose and append
  its virtual sibling after the native root children. Ordinary native sibling
  values remain unchanged. No codec, wire, actor, readiness, ACK or native phase
  is changed.

## Final affected validation

One Unity2021.3.5f1 / llvmpipe GL editor at a time,
`ADB_SERVER_SOCKET=tcp:localhost:5082`, `ADB_MDNS_AUTO_CONNECT=0`.

| Proof | Actual result | Elapsed |
| --- | --- | --- |
| `results-presentation-runtime/run-b6v0_ek6`, final complete six-case footer scope (`--footer-only`) | **136 assertions +5 compiling causal controls PASS** |52.067s |
| `results-presentation-runtime/run-tai0f2r9`, existing original-picture production path plus intermediate footer scope | **530 assertions PASS** |26.345s |
| `results-window-runtime/run-838cgst2`, existing native permission/ownership production path | **58 assertions PASS** |13.046s |

Qualified unique final coverage is **588 assertions +5 controls**:136 fresh footer
assertions plus394 unchanged original-picture assertions from the530-assertion
consumer and58 native-window assertions. The earlier footer portion of that530
pass is superseded, not counted twice. `inherited-projections.json` records23
exact source/branch projections. The final one-line repair changes only
FooterFrame.Reset's position-ownership guard; the original-window fixture never
acquires that frame lease, and the original-picture prefix is byte-identical.

The final footer scope executes actual production ReadyPark, the complete
original Results/root/material graph, real Unity ScrollRect/Scrollbar/Mask and
source-to-codec-to-inert-picture playback. Win, loss with Retry, and resign with
Return are checked at normalized0/.5/1. Actual content moves at all positions;
the viewport/footer/frame and world pose remain stable. The observer has a
shorter private content height, no private result rows, a different prior fit
and a different canvas/host scale decomposition. All displayed geometry comes
from the author. Native Ready votes once without an invented all-player ACK;
original Return/Retry pointer handlers invoke their registered callbacks.

Actual GraphicRaycaster ordering reaches the common footer metadata surface;
ExecuteHierarchy submits the original operation/opening without a private vote
or copied callback. The containing native-window proof retains disabled,
intercepted, stale/closing, client, spectator and no-character permissions,
pooled-owner retirement and native all-ready continuation.

The five controls reproduce old fitted-footer overlap, detached mount loss,
Background overpaint, a stranded takeover receipt and successor overwrite.
The strengthened same-pose successor case also failed against the preceding
actual production revision (`run-7s61lm2q`) before the final one-line repair.

Nine independent author/observer head-image pairs cover all result/scroll states.
RGBA numerical audit: all win pairs are byte-identical; loss differs in only
three channels per image and resign in two, each by at most one byte. The
existing pixel threshold was unchanged. RGB review derivatives retain the
original PNGs and raw RGBA hashes; they visibly show free footer space below
scrolled rows and the original Return/Retry faces. These are declared GL fixture
outputs, not headset acceptance.

## Retained attempts and limits

| Run | Actual outcome |
| --- | --- |
| `ojpkqn9_`,8.812s |Fixture compile failure: added boundary omitted qualified UIWindow name. |
| `5p155bd8`,9.055s |Fixture compile failure: Rect helper shadowed the Unity Rect type. |
| `yr0ckxqp`,28.629s |Full native picture exposed detached Ready sibling/background overpaint; initial fitted-control port did not yet use measured native root y=-73. |
| `rzbl7ino`,25.998s |Old fitted overlap control now failed correctly; production still exposed Background overpaint. |
| `xy0uh4u3`,12.611s |Mutation preparation refused an ambiguous repeated source token. No compiling control was claimed. |
| `eo7grohq`,33.875s |All nine geometry/pixel states passed; native Return assertion exposed the fixture's serialized closed root CanvasGroup permission. Explicit native-open bootstrap port repaired it. |
| `oia0t268`,32.707s |Fixture modeled two players in one process: observer Tick reset the author singleton before the expected source rect was saved. Capture the source rect before switching roles. |
| `s0szq6la`,47.433s |Intermediate136+5 PASS, before the strengthened successor ownership review. |
| `7s61lm2q`,24.687s |Deliberate actual-production reproduction: same-pose successor FitContentRoot was overwritten. Repaired and rerun in final scope. |

All twelve measured invocations total **315.265s**, including failures and
intermediate/duplicated passes. Timings come from each run's completion-log and
result timestamps. No failed invocation is relabeled as a complete pass.

Native bootstrap/controllers, scenario-filled values, option visibility by
outcome, animation clocks, all-player ACKs and Photon remain explicit ports.
The original static assets/fonts/material identities execute; their fragment
programs use the existing declared GL ports. The Ready material's placeholder
appearance and unplayed native header animation cannot establish Windows-D3D11
or HMD readability. No new paired device/headset/Photon acceptance is claimed.
The parent owns unchanged765/764 checkpoint qualification and final union checks.

Every test reaped its processes before canonical `cleanup_completed_run`.
Receipts preserve failures, sources, manifests, captures and original inputs;
own generated Unity Library/bin/obj caches were removed. A separate read-only
native export audit also has a completion receipt. Hash-verified archive and
final bindings are retained in
`.planning/validation/build766/footer-lane` before worker retirement. No shared
references, another worker's active run or baseline symlink was modified.
