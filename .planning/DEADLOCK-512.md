# General continuation audit — ModBuild 512

## Evidence and scope

The user requested a general audit after the build-510 chest softlock and build-511 fix.
Integration starts at dev 6783719c. The latest supplied maintainer log still identifies
build 510; retained remote logs identify 500. There is no build-512 hardware evidence.
This review follows native input to the action/callback that actually releases the waiter;
a hidden window, an accepted click or a green callback stub alone is not completion.

The audit covers mod-induced progression stalls and mandatory input availability. It does
not claim to prove freedom from all native engine, driver or network failures. Native game
rules, ownership, reward selection and readiness remain authoritative. Recovery never
answers a decision on a timer or clears a gameplay lock to make the game appear unstuck.

## Findings

### Map event rewards bypass the scenario reward controller

UIEventPanel starts UIRewardsManager or CampaignRewardsManager directly on the map.
Build 511 resolved the VR Continue adapter only through ScenarioRewardManager.IsShown,
so these native reward windows could float without a working confirmation adapter.
The correction resolves the live original reward window independently of the scenario
controller while retaining the active scenario's manager priority and native authority.
Map event completion must reach the original following reward/distribution callback.

### A failed map modal conversion requested a screen the map refused

ModalFallback records a failed blocking conversion and publishes ScreenWanted, but
FlatScreen.WantVisible rejected every ordinary map-room screen before consulting it.
A failed conversion also supplies no floated modal for the escape chord to reach.
The explicit map fallback request now participates in both desktop visibility and the
existing ManualScreenActive takeover flag, restoring converted widgets to their original
screen. The request ends with native modal closure; loading and native movie gates remain.
It also covers an explicitly configured screen-style blocking modal on the map.

### Travel parking failure removed both confirmation paths

MapTravelConfirm stopped parking its native travel control after a failed adoption or
external reparent, while its prefix continued suppressing the original same-location
confirmation. Offline travel then had neither an accessible VR button nor its original
input. Reset also retained the parking-failure flag across the map hierarchy's lifetime.
On this concrete failure, the prefix now leaves the native confirmation method available;
native CheckTravel and multiplayer readiness remain untouched. Reset retries ordinary
VR parking against the next map hierarchy.

### Shared reward first reveal could wait on an uninvolved peer

Initial pose election considered compatible live VR peers without evidence that they
could publish a pose for this reward. The follower could hide its mandatory native window
while the elected peer had no matching reward. The correction is being validated as an
explicit per-opening, key-scoped participation handshake, not absence inferred from a
packet timeout. Existing reward presentation record 73 remains unchanged.

### Close admission must use the current mandatory decision

A mod close cross is attached according to the window's state at conversion time.
Pooled DialogPopup windows can change native escape/decision policy on a later Show while
the same conversion survives. Final close admission must recheck the current mandatory
classification before UserClosing, native Escape/Hide or mode exit. A stale cross must
not discard the new decision's callback; its alternative is original desktop input.

## Coverage without a newly demonstrated defect

| Area | Paths inspected and result |
|---|---|
| Native map locks | Laser/finger admission, selection/deselection, private quest/loadout ownership and native readiness remain authoritative. |
| Tutorials | Merchant/FTUE toggle dispatch, hint queue/owner changes, allowed hint omission callback, controls lessons and action-dismissed instructions reviewed. |
| Video | Intro Escape and hero EndReached retain native callbacks; distinct cosmetic shared clips are not blindly mapped to a local native skip. |
| Desktop input | VirtualMouseBridge writes and queues the left-button state; native InControl UnityMouseProvider reads Mouse.current.leftButton. This reaches raw native confirmation as well as uGUI widgets. |
| Thread waits | No synchronous Task wait/result cycle found in mod runtime. ModuleConfig's single monitor protects only a bounded registry operation, without callbacks or nested locks. Preloader sleep is a bounded fatal-startup log flush. |
| Scene ownership | Persistent native windows keep their original scene on conversion; release restores renderer/canvas state and original hierarchy. Exception rollback is under additional review. |

## Validation

Integration and final required gate results will be recorded after worker review.
Headset tests must cover map event rewards, chest reward continuation, current mandatory
close admission, ordinary travel, map conversion recovery and simultaneous multiplayer
reward opening/late participants. Automated substitutes do not establish rendered pixels
or actual controller hit delivery.
