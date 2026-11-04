# Deadlock review758: windows, prompts and presentation recovery

Worker base: `cd827d688` / Build757. The integrator classifies the combined
mod-wide work as C and owns final strict/source/full-gate validation and delivery.
This lane changes presentation ownership/recovery only. No actor, game phase,
native option, network acknowledgment, transport or save is synthesized.

## Scope and inventory

The ownership boundary covers WorldUI except MapRoom, TownServices and the
Map/Quest/Loadout/Ready composite prefixes assigned to the map reviewer.
The original scope contains294 C# files,182089 source lines. The review inventory
records every original path, SHA256 and its continuation/input/lifecycle entry
points under ignored `debug/deadlock758-windows/review-inventory.json`.
This inventory is a source-review map, not line coverage or proof of every
possible native/hardware state.

| Domain | Files | Progression/reachability seams reviewed |
|---|---:|---|
| WorldUI root |24| Driver tick/shutdown isolation, loading cover, actor/tooltip presentation versus action authority |
| Buttons |6| Press/release, disabled state, native visual callbacks and stale input |
| Composites |16| Native producer identity, pooled/reentrant confirmations, original home restoration, level-up/card reveal and story ownership |
| Conversion |33| Transactional conversion rollback, native subtree persistence, render/input hiding, reveal and complete restoration |
| FlatScreen |24| Manual/rescue takeover, map fallback, native pointer forwarding, movie lifecycle and return from screen presentation |
| Grab |13| Menu-close admission, one-shot placement, carry release, keyboard focus/native listener restoration and input-mode guard |
| Materialise |5| Finish/cancel/disable/destroy exits, pointer blindness and exactly-once release callbacks |
| MenuDepth |25| Native graphic/actor input mapping, source lifetime, initial and returned captures; rendering does not answer a decision |
| Modal |59| Required-choice classification, float/screen fallback, denied/dormant recovery, native close/Continue paths and shared placement boundaries |
| Options |26| Always-reopenable VR options, current menu identity, consumed input, gated actions and page lifecycle |
| Patches |20| Native dead-input guards, ESC exceptions, tutorial-compatible pointer permissions and cloned content isolation |
| Sharpness |13| Capture failure/direct-render fallback, owned-member lifetime, release and bounded capacity |
| Surfaces |26| Damage/card pick/choice docks, rewards, mandatory plain-GameObject popups and native confirmation continuation |
| Tooltips |4| Nonblocking hover lifetime, source/owner restoration and popup pointer reachability |

The source review follows all window continuation owners rather than assuming a
visible/selected initiative portrait proves an active input wait. It reads the
native UI methods where they supply the permission, callback, show/hide or promise.
The reported issue27 transition is still not established by these separate fixes.

## W1: successful conversion with a permanently missing initial dialog pose

`DialogSurface.ConvertNow` previously attempted placement only inside its one
conversion call. `CanvasConversion.Convert` explicitly creates an unpositioned
identity host. Its `WorldCamera` can be null while `ConversionActive` remains true:
that gate reads VRSession, not camera readiness, and the world camera resolves
the current rig camera or Camera.main. A confirmation encountered during camera
handoff could retain a successful conversion with no placement. Subsequent ticks
saw the cached panel and never tried the opening pose again. A native pending
confirmation could therefore remain at the converter origin instead of becoming
reachable when the head became available.

The surface now distinguishes converted from initially placed. Only the first
pose of an unplaced opening is retried when a camera becomes available. It retains
the same original native request, one conversion,0.75m placement formula and yaw.
After that first placement, head movement and manual movement do not re-place it.
Native close/reopen, scene replacement, owner containment, manual screen and
conversion-failure fallback keep their existing authority and lifetimes.

Portable coverage executes the complete production surface and owner callers:
388 assertions and31 compiling controls pass. Two new controls restore missing
late-camera retry and repeated placement; both fail their intended assertions.

The actual Unity/native-tree proof executes the production DialogSurface against
read-only imported original confirmation layout, images, font/SDF and real uGUI
raycasts. It passes21 assertions and two compiling causal controls. Final eye
pixels contain the original confirmation text and Yes/No graphics; both option
graphics are hit-testable. There is no synthetic option click during recovery.
An explicit pointer still dispatches once. Changed head pose and a manually moved
panel remain fixed; a new pooled opening gets its own delayed initial pose.

The existing fixture's PlaceHost service port omitted the production mm-per-pixel
scale. This review binds the actual unchanged production PlaceHost body into that
fixture; geometry is no longer certified through the inaccurate scale port. The
existing converter allocation and live-camera rebinding remain explicit ports.
This proves the conditional defect, not that a missing head caused issue27, and
does not certify paired Photon/HMD/D3D11 behavior.

## W2: one permanently empty window starves another window's recovery

`EmptyRefusedNow` and `EmptyHeldNow` formerly each used one global6Hz deadline
for every native window in their respective set. With stable enrollment order,
the first still-empty open window consumed every due probe. A later window whose
native content returned could be skipped indefinitely and remain absent in VR.
The fallback was intended to reconsider that same native opening; closing the
first window was not a valid precondition for recovering the second one.

Each set now uses WindowProbeCadence with a weak deadline per original UIWindow.
Every window keeps its own6Hz bound, and neither a sibling nor the other recovery
set consumes its opportunity. Native closure/content return forget that exact
deadline; hierarchy revival and module reset retire their corresponding state.
Weak keys do not retain destroyed native originals. Clock restart also permits
a fresh probe. The existing content test, suppression/float policy and original
window state are unchanged. No new automatic close, answer, relocated window or
unbounded per-frame hierarchy scan is introduced.

The complete existing modal-close suite now compiles both actual production
readmission methods plus the actual cadence helper. Its source-bound fixture
provides explicit native-window and drawn-content/time inputs; it does not
pretend to execute Unity's graphic/cull measurements.188 assertions and ten
compiling controls pass in33.64s. Stable first-empty/second-populated sequences
reproduce both original starvation branches. The two legacy global-clock controls
fail at the exact missed-recovery assertions. Other checks retain6Hz for repeated
callers, closed/reopened pooled originals, clock reset and no native action writes.
All previous mandatory-close, shared quest/city and navigation controls remain.

## Existing safety contracts retained

| Situation | Existing safe route retained by the review |
|---|---|
| Required story, event, damage, reveal or confirmation | Original choice/Continue; stale X rechecks live identity and cannot hide a new waiter |
| Unknown interactive native window | Catch-all admission; interactive or mandatory continuations are exempt from the HUD churn fuse |
| Failed/empty blocking conversion | Original desktop presentation through the existing fail-open route; no forced continuation |
| Decision row cannot convert or measure | Claim returns to generic native window fallback; explicit original options remain |
| Plain GameObject distribution/doom picker | Dedicated original widget surface; native ready/undo/authority remains responsible for commit |
| Manual native-screen rescue | Presentation hand-back restores original subtree; no Hide/Cancel/ACK is invented |
| Reward or level-up Continue predates VR in gamepad mode | Existing native pointer/tracker binding and permission checks; independent ESC cannot veto an otherwise allowed pointer |
| Pooled confirmation changes producer/page/actor | Original request identity/navigation generation, exact No/outcome and original home restoration |
| Show during old Hide callback | Existing newer-opening-only native Show recovery after the old hide tail |
| Effect interrupted by hide/destroy/reopen | Existing finish/cancel ownership and release callbacks; no clickable ghost |
| Ordinary relocated/grabbed window | Fixed accepted pose; only the already authorized out-of-playfield story recovery can recall a critical story |
| Ambiguous/stalled native multiplayer barrier | Root/network review owns diagnostics; no counterfeit client-ready or phase skip is added here |

These routes do not guarantee that an unobserved game exception or lost network
participant can always recover automatically. Unknown mandatory authority stays
with the original game; describing an unresolved wait is safer than guessing an
answer. Parent player guidance may explain the existing manual screen/recovery
controls and who must act, without private card disclosure or blame.

## Evidence, failures and cleanup

- Portable confirmation: `debug/dialog-surface/run-yc_xevpw`,388/31.
- Initial native fixture: `debug/deadlock758-windows/native/run-dj8o682e` failed
  strict compilation on a fixture-only nullable assertion; retained as a failure.
- Repaired native proof: `debug/deadlock758-windows/native/run-x1p0g_01`,21/2;
  final pixels inspected. The editor case loop took1.616s after setup/import;
  this is not total invocation time or a hardware frame-rate measurement.
- Modal recovery: `debug/modal-close/run-d8hw80t4`,188/10,33.64s.

Every executed run used canonical completion cleanup after reaping processes.
Generated project caches were removed; source bindings, original failures,
executed artifacts, capture and result logs remain. The private ADB endpoint5058
was task-owned and is no longer listening. No main/shared reference or another
agent's live run was removed. Parent owns verified final archival/worktree
retirement and the integrated complete gate; no worker push, release or ZIP.
