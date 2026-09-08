# REVIEW — lane **net** (refactor 2026-09)

> Base `b40f8564` (ModBuild 480, brief commit). Worktree
> `/home/claw/gloomhaven_vr/.claude/worktrees/agent-a64976ea655be15da`, branch
> `worktree-agent-a64976ea655be15da`. Own guard baseline taken at HEAD; a no-op `check --summary`
> on the untouched tree is fully green (16 checkers, 210 164 wire assertions, "no compiled
> behaviour differs"), so every red from here on is this lane's.
>
> File set: `src/GloomhavenVR/Net/` (23 root files, `Remote/` 46, `Avatar/` 8, `Board/` 6,
> `Desync/` 3 — 135 k lines) and `tests/GloomhavenVR.WireTests/`.
>
> **How this was read.** The four read-only sub-agents the brief allowed were killed by a session
> limit before reporting; the rule is now at most one helper alive at a time, so the reading below
> is the integrator's own, done in the order of risk reduced: the receive path first
> (`FfsNetTransport.ReceivePrefix` → `NetAvatarDriver.OnPacketReceived` → `ApplyPending` →
> `RemoteAvatar.SetExtras` and the static peer tables), then teardown and latches, then the
> serializer pair, then every `catch` in the set (341), every static log latch, every
> `FindObjectsOfType`/`GetComponentsInChildren` by caller cadence, the R2 open items, the
> mirror pairs against their `Cards/` halves, the eight `STALE-DOC-REFS.md` lines, and the
> census's largest methods (`TickExtrasSend` in full, `SetUseBars`, `RemoteElementStrip.Refresh`).
> Not read line-by-line: the interiors of `RemoteHandFan`, `RemoteInitiativeTrack`,
> `RemoteWidgetMirror`, `RemoteBoardCard`, `RemoteMapStory`, `RemoteItemFan`,
> `RemoteDecisionWidgets`, `RemoteBrowserFan`, `RemotePileFronts`, `RemoteBurnFx` beyond the
> targeted passes above — they were swept by grep for the defect classes named in the brief, and
> the R2 review of yesterday covered their 1:1 behaviour. That is a stated gap, not a claim.

## 0. Instruments at `b40f8564` (Net set)

- `hygiene2.py`: 1 288 methods; 88.3 % ≤ 50 code lines; 24 over 100 in `Remote/`, 4 in root,
  2 in `Avatar/`, 2 in `Board/`. Largest: `NetAvatarDriver.TickExtrasSend` 1 091 (span 1 921,
  nest 4), `PresenceSerializer.TryRead` 987 (nest 7) / `Write` 805 (nest 6), `RemoteAvatar
  .SetExtras` 400, `BoardTuningSampler.Sample` 292, `RemoteBoardTuning` ctor 240,
  `RemoteElementStrip.Refresh` 230, `RemoteBoardFurniture` ctor 206.
- `dupes2.py Net 12`: 3 groups, all cross-file, 39 redundant lines (the two pairs in §2.5).
  Over the whole tree at window 20 the Net↔Cards mirror pairs are: `RemoteHandFan.cs:5354-5397`
  ↔ `Cards/CardFan.cs:1299-1347` (39), `RemoteFanEnhancementRefresh.cs:179-214` ↔
  `Cards/HandFanEnhancementRefresh.cs:164-199` (36), `RemoteControlBoard.cs:3753-3787` ↔
  `Cards/Piles/PileViewer.cs:1294-1336` (25).
- `loadbearing.py Net`: 65 fields written by diagnostics, 18 read outside a diagnostic — every one
  of the 18 is a log-latch bookkeeping field (`_logged*`, `_shared*Heartbeat*`, `_oversizeText`),
  none gates a mechanism; confirmed by reading each read site. No `a-write-inside-a-logger` case
  in Net today.
- A private-member reachability scan (declaration-only names across the whole tree) returns five
  names: the two compile-time wire-order guards (`PileKindWireOrderGuard`,
  `ControlBoardWireOrderGuard` — registry entries, untouchable), `PeerBoardFade.LateUpdate` (Unity
  message), `EncounterChoice.Finalizer` (`[HarmonyFinalizer]`), and **one genuinely dead const**
  (§2.4 X1).
- `check-enum-arrays.py`: 15 literal-sized arrays, all agree with their enum. `check-mirror-dials
  .py`: 8 live reads, 2 OPEN.

## 1. The worklist, ranked by risk reduced

| # | finding | class | tier | action |
|---|---|---|---|---|
| N1 | `RemoteCardArt.BuildBurnRig`'s catch leaves `_burnRigState == Ready` with null arrays (the F1 shape, cause fixed, state machine not) | defect | 3 | **fix** |
| N2 | a packet `TryRead` rejects is dropped with no line and no count — the receive side cannot say "your packets are malformed" | risk-gap | 3 | **fix** (instrument only) |
| N3 | `RemoteAvatar`'s "Decision widgets RECEIVED" line describes ROLE bytes with the OPTION-FLAG vocabulary | instrument lies | 3 | **fix** |
| N4 | two card-FX drivers leave `FxSurface.Unnamed`, so their arming lines share latch index 0 (R2 NOTE 2) | instrument | 3 | **fix** |
| N5 | `RemoteMapRoom.LogPlacardScale` prints a sentence falsified by ModBuild 480 and a factor the product no longer uses; MIRROR-DIALS `OPEN` entry stale | doc-drift in a log string / gate debt | 3 | **fix** |
| N6 | three peer-teardown lists in `NetAvatarDriver` that differ (staleness / `RemovePlayer` / `OnDisable`) | parallel-construction | 2 | **fix** (one helper) |
| N7 | records 36/39/41/43 are sampled AFTER the pre-emption gate — their edges never pre-empt the 5 Hz cadence | risk-gap (TIMING) | 3 | deferred — needs a ruling |
| N8 | `RemoteBoardFurniture` never destroys the materials it mints; a peer rebuild leaks them | risk-gap | 3 | deferred — needs a registry |
| N9 | `NetFigures.Resolve` has no negative cache; an unresolvable held actor rebuilds the lookup per rig packet | risk-gap | 3 | no action — bounded |
| D1–D12 | twelve falsified/stale comments (list in §2.6), incl. R2's "most dangerous" one | doc-drift | 0 | **fix** |
| S1–S8 | the eight `STALE-DOC-REFS.md` lines naming Net files | doc-drift | 0 | **fix** |
| X1 | `RemoteMapRoom.FallbackScaleFactor` — unreferenced private const | dead | 0 | **fix** |
| M1 | `PresenceSerializer`: lift the record loop body out of `TryRead` and the records block out of `Write` | motion | 1 | **fix** (empty guard except the two new private helpers; vectors green) |
| M2 | `TickExtrasSend`: lift `SampleBoardUi`, `FillEnvironmentRecords`, `FillHeldCardRecords` | motion | 1 | **fix** |
| M3 | `RemoteBoardFurniture.SetUseBars`: lift `BuildUseBarRow` | motion | 1 | **fix** |
| T1 | `DescribeOptionStates` ×3 (RemoteAvatar, RemoteBoardFurniture, inline in NetAvatarDriver) | duplication | 2 | **fix** (one helper, output byte-identical) |
| T2 | `TryReadFrame` ×2 (RemoteMapStory, RemoteStorySync), byte-identical | duplication | 2 | **fix** |
| T3 | five copies of the 3 s peer-staleness constant | duplication | 2 | **fix** (alias to `NetProtocol.StaleTimeoutSeconds`) |
| P1–P3 | the three Net↔Cards mirror pairs | parallel-construction | 2 | NEEDED-OUTSIDE (local halves are lane cards') |
| R-open | R2 F8/F10/F11/F12/F13/F15/F19 | 1:1 | — | deferred with reasons (§3) |

## 2. Findings

### 2.1 Defects and risk-gaps (receive paths, gates, latches)

#### N1 — the burn rig's catch leaves `Ready` standing over null arrays
- file:line: `src/GloomhavenVR/Net/Remote/RemoteCardArt.cs:2466-2562` (`BuildBurnRig`),
  consumers `:2259`, `:2331` (`_burnRigState != Ready || _burnImages == null` ⇒ silent `return
  false`), sibling catches `:2294`, `:2421` (which DO set `Refused`).
- class: defect · tier: 3
- evidence: `BuildBurnRig` sets `_burnRigState = BurnRig.Ready` at `:2551`, then still runs
  `BuildFlameQuad(...)` and `ReportBurnRigOnce(...)` inside the same `try`. Any exception in those
  two calls lands in the catch at `:2557`, which nulls `_burnImages`/`_burnTexts` but leaves the
  state at `Ready`, and logs at `VRLog.Debug` — invisible at the shipped level. Every later
  `SetAbilityCardFxProgress`/`ClearAbilityCardFx` then returns false forever for that card with
  nothing in the log. This is exactly the ModBuild 480 F1 mechanism ("the catch nulls `_burnImages`
  at the DEBUG tier, and every later write is refused forever"); 480 fixed the one throw it had
  found (the latch arrays) and left the catch that made it silent. The two sibling catches in the
  same class set `Refused`; this one does not.
- proposed action: in the catch set `_burnRigState = BurnRig.Refused` (the state the file's own
  `Refused` consumers already understand) and raise the line to `VRLog.Note` so a refused rig is
  readable at the default level. Text of the line unchanged.
- guard expectation: CHANGED confined to `RemoteCardArt`. hardware-observable: only if a rig
  build throws — token `Remote burn rig skipped`.

#### N2 — a rejected packet is invisible
- file:line: `src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs:3622-3681` (`OnPacketReceived`);
  `PresenceState.cs:3645-3721` (six `return false` sites, none logs);
  `AvatarSerializer.TryRead` likewise.
- class: risk-gap · tier: 3
- evidence: `OnPacketReceived` counts `_rxCount++` for every packet, then `switch (type)` with
  `if (TryRead(...))` — a packet that fails magic/version/type or the length guard falls out of
  the switch with no counter and no line. The 10 s summary line reports "RX N packet(s)" and
  pending counts only. A peer whose packets are systematically malformed (a mismatched build is
  caught later by `VersionGuard`, but only if an extras packet PARSED) looks identical to a
  healthy one: sends counted on their side, "RX" counted on ours, nobody appears. The 2026-07
  registry already noted the receive side "logged nothing" and added the RX line; the reject
  branch was left out of it.
- proposed action: count rejects per type in the existing summary line (`RX … ; K rejected`) and
  print one `PACKET REJECTED` line at `Note` for the first reject from a sender (change-gated per
  sender id, so a flood is one line per peer). No behaviour change; the `FIRST PACKET RECEIVED`
  and `RX` tokens stay.
- guard expectation: CHANGED confined to `NetAvatarDriver`. hardware-observable: no (unless a
  reject happens; then the new token).

#### N3 — the widgets RECEIVED line reads role bytes as option flags
- file:line: `src/GloomhavenVR/Net/Remote/RemoteAvatar.cs:1806` calls
  `DescribeOptionStates(roles)` — the same describer used for record 24's option flags at
  `:1783` — on record 29's ROLE codes (`NetProtocol.DecisionRoleUnknown..DecisionRoleShortRestNo`,
  `NetProtocol.cs:22364-22383`).
- class: instrument lies · tier: 3
- evidence: role 4 (`ShortRestYes`, binary 100) prints as `#0=greyed+CHOSEN`; role 1
  (`BurnAvailable`) prints `#0=OFFERED`. The sender's own line ("Decision widgets SENT",
  `NetAvatarDriver.cs:2500-2530`) prints roles as numbers. A reader pairing the two lines to decide
  sender-vs-mirror — the documented way to use them — reads a contradiction that is not there.
- proposed action: print the role list as numbers, the sender's format. Token "Decision widgets
  RECEIVED" unchanged.
- guard expectation: CHANGED confined to `RemoteAvatar`.

#### N4 — two drivers arm the card-FX rig without naming themselves (R2 NOTE 2, still open)
- file:line: `RemoteCardFx.cs:1147` (`new RemoteCardArt(...)`, no `Surface` assignment);
  `RemoteHandFan.cs:5150-5155` ("Surface IS DELIBERATELY LEFT Unnamed… that enum's own doc says to
  ADD one… that file was not handed to this lane"); enum at `RemoteCardArt.cs:1979-1992`
  (`Unnamed, Recess, Flight, Pile, Active, Held`); doc at `:2058-2059` claims "a face nobody drives
  keeps Unnamed and never reaches the instrument".
- class: instrument · tier: 3
- evidence: both drivers DO drive the rig (hand-fan used-card look; plain flights), both latch on
  index 0, so whichever builds first silences the other's `ReportBurnRigOnce` for the process.
  The hand-fan comment asks for the member to be added and names the file boundary as the only
  reason it was not; both files are in this lane's set now.
- proposed action: add `HandFan` and `CardFlight` members (appended — the enum is instrument-only,
  every array is `Enum.GetValues`-sized since 480), assign them at the two sites, correct the
  `:2058` doc and retire the `:5150` paragraph's "not handed to this lane" clause.
- guard expectation: CHANGED confined to `RemoteCardArt`, `RemoteCardFx`, `RemoteHandFan`.

#### N5 — `MAP PLACARD SCALE` explains a breach that ModBuild 480 closed, and prints the wrong factor
- file:line: `src/GloomhavenVR/Net/Remote/RemoteMapRoom.cs:1176-1197` (`LogPlacardScale`),
  fixed product at `:1340-1345`; `.planning/refactor/MIRROR-DIALS.allow` entry
  `RemoteMapRoom.cs | CanvasScaleMm | OPEN`.
- class: doc-drift (inside a log string) + stale gate allowance · tier: 3 (log content)
- evidence: since `cfa5b2ae` the placard is sized from `NetAvatarDriver.TryGetPeerCanvasScaleMm`
  (the owner's, `:1340`). The line still reads the VIEWER's `WorldUIConfig.CanvasScaleMm.Value`
  (`:1181`) and prints it as the millimetre factor of the product, then says "this class … has no
  route to it without an accessor beside `NetAvatarDriver.TryGetPeerWindowLegibility`" and "unequal
  means this viewer sees that peer's placard at exactly viewer/owner times the size" — every clause
  of which is now false. The MIRROR-DIALS entry is OPEN "so the gate can exist while the fix
  lands"; the fix landed, the entry stayed because the diagnostic read kept the line alive.
- proposed action: print the millimetres the product actually used (the owner's, with the
  "SHIPPED default" fallback marker the line already has), drop the viewer read, rewrite the
  explanation; keep the `MAP PLACARD SCALE` token and every shouted run
  (`THE LEGIBILITY FACTOR IS THE OWNER'S`, `IS NOT`) inside the new text; delete the OPEN entry
  (the gate fails on a read that no longer exists).
- guard expectation: CHANGED confined to `RemoteMapRoom`; `check-surface` log-token census
  unchanged; `check-mirror-dials` 7 reads / 1 OPEN.

#### N6 — three teardown lists, three different contents
- file:line: `NetAvatarDriver.cs:4076-4090` (staleness sweep), `:4101-4119` (`RemovePlayer`),
  `:4121-4131` + `:828-897` (`DestroyAllAvatars` + `OnDisable`), `:958-990` (flat-net teardown).
- class: parallel-construction · tier: 2
- evidence, side by side (what each forgets for a peer id):

  | step | staleness | RemovePlayer | DestroyAllAvatars/OnDisable | flat-net |
  |---|---|---|---|---|
  | `avatar.Destroy` + `_avatars.Remove` | ✓ | ✓ | ✓ | ✓ (via DestroyAll) |
  | `NetFigures.ReleaseRemote` / `NetProps.ReleaseRemote` | ✓ | ✓ | ✓ | ✓ |
  | `NetPlayerActors.ForgetAvatarFetch` | ✓ | ✓ | ✓ | ✓ |
  | `CharacterFocus.ForgetPeer` | ✓ | ✓ | — (global `Reset` in OnDisable only) | — |
  | `_peerEnv.Remove` | ✓ | ✓ | — (**never cleared in OnDisable**) | ✓ (`Clear`) |
  | `RemoteTestTriggers.ForgetPeer` | ✓ | ✓ | — (OnDisable does not `Reset`) | ✓ |
  | `PeerCardFaceCensus.ReportPeerGone` | ✓ | **—** | **—** | **—** |
  | `_pending`/`_pendingExtras.Remove` | n/a | ✓ | ✓ (Clear) | ✓ |

  Consequences today: the census keeps a departed peer's rows after `RemovePlayer` (uncalled
  today — the documented forward hook) and after a driver teardown; `_peerEnv` survives a
  driver disable/enable cycle until its 3 s staleness (harmless because every consumer tests
  staleness, which is why this is Tier 2 and not a defect). The risk is the NEXT per-peer table:
  the fourth list to remember.
- proposed action: one private `ForgetPeer(int id, bool destroyAvatar)` carrying the union, used
  by all three; `OnDisable` additionally `_peerEnv.Clear()`. Side-by-side in the commit.
- guard expectation: CHANGED confined to `NetAvatarDriver`. hardware-observable: no.

#### N7 — four records ride the cadence, not the edge
- file:line: `NetAvatarDriver.cs:3535-3600` (records 36 held-card face, 39 sacrifice seat,
  41 spent half, 43 fan source are sampled here, AFTER the pre-emption test at `:1963-1995`).
- class: risk-gap (1:1 TIMING) · tier: 3
- evidence: every other discrete, human-paced edge in this method pre-empts the 5 Hz gate (card
  count, browse, highlight, cap press, decision rows, …, each with a paragraph saying why 200 ms
  matters). These four are sampled only when a packet is already going out, so a pluck that
  changes the held face, a short-rest seat or a spent half reaches peers up to 200 ms after the rig
  packet that moved the slab — the slab shows a BACK for that window (`RemoteHeldCardFace`
  re-resolves on `code != _resolvedCode`). Whether this is visible on hardware is not known; the
  face resolve itself is throttled to `RefreshSeconds` (250 ms) on the receiver anyway, which may
  be why nobody has seen it.
- proposed action: none in this lane. Adding four `Changed` terms to the pre-emption test is a
  timing change that costs a packet per edge; it wants the user's ruling on whether a ≤200 ms
  face delay is a 1:1 breach, and a hardware read of the `Held-card face SENT` / `Remote held
  card FRONT` pair with timestamps.
- guard expectation: n/a.

#### N8 — the furniture's materials are never destroyed
- file:line: `RemoteBoardFurniture.cs:3396-3412` (`SetUseBars` rebuild destroys row
  GameObjects), `:4846` (`Destroy()` ⇒ `_decisionWidgets?.Destroy()` only);
  `Board/BoardVisual.cs` `Unlit` (`new Material` per call), `Quad` (built-in mesh, no leak).
- class: risk-gap · tier: 3
- evidence: `Object.Destroy(GameObject)` does not destroy a `Material` created with `new
  Material(...)`; Unity keeps it until `Resources.UnloadUnusedAssets` (a scene load). Every
  `BoardVisual.Unlit` on a peer board (dozens per board, plus N per use-bar tile) is therefore
  leaked on every drawer rebuild and on every peer teardown. Slow, memory-only, and reset by the
  next scenario load — but a long map-room session with peers joining/leaving grows it. The
  2026-07 registry's "material assets first, roots last" rule exists for exactly this and was
  applied to `RemoteCardArt`/`RemoteAvatar`, not to the furniture.
- proposed action: none here — it needs an owned-materials list on the furniture (the pattern
  `RemoteCardArt._ownedMaterials` already uses) and a teardown that walks it; a design, not a
  minimal fix. Recorded.

#### N9 — `NetFigures.Resolve` has no negative cache
- file:line: `NetFigures.cs:800-833`; caller `:381` (`ApplyRemoteHeld`, per rig packet, 15 Hz).
- class: risk-gap · tier: 3
- evidence: a miss calls `RebuildLookup()` — a walk over `WorldspaceUITools._panelUIControllers`,
  or `Object.FindObjectsOfType<ActorBehaviour>()` when the tools singleton is absent. A peer holding
  a figure whose actor this client cannot resolve (mid-spawn, dead) triggers that per packet for
  the duration of the hold.
- proposed action: NO ACTION — the list walk is cheap, the `FindObjectsOfType` branch is only
  reachable outside a scenario where no figure is held, and a retry window would delay a
  legitimately late-spawning actor (a tuning value this lane may not invent). Noted for the
  `findobjectsoftype-is-the-default-suspect` file.

#### N10 — smaller items, recorded, no action
- `FfsNetTransport.Send` (`:161-177`) logs `VRLog.Error` per failed send with no throttle; a
  send that fails every tick is 20 lines/s. Unreachable today (the driver gates on
  `LocalPlayerId > 0` before sending). Left.
- `DesyncWatch.ApplyPatience` (`:231-270`) — its catch is a `Warn` per frame (debug tier); and
  `Uninstall` never restores `ActionProcessor.MaxConsecutiveIncorrectActionsAllowed` to the game's
  value. Both harmless (shutdown is process exit). Left.
- `VersionGuard.Peers` is never pruned for a departed peer, so `IsModdedPeer(id)` stays true for
  the session. Consequence nil: the host leaving ends the session and Bolt ids are not reused
  within one. Left.
- `ReceivePrefix` (`FfsNetTransport.cs:194-235`) gates `EnemyInfoContinue` on `!FlatNetMode` at
  the prefix while `AssignmentChoice`/`EncounterChoice` test it inside their handlers — one gate,
  two placements. Equivalent; left, named here so the next handler picks one.
- `PeerBoardFade.FollowerRoots[playerId]` lists are pruned of destroyed transforms on the next
  rescan but the per-player list itself is never removed; a handful of entries per id ever seen.
- `PresenceSerializer.TryRead` returns `true` after a truncated TLV (`break` at `:3743`) with the
  records parsed so far applied — by design (forward compatibility); a half-applied packet is not
  possible because the pre-tail fields are length-validated before any write.
- `RemoteElementStrip.Refresh` allocates four small arrays per call (`new EColumn[6]`, `bool[6]`
  ×2, `ReadRowOrder()`), at 4 Hz per peer. Negligible; left.

### 2.2 The wire (frozen — verified, nothing proposed)

- `AvatarSerializer`: write order `head, left(+5), right(+5), heldFigure(24), style(1, always),
  heldCard(20)`; `need` at `:171` sums exactly those; the registry's §I.3 holds.
- `PresenceSerializer`: the additive blocks are written and read in flag-bit order; the mask-size
  byte is validated inside the block; the extension tail is `[count][id][len][…]` with
  `i += len` after every record (`:5117`), unknown ids skipped by length; the record count is
  patched at `countAt` (`:2021`, `:3118`). No block trusts its own knowledge of a length instead
  of the `len` byte. No block writes a record whose read-side default differs from the write-side
  gate (checked the 54 `if (state.Has…)` blocks against their `else if (id == … && len >= …)`
  twins).
- Ids 1..45 allocated except 38, 40, 42 (R2 NOTE 9 stands — undocumented holes; a comment at the
  `ExtId*` declarations naming them would cost nothing and is added with the doc fixes). 46 is the
  integrator's.
- Both wire-order guards are present (`NetAvatarDriver.cs:42`, `LocalRigSampler.cs:39`).

### 2.3 Motion candidates (Tier 1), with the locals that cross each cut

- **M1 `PresenceSerializer`.** `TryRead`'s record loop (`:3733-5118`): the body from `byte id =
  buffer[i++]; int len = buffer[i++]; if (length < i + len) break;` to `i += len;` reads only
  `buffer`, `i`, `id`, `len`, `state` and never writes `i` (checked: no `i =`/`i +=` inside;
  every `break`/`continue` inside belongs to an inner loop). Lift into
  `ReadExtensionRecord(byte[] buffer, int i, byte id, int len, ref PresenceState state)`. `Write`'s
  records block (`:2022-3117`) reads only `state`, `buffer`, `i` and the local `records`; no local
  declared before it (`flags`, `boardStyle`, `extensions`, `kindFlags`) is used inside it
  (checked). Lift into `WriteExtensionRecords(in PresenceState state, byte[] buffer, ref int i)`
  returning the count, with `buffer[countAt] = …` left in the caller. Two cuts, nesting drops by
  three levels each, bytes untouched.
- **M2 `TickExtrasSend`.** The method is two halves — ~860 lines sampling ~40 records into locals
  plus `xxxChanged` flags feeding one pre-emption test, then ~1 600 lines filling `extras` from
  those same locals. A per-record split is NOT pure motion (≈80 locals cross the gate). Three
  blocks are self-contained: the board-UI key (`:1195-1235`, reads `trayNow` only → `static int
  SampleBoardUi(PlayTray tray)`), the environment/story/map fill (`:1999-2016`, reads nothing
  from the first half → `FillEnvironmentRecords(ref extras)`), and the tail records 36/39/41/43
  (`:3535-3600`, read only fields → `FillHeldCardRecords(ref extras)`). ~200 code lines out.
- **M3 `RemoteBoardFurniture.SetUseBars`.** The per-row block (`:3437-3520`) reads `b, n, scale,
  rowH, budget, cursor` and the fields `_useBars`, `_decisionTuning`; `rows`, `totalSlots`,
  `cursor` stay in the caller → `UseBarRow BuildUseBarRow(int bar, int slots, float scale,
  float rowH, float budget, float y)`.
- Named, not acted (value below the reading they need): `RemoteElementStrip.Refresh` (three
  phases — state/signature read, mirror promotion/demotion, chip layout; the layout loop shares
  the plate-bounds locals), `RemoteAvatar.SetExtras` (already a flat sequence of per-record
  apply-and-log blocks; a split per record would be 40 two-line methods), `BoardTuningSampler
  .Sample` / `RemoteBoardTuning` ctor (a flat list of `Fac`/`F` pairs whose ORDER is the wire's
  ascending-id order — `check-tune-fields.py` guards it; splitting invites a reorder).

### 2.4 Dead code (Tier 0)

#### X1 — `RemoteMapRoom.FallbackScaleFactor`
- file:line: `RemoteMapRoom.cs:1094-1107` (declaration + doc), `:1126` (one `<see cref>`).
- evidence: no code reference in the tree (scan §0); `ScaleFactorFor(int)` (`:1201`) computes the
  same product with the peer's legibility and the same `Defaults.WindowLegibility` fallback;
  `git log -S` shows it arrived with the map-room placard and was superseded by the 2026-08-28
  legibility ruling. Not a Harmony/Unity/reflection/config/log/test/instrument member.
- action: delete; fold its one true sentence ("written as the multiply so a stale copy cannot come
  back") into `ScaleFactorFor`'s doc; demote the cref.
- guard: the const disappears from `RemoteMapRoom`, nothing else.

### 2.5 Duplication inside the set (Tier 2)

- **T1 `DescribeOptionStates`** — `RemoteAvatar.cs:2856-2879`, `RemoteBoardFurniture.cs:3213-3240`
  (`DescribeStates`, plus an `unstated` arm for `i >= states.Length`), and the inline builder in
  `NetAvatarDriver.cs:2455-2475`. Same vocabulary (`#i=OFFERED|greyed +dim +CHOSEN +HOVER +PRESS`),
  same bit order. One `NetProtocol.DescribeDecisionOptionStates(byte[]? states, int count)` in
  `NetProtocol` (compiled into the wire tests, no Unity/VRLog dependency) with the `unstated` arm
  covers all three byte-for-byte. Together with N3 (which stops the fourth, wrong, use).
- **T2 `TryReadFrame`** — `RemoteMapStory.cs:1279-1297` and `RemoteStorySync.cs:508-526`,
  byte-identical after normalisation (one qualifies `WorldUI.`). One `internal static` on a small
  new `Net/Remote/SharedWindowFrame.cs`.
- **T3 the 3 s** — `RemoteMapRoom.PeerStaleSeconds`, `RemoteMapStory.PeerStaleSeconds`,
  `RemoteStorySync.PeerStaleSeconds`, `RemoteTestTriggers.StaleSeconds`,
  `NetAvatarDriver.EnvClockStaleSeconds`, all `3f`, all "forget the peer's entry when their
  records stop" — the same rule `NetProtocol.StaleTimeoutSeconds` (3 s) applies to the avatar.
  Alias the five to it: the peer's tables then go with the peer's avatar by construction. Same
  value, so not a tuning change; constant-folded, so the guard sees only five vanished consts.

### 2.6 Doc-drift (Tier 0) — the wrong sentence and the right one

| # | file:line | claim | what is true |
|---|---|---|---|
| D1 | `Board/BoardTuning.cs:369-374` | "`[Cards] FanCloseDuration` is DELIBERATELY NOT SAMPLED… PENDING debt" | sampled by `:367-368`, the statement directly above; `:360-364` says it rides since ModBuild 306; `check-wire-coverage` reports 0 PENDING |
| D2 | `Remote/RemoteBoardFurniture.cs:5059-5062` | "The viewer's `[ButtonAnim] Enable` switch gates it… DURATIONS are the AUTHORED ones and not the viewer's tuning" | `:937` `new CapAnim(tuning.ButtonAnimOn, …)` — the OWNER's switch and the owner's record-28 durations. R2 called this the most dangerous line in the set: it describes the defect the code no longer has and invites its return |
| D3 | `Remote/RemoteControlBoard.cs:1036-1037` | "re-derived locally every frame from THIS client's read of who is at turn" | `Board/CharacterFocus.MarkForPeer:801-811` reads only `Peers[playerId]` (record 22); no local turn state |
| D4 | `Remote/RemoteControlBoard.cs:101-102` | "a runtime guard (`StripColliders`) destroys anything that ever slips through" | a build-time sweep at fixed call sites; lazily built content is never swept; inertness holds by each builder's discipline |
| D5 | `Remote/RemoteElementStrip.cs:202-205` | the vanilla `Inert` branch "writes exactly ONE field… and calls `ShowCreating()`" | `decompiled/GH.Runtime/InfusionElementUI.cs:102-117`: `ToggleEnable(true)`, `elementImage.enabled`, `creationImage.enabled`, the tooltip text, `SetAvailable(false)`; `ShowCreating()` only on a state change. The conclusion (mirror the widget) still follows |
| D6 | `Remote/RemoteCardArt.cs:1628-1631` | `RemoteHeldCardFace` "is not a `PeerBoardFade` follower and never fades today" | `RemoteHeldCardFace.cs:146` registers `PeerBoardFade.Follow(…, WhileOverBoard)`; that file's `:141` even quotes this sentence as the one it retired |
| D7 | `Remote/RemoteCardArt.cs:2058-2059` | "a face nobody drives keeps `Unnamed` and never reaches the instrument" | two drivers reach it at `Unnamed` (N4) |
| D8 | `Remote/RemoteBoardCard.cs:1344-1352` | "`FxSurface.Unnamed`'s own doc… OWES a correction plus an `Active` member; that file was not handed to this lane, so the active wash deliberately leaves `Surface` at `Unnamed`" | the `Active` member exists and `:1146` assigns it |
| D9 | `Remote/RemoteDecisionPrompt.cs:44-46` | "the mandatory-use variant renders the hint ALONE, without the active-bonus card names" | `:136-148`: record 33 carries the keys since ModBuild 307 and `MandatoryNames(names)` is prefixed |
| D10 | `Remote/RemoteMapRoom.cs:1176-1197` | (the log string, N5) | — |
| D11 | `Remote/RemoteDecisionWidgets.cs:819` | "which it must have been, or the Singleton would not be initialized" | an inference; `UIWindow.Hide` deactivates only conditionally (`UIWindow.cs:744-747`). Right conclusion, wrong argument — reworded to the check |
| D12 | `NetProtocol.cs` `ExtId*` block | ids 38, 40, 42 are holes with no sentence | add one line at the allocator note saying they are unallocated and why the next id is 46 |

`STALE-DOC-REFS.md` lines 29-36 (line numbers in that file are themselves stale; the sites are):
`BoardTuning.cs:880` `RoundCapShape` → the sibling is `GenericCapShape`, both through `Shape()`
(`:1424`); `NetFigures.cs:166` `Clear` → only `ReleaseRemote` clears it (the driver's teardown
calls it per player); `NetModule.cs:145` `SettingsPanel` → `WorldUI.VariantTilesTable` (the mask
picker, `:236-240`); `NetProtocol.cs:20826` `BoardUiRecordBytesWithCap` → `BoardUiRecordBytes` (3)
against `BoardUiRecordBytesLegacy` (2), the exact test `TryRead` makes; `PresenceState.cs:852`
`DecisionRoleNo` → `NetProtocol.DecisionRoleMax` (= `DecisionRoleShortRestNo`);
`RemoteBoardFurniture.cs:2678` `TextMeshPro.renderedHeight` → a real TMP member, sentence true,
line cleared as verified (a cref into TMP would risk CS1574 under TreatWarningsAsErrors);
`RemoteBoardVisibility.cs:49` `PeerBoardFade.Off` → `PeerBoardFadeMode.Off`;
`RemoteItemFan.cs:1588` `CollapseSeconds` → the collapse glides over the owner's
`FanCloseDuration` (record 28), not a local constant — sentence fixed.

### 2.7 Parallel construction across lanes → NEEDED-OUTSIDE (not commits)

- **P1 gaze bias** — `RemoteHandFan.UpdateGazeBias(:5352-5423)` ↔ `CardFan.UpdateGazeBias
  (:1297-1370)`. Bodies identical after normalisation except: the remote takes `dt` as a parameter
  and clamps `Mathf.Max(dt,0)`, the local reads `Time.unscaledDeltaTime`; the log tag/prefix. Six
  constants duplicated (pinned by `check-mirrors.sh:329-334`). The remote reads NO viewer value
  (the peer's synced head drives it). Merge target: a `GazeBiasLean` struct in `Cards/` with
  `Update(away, headForward, dt)`; both callers keep their own log line. Lane cards' file.
- **P2 enhancement stickers** — `RemoteFanEnhancementRefresh.cs:170-218` ↔
  `HandFanEnhancementRefresh.cs:155-203`: the per-sticker read/compare/write loop body is
  identical (the two arms of `SaveDataShared.ApplyEnhancementIcons`); only the counters' names and
  the log prefix differ. Merge target: `EnhancementStickerWriter.Apply(EnhancementButtonBase
  sticker, out EEnhancement want, out EEnhancement had)` in `Cards/`. Lane cards' file.
- **P3 ember column** — `RemoteControlBoard.cs:3748-3792` ↔ `PileViewer.cs:1288-1340`: the
  particle recipe (shape, velocity, noise, colour and size curves, renderer) is value-identical —
  a hardware-tuned recipe living in two files with no gate on it. Merge target: `SoftCueArt
  .ConfigureEmberColumn(ParticleSystem ps, float w, float h, Shader shader)` in `WorldUI/` (lane
  worldui-front); the remote keeps `SlabW/SlabH` as its inputs (the owner's slab size).

### 2.8 The two OPEN mirror-dial entries

- `RemoteMapRoom | CanvasScaleMm` — stale (N5): the mechanism reads the owner's since 480; only
  the diagnostic still reads the viewer's. Retired with N5.
- `RemoteBoardContent | PerfConfig.RemoteContentSeconds` — genuinely open and NOT a fix: the dial
  is `[Optimize] RemoteContentInterval`, a per-machine perf cadence that ships at 0 (= the 0.25 s
  default) and reaches eleven consumers. Closing it means either deleting the dial's effect
  (then the key must be marked INERT, not removed — §4 of the brief) or ruling it `not-1to1`
  ("a cadence the user set for their own machine", the allow file's own vocabulary). Either is a
  decision for the user; recorded, entry left OPEN.

## 3. R2 open items — status at `b40f8564`

| item | status | what would close it |
|---|---|---|
| F1, F2, F3 (mechanism), F4, F5, F6, F14, F17, F18 | closed in the 480 round (verified at the sites) | — |
| F3 (instrument) | open — N5 | this lane |
| F7, F9, F16 | documented in 480 (`8c6ea068`, `365257d5`), mechanism unchanged | F7 needs the game's four-style sampler; F9/F16 the two-canvas-width design |
| F8 | closed (`SharedWindowSizeLaw` extended to pose; allow entries retired in `d55a8564`) | — |
| F10 | open | the row seat from a TMP height measured on a VIEWER-language string — needs the owner's measured height on the wire (a new field, integrator's) |
| F11 | open | phase of the wanted-slot pulse: needs a phase/epoch byte (wire) |
| F12 | open | ruling (§2.8) |
| F13 | open | a 0.1 s colour cross-fade on mirrored plates — an animation addition; hardware |
| F15 | open | `BaseColor` captured after the local antique tint; needs the owner/viewer prompt overlap on hardware to confirm |
| F19 | open | the voice badge's presence/deflection are viewer-local by the class's stated intent; a ruling |
| NOTES 1, 3, 5, 6, 7, 8, 10, 11, 12, 13, 14 | unchanged | recorded; none is a source-demonstrable defect |
| NOTE 2 | open — N4 | this lane |
| NOTE 9 | open — D12 | this lane |

## 4. Negative results and leave-alone (examined, rejected)

- `NetAvatarDriver.OnDisable` resets 44 of the 60-odd `_lastSent*`/`_logged*` fields (`:828-897`).
  The unreset ones (`_lastSentHandCount`, `_lastSentItemCount`, `_lastSentItemClip`,
  `_lastSentBrowse*`, `_lastSentMaskSizeCode`, `_lastSentHandScaleCode`, `_lastSentBoardStyleCode`,
  `_lastSentPickBanner`, `_lastSentTooltip`, `_lastSentHighlight`, `_lastSentSlotCardSize`,
  `_lastSentUsableMask`, `_lastSentFaceCode*`, `_lastSentSeatCode*`, `_lastSentSpentMask`,
  `_lastSentFanSource`, `_lastFx*`, `_rx*`, `_loggedEnv*`) are all read by change tests that gate
  ONLY a log edge or a pre-emption; every record is written from live state on every packet. A
  stale one costs at most one missing "SENT" line in the next session. Not a defect; the
  hand-maintained list is the cost of the pattern and is named in N6's helper doc.
- `TickExtrasSend`'s pre-emption terms: every `xxxChanged` compares a value written back in the
  fill section on the same packet (the F17 class was searched for; none found).
- `RemoteAvatar.SetExtras`: every absent record decodes to its documented default (hand scale,
  mask size, fan source, seats, use bars, tuning pages reset on absence, focus cleared, …).
- `ReceivePrefix` returns `true` on any exception; the send-args array is pre-boxed; own echo is
  rejected in the driver; all as the registry says.
- The static peer tables (`RemoteMapRoom`, `RemoteMapStory`, `RemoteStorySync`,
  `RemoteTestTriggers`, `_peerEnv`) each prune at 3 s; a peer who disconnects mid-map-room loses
  their placard within 3 s. `RemoteMapRoom.Reset` destroys placard GameObjects.
- `NetCardFx.Reset` keeps `s_seq` — deliberate (the receiver detects change, the counter must stay
  dense across a session).
- All eighteen static `_logged*`/`s_logged*` latches are one-shot log latches (registry §II).
- `RemoteBoardContent.RefreshSeconds`, `NetModule.NameTagsWanted`, the three `RemoteTestTriggers`
  reads: verdicts in `MIRROR-DIALS.allow` re-read and agreed.
- `VersionDialog` reads the viewer's `CanvasScaleMm` — a local dialog, not a mirror.
- 2026-07 §8 leave-alone list re-checked: every item still present and still correct.

## 4b. Outcome — what Phase 2 did with the list above

Written after the fixes, so the review and its consequences stay in one file.

| finding | outcome | commit |
|---|---|---|
| D1–D12, S1–S8, X1 | done | `4438e684` |
| M1 (`PresenceSerializer`) | done — pure motion, vectors green | `2540d4c5` |
| M2 (`TickExtrasSend` ×3) | done — pure motion | `80fea450` (integrator snapshot), evidence in `85c28c8d` |
| M3 (`SetUseBars`) | done — pure motion | `85c28c8d` |
| T1, T2, T3 | done — identical output; **T3 was six copies, not five** | `3e30db4c` |
| N1, N4 | done | `49e69ffb` |
| N2, N6 | done | `f6868a0d` |
| N3, N5 (+ the stale `MIRROR-DIALS` entry) | done | `e5e25c33` |
| N7, N8, N9, R2 F8/F10–F13/F15/F19, `RemoteContentSeconds` | deferred, reasons in §2 and §3 | — |
| P1, P2, P3 | `NEEDED-OUTSIDE-net.md` | — |

**Two things this review got wrong, found by doing the work:**

1. **T3 was six copies, not five.** `RemoteSharedGaze.PeerStaleSeconds` was missed by the census
   in §0 and turned up only when the same grep was re-run after the first five were aliased. The
   scan had been written to find *declarations matching a name pattern*, and it found them; what it
   could not do is notice that a sixth file spelled the same fact a sixth way. A duplication census
   keyed on names is blind to exactly the duplication that matters.

2. **A dedup can disarm the gate that watches it, silently and while green.** Merging the two
   `TryReadFrame` copies (T2) took the wire suite from 210 164 assertions to 210 162 — still
   passing. The cause: `SharedWindowSizeVectors`' `no-unmediated-scale-write-in-an-applier` gate
   walks the `localScale` lines of those two files and asserts one per line, so moving the read out
   moved its assertions out with it and left the merged read ungated. Nothing in the guard, the
   build or the suite's exit code said so — only the assertion COUNT did, which is the argument for
   printing it in every commit message rather than just "green". The new file is in the gate's list
   now (§2.5, `3e30db4c`).

**And one the review got right for the wrong reason:** N5 was filed as "doc-drift inside a log
string". It is that, but the fix also had to keep a shouted run (`THE LEGIBILITY FACTOR IS THE
OWNER'S`) that `check-surface.py` counts as a grep token — the first draft dropped it and the gate
convicted the commit. A log line's *prose* is part of the tested surface here, not decoration.

## 5. Where the brief (and my prompt) were wrong

1. "Up to 4 read-only sub-agents" — withdrawn mid-lane (one helper at a time); all four died with
   the session limit before reporting anything. The review above is single-reader.
2. `STALE-DOC-REFS.md`'s line numbers for Net are two weeks stale (e.g. `NetModule.cs:111` is
   `:145`, `NetProtocol.cs:15036` is `:20826`); the symbols, not the lines, locate them.
3. The prompt says `MIRROR-DIALS.allow` "has 2 OPEN entries — check whether
   `RemoteMapRoom.CanvasScaleMm` is now stale": it is stale in substance and alive in letter,
   because the fix moved the mechanism and left the diagnostic reading the viewer's dial (N5).
4. R2 NOTE 2's "instrument-only; no picture is affected" is right, but the hand-fan driver's
   reason for staying `Unnamed` ("that file was not handed to this lane") no longer applies — both
   files are one lane now, which is the only reason N4 is a fix and not a finding.
5. The census names `RemoteControlBoard`↔`PileViewer` as a 35-line clone; at window 20 the group
   is the ember-column particle recipe (25 lines ×6 overlapping windows), not board logic.
6. `INVARIANTS-Net-Rig.md` is 2026-07 (29 files, 9 100 lines, `MaxSize 44`); its Part I is still
   exact for the fixed part of the extras packet and the rig packet, and wrong by omission for
   everything behind `PileBrowseExtensionBit` (45 records, `MaxSize` 2100). The build notes at
   the top of `NetProtocol.cs` are the current registry.
