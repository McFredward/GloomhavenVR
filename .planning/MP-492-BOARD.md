# ModBuild 492: brief remote-board disappearance during long rest

## Finding

The supplied build-491 logs identify both long-rest burn windows, but do not
establish why an entire remote board briefly disappeared. No source-proven
board-visibility defect was found in this review, so this lane changes no runtime
behavior. In particular, it does not disable the existing peer-board fade or
invent a burn-related visibility exception.

Both `.planning/debug/LogOutput.log` and `remote/LogOutput.log` identify ModBuild
491 at line 17. The older regression screenshots belong to the preceding report;
none provides the timing or rendered frame for this incident.

## Correlated rest windows

There is one completed long rest per character in this session. Each observer's
window is matched to the owner's window by the named long-rest banner and the
same card's burn flight, not by equal line numbers or equal local clocks.

| Character | Owner evidence in LogOutput.log | Observer evidence in LogOutput.log | Chosen loss |
| --- | --- | --- | --- |
| Testo, host | host 51842: turn arrived; 51898: long-rest loss prompt; 54750: resolved; 54809: burn flight | remote 29260: same prompt received; 30506–30507: mirrored burn flight | GnawingHorde |
| Testi, other player | remote 32788: turn arrived; 32838: long-rest loss prompt; 33549: resolved; 33623: burn flight | host 58856: same prompt received; 60317–60318: mirrored burn flight | GrabandGo |

The matching `Player.log` ranges, located with identical unique prompt/flight
lines, are host 283570–301375 and remote 253059–269461 for Testo; host
328127–336866 and remote 294014–302021 for Testi. Native `PlayerLongRested` messages
also appear for both rests: host 300916/336309 and remote 268940/301520.

The strongest candidate window for the user's observation is therefore the
host's Testi window, 58856–60318. The log does not identify a specific missing
rendered frame within it. The reverse viewpoint was reviewed as well.

## What the logs establish

- Both viewers applied `RemoteBoards = Always` once: host 9515, remote 965.
  The action-only visibility option cannot explain a phase-related disappearance
  on these settings.
- Each remote board was built once: host 13077 uses the owner's Steel asset;
  remote 4199 uses the owner's Bronze asset. No style, tuning or slot-size rebuild
  was logged during either rest.
- The scenario gate opens at scenario entry and has no closing edge during the
  rests. Each avatar is destroyed only at session exit, host 95116 and remote
  56291. There is no corresponding destroy/recreate sequence at the burn.
- No see-through ON/OFF or delivery transition occurs during either long-rest
  window. On the host, the early ON/OFF pair is 15378/15408 and the next ON is
  66114, after the GrabandGo flight. On the other player, the first ON is 39623,
  after both long rests. No cull fail-safe transition is logged in those windows.
- The burn mirror receives an owner release for both cards. Host 60318 records
  GrabandGo held for 2.17 seconds, seated for 155 frames with no stationary
  fallback interval. Remote 30507 records GnawingHorde held for 1.97 seconds,
  seated for 22 frames plus 1.27 seconds on a stationary fallback slab. These
  readings concern the individual card; they do not measure the board root.
- Host 60135 records a 90.37 ms frame before the GrabandGo release: 54.23 ms in
  Net.Avatar, including 48.01 ms in Net.Fans and 5.83 ms in Net.Board. This is
  evidence of a stall in the candidate window. It does not demonstrate that the
  board stopped rendering or that the compositor hid it.
- The exact `Player.log` windows contain no Unity exception, D3D/device-loss or
  renderer creation/presentation error found by the targeted scan. They contain
  recurring Hydra `GetEnvironmentInfo` DNS failures: host 293356/330947, remote
  256009/266123/301090. Nothing in those messages connects them to board geometry
  or its visibility, so they are not assigned as this incident's cause.

## Source review

`RemoteControlBoard.Tick` deactivates the whole root only for a missing received
board, the Off mode, or a closed scenario/action-only gate. Actor focus resolves
the displayed content and cannot independently hide the frame. The exhausted
actor rule explicitly retains the board surface. Style, tuning and slot-width
changes can rebuild it, with the diagnostic edges checked above.

`NetAvatarDriver` samples `PlayTray.Current.Root` and includes the board pose
whenever that transform exists; long-rest selection and card-burning state are
not terms in that presence decision. `RemoteAvatar.SetExtras` does accept a
packet's absent board flag immediately. Neither that flag's per-packet history
nor `RemoteControlBoard.SetActive` transitions are logged in this capture, so a
brief absent-board state cannot be ruled out merely because there is no rebuild.
No source path specific to long rest was found that would create such a packet.

`RemoteBurnFx` owns a separate `GloomhavenVR.RemoteBurnFx[player]` root. Its hide
and destruction operations target its own card objects/root. The board callback
`SuppressBurnRecess` blanks one `RemoteBoardCard` and its seat masks, not the board
root. Native card appearance is applied inside card presentation roots, while
`RemoteTrayVisual` instantiates the board prefab separately. This review found no
burn-material write or burn teardown that targets the board's mesh.

Peer-board fading is driven by the viewer's occlusion calculation, with registered
burn/card roots following the existing board fade. Its recorded state did not
change in these windows. The draw-order cluster can change membership when cards
appear/disappear; the log reports cluster composition rather than the final
per-eye visibility of every renderer. A normal composition update is not proof
of a rendering defect.

## Remaining evidence gap

The capture has no frame-by-frame record of board-root active state, received
HasBoard transitions, renderer visibility in each eye, final material alpha,
clip/cull decisions and the exact moment observed by the user. Consequently it
cannot distinguish a transient root gate from a rendering/order/compositor
artifact. A content census sampled before or after the event cannot close that
gap.

## Added transition diagnostic

After the initial read-only report, the integration review requested a narrow
measurement for the next reproduction. `RemoteControlBoard` now emits
`REMOTE BOARD VISIBILITY` only when its root is created, actually changes
`activeSelf`, or is submitted for Unity's deferred destruction. Stable visible
and stable hidden frames emit nothing. Existing gate and rebuild call sites pass
an explicit reason; their decisions and lifecycle operations are unchanged.

Each edge includes player, frame/time, root instance ID, before/after activeSelf,
actual activeInHierarchy, received HasBoard, scenario gate, visibility mode,
synced focus actor ID, root CanvasGroup alpha, pose and scale. The focus ID is
labelled as the transmitted focus, not a newly resolved fallback actor. Creation
is labelled as preceding content/pose initialization; destruction is labelled as
a request, not evidence that a rendered pixel has already disappeared. There is
no added actor-model lookup, renderer enumeration, wire record or per-frame log.
Native material changes and compositor outcomes remain outside this instrument.

If the event recurs, an ACTIVE_CHANGED-to-false or DESTROY_REQUESTED line can now
attribute an actual board-root transition. Absence of such an edge narrows the
search toward rendering/order/materials and does not prove the picture was fine.
No speculative visibility/fade behavior change is shipped as a fix.

Validation: both clients' LogOutput/Player logs and source paths inspected;
`git diff --check` passed; strict Release build passed with 0 errors and 0 warnings;
existing wire suite passed 251752 assertions. No tests were added for this
observation-only change. No headset rendering outcome is claimed.
