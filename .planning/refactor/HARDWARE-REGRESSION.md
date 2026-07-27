# Hardware regression pass — what this refactor could plausibly have broken

> Not a generic test script. This lists **only** what the refactor actually touched, ordered by
> how likely a defect is and how hard it would be to attribute later. Everything else in the mod
> is provably byte-identical (`scripts/refactor-guard.sh check`), so testing it tests nothing.
>
> The compiled form of the whole mod differs from the pre-refactor build in exactly these ways:
> removed dead members, 49 rewritten config descriptions, 3 added const declarations, and one
> reordered-but-verified partial class. Nothing else changed. That is what makes this list short.

## 0. Before you start

Install as usual, then check the startup line names the commit you expect — a stale DLL is the
single most common way a "regression" turns out not to be one:

```
[Core] v0.1.0 build <hash> [main] (built …) loaded — N modules initialized
```

Then confirm the four static checkers passed on the build you are running (`scripts/refactor-guard.sh check`).
If any of them is red, stop: the build is not the one this list describes.

## 1. Highest risk — dead code that was removed

Deletion is the only category here that can break something silently. Each item below was
grep-verified against the Harmony surface, Unity messages, reflection, config keys, log tokens and
the debug menu — but a Unity project can always reach code in a way static analysis cannot see.

| What to do | What was removed under it | Failure would look like |
|---|---|---|
| **Open the campaign/world map** (both the world map and a city/village view), pan and zoom it | ~500 lines of dead map-capture code + 18 accessor properties | map renders wrong, black, or throws on open |
| **Open the control board, dock and undock cards, use the item slot** | `PlayTray.RenderOnTop`, the whole pick-field cluster, `ReseatProud`/`SeatOnBoardFace`, `_boardColliders` | widgets seated at the wrong depth; laser no longer hits the board surface |
| **Burn a card; consume an item** | the consumed-plume pair (`SpawnConsumedPlume`, `_plume`) | the on-card "verbraucht" effect missing (the plume was the big surrounding cloud, already removed by request — the card effect must still be there) |
| **Open the ability fan with the ghost-hand setting on** | `HandGhost.Engaged` / `RendererCount` | ghost hand does not fade |
| **Multiplayer: watch a peer open their card fan** | `RemoteHandFan.PalmStandoff` | the peer's fan hangs off the wrong point of the hand |
| **Turn the palm to reveal the fan, at several hand pitches** | `PalmGate.UseDevicePalmNormal` | the fan opens at the wrong roll angle, or not at all |

## 2. Config — 49 descriptions rewritten, **nothing unbound**

The point of this batch was that several settings *invited* you to tune something nothing read
(one of them opened with "LIVE FIT KNOB … dial without a rebuild").

- Start once with your **existing** `.cfg` files in place. Every key must still load, and every
  value you had must survive. No key was removed, so nothing should reset.
- Open `dev.gloomhavenvr.cards.cfg` and the others: entries that do nothing now say
  `LEGACY — no effect, superseded by <X>`. Spot-check two or three of the named successors and
  confirm the successor **does** work.
- **Two entries deliberately did NOT get the legacy label** because they turned out to be live
  despite sitting among the dead ones: `[Hands] {Style}Scale` and `[FigureGrab] HeldUpright`.
  Confirm both still do something — they are the two a mistake would most easily have caught.

## 3. Multiplayer — the wire

Nothing about the packet layout changed, and 188 byte-exact assertions prove it on every build.
What *did* change is that two enums are now explicitly numbered and guarded. A mistake there would
be invisible locally and only wrong on the other headset.

- With a second player: open the **discard**, **burnt** and **item** piles in turn and confirm the
  peer sees the right pile browse open each time (this is the `PileKind` order).
- Change the **control board style** and confirm the peer sees the same board (this is
  `ControlBoard`, where `Oak = 0` is load-bearing: "style bits absent" and "Oak" must render alike).
- Confirm a peer's board still shows everything it did before — objectives, elements, initiative,
  card faces during the reveal phase.

## 4. Startup and ordering

The file splits cannot change behaviour (proved), but the frame-order lock is new and the module
wiring was documented rather than changed.

- Watch the log for the module init lines and any `TickGuard` exception storm in the first minute.
- Doff and don the headset once — the control board must still be there (that watchdog is
  unconditional by design and was not touched).

## 5. Performance — the instrumentation from the earlier pass

This is the one place where you are collecting data rather than checking for breakage.

- Play normally for a few minutes, then move your head fast the way that provoked the judder.
- Grep the log for `[Perf] FRAME`, `[Perf] STEPS` and `[Perf] SPIKE`.
- The `SPIKE` lines say outright when the mod is under 25 % of an over-budget frame
  (`VERDICT: NOT the mod`). That is the answer to "is the judder us or the game", and it is the
  main thing this build can tell us that the previous one could not.
- If the mod *is* implicated, the `STEPS` ranking names the subsystem. The three interval levers
  (`FanRelayoutMinInterval`, `WallFadeEvalInterval`, `RemoteContentInterval`) all sit at today's
  behaviour and exist to be A/B'd against those numbers.

## 6. Known-open items — expected, not regressions

Do not spend time on these; they are recorded in `LOG.md` and were deliberately left:

- `ComfortSettings` still says "grip" where it means the thumbstick click (following the text
  picks up a mini instead).
- `BoardScale` differs between a fresh install (0.5) and a migrated config (0.4).
- The remote board shows a pick field the local board never showed — predates this work.
- The item fan uses rig scale where the browse fan uses board scale — probably a bug, needs a
  hardware round of its own before anyone touches it.
- `docs/TESTING-P4.md` §5 still asks you to test a comfort vignette that no longer exists.
