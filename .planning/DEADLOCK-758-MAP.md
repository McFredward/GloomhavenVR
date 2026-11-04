# Build758 deadlock review: maps, town interactions and tutorials

Review base: `cd827d688` (Build757, `dev`). This worker owns the map-room, town-service,
map/loadout/quest composites and tutorial sources. The integrator reviews the union with
network, card/board, general-window, rig and core changes; this is not a standalone
claim that the whole mod or every native game state has been proven deadlock-free.

## Review method and boundary

Every owned production source is inventoried in the appendix, with a hash and line census
in the retained `deadlock758-map/owned-source-census.json` evidence. Progress-bearing
entry points, pending state, permission gates, continuation ownership, cancellation,
exception cleanup and teardown were traced through the read-only native sources and
neighboring mod callers. Presentation-only assets, layout, animation, sound and data helpers
were classified and scanned for cross-owner gates and waits; the inventory is not a claim
that a human read every comment or that arbitrary native/driver failures can be repaired.

The following invariants apply to both singleplayer and multiplayer:

- A UI choice still belongs to its original native callback and native player/controller.
  No acknowledgement, phase transition, purchase, donation or quest vote is synthesized.
- Leaving, replacing or losing an optional presentation must not retain its input/window
  claim. Fallback restores original UI; it does not accept or cancel a mandatory choice.
- New-save onboarding, campaign/Guildmaster/2D/3D, immersive/original service windows,
  offline/host/client and flat/unmodded hosts retain their native permission boundaries.
- Actual cards, purses, transactions and effects remain shared. Visitor-local town pre-drop
  guides retain the explicit user exception. No new gameplay or private-card data is sent.

## Proven defects fixed

### 1. Enhancement sale requested while another player joins

**Original causal chain.** Native `UINewEnhancementWindow.ConfirmSell` sets
`_isConfirmationBoxOpened = true` before checking whether another player is joining.
Both host and client can then return without creating a confirmation. No original
confirmation callback exists to reset the latch. The original slot-selection handler and
future sale attempts reject input while the flag remains set; ordinary shop exit/entry does
not reset it. This is reachable through the native selectable slot: its original
`DetermineInteractability` validates ownership but does not check joining. Native
`ConfirmBuy` already checks joining before mutating this flag.

**Fix.** `TownServiceEnhancementJoinGuard` refuses this same joining-only sale attempt
before any native mutation, and only while VR/window conversion runs. An existing actual
confirmation is preserved. Once joining ends, a fresh explicit sale follows the unchanged
native body. No native callback, sale or network action is manufactured. The integrator
registers the Harmony patch in the shipping WorldUI module.

**Causal verification.** The map-flow fixture executes byte-identical original
`ConfirmSell` and native slot-permission method bodies. The unguarded original reproduces
the phantom latch; disabling the shipping prefix fails the positive assertion. Cases include
host/client joining, fresh retry, existing confirmation identity, foreign native ownership,
original Cancel, singleplayer sale, client forwarding, VR-off and conversion-off. External
native rendering, payment services and Photon transport remain explicit boundaries.

### 2. Optional figure-grab lesson blocks native tutorial when grabbing is disabled

**Original causal chain.** The added HT10 figure-grab lesson acquires a tutorial-chain
hold even when `GrabFigures` is off. The actual pickup driver releases figures and returns
under that configuration, so the requested optional action cannot occur. Native tutorial
messages remain held until the old 300-second lesson ceiling. Disabling grabbing after the
lesson armed/shown has the same impossible-action outcome.

**Fix.** Skip admitting this optional lesson if its required feature is disabled, or finish
only this added lesson and release its exact chain hold if the feature is disabled later.
The queued original messages continue through the existing path; no native tutorial task,
card action or progression choice is completed by the mod. Enabled grabbing keeps its
existing real-pickup completion and lesson timing. Controls-table lessons retain their
existing explicit Skip path and task-specific hand/controller presentation.

**Causal verification.** The full shipping `TutorialGrabStep` and tutorial-chain hold run
in the tutorial-scope fixture. Compiled controls individually remove the admission and
mid-lesson capability guards and reproduce the hold. Feature-off before admission,
feature-off while armed, feature-off after shown, enabled real pickup and native-context
teardown are covered. Scene and controller presentation endpoints remain fixture boundaries.

### 3. Failed optional ability fan still consumes the enchantress interaction

**Original causal chain.** `MapRoomHand` permanently stands down its owned ability fan
after its existing probe/tick failure. Immersive-service configuration still says enabled;
that intention alone allows the enchantress to consume/hide her original window even though
no usable ability card can be supplied. A missing fan can also stop the approach code before
it requests the native source. Merchant item hands and temple purses have independent sources,
so globally disabling all immersive services would unnecessarily remove working features.

**Fix.** Expose the existing actual failure latch as read-only capability, rather than
changing configuration. A failed ability fan cannot advertise an actionable physical
improvement target or preclaim the original enchantress window. A permitted local approach
can still request that original window without a physical card. The presentation restores
its established native-window fallback once per opening. If a real native confirmation is
already pending, its exact source and callback are retained until the player answers; only
then may fallback replace the optional physical presentation. There is no retry loop,
auto-confirm/cancel, global NPC disablement or new native gameplay operation. Merchant and
temple capability, healthy fans and explicit original-window mode remain intact.

**Causal verification.** The full shipping enhancement handoff runs in Unity. Extracted
shipping ownership/recovery methods run with the real quiet-controller/mask fixture, with
the fault injected at the scene-resource boundary. Shipping properties and coordinator
call-site are separately source-bound. Compiled controls remove failure admission,
preclaim refusal, confirmation preservation or original-control restoration and must fail.
This is a source-proven exceptional fallback; an actual headset resource exception was not
injected into the game process during this review.

## Other progress-bearing paths reviewed

| Area | Cases traced and existing safety boundary |
| --- | --- |
| Quest proposal and readiness | Original proposal/action identity, host/client actor role, original ready callback, cancel/reselect, stale or foreign proposal, city-event versus quest singleton ownership; no private dismissal of accepted shared proposals. |
| Loadout and story transition | Native Continue/Ready parked under the current exact owner; a withheld backdrop depends on an independently reachable Continue, not its own float state; ownership change/throw returns original control and drops presentation claim. |
| City-event readiness | Current native city-event source and ready state only; original native validation on departure; unchanged explicit player choices and original multiplayer authority. |
| Ready roster | Native participants/connection status and actual assigned characters; informative native roster remains separate from Continue input, so an icon-row exception cannot take away Continue. |
| Character selection and map switching | Owned-character selection, no force-select on every visit, map/original-town source switch scopes, native permission; no new UI-driven player authority. |
| Merchant | Before-native multiplayer claim, replacement validation, permission loss, explicit reclaim/cancel, native confirmation ownership, pending-claim deadline and lifecycle release, actual return-flight completion. Pending staging timeout returns presentation only and cannot commit a transaction. |
| Enchantress | Selected original native card/slot/area, original buy/sell tabs, replacement, grant revocation, cancellation, modal/reclaim ownership, source release, visit overlap and independent concurrent residents. Added joining/fan-capability fixes cover the newly proven holes. |
| Temple | Actual affordability/donation eligibility, inspectable purse without permission to donate, original confirmation, exact owner, original blessing event and cancel/hidden cleanup. No temple confirmation is auto-answered. |
| Service setup faults | Existing native source/mask release and original-window fallback, source-preparation exception cleanup, unavailable or loading optional assets, original windows when immersive mode/hand support is disabled. |
| New-save/FTUE | Exact original first-use merchant tutorial source and promise/callback, explicit original-window close text/identity; immersive-only omission of the flat-only lesson; campaign/Guildmaster/native tutorial scope. |
| Added VR lessons | Native lesson context, exact added chain ownership, cancellation/teardown, controllers versus hands, ControlsBox Skip/Stop and original message replay; disabled figure pickup fixed above. |
| Motion and visual helpers | Finite loops/queues, exact source lifetime and no native gameplay continuation waits. Assets/audio/poses/geometry can fail presentation but do not author native turn or quest advancement. |

The owned code contains no blocking task/thread/monitor wait on progression. Its enumerations
are finite hierarchy/list walks or explicitly bounded preparation; elapsed animation waits
are presentation-only. Existing exception fallbacks are not a license to replay arbitrary
native callbacks after they may already have side effects.

## Secondary startup/loading audit (read-only)

The actual source directory is `GloomhavenVR.Preload`, not `GloomhavenVR.Preloader`.
Reviewed its three C# files, all seven `Core/Startup` sources, `CoreModule`,
`ScenarioInteractionPreparation` and the adjacent `ScenarioRoomLoading`:

- Preloader install/config failures are caught and do not abort native game boot. Relaunch
  has explicit feature gating, Frame opt-in exclusion, inherited-process and persistent
  finite attempt guards. The one log-flush sleep is finite (1.2 seconds), before saves exist.
- Runtime dependencies and installed-runtime candidates are finite. Missing dependencies or
  failed candidate init report failure and leave VR unavailable/Flat; there is no retry loop
  waiting for a mod network handshake before game startup.
- XR success initially means the native display exists, as required by asynchronous READY
  delivery. Its 900-frame rendering watchdog reports an unavailable device/session; it does
  not own a quest/input latch or force a native continuation. Synchronous loader/native driver
  calls cannot safely be interrupted by an invented managed timeout.
- Standalone passthrough capability/probe limits and activation deadline affect only optional
  composition. Unsupported/failed passthrough does not veto the XR instance or normal VR.
- Scenario/room preparation observes real native asset jobs. It writes no original
  loading/input flags; the added spinner is presentation-only. The 90-second stop cancels
  optional cache preparation, retains already adopted live presentation and leaves native
  lazy loading available. It never pretends a native material/job succeeded.

No additional source-proven startup deadlock was found within this secondary scope. Driver
hangs, unavailable physical tracking or an original engine/network failure remain external
limits; they are not justification for moving a turn forward or accepting a dialog.

## Evidence and remaining limits

Focused verification and failure/repair records are retained under
`.planning/debug/deadlock758-map/`, with final input hashes in `validation-inputs.json`.
These are partial worker checks; the parent runs the final strict build, all source gates,
complete local suite union and wire goldens once. Already passing unaffected suites are not
repeated merely because a fixture boundary needed a bounded repair.

The reported Issue27 turn still has no demonstrated source cause in this owned area. An
original outstanding action, missing native control or human misunderstanding is not
converted into a fact without the required turn/native state. None of the above fixes is
claimed to explain that particular user video or a Photon desynchronization.

Automated replay proves only the stated source paths and causal controls. It does not prove
headset pixels, real four-player timing, arbitrary disconnect interleavings, original game
rule validity, GPU-driver recovery or complete freedom from every possible future deadlock.

## Focused verification results

| Fixture | Runtime assertions | Compiled causal controls | Additional source controls | Time |
| --- | ---: | ---: | ---: | ---: |
| `map-flow` (final native-ownership follow-up) | 2,026 | 9 | 1 | 20.9 s |
| `tutorial-scope` | 51 | 9 | 1 (plus 17 source bindings) | 20.2 s |
| `town-enhancement-handoff` | 1,517 | 80 | Existing bridge/registration source checks | 136.3 s |
| `town-quiet-controller` | 181 | 17 | Actual hand-failure property/call-site bindings | 23.6 s |
| Native enhancement mode/Toggle/area compatibility | 225 | 3 | Byte-identical native-method extraction | 33.3 s |

Total: **4,000 focused runtime assertions and 118 compiled causal controls**. These are
worker-scope counts, not the final complete-gate count. Retained validation wall time for all
worker invocations is about 259 seconds including the earlier positive map-flow checkpoint
and two fixture compile failures. The failures were missing fixture-only `TutorialHints` and
`TownServiceVisitTarget` types; after each bounded boundary repair only its failed suite was
repeated. Neither failure triggered a production workaround or weakened negative control.

The standalone native-mode compatibility invocation also reaps its process group and calls
canonical `cleanup_completed_run`. The other invocations use the central runner. All
regenerable test project caches were removed after completion; source snapshots, manifests,
logs, original input hashes and results remain. No full build archive/movie copies were made.

## Owned source inventory

158 owned C# sources, 64,481 physical lines at implementation checkpoint `b7d4a4cb0`.

### Tutorials (18 files)

- `src/GloomhavenVR/Compat/Tutorial/Controls/ControllerVisual.cs`
- `src/GloomhavenVR/Compat/Tutorial/Controls/ControlsBox.cs`
- `src/GloomhavenVR/Compat/Tutorial/Controls/ControlsButtonFeedback.cs`
- `src/GloomhavenVR/Compat/Tutorial/Controls/ControlsLesson.cs`
- `src/GloomhavenVR/Compat/Tutorial/Controls/ControlsProgress.cs`
- `src/GloomhavenVR/Compat/Tutorial/Controls/ControlsTutorial.cs`
- `src/GloomhavenVR/Compat/Tutorial/LevelMessageHeal.cs`
- `src/GloomhavenVR/Compat/Tutorial/MerchantTutorialExitText.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialAoeHint.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialAoeHintRefresh.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialCameraSkip.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialCardNames.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialChainHold.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialFlowPatches.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialGrabStep.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialHintPatches.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialLessonScope.cs`
- `src/GloomhavenVR/Compat/Tutorial/TutorialVR.cs`

### Map/loadout/quest composites (6 files)

- `src/GloomhavenVR/WorldUI/Composites/LoadoutConfirmPark.cs`
- `src/GloomhavenVR/WorldUI/Composites/LoadoutWindowOwnership.cs`
- `src/GloomhavenVR/WorldUI/Composites/MapDialogSeat.cs`
- `src/GloomhavenVR/WorldUI/Composites/MapStoryCurtainPolicy.cs`
- `src/GloomhavenVR/WorldUI/Composites/QuestJourneyCurtain.cs`
- `src/GloomhavenVR/WorldUI/Composites/QuestPreparationHint.cs`

### Map room (43 files)

- `src/GloomhavenVR/WorldUI/MapRoom/CharacterUiOperability.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterBannerBorrow.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterBannerFrame.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterDestinations.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterMapTable.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterRoomGeometry.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterRoomLayout.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterTableAssets.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterTableFit.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/HoverCardPose.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapButtonRail.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapButtonTooltipPresentation.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapCharacterSelection.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapCityEventPulse.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapCityEventReadyUp.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapCityEventSource.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapHoverVerdict.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapIconHoverAnimation.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapIconHoverPads.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapIconLayer.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapIconPickGeometry.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapInputGate.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapLocationInteractor.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapParchment.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapQuestDepartureValidation.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapQuestReadyRoster.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapQuestReadyUp.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapRetirementPrompt.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapRoomDriver.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapRoomHand.1.Core.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapRoomHand.2.Fan.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapRoomHand.3.Wrist.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapRoomHand.4.FanOrder.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapRoomHand.5.Merchant.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapRoomSeat.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapSelectionTransition.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapTableLegs.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/MapTravelConfirm.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/NativeMapQuestSelection.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/NativeMapTravelAudio.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/ReadyToggleParkClaim.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/TownWindowCloseScope.cs`
- `src/GloomhavenVR/WorldUI/MapRoom/TownWindowMapSwitch.cs`

### Town services (91 files)

- `src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.EnhancementPreparation.cs`
- `src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.TempleTooltip.cs`
- `src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceAbilityBody.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceActivityAudio.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceActivityHandover.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceActivityMotion.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceActivityProps.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceActivityRig.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceActivitySoundClock.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceArcaneEffect.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceAssets.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceAvailability.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceBackdropAssets.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceBookInk.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCabinetAudio.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCardBody.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCardFace.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCardFlights.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCardSlots.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCatalog.Preparation.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCatalog.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCatalogCategory.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCatalogPointer.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceCatalogPreview.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceConfirmationMask.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceDecor.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceDecorMaterial.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceDepthOrder.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceEnhancementHandoff.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceEnhancementJoinGuard.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceEnhancementVoiceCapture.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceFace.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceFaceAttention.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceFaceMotion.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceFaceRig.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceFaceSpeech.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceGrounding.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceInspectionBody.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceLayout.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceLightList.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceLighting.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantCaption.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantCounter.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantDrawer.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantHandoff.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantRows.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantTransaction.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantZone.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceMotionClips.Data.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceMotionClips.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceNativeAssets.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceNativeEnhancementCardMask.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceNativeListVeil.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceOccupationBadge.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceOfferFeedback.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceOfferingPose.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServicePalmConfirmation.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServicePhysicalRay.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServicePlacement.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServicePopulation.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServicePresentation.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServicePublicMerchant.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServicePursePresentation.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceQuietController.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceRitual.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceRitualConfirmationGuard.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceRitualLayout.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceSharedCue.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceSleeveLining.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceStation.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceSurface.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceSync.CatalogBank.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceSync.Stock.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceSync.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceTemplateAssets.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceTempleBowl.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceTempleBowlMarker.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceTempleController.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceTempleOffering.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceTerrainContact.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceToken.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceTray.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceTutorialPatches.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceVisitTarget.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceVoice.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceVoiceCurve.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceVoiceSchedule.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceWindowMask.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceWorkspace.cs`
- `src/GloomhavenVR/WorldUI/TownServices/TownServiceWorkspacePractical.cs`

