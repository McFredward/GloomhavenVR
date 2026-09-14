# Single-player loadout softlock persists — build 501 hardware evidence

Investigated 2026-09-14 from dev `5aeb2cce`. This report records the supplied hardware
logs and read-only native/source analysis. It does not establish headset success of build 502.

## Finding

Build 500 admitted the wrong window identity. The loadout controller owns the inner
`Party Display UI ` window, while the curtain captured its outer `New Party display`
window (`PartyPanel`). The helper accepts the inner window and descendants, so it cannot
exempt that outer ancestor. The game opens the battle-goal choice correctly; the mod still
refuses its enclosing VR window. This is a presentation/input softlock, not a stopped thread.

The successful earlier multiplayer session did not exercise the new ownership exemption.
Its longer waiting interval triggered an older 90-tick curtain recovery. In single player,
the quest-information popup arrives sooner and cancels that recovery before required choices
become visible. The two clients therefore expose different outcomes of the same identity defect.

## Provenance

Paths below are relative to the main checkout's gitignored `.planning/debug/`.

| File | Bytes | Modified, local time | Build evidence |
| --- | ---: | --- | --- |
| `LogOutput.log` | 5,284,600 | 2026-09-14 22:03:22 | lines 17/70: 501, dev `071847035`, built 19:57:13 UTC, VirtualDesktopXR |
| `Player.log` | 5,997,290 | 2026-09-14 22:03:23 | Same local session, including native GUI messages |
| `remote/LogOutput.log` | 19,236,583 | 2026-09-14 21:11:39 | lines 18/71: earlier 500, dev `7745d90c8`, built 18:53:11 UTC |
| `remote/Player.log` | 26,301,127 | 2026-09-14 21:11:49 | Earlier multiplayer comparison only |

There is no fresh screenshot for this incident. The newest existing screenshot is September 10;
card and health screenshots are older still. No pixel-level conclusion is drawn from them.
Current main logs explicitly report `online=False, client=False` at lines 3477 and 5596.
The remote logs must not be combined with the new local timeline as a current peer session.

## Two independent reproductions

Line numbers prefixed P refer to `Player.log`; unprefixed numbers refer to `LogOutput.log`.

| Event | First attempt | Second attempt |
| --- | --- | --- |
| Native journey target | P8936: `Quest_Campaign_078_Scenario0` | P12781: `Quest_Campaign_039_Scenario0` |
| Curtain captures outer `New Party display` | 3369 | 5694 |
| Native `EnableLoadoutInteraction` | P9467 | P13735 |
| Native `Show(loadout)` removes its hide request; printed remaining set is empty | P9472 | P13740 |
| Native battle-goal picker is shown | P9483 | P13751 |
| Native inner party window is shown | P9489 | P13757 |
| Generic mutual-hold recovery is cancelled | 3668, after 19 ticks | 5966, after 18 ticks |
| Only `UI Quest Popup` remains floated; no confirmation parked | 3719 | 6016 |
| Native `ActiveDisplay=BATTLE_GOALS`, but capture is the quest popup | 3750–3751 | 6053–6054 |
| Curtain finally lapses | 3978, leaving the map room | 6069, teardown |

The first attempt explicitly logs the refused nested battle-goal picker and inner party
window at **3664–3665**: their ancestor `New Party display` is refused by story-curtain row 0.
Those named diagnostics are bounded; lack of a second identical line is not evidence that
its ancestor became admitted in the second attempt. The second attempt repeats the same
captured member, opened native choices and sole remaining popup.

The exact emitted marker **`STORY CURTAIN LOADOUT HANDOVER:` occurs zero times** in the
current local log. The token without its colon appears inside explanatory curtain prose;
those mentions do not mean that the exemption executed. The exact marker is also absent
from the older remote log.

The first attempt exits through functioning VR clicks on `Main Menu` and `Yes` (3865, 3928).
The second still records a tracked-head/input heartbeat (6046), then module teardown
(6067–6069). These events support a UI softlock, not a stopped game loop.

## Window identity proof

**LogOutput 2499** already prints both identities in one measurement:

- Floated root: `New Party display`, ID `PartyPanel`.
- Live `NewPartyDisplayUI` component: `Party Display UI `, one descendant level below it.
- The live party display drives a **different `UIWindow` instance** from the floated root.

The later path reading at **3645** places that inner component back under
`Campaign Canvas/New Party display/Party Display UI ` while the quest popup is adopted.
This agrees with the explicit ancestor refusal at 3664–3665.

Read-only native `NewPartyDisplayUI.cs:275–277` resolves its `window` through
`GetComponent<UIWindow>()` on its own GameObject. Its `Show` method at **652–665** removes
the caller's hide request, prints the remaining set, then opens that inner window.
`UILoadoutManager.cs:317–340` reopens quest information and the party display;
**364–373** opens the battle-goal panel for the first eligible character.

By contrast, build 501 `LoadoutWindowOwnership.IsCurrentContent` accepts only equality
with `party.window` or `candidate.transform.IsChildOf(owner.transform)` after checking
native loadout ownership. `StoryComposite.CurtainRefuses` asks this helper for the actual
captured outer member. An ancestor cannot pass a descendant-only test. Extending the test
must identify the actual native party host; admitting arbitrary common ancestors would
also admit unrelated UI.

## Why the earlier multiplayer test worked

Older `remote/LogOutput.log` captures the same outer party member at **2512** and prints
the same inner/outer identity split at **1389**. Its sequence differs at the waiting barrier:

1. **2950:** the old mutual hold is measured, with only the loadout backdrop floated.
2. **2979:** `MUTUAL HOLD BROKEN` actually fires after **90 consecutive ticks**, lifting
   the curtain despite the unresolved identity defect.
3. **3013:** the outer party window converts again.
4. **3040:** `MUTUAL HOLD LIFT: RESOLVED` after two ticks.
5. Native `EnableLoadoutInteraction` subsequently appears at **remote/Player.log 14597**.

In the new single-player attempts, quest information appears after only 19/18 ticks and
satisfies the generic nonempty-room condition. That prevents the fallback from reaching
90 ticks. This observed ordering explains the multiplayer/single-player difference;
it does not justify relying on a wait or broad curtain timeout as the fix.

## Other observations and limits

- Build 500's map-input lock now rejects map-icon presses during the locked sequence
  (3485, 3603, 3861, 5619, 5807). The continued missing decision UI does not show that this
  separate input fix failed.
- One actual mod error appears earlier, **1553**: `OPTIONS TAP` reports that the scenario
  escape menu did not open. It predates both map attempts. Later map escape/main-menu
  controls work. It is a separate lead, not evidence for the missing battle-goal window.
- Two Hydra DNS/backend exceptions appear in Player.log (**282**, **9611**); the latter
  occurs after the first missing picker is established. Neither explains the source-proven
  ancestor refusal. The unstacked `NullReferenceException` at **P14049** is during teardown.
- The old automated map-flow harness did not reproduce the actual nested native window
  topology. A replacement regression case must retain distinct outer `PartyPanel` and inner
  controller-owned windows, plus unrelated ancestors and children. Passing that case remains
  source verification; both single-player quests still require headset replay after the fix.
