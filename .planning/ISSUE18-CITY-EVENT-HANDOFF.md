# Issue 18: pooled readiness window handoff after city events

Base: `dev` 0a87ac8a1 (Build729). This repair is presentation-only; integration
chooses the next build number. No networking grammar, player assignment,
readiness collection, game data or native gameplay callback is changed.

## Evidence and qualification

[Issue18](https://github.com/McFredward/GloomhavenVR/issues/18) describes a
four-VR-player session where a city-event button remains visible and subsequent
quest previews have no start control. Returning to the main menu repairs it.
The linked video shows the native message **Waiting for all players to have mercs
assigned**, naming Smartypants, at about10seconds. At about35seconds that notice
is absent and the start control is still absent. Neither the video's mod build
nor matching reporter logs were supplied. Current maintainer729 and remote717
logs belong to different runs and must not be treated as paired evidence.

Full audio review adds important context: the narrator says all four players
already have characters, and another player toggles assignment as a recovery
attempt before describing a city offer which persists after Cancel. The temporary
notice does not establish a user mistake. The already cached Whisper model
transcribed the audio locally; no audio was uploaded to an API. Eight additional
full-resolution frames at 28–49 seconds were reviewed.

The video does not prove which native controllable belongs to that player.
Readiness must continue to refuse an unassigned participant. A table showing
four characters is not proof that four network players own a participant each.
The source defect below is independently reproducible and consistent with the
later missing-control symptom; it is not claimed to prove the reporter's exact
machine history or to constitute a fresh four-headset reproduction.

## Reproduced mod defect

`UIReadyToggle` is one native pooled object for CityEvents, Quests, rewards and
other phases. Native source `UIReadyToggle.cs:18` requires both Toggle and
UIWindow on the same GameObject; Awake obtains that exact UIWindow. Its
`IsVisible` property reads `window.IsOpen`.

City-event readiness has no quest card to host it, so it can first be converted
as a standalone modal. The following native quest reuses the same object.
`MapTravelConfirm.Park` previously moved it into the quest without retiring that
old conversion, and recorded the old **mod modal host** as its native home.
The catch-all's next refusal/release then restored the native ready root out of
the quest. `ReconcileCore` interpreted that competing mod write as a game-owned
reparent and permanently set `_parkStandDown` until the map room was rebuilt.
Consequently subsequent proposals had no parked travel confirmation, and room
reentry appeared to cure it.

The same ready object was also map-sticky. After native Reset/Hide ended the
city offer, the modal could keep the obsolete label visible even though its
native handler/readiness was no longer available. A readiness offer does not
have the persistent lifetime of a merchant window.

## Repair

Before taking any native home snapshot, Park now uses the existing
`ModalFallback.ReleaseForComposite` for that exact ready UIWindow. This same
kernel already handles original introductions and level-up content. It
synchronously retires the old float/capture/fit writer and any pending vanish
callback, and restores its genuine home before the new parker records it.
A real user-close owner refuses takeover for the moment; the next tick can retry
without permanently disabling parking. No Hide/Confirm/ReadyUp is synthesized.

Converted ready toggles no longer receive Sticky. Their original IsOpen governs
presentation lifetime in every readiness phase. The map's existing nonblocking
input classification remains unchanged, and merchant/temple windows still stay
open across ordinary sibling hides.

Native explicit Cancel reaches `CancelPreviewCityEvent`, then
`ToggleReadyUpUI(false, CityEvents)`, `UIReadyToggle.ToggleVisibility(false)`,
`UpdateVisiblity` and `window.Hide`. The same non-sticky lifetime repair therefore
also covers the cancellation described in the audio; no alternate Cancel or
native state mutation is introduced.

An unrelated native `ResetUI` ordering observation was reviewed but deliberately
not patched: ClearHostSelectedQuest can change the current state before
ClearHostSelectedCityEvent. The pooled popup is also hidden by
HideQuestSelectedAction, so this observation alone would not prove a persistent
visible city popup. Fixing it would not eliminate the reproduced competing
mod-conversion writer. The repair targets that demonstrated ownership defect.

## Validation

`map-ready-handoff-tests.sh` compiles the production Reconcile/ReconcileCore,
Park, Unpark, ResolveContainer, native-target transfer kernel, modal withdrawal
primitive, and original modal keepalive/Sticky expressions against a bounded
uGUI hierarchy model.52 assertions execute both relevant pump orders, pooled
city-to-quest reuse, pending dissolve callbacks, user-close refusal/retry, native
phase-end hides, unchanged merchant lifetime, native nonparticipant refusal,
late native visibility, successive/replacement quests and offline options.
Five causal negative controls must fail at the corresponding runtime assertion:
missing takeover, missing pending-conversion takeover, sticky ready lifetime,
bypassing user-close ownership, and bypassing native visibility.

This is real production-coordinator execution with modeled Unity/native public
boundaries. It does not execute the headset renderer, Photon peer delivery, or
native assignment/ACK aggregation. The fixture intentionally does not claim
that duplicate local executions are a four-player network test. The existing
`map-quest-ready-runtime` suite separately covers native callback dispatch and
unchanged readiness semantics.

Strict actual-game-reference Release builds with0warnings/0errors. Focused
adjacent suites include map-flow, map-quest-ready-runtime, city-event-button and
modal-close. Logs, exact source/extracted source, failed fixture hypotheses,
negative controls, result manifests and cleanup receipts are preserved under
`.planning/debug/map-ready-handoff/`. Generated cache cleanup uses the central
completion API after the owning subprocess is reaped. Hardware confirmation of
the reporter's complete session remains outstanding.
