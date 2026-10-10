# Ordinary wrist-board rotation controls — 2026-10-10

The normal board category exposed wrist position, while the existing
`Cards/WristBoardAnglesDegrees` entry was only reachable through Advanced.
Expose its three components immediately after the three position sliders.
Reuse the production bar/arrow kit: each axis spans −180° to +180°, with the
existing degree-based one-degree grid and fine arrows. Hide these rows with the
existing wrist-mode dependency. Use the same short EN/DE caption in both menus.

No configuration key, default, placement routine or wire layout changes.
Opening/rebuilding the menu preserves saved angles, including equivalent turns
outside the gesture range; changing one component preserves the other two.
The original board root already composes wrist rotation with all three angles.
Remote observers receive this resulting owner-authored orientation in the
existing fast board-pose record rather than applying their own calibration.

Focused validation: the actual Unity `wrist-board-runtime` suite passes
324 assertions and three existing causal controls against a private complete
current-source production DLL and unchanged authored board/hand assets.
The added cases exercise actual curated toggle/rebuild, all three slider and
fine-arrow callbacks, retained/out-of-range calibration, actual LateUpdate
placement and the original owner-pose serializer/remote pose state. Options
coverage passes with 90 matching ordinary/Advanced caption pairs. The private
Debug build has zero warnings and errors. No complete gate was rerun: unchanged
areas retain the prior build661 evidence recorded in STATE.md.

Tracked XR samples and harvested UI donor shapes are explicit fixture boundaries.
These are source/runtime checks, not a headset comfort, appearance or latency
claim. Compact logs, source hashes, fixture and results are retained under the
main checkout's gitignored `.planning/debug/wrist-rotation-20261010/`; generated
Unity projects and both task worktrees are removed after integration.
