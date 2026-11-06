# Build762 independent input and native-authority review

Base: qualified dev `734c3418b` (Build761). This review executes no test, Unity
editor, network operation or game-state change. Game references are read-only.
Implementation workers own fixes; the integrator owns final Class C qualification.

## Genuine shared input scope

The exact live `SharedWindows` subject/participation rules define the boundary:

| Kind | Original subject | Input/lifetime qualification |
|---|---|---|
| ScenarioStory1 | StoryController.window/dialogBox | Shared for an online session; original dialog key plus currentDialogIndex distinguish pages. |
| MapStory2 | MapStoryController.window, or its temporary composed host | Live map participation gate applies. The composed UILoadoutManager contains PRIVATE loadout/battle-goal content; sharing that host never authorizes every descendant scroll control. Only the original public story subtree participates. |
| QuestConfirm3 | NativeMapQuestSelection.ConfirmationWindow | Original selected/proposed subject, not unrelated hover preview. Same quest can reopen; localization key alone is not an opening generation. |
| Encounter4 | UIEventPanel or exact city-event ready preflight | Preflight ContentKey is native action-occurrence scoped. Event card ID alone does not identify a page: currentScreen.Name is its original YML page key. |
| Video5 | NativeVideoWindow.Window | Public movie placement; no artificial scroll control is required when the original has none. |
| RewardShowcase6 | RewardShowcase.Window | Native reveal/Continue permissions and votes remain original; only actual scrollable public controls are candidates. |
| Results7 | ScenarioResultsNative.Window plus parked ready footer | Common observer picture is inert; hidden original controls remain alive. Route input before native private mutation and apply at the common author. |

`ParticipatesHere` changes live on session/map transitions. Scroll addressing must
not survive retirement, another opening, a page change on a reused ScrollRect,
replacement controls or the story/composite identity handoff. Translated TMP text
and viewer-local transforms are unsuitable keys. The existing
RemoteStorySync.HashDialog uses original localization keys and excludes localized
titles; reuse that addressing. UIEventPanel.eventData.ID + currentScreen.Name
are exact read-only original event/page identities.

The generic RayUguiDriver path and the Results-specific fallback must not both
consume the same stick delta. Shared scrollbar drags need the same common input
route as wheel/stick scrolling. No broad input callback/path received over the
network may invoke readiness, choice or gameplay callbacks. Native masks,
interactability, closest-hit and callback ownership remain applicable.

Results' visual lease suppresses only original draw alpha/mesh rendering; it
does not disable native controllers or input. Merely broadcasting a client's
new scroll value AFTER private native mutation would leave its interaction
geometry divergent from the common visible picture. The shared route must own
that mutation boundary.

## Native cheat transport audit

The native DebugMenu.WinNoToggle seam already sends DebugWinScenario from the
host, followed by the original WinScenario. Native clients handle the original
GameAction through ClientDebugWinScenario. This supports unmodded peers and
needs no custom victory command; guests cannot safely call the local win path.

Other gold/unlock/reveal/requirements operations have no corresponding original
shared action. A direct local call would mutate only one native model. A cosmetic
side channel may interleave with native deterministic actions. The proposed
narrow host-only native-queue extension therefore requires actual queue coverage,
not just a packet round-trip.

Read-only ILSpy of the shipped bolt.user.dll establishes an essential boundary:
GameActionEvent_Meta compresses ActionTypeID to SEVEN bits, unlike the int in a
NetworkAction side-action token. The initially suggested sentinel60124 cannot
use this wire. The native enum currently occupies0..121;127 is representable and
unused. Runtime Enum.IsDefined refusal protects a future native enum addition;
no generated Bolt, FFSNet.NetworkManager or rule library is patched.

Event.BinaryData is a separate inherited attachment, not one of the18 declared
GameAction fields. Actual EventChannel.PackEvent writes it length-prefixed with
maximum packetSize/2; ReadEvent reads the same bytes before declared fields.
The native NetworkManager sets packetSize1200, so a strict512-byte cheat envelope
fits the600-byte limit. Failed reliable event packing can stall the ordered queue;
reject oversized/invalid envelopes before enqueue. The preserved read-only
metadata excerpts and original assembly hashes belong to the ignored review lane.

Native ProcessAction calls Execute and then FinishProcessingAction synchronously.
An accepted host command is forwarded to PlayerRegistry.AllPlayers, not merely
Participants or the mod's delayed CensusRoster. Revalidate the actual full roster
and pending ConnectingUsers/JoiningPlayers/OtherClientsAreJoining at enqueue and
host execution. Invalid host commands must finish without mutation and be marked
invalid so native forwarding and action-ID advancement do not occur. Client
application of an accepted host command cannot depend on local Cheats.Enabled,
3D-map activation or UI visibility; those are not deterministic campaign state.

The native map phase (MapHQ/MapAtLinkedScenario) and exact map seed/mode/party are
better commit gates than CurrentScenarioState==null. YMLLoading clears that field
during loading, and it is not an authoritative map-state discriminator. Likewise,
MapRoomDriver.Active is viewer-local: a compatible client using the2D map must
still apply the same admitted host operation. Only the host writes its campaign
save. Gold commands name the actual selected model identity, never the receiver's
current character selection. Requirements bypass must retire on session/roster
changes so a later unmodded participant does not inherit an unknown rule override.

## Direct fixture-consumer inventory

The root's new PublishTownServicesFinal call is imported verbatim by
`scripts/check-town-final-capture.py`: its town-only boundary needs one explicitly
inactive SharedWindowScrollSync.LateTick(float) port. The production publisher
and all original negative controls remain unchanged. No other actual whole
NativePresentation publisher compiler was found in the fixture inventory.

Peer-retirement imports actual OnEnable/OnDisable/ForgetPeer but deliberately
ports Subscribe/ResetNativePresentation/ForgetNativePresentation, so the root's
new Subscribe/Forget/Reset calls do not require a new type in that old fixture.
Results transport imports QueueResults/QueueResultsReady/TickFragments/SendToken;
Town638 imports SendToken; NPC661 imports exact Town admission members. Those
members do not acquire the new scroll dependency merely because their enclosing
source file changes. Actual changed source slices still require final recheck.

Menu-world is an affected containing suite: it executes actual RayUguiDriver
Tick/TickPressed/Cancel and native pointer/grab authority. It must not be reused
if shared scrollbar pointer interception changes those kernels. VR-options is
also freshly required for changed Cheats UI. Board-rules and both character-
tooltip suites load the complete rebuilt plugin, despite receipts that list
narrow source subsets; they require fresh final-DLL coverage.

## Conservative proof reuse

Ignored main-checkout `.planning/validation/build762/review-lane/` retains:

- `input-census.py/json`: all290 prior obligations, original log digests and
  explicit source receipts. Receipt candidates are NOT automatic qualifications;
  unresolved generated source slices and missing dependency closure remain fresh.
- `shared-toolchain-reference-recheck.json`:1239 exact native/reference/toolchain
  bytes inherited from the already audited761 environment, plus build-context
  records. These include Unity2021.3.5, .NET, native Managed, NuGet packages and
  serialized game inputs; no toolchain or native scene was changed.
- `isolated-reuse-audit.py/json`: a deliberately narrow reviewed closure. Four
  additional costly scopes are isolated from the current input/cheat edits:
  menu-depth-capture, travel-audio-runtime, stereo-movie-render and scenario-card-
  preparation. Their original passed logs, source/fixture/recipe bytes, wildcard
  fixture inventories and common1242 native/toolchain/build inputs are checked.
  Native travel additionally binds the exact serialized AudioItem asset/object
  inputs. The existing16 isolated and five worker audits are rechecked separately.

The first recheck retains22 of those25 reviewed obligations. Active-dismiss,
card-progress-lifetime and initiative-row already have changed inputs and require
fresh coverage. No new scroll/cheat behavior is qualified by this old evidence.
Run the audit again at the actual final source freeze. All original failed
invocations remain failed; no prior partial receipt becomes a fresh full pass.
Headset, Photon and D3D11 acceptance remain separate from this source/input audit.

## Exact projection follow-up and fixture repairs

The previous fresh menu-world warning was conditional on changes to its imported
Ray Tick/TickPressed/Cancel kernels. The scroll checkpoint changes only
TickStickScroll. An independent source-generation audit now regenerates ALL31
menu-world production/control source sets from the current recipe and compares
them byte-for-byte to the actual executed761 cases. All31 match, all31 DLL hashes
match, and the other124 full input pins plus1239 native/toolchain inputs match.
TickStickScroll and UguiPointer are explicit unrelated ports in that container;
this reuse does not qualify the new shared scroll feature.

The LevelUp94 producer likewise imports only TryIntersect/HitCorners/ToScreen/
HitRectOf from RayUguiDriver. Current PopupRay759.cs matches every retained case
(SHA256 e1cf9178d41445b32cb8b0dfd765ed5c7b57bef59aafb1d9be6248626e8d4b5b).
The other61 full source inputs,156 native exports,102 executed binaries and
1239 shared native/toolchain inputs match; all eight original editor domains
completed all94 variants. `levelup-projection-reuse.py/json` retains that complete
prior proof without rerunning it or weakening the old source pins. Both projection
audits must run once more against the final integrated freeze.

By explicit integrator delegation, four private pointer-fixture imports now carry
an INACTIVE SharedScrollBoundary762: ugui-scroll760-runtime, menu-depth-laser,
menu-depth-rotation and its integrated container. No session/publisher exists in
those containers; Wheel/Pointer returning false is the production bridge's exact
unbound behavior. Manual rotation pointer storage includes the two new production
fields. Native callback/scroll/geometry assertions and all mutation recipes stay
unchanged. The dedicated shared-scroll fixture owns active multiplayer proof.

All four repaired production variants compile before the Unity launch boundary.
The compile-only wrapper deliberately stops before any editor or runtime oracle;
this is not a suite pass. The initial scroll preparation stopped because this
worktree lacked the read-only decompiled reference link; that failure is retained.
After linking the original read-only source tree, preparation/compilation succeeds.
Canonical completion cleanup runs for both the preparation failure and intentional
compile-only stops. Root runs each affected complete containing suite once.
