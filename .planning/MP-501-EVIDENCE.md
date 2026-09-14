# Multiplayer card departures and held figures: build 500 evidence

Investigation date: 2026-09-14. Source baseline: `dev` commit `7745d90c8`.
This report records the hardware evidence for the subsequent build 501 fixes; it
does not claim that their rendered result has already been tested.

## Provenance

Paths below are relative to the main checkout's gitignored `.planning/debug/`.
Line numbers refer to the complete original files, without filtering. The main
`LogOutput.log` banner is line 17 and commit banner line 70; remote equivalents
are lines 18 and 71. Both say ModBuild **500**, assembly **1.0.0.0**, commit
**7745d90c8**, built **2026-09-14 18:53:11 UTC**. These are matching multiplayer
views, unlike the stale remote files available during the preceding investigation.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `LogOutput.log` | 27,391,003 | `21468f6a9529ca5d1bc70261c45c488e84d3cc79e70ffb7d1c903f3dfe704baa` |
| `Player.log` | 34,968,702 | `e8bc41a03fe0e9d735a017bd8e4fc5a8103faa73b695f38ceaa772fb249ae56f` |
| `remote/LogOutput.log` | 19,236,583 | `560c95d1cac5cbd61f37bad74216ad2a94edaba9356542b1eb4694cbfd288d10` |
| `remote/Player.log` | 26,301,127 | `024e113e85e1a9874bd7d391ae0899da74553df117a7b9d5298486d45716041c` |

The main file represents player 1, the remote file player 2. Latest image files
predate this test: `abstehendes_symbol.png` is September 10, all card-regression
JPGs September 9 or earlier. There is no supplied image of these two new symptoms.
Their old pixels cannot establish the appearance of build 500.

## Flight departure race occurs in both views

Every actual `[Net] FLIGHT FACE` diagnostic in these logs starts its flight while
the corresponding source recess is still marked occupied. This includes the active
destination as well as discard flights. These are all nine events, not a sample:

| View / LogOutput line | Route | Receive / first-draw frame | Mirror mask | Frames since mask changed |
| --- | --- | ---: | --- | ---: |
| Main 9742 | Slot1 → Discard | 28470 / 28470 | `0x3` | 4282 |
| Main 10511 | Slot0 → Discard | 30511 / 30511 | `0x1` | 2007 |
| Main 17509 | Slot0 → Discard | 47376 / 47376 | `0x1` | 554 |
| Remote 6853 | Slot0 → Discard | 19816 / 19816 | `0x3` | 6917 |
| Remote 7079 | Slot0 → Active | 20424 / 20424 | `0x1` | 608 |
| Remote 10970 | Slot0 → Discard | 33105 / 33105 | `0x3` | 3827 |
| Remote 11125 | Slot0 → Discard | 33639 / 33639 | `0x1` | 534 |
| Remote 13666 | Slot0 → Discard | 41346 / 41346 | `0x3` | 2372 |
| Remote 13839 | Slot0 → Discard | 41893 / 41893 | `0x1` | 547 |

All nine use a front through `RemoteAbilityCardSource.LiveWidget`. Subsequent
`Remote board content` lines main 9746, 10514 and 17511 still report the same
occupied source mask. Thus changing the flight curve or fixing artwork alone
cannot address the departure ownership overlap described by the user.

`RemoteCardFx.TryPlay` starts synchronously during packet application and its
`FLIGHT FACE` line reads `RemoteAvatar.MirroredSlotMask` at that point. The
instrument proves the competing presentation states, not the exact number of
rendered frames containing both cards. Its `first drawn frame` label is a code
execution marker, not a post-render screenshot. Similarly the large final column
is the age of the unchanged mask before departure, **not** the overlap duration.
Frame counters are local to each client and must not be subtracted across clients.

The source diagnostic's old prose principally treats this race as an artwork
lookup issue. Resolving the face from the still-present recess solved that older
issue without establishing that the departing source stopped drawing atomically.
The user now supplies the missing visual observation of that overlap.

Own turn-clear events do appear with explicit authoritative-model destination:
main 8585–8587, for example, identifies the first discard of Scurry and its actual
Slot0 origin. All eight remote discard `PILE FLIGHT` rows report an endpoint at
the discard anchor with zero endpoint error. There is no wrong destination in
those measured flights; departure timing is the demonstrated defect.

### Burn timing observations for the all-flights review

Burn releases are paired by card and occurrence, never by cross-client frame:

| Card | Owner BURN HOLD | Observer BURN FLIGHT |
| --- | --- | --- |
| Sweeping Blow | Remote 9359: 2.01 s | Main 12445: 1.97 s |
| Overwhelming Assault | Remote 11942: 2.00 s | Main 17395: 2.44 s |
| Gnawing Horde | Main 17959: 2.01 s | Remote 12486: 1.88 s |
| Leaping Cleave | Remote 14505: 2.01 s | Main 21867: 2.03 s |

All observer rows say `signal=owner release event`; all owner rows end with the
artwork handle cleared at `_GreyOut 1.00`. Observer `held` starts at its own burn
discovery, so unequal durations do not by themselves prove a release arriving
early or late. Overwhelming Assault is worth checking in the source review:
the observer spends 2.10 s on its stationary slab, whereas Sweeping Blow spends
0.32 s and the other two zero. There is no synchronized render timestamp here
that could certify exact 1:1 burn completion or prove a duplicate final frame.

## Figure release occurs after native movement setup

The `Player.log` files expose actual game actions omitted from the mod-only log.
Three concrete held-figure episodes coincide with movement confirmed by the other
player. Each holder eventually force-releases the miniature:

| Holder view | Confirmed action | Movement setup | Local release |
| --- | --- | --- | --- |
| Main, Brute | Player.log 49820: action 55, player 2 | 49840 `ActorHasMoved`; 49845 loco target; 49847 waiting for move | 49900–49901; LogOutput 9580–9581 |
| Remote, Mindthief | Player.log 76579: action 77, player 1 | 76599 `ActorHasMoved` | 76644–76645; LogOutput 10702–10703 |
| Remote, Mindthief | Player.log 102882: action 104, player 1 | 102902 `ActorHasMoved`; 102907 loco target; 102909 waiting for move | 102950–102951; LogOutput 13361–13362 |

The release diagnostics say `GRAB STATE heal ... force-released ... no longer
allows this hand`. They establish automatic release, not the exact busy clause
that fired. None records the observer's actor transform at movement initialization.

Source nevertheless identifies a concrete mechanism matching the airborne walk:

- Native `ActorBehaviour.SetLocoTarget` (read-only decompilation, lines 261–274)
  reads `base.transform.position` into `m_StartingDistanceToIntermediateTarget`,
  `m_StartPosition` and `m_VectorToTarget`. `Choreographer` calls it at line 4427.
- Build 500 `NetFigures.EaseSlot` continues writing the received held pose without
  a per-actor idle/action guard. Its `ReleaseRemoteSlot` restores scale and drops
  membership; it does not first restore the figure's board pose.
- `ActorBehaviour_HeldTransform_Patch` suppresses native Update and LateUpdate
  while remote-held membership remains true. Removing membership later cannot
  retroactively correct the position already captured by `SetLocoTarget`.

Therefore a hand transform can become the native movement origin, and merely
waiting for the owner's next released-hold packet is insufficient. The handover
must occur before native movement samples its start. The exact headset incident
cannot be selected uniquely from these logs; this is source-proven susceptibility
with matching movement/release sequences, not a reconstructed video frame.

## Other observations within scope

Neither `LogOutput.log` contains an Error-level mod event. Both Player logs contain
twelve Hydra destination-host resolution failures spread over the run (first at
main 279 / remote 282), and an unstacked `NullReferenceException` during teardown
(main 127858 / remote 120655). Remote startup also reports two OpenXR space/time
errors at 300–301. These are real observations, but no evidence links them to
card departure overlap or held-figure movement origins.

Build 500's exact `STORY CURTAIN LOADOUT HANDOVER:` event occurs zero times in
both views. The token is mentioned only in explanatory curtain prose. Both
clients reach the scenario; this test therefore exercises multiplayer preparation
successfully, but does not demonstrate replay of the previous offline frozen-root
handover branch. Do not close that specific hardware follow-up from a token grep.
