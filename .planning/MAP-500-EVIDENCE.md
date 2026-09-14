# Map loadout softlock — build 499 hardware evidence

Investigated 2026-09-14 against dev `ed35cfb25`. This report is evidence and source analysis;
headset confirmation of the follow-up fix remains pending.

## Finding

The game reaches battle-goal selection, but the mod keeps the original party window and its
nested battle-goal picker behind the story curtain. A quest-information window is still
floated, so the existing “something remains visible” and mutual-hold safeguards do not
recover the required decision UI. This is an input/presentation softlock, not evidence of a
threading deadlock. Frames, controller tracking, hover, dragging and clicks continue.

A second defect allows a new map-location click while the map manager is locked. This stages
a location again and exposes the travel control in the already committed loadout sequence.
The quest-information window itself is expected here: the native loadout explicitly reopens
it. The missing battle-goal window and the inappropriate travel control are separate defects.

## Evidence provenance

All paths below are relative to the main checkout’s gitignored `.planning/debug/`.

| File | Size | Last modified | Provenance |
| --- | ---: | --- | --- |
| `LogOutput.log` | 11,348,027 bytes | 2026-09-14 20:30:49 local | line 18: ModBuild 499 / assembly 1.0.0.0; line 71: dev ed35cfb25, built 18:23:55 UTC |
| `Player.log` | 12,079,727 bytes | 2026-09-14 20:30:49 local | Same local session and duplicate mod event sequence |
| `remote/LogOutput.log` | 80,216,973 bytes | 2026-09-09 21:40:47 local | lines 17/70: build 491, dev 050e88018; not this session |
| `remote/Player.log` | 108,148,437 bytes | 2026-09-09 21:40:36 local | Historical remote evidence only |

The current loadout diagnostics explicitly read `online=False, client=False` (local log
9216 and 9864). Do not attribute this incident to a current peer disconnect or combine the
old remote evidence into its timeline. No new screenshot captures this incident; the supplied
images predate the session.

## Ordered reconstruction

Line references in this section are to current `LogOutput.log` unless otherwise stated.

1. **9125, frame 22525:** party commitment is detected from the selected location being cleared
   and `AdventureMapUIManager.IsLocked=True`. **9146–9147:** the one-shot room sweep runs;
   **9170:** the old travel options are returned to their home after the quest popup closes.
2. **9249:** the story curtain captures one existing float: `New Party display`. The membership
   is frozen by object reference. This is correct while the story alone owns the screen.
3. **9484:** the quest introduction is over. **9485:** the mutual-hold diagnostic observes the
   party window withheld while the loadout backdrop is the only float; its recovery threshold
   is 90 consecutive ticks.
4. **Player.log 17470:** native `EnableLoadoutInteraction` executes. **9524:** the quest popup
   is floated again. **9529:** the global party `ActiveDisplay` is already `BATTLE_GOALS`.
5. **9533:** the decisive refusal explicitly names `UI Battle Goal Picker Window`: its ancestor
   `New Party display` is refused by the story curtain. **9534** reports the same ancestor
   refusal for `Party Display UI `. Native content exists but is excluded from VR conversion.
6. **9535:** the loadout-backdrop claim rises. **9537:** the mutual hold is declared cleared after
   only 19 ticks because two windows now float. **9538:** the loadout backdrop is released.
   The newly reopened quest information, not the required party UI, satisfied the floor.
7. **9588:** backdrop withdrawal is reported successful with exactly one remaining float:
   `UI Quest Popup`. The diagnostic says continue reachable but also says no confirmation was
   parked. This is not proof of an operable continuation: `ContinueReachableOffTheLoadout`
   returns true when the game is not currently requesting its confirm control.
8. **9625:** a VR map-icon click is dispatched anyway. **9627:** `IsLocked=True` and party
   commitment remain true, but `LocationToTravel` has become selected again. **9630:** the
   travel presentation fallback reveals the container; **9645 onward** measures it in the
   quest popup. This places travel controls in a phase that has already committed its journey.
   **9660–9661:** another location press is processed. **9914–9915:** the laser hovers and
   clicks the actual `Adventure button`.
9. **9674 / 9788:** `ActiveDisplay=BATTLE_GOALS` persists while the quest popup is the capture
   subject. **9846:** one floated window, two tracked open, map room active. **9906:** party
   commitment, story/loadout state and map lock still stand. The party curtain only lapses at
   **9940**, during teardown. Its enclosing commitment spans frames 22525–25916 (**9939**).

The quest popup’s final pose history (**9947**) records 2,526 sampled frames over 28.3 seconds
and 48 writes from the player’s own grab. **9629** records a healthy XR/input heartbeat and
**9935** reaches frame 25841. These observations exclude a stopped main loop as the mechanism.

## Source explanation

Read-only native source in the main checkout:

- `decompiled/GH.Runtime/UILoadoutManager.cs:257`: `EnterLoadout` hides the party root for the
  introduction. At **314–345**, `EnableLoadoutInteraction` reopens the quest popup and the same
  party root. At **364–372**, `AutoselectCharacter` opens the required battle-goal panel for an
  owned character without a chosen goal. Reusing the same root does not mean reusing the same
  screen phase or content.
- `decompiled/GH.Runtime/AdventureMapUIManager.cs:362–385`: native lock ownership enables
  `lockMapInteractionMask`. `MapLocation.OnPointerClick` (**278–291**) subsequently delegates
  to `Select`; directly dispatching that handler does not recreate the native lock mask.
- `src/GloomhavenVR/WorldUI/Composites/StoryComposite.cs:2108–2118`: `CurtainRefuses` accepts a
  frozen root reference as sufficient refusal. **2143–2185** continues holding while the
  loadout remains open and any nonmember float remains. Reopened party content remains a
  member; the quest popup supplies the nonmember float. The comment claiming the frozen set
  cannot exclude windows opened afterwards overlooks reopening the same native root.
- `src/GloomhavenVR/WorldUI/Composites/LoadoutConfirmPark.cs:488–496`: “continue reachable”
  returns true if `GameWantsConfirmShown` is false. This is appropriate for not insisting on a
  currently inactive button, but it does not establish that the actual battle-goal decision
  is visible. It therefore cannot prove this handover succeeded on its own.
- `src/GloomhavenVR/WorldUI/MapRoom/MapLocationInteractor.cs:1136` checks the map lock for
  deselection, but the click dispatch at **2311** has no equivalent lock admission in the
  investigated source. Poke and laser routes require the same native lock contract.

## Scope and exclusions

There are zero error-level entries in current `LogOutput.log`. `Player.log:17618` contains a
Hydra DNS/backend exception after the missing picker is already established. The unstacked
`NullReferenceException` at **18198** occurs after module teardown and `Bootstrap.OnDestroy`;
it is not evidence of the earlier softlock’s cause. Repeated large explanatory warning
messages are diagnostics, not thrown exceptions.

Build 499’s card-loss fallback guard is not the mechanism established here: this incident
occurs in the active map room, and the log directly identifies a story-curtain refusal.
The frozen-reference refusal lines trace back to commit `cf35c0de2` (2026-08-23). This proves
older logic participates; it does not date the first reproducible hardware failure or establish
which later change first exposed it. Do not call the previous hotfix the cause without evidence.

## Acceptance evidence for the follow-up

The same native sequence must float the original party UI and its battle-goal content after
the last introduction page, retain the intended quest-information popup, and provide the
native enter/ready control when the game activates it. No gameplay state should be forged to
escape the issue. Map icon hover, selection, deselection and travel admission must honor the
native lock through both laser and poke paths. Verify normal browsing and cancellation still
work after a real unlock. Repeat in offline and multiplayer loadout, including already chosen
goals and solo/no-goal scenarios. Source tests do not establish headset rendering success.
