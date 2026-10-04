# Wrist-mounted control board

The optional wrist mode moves the existing control board; it does not build a
second board or change the original cards, slots, piles, decision panels or docks.
The user requested this third anchoring mode on 2026-10-04. It is selected only in
VR Options, under **Board & cards → Control board**. The default remains off.

## Configuration and placement

All keys live in `[Cards]` in the existing cards config:

| Key | Default | Meaning |
| --- | --- | --- |
| `WristBoardEnabled` | `false` | Use the selected wrist instead of follow/fixed placement. |
| `WristBoardHand` | `NonMain` | `NonMain`, `Left` or `Right`; the non-main side follows the selected main controller. |
| `WristBoardOffsetMeters` | `(0, -0.19, -0.075)` | Three-axis real-metre offset in the actual `HandRig.Wrist` frame. |
| `WristBoardAnglesDegrees` | `(0, 0, 90)` | Three-axis rotation in that wrist frame. |
| `WristBoardScale` | `0.5` | Uniform multiplier on the existing board/style size, bounded to 0.2–1.5. |

Hand, offsets, angles and size are in Advanced. Their existing config-row builders
provide translated choices and ordinary precise steppers. The saved `TrayFollow`
key, its default and its ordinary button behavior remain unchanged. Wrist mode
remembers the normal seat and size; disabling it returns there in the
same short transition used by the grab bar and restores the remembered follow or
fixed mode. Ordinary size edits while attached update the remembered normal
size by the same config-scale ratio without recording wrist position. Selecting wrist mode before the first board exists falls back to the
original head placement when it is later disabled.

`HandRig.Wrist` is a compensated socket. In the authored hand prefabs its +Y points
along the fingers and +Z points out of the palm. The initial board extends back
along the forearm and faces out from the back of the hand. These are configurable
starting values; headset comfort and readability still need a hardware check.

## One root and one pose owner

`PlayTray.Wrist.cs` takes the live wrist as a **pose source**, while keeping the
same board root under its original rig-space parent. Parenting to a hand-art root
would multiply the board by glove visual scale and destroy its dock hierarchy
when hands are rebuilt. The root reads the current wrist in ordinary placement,
its existing late component and `Application.onBeforeRender`. Only entering,
leaving or changing the wrist source uses `GrabBarTween.DurationSeconds`; settled tracking has no
additional smoothing or sample clock.

Tracking loss holds the last valid rig-local pose until the chosen hand returns.
Main-controller changes, hand-style rebuilds, snap turns, rig scaling and recenter
keep the same owner root and original docks. The remembered seat has no renderer, collider or gameplay controller and stays
under a separate durable world holder. Its saved rig frame carries FOLLOW through
actual hand or rig teardown; FIXED uses the unchanged `FollowPinAnchor.TickCarry`
policy, preserving world pose and size across ordinary locomotion/zoom. Board-style
rebuilds retain this remembered seat. A rebuild during mode exit retains the return intent
and resumes its short transition from the captured intermediate pose. The ordinary lost-board watchdog, pin housekeeping and
head orientation writes yield while wrist placement owns the root. Wrist movement
never rewrites saved normal placement values. Retry returns the player through its
usual rig path; the board remains attached to the current tracked wrist.

The entire follow/fixed cap, engraving and grab handle are hidden while attached
and during its return. Their colliders are inactive, so an invisible handle cannot
compete with tracked anchoring. The settings gear, playable card slots and other
original controls remain on the same board root.

## Multiplayer parity

The existing owner-authored fast rig record **70** carries the actual final board
world position, rotation and uniform scale. Receivers reuse their existing board
pose interpolation. Wrist-specific offsets, hand choice and scale are never
recomputed from the observer's settings or wrist. All mounted content follows
that same board root.

Additive wire-v3 record **100** carries the owner's hidden-control state in the
same fast rig packet: `100, 1, 1`. Its absence clears that state on the next explicit
board sample. Ordinary-mode packets retain their original byte prefix and length.
Receivers hide or restore the full original remote cap, engraving and handle from
this owner state, including a mode return or board rebuild. Delayed presence
snapshots cannot overwrite the faster owner board pose/state. Malformed,
duplicated, truncated or out-of-domain records are rejected.

## Verification and limits

`check-wrist-board-runtime.py` compiles the complete current production DLL into a
private output and runs its actual placement, lifecycle, real menu-row/step and
remote-furniture code in Unity 2021.3.5. Original authored hand and board prefab/model and
texture assets are imported unchanged and rebundled for the editor platform.
Tracked XR samples are the explicit external boundary. Source and asset hashes,
assertions, causal controls and rendered evidence are retained per run.

This checks root motion, scale independence, original dock geometry, left/right/
non-main selection, controller-only lessons, style changes, tracking loss,
recenter/scale, normal-mode restoration and owner-derived remote visibility. It
also exercises the original codec and its golden vectors. Neither an editor
render nor a green check establishes headset comfort, occlusion under every hand
pose or real network latency. Those remain hardware validation.
