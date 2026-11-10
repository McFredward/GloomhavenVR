# MP770 shared Results scrolling

## Scope and source finding

Class B: bounded observer presentation fix. The host receives and executes guest
scroll input promptly. The observer coordinator currently applies the small,
ordered scroll state only to its hidden native `ScrollRect`/`Scrollbar`. The
visible shared Results picture is inert and has no such components. It changes
only when the larger Results graph updates; its transform writer can also
replace a more recent small scroll state. This explains a guest seeing its own
scroll late without a slow request path. The exact hardware delay remains the
maintainer's observation, not a measured fixture claim.

Project accepted author state onto the inert picture's exact content/handle
paths each frame and immediately after a graph paint. Derive travel from the
common picture's bounds, not the different guest-native list. Do not predict
input, add clone gameplay callbacks, change the wire protocol, or bypass author
ordering. Retain genuine native permission and lifecycle checks.

## Validation plan

Reuse qualified Build769 proof for unchanged code. Run the focused shared-window
scroll runtime fixture with actual original Results construction, neutralization,
Unity UI, pointer/joystick callbacks and three coordinator mailboxes. Add visible
picture checks for host and guest input, stale graph overwrites, concurrent input,
scrollbar handle projection, and a compiling causal control restoring the missing
projection. Strict build and final source gates are performed on the integrated
union by the parent. No complete suite, archive or headset/device changes.

All fixture attempts use private ADB port5085 and canonical completed-run cleanup.

## Implementation

`SharedWindowScrollPicture` maps only native identity paths into the already
validated common picture. Each accepted normalized state moves its inert content
using that picture's current viewport/content bounds and pivot. Each coordinator
Update/LateUpdate projects it; `AttachResultsInputs` projects again before its
unchanged-descriptor fast path, after the existing Results graph writer.
Linked handle value uses the same accepted ScrollRect state, preserving author
handle size and direction. A standalone handle uses its own accepted state.
No new native controller, Selectable, callback, wire field, local prediction,
request authority or input timing was introduced.

## Executed evidence

Final focused invocation: `run-16a5btgr`, 20.38 seconds, source pins stable:

- Production:184 assertions, including visible inert content and rendered pixel
  changes after guest own wheel input, own-visible joystick/scrollbar response,
  host input, private-height differences, accepted source echo, stale graph
  overwrites, concurrent opposite input and stale/foreign packet rejection.
- Three compiling counterfactuals fail at the exact intended assertion: missing
  visible small-state projection, stale whole-graph overwrite after unchanged
  descriptors, and linked handle/content split across native LateUpdate.
- Native Unity differential checks cover both axes, undersized content/pivots,
  and every scrollbar axis/reverse direction. Inert copies retain no ScrollRect
  or Scrollbar controllers.

The first attempt (`run-mnm9zmcu`,26.20s) failed the new linked-handle check because
capturing immediately after input can precede Unity's native visual bar update.
The content/pixel checks and missing-projection control already passed. The
bounded linked-state repair passed `run-bmovi69w` (18.32s); added native geometry
checks passed `run-oz82ih2k` (14.62s). The final four-case run supersedes these.
Total targeted runtime validation elapsed79.52s across four attempts. Every run
was reaped; canonical cleanup removed its compiler/build and Unity Library
caches. Evidence and failed attempts are retained under the parent checkout's
`.planning/validation/build770/scroll-lane/`.

Parent integration performs the strict build and all source gates once on the
final union; unchanged Build769 complete checkpoint is reused. This fixture runs
real Unity UI, actual Results clone Build/Prune/RootPose and binding application,
production pointer/joystick/coordinator/projection code, and rendered pixels.
Transport is a three-coordinator mailbox, not Photon or headset hardware. The
maintainer must verify perceived near-instant HMD behavior;184 fixture assertions
do not establish a measured Internet roundtrip or hardware latency guarantee.
