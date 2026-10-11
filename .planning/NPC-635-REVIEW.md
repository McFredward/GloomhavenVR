# Build635: original enchantress overlay geometry and lifecycle

The supplied Build634 singleplayer screenshot shows a diagonal, narrow aura
across the offered Berserker card, with missing or distorted enhancement areas.
The logs contain original selectable-area counts3/2/2/3/1: native enhancement
eligibility recovered in634, but those counts did not establish a correct picture
or a reachable button. Remote logs still identify Build629; they cannot validate
this634 failure or the635 repair. Immutable inputs and hashes are retained under
`.planning/debug/npc635/inputs/`.

## Source causes and repair

- Quiet preparation intentionally avoids `UINewEnhancementWindow.EnterShop`, but
  that method starts the original `UIEnchantressEffect`. Start that exact effect
  only while its actual original highlighted card is offered, and stop it on
  removal/release. Original LoopAnimator/LeanTween rotation and alpha pulse own
  the animation; hidden prewarming runs no effect loop or flat service window.
- The game's highlight pool uses `SetParent(target)` with world-position retention.
  Its `Highlight` then changes only pivot, dimensions and position. Reusing that
  flat-world basis below a small rotated VR print distorts or reverses original
  buttons. Restore the serialized template's local identity rotation/scale on
  the actual pooled buttons, retaining native size, state, hover and callbacks.
- Native card pooling resets scale/centre but does not reset local rotation.
  The holder correctly compensates a retained print yaw, but Aura and GUI_Frame
  are siblings of that print. Derive their plane and centre from the actual
  physical paper rather than the holder's assumed XY plane. Fit the holder's
  third axis consistently with its other axes.
- The principal-axis aura fit previously kept the circle round by discarding its
  texture rotation phase. Carry the original world-Z clock into the original
  drawing transforms after compensating the parent. Capture it before fitting
  changes its parent's basis. No replacement ring or hierarchy is introduced.
- Holder fitting already applies physical size changes. Correct only the change
  of the physical-to-holder ratio rather than scaling the circle twice.
- Restore the original frame's full anchored XYZ position, aura position and
  original drawing transforms when releasing the offering.

The shutdown log also contains a destroyed native `CanvasGroup` during
`UIEnhancementCardHighlighter.Hide`. Guard the exact serialized holder/group only
after destruction; live native selection/callback failures still propagate.

## Original-content/input and 1:1 review

Older fixtures created enhancement areas in an already-correct local basis and
did not execute the omitted native effect lifecycle. Equal owner/observer
geometry could therefore compare two equally wrong pictures. This round imports
the shipped11-node highlighter, original effect sprites/layout and pooled area,
executes the real native effect/tween, reproduces world-preserving pool writes,
and checks geometry against the physical print independently of remote equality.

Source publication and observer playback retain the existing original-prefab
records and offered-print affinity. No network production code, wire layout,
disclosure rule, configuration or bundle changes. Actual offered cards, original
areas, hover/selection and intermediate ring motion remain shared. Visitor-local
pre-drop guides keep the maintainer's existing exception. Observer clones remain
inert; they never run enhancement gameplay callbacks.

The source-to-observer proof covers retained pooled print yaws90/0/-43,
yaw/tilt/bob/scale,140 interpolated subframes, delayed independent original
headers, hover/selection opacity and inert callbacks. Maximum area-corner error
is0.677µm; minimum native ring diameter is1.2013 times physical card height.
Owner and observer renders were inspected and match. The native callback proof
executes the original ExtendedButton/area selection in the same mouse input mode
reported by hardware, not an invented replacement enhancement picker.

## Validation and limits

Receipts, source bindings, failures, causal controls and renders live under
`.planning/debug/npc635/proofs/`. Focused evidence:

| Check | Result |
| --- | --- |
| Actual native effect lifecycle and input callback |111 assertions,3 causal controls |
| Quiet-controller lifecycle |171 assertions,2 affected controls |
| Integrated final owned-card handoff |1515 assertions; prior unchanged geometry controls retained |
| Original native GL geometry/clock/raycast |328 assertions; two unchanged controls inherited from326 run |
| Original source→codec→observer overlay |1081 assertions,5 controls,140 subframes |
| Source gates |14/14 PASS |
| Golden wire vectors |296631 assertions PASS |
| Strict Release and Debug builds |0 errors,0 warnings |
| Bilingual player documentation |5 pairs PASS |
| Compiled634→635 scope |1227 types unchanged in count;4 intended runtime changes,8 build constants only |

The final mask SHA is
`4ddc3fbe719698613e932a9669ed0dcecce8cda3a36bf4a7f164564545debef2`.
The observer proof bound the immediately preceding geometry SHA799e8c91…;
its final delta only restores frame depth with `anchoredPosition3D` and has two
additional targeted restore checks. Already successful geometry/control runs
were not repeated for that bounded change. The final integrated handoff binds
all four changed runtime sources. There is no new complete146-suite pass claimed;
unchanged subsystem evidence is inherited from the completed632 gate and634
follow-up, following the maintainer's focused-test instruction.

These tests do not boot an entire campaign/payment/navigation stack or two real
networked game processes. The GL rendering uses a diagnostic paper rectangle,
an equivalent editor UI shader and a Button adapter; original sprites/layout
and UIEnchantressEffect/LoopAnimator/LeanTween are real. Actual original callback
behavior is covered separately. Passing geometry and renders do not certify
headset pixels, network latency or hardware acceptance.

Both peers must install635. Test card offers with several eligible areas,
laser hover/selection, cancel/reoffer, and head-relative rotation; verify a
complete continuously spinning circle and exact areas locally and remotely.

## Subsequent636 integration check

The four production repair types remain unchanged after the independent material
lane. A targeted legacy offered-orientation fixture exposed a scheduling error
in its final still-image comparison: it rendered120ms after a synthetic sample,
while that sample's pure rotation used143ms of interpolation. The exact received
target already matched the owner. At the interpolation endpoint the unchanged
pixel threshold passes, with ring-corner error at most0.241µm. The fixture-only
repair adds settled-ring checks and an omitted-settle causal control; previous
intermediate geometry checks and the original635140-subframe proof remain.
The affected fixture passes602 assertions; the final control uses the original
forward120ms boundary. Its first backwards-clock variant is preserved and is not
used as the final causal evidence.
This does not claim lower hardware latency or replace a paired headset test.
