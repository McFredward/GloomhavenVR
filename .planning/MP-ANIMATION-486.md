# Multiplayer native bonus animation — ModBuild 486

## Requirement and cause

The user rejected the remaining intermediate-animation gap in 484: the original remote bonus
widgets must also show the owner's native movement and scale throughout their reveal. Applying
the native final pose and the owner's slot alpha alone did not satisfy that requirement.

The native `UIUseActiveBonus.showAnimation` exposes its authored `LeanTweenGUIAnimator` settings
at runtime. Their target objects and setting order can be inspected without serialized assets
in this checkout and without invoking native controllers on a remote board. The mirror's normal
refresh also overwrites target transforms and graphic state, so a received pose must be applied
last, after cloning, fitting and painting.

## Implementation

- Sample original target values in `UseBarsSurface.LateTick`, after native LeanTween Update and
  local dock placement. Cache verified bindings and publish immutable snapshots only on changes.
- Bind each slot by replicated actor ID, its existing record-45 identity and the original show
  setting ordinal. Resolve targets on the original native prefab, then map them into its visible
  clone. A delayed frame cannot move a replacement character's bonus widget.
- Preserve native coordinate semantics: MOVE uses `anchoredPosition3D`, MOVE_LOCAL uses
  `localPosition`, scale uses `localScale`, size uses `sizeDelta`, and UV movement preserves the
  original UV rectangle's size. No board or VR scale is applied a second time.
- Send additive TLV 49 in dedicated message 3. Message 4 wraps its bounded fragments using the
  existing record-48 envelope shape. Presence and record 47 remain unchanged. Each frame carries
  the owner's sample time and complete slot/entry counts; malformed or partial frames are inert.
- Keep the first waiting animation frame and its latest successor. Independent reassembly
  generations prevent animation packets from invalidating a pending board snapshot. Both
  cosmetic streams and the legacy handshake share one event per 50 ms through a weighted
  scheduler. Small independent pages coalesce within the existing event cap; slow frames never
  produce catch-up bursts. The follow-up review added the other native streams and explicit
  clear/reopen boundary retention; see [MP-PARITY-486.md](MP-PARITY-486.md).
- Retain bounded received history while board structure is arriving. Replay actual sample times
  at normal speed and interpolate between received values, holding the last value when data
  runs out. Clone rebuilds retain the playback clock. Final-frame repetition recovers packet loss
  without restarting the animation.

The maximum animation message is 2,582 bytes within its 3,072-byte buffer; each transport event
remains at most 864 bytes. No game data, configuration keys or asset bundle changes are required.

## Verification

Integrated gate results are recorded in `STATE.md`. Hardware appearance and network timing require a multiplayer
headset test; automated results cannot establish visual parity on their own. The receive timeout
investigated in 484 remains a separate unresolved underlying cause.
