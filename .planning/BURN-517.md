# Burn replay review — build 517

## Evidence

The maintainer reports a short-rest burn restarting several times, with the card
returning to blue between starts. The available local logs still identify release
1.0.3 / build 515 and the remote logs identify historical build 500. They do not
establish the exact call order of this latest observation. Findings below are
source-proven paths, with automated regression coverage; headset confirmation is
separate.

Native `CardsHandUI.FinalizeShortRest` starts `BurnCard` before submitting the
model loss. `FullAbilityCard.SetPile(Lost)` can then request `LostMode` for the
same card. Both calls pass through `CardEffects.ToggleEffect`, which restores the
material and stops the previous coroutine before starting another. Dialog hover
cleanup also invokes `RefreshPile`, whose false/settled effect path performs that
same destructive reset. Repeated requests describe one loss, not new animations.

## Review coverage

| Path | Required behavior |
|---|---|
| Short rest acceptance and dialog hover exit | One original native burn, with the spent starting appearance retained; repeated effect aliases cannot restart or settle it early. |
| Long rest and damage sacrifice, including two discarded cards | Independent originals retain independent episodes; shader and native hand-loss completion both precede layout/flight release. |
| Played lost action and expired active card | Native effect refresh cannot rewind playback; completed activation may still restore its normal native look. |
| Rest redraw / recovered card | An uncommitted choice is not a completed burn; authoritative recovery permits a genuine later burn. |
| Widget replacement, temporary empty UI, historical focus | Presentation claims belong to native originals rather than pooled UI widgets; historical baselines do not invent new flights. |
| Consumed item updates and recreated item hosts | Repeated updates cannot overlap material timelines; an already-consumed new host paints the native settled state without replaying consumption. |
| Remote recess, active card, held card, fan and flight | Owner output remains authoritative. A missing address sample does not erase the same still-lost card's last painted output. |
| Character change | Existing native completion barrier remains in place; source identity and completed claims prevent rediscovering an old burn. |
| Pool recycle and scene return | Retire playback protection before the native reset; ordinary face movement into a dialog is not retirement. |

The adopted-owner registry resolves a full face while a native dialog temporarily
reparents it away from both its `AbilityCardUI` and its VR host. This avoids using
the full face's stale or absent action-only model metadata. Weak ownership does
not retain discarded scene objects indefinitely.

The original native iterators, yields and gameplay callbacks remain authoritative.
There is no replacement burn animation, shortened timeout, new wire record, or
change to remote concealment rules. Local controlled cards remain visible;
remote short-rest flights remain covered; the 3D map remains public.

Detailed lane reports: [BURN-LOCAL-517.md](BURN-LOCAL-517.md),
[BURN-DRIVER-517.md](BURN-DRIVER-517.md) and [BURN-REMOTE-517.md](BURN-REMOTE-517.md).

The historical reconstruction path is explicit: `CardsHandUI.SpawnCards` initializes
even already-lost cards as `Hand`; a later `UpdateCards` changes them to `Lost`,
which would start another native burn. Item host reconstruction similarly calls
`Show` after assigning an already-consumed original, then forces another state
update. Item burns use a native 0.001-second ramp, distinct from the two-second
ability-card ramp; repeated requests can still reset their materials and smoke.

## Validation

Focused production-path coverage:

- Ability burn replay: 496 runtime assertions, three source bindings and 13 negative controls.
- Consumed items: 225 runtime assertions and 11 negative controls.
- Local layout/flight sequencing: 219 runtime assertions and 16 negative controls.
- Remote discovery/output/release: 108 runtime assertions and 21 negative controls.
- Scene lifetime: 36 runtime assertions, 23 source bindings and eight negative controls,
  including retirement of burn protection before native pooling becomes possible.

The integration review also updated the layout harness's literal iterator-registration
binding to the nullable callback capture used at the first native step. Its runtime
completion and cancellation checks remain active; the old construction-time registration
is not accepted as completion evidence.

All 17 integration checkers and production suites pass, including 254,019 golden wire
assertions. Strict Release passes with zero warnings/errors. Bilingual documentation,
changed shell script syntax and whitespace checks pass. Patch inventory has 128 classes /
195 methods, registered exactly once; reviewed surfaces have 625 config keys, 171 patch
signatures and 4,729 log tokens, with no removals. The bundle remains 74,942,975 bytes.

The retained build-502 compiled comparison reports 77 changed types, 47 added and none
removed. The private build-516 comparison has 16 changed types, two added and none removed:
seven changes are solely the propagated 516-to-517 constant. The nine substantive changes
are burn playback, original-card ownership, local discovery/flight claims, item hosting,
scene retirement, module registration and remote discovery/output retention. Added types
are the native episode helper and ability-widget pool retirement patch. No unrelated
compiled behavior changed. The guard's exit status 1 reflects these expected compiled
changes, not a failed checker.

Automated results cannot establish the final headset picture.
Retest short-rest acceptance while
moving over and away from the confirmation button, consecutive rests, damage
sacrifice, long rest, active-card expiry, character changes and recovery followed
by another loss, with a current remote observer.
