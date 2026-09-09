# Multiplayer round489 — owner presentation and pending decisions

## Evidence and scope

Both supplied `LogOutput.log` banners identify **ModBuild488** (main and `remote`, line17).
The main client broadcasts player1; the other client broadcasts player2. Both health screenshots,
`boni_healthbar1.jpg` and `boni_healthbar2.jpg`, were inspected before assigning a cause.
Hardware evidence remains in the ignored `.planning/debug/` tree. These files are observations
of488; automated489 validation does not establish a correct headset picture.

The user's standing rule covers original widgets, appearance, state, effects and intermediate
animation on every board. Character control remains with the game's actual player claimant.
No new secrecy exception or gameplay mutation is introduced.

## Findings and changes

| Report | Source-proven cause and correction |
|---|---|
| 1 — active-card wash | Remote active cards inferred burn/ghost from their eventual destination. Actual owner material, graphic and native group output now drives the inert original card. |
| 2 — held back flash | Rig pose could precede extras36. The unchanged source-address record36 and actor attribution59 now accompany the pose atomically. An unresolved public front is withheld until the matching front is ready; covered selection cards stay covered. |
| 3 — owner bonus hints | Native highlighting reparents the original tooltip onto a flat holder. Restore its original converted host through the paired presentation operation; never invoke selection callbacks on a mirror. |
| 4 — character decisions | Decision lifetime was conflated with the owner's current board focus. Record60 separates pending actor from board visibility. Every local/remote board viewing that actor reads the actual owner's same original inert widgets, including native animation histories. |
| 5 — peer health preview | Record57 carries the actual attacked actor's committed-health endpoint and selected damage preview. Peers use the native HealthBar/InfoBar presentation methods and verify the sender's character control. |
| 6 — incorrect shield preview | The old bridge combined already-adjusted projected HP with a stale independent damage amount. Fix the bar endpoint at `CurrentDamageData.PreDamageHealth`; effective damage projects the already-replayed model HP with shield reduced by Pierce before applying the health cap. Selected card avoidance hides the native damage preview; hovering the damage button restores its alternative. The screenshots' growing green-plus-orange endpoint is consistent with the old double-accounting path. |
| 7 — premature wrist HP | Pending damage mutates model HP before confirmation. Wrist text now reads the game's saved pre-damage HP while the matching response is unresolved. |
| 8 — wrist conditions | Raw condition enum names bypassed localization. Use the same native condition translation keys through `Core/Loc.Game`. |
| 9 — active exit destination | Previous active cards were absent from the admitted departure population. Capture their original model seat and route only after actual Discard/Lost/PermanentlyLost membership; record61 preserves actor/source across focus changes. |
| 10 — Trample | Both logs prove the original Disarm rule rejected the attacks. A Deep Terror inflicted Disarm on the Brute even though the attack dealt zero damage. Movement still ran, merged attack and later attacks were blocked, and Disarm expired at turn end. No targeting or rules change is justified. |
| 11 — recovered fan wash | The peer read its own pooled native widget's spent flags. Stop replaying proxy state; clear inherited presentation and apply the owner's current native output. |
| 12 — burn flight timing | The old gate watched only FullAbilityCard effects and could force release at a deadline or focus switch. Also wait for the owning hand's actual native loss sequence, retain outstanding holds across focus changes, and release the remote presentation on the owner's semantic completion event. |
| 13 — uneven burnt-fan fire | A hidden local proxy's flame visibility was retained as if it were the owner's. Mirror actual original flame visibility, texture role and material progress consistently. |

## Additional review corrections

- A single remembered semantic flight could be overwritten by presence coalescing. Additive62
  repeats up to eight recent sequence-addressed flights for two seconds, with privacy and actor/source attached to
  each. Receivers deduplicate across repeated packets and sequence wrap. An initial empty history
  establishes the baseline before the first actual flight; late joining does not replay old events.
  Expired history preserves the latest sequence, and an explicit missing/foreign actor cannot
  fall back to the currently focused character.
- Active flight seats now come from the complete model population, so a missing local widget
  cannot renumber another card. Retained sources require actual local control even when the
  owner has switched focus to a foreign character.
- Pausing the native clock must not make an unchanged shader sample look like a cancelled
  coroutine. The burn completion watchdog follows the native clock.
- Native card CanvasGroups are explicit output, rather than a broad alpha reset. Codec validation
  rejects incomplete cards, duplicate bindings, invalid addresses and nonfinite values atomically.
- The native health helper added raw shield even for Pierce. The shared owner/peer projection
  now subtracts Pierce first and preserves native selected-card avoidance and damage-button hover.
- The original HelpBox prompt replaces the reconstructed text label. Its native runtime output
  is carried independently in record63, including intermediate warning/text geometry.

- Lossless compression reduces a synthetic complete eight-card native appearance frame from
  10,246 raw bytes to1,147 wire bytes/two pages; all32 cards from40,966 to4,012/five pages.
  These are byte-preservation fixtures, not measured headset latency. Randomized material output
  compresses less, and the full six-stream saturation test also covers uncompressible bounds.
  See [compression review](MP-489-COMPRESSION.md).

## Wire and installation

GVR1/v3 and all previous record grammars remain unchanged. New allocations: health57;
card appearance58 (messages12/13); rig actor59; decision attribution60; flight source61;
recent flight history62; native decision prompt63 (messages14/15). Holes38/40/42 remain unused.
Lossless snapshot compression uses new message16/record64, retaining original bytes and all old
envelopes. Paired record65 carries selected damage avoidance; record66 is next free. Appearance and native prompts have independent bounded fragmentation,
sequence queues and teardown. The extras worst case is3826 bytes; its4088-byte buffer retains
262 bytes of headroom and fits the unchanged4096-byte extras reassembly limit.

DLL-only after the full483 asset installation. No bundle, configuration or gameplay patch change.

## Validation

Strict Release: zero errors and warnings. All17 guard checkers pass, including251,037 wire
assertions. Patch registration remains107 classes/165 methods; configuration625, patch surface150
and asset bundle74,943,763 bytes are unchanged. Log tokens4713→4714, with no removed surface.
Documentation localization and all16 metadata-only reference assemblies pass their checks.
The compiled comparison reports38 changed types and29 added types, with none removed;
its status1 is expected for these behavior changes after all17 prerequisite checkers pass.
The remaining acceptance step is a new headset multiplayer run covering both client roles,
shield toggle/undo, foreign character views, active exits to both piles, short/long rest,
held-front pickup, and damage burns including a character switch during the animation.

Detailed evidence and implementation reports:
[Cards](MP-489-CARDS.md), [decisions](MP-489-DECISIONS.md),
[flights](MP-489-CARD-FLIGHTS.md), [Trample](MP-489-TRAMPLE.md).
