# Review E — worldui-frame lane, Board/*.cs + Board/Patches/*.cs (read-only, 2026-09-08)

Worktree: /home/claw/gloomhaven_vr/.claude/worktrees/agent-a236ff8d959582747
Status: COMPLETE — all 25 files read whole; findings E-01..E-23.

## Files read (running log)
- read: BRIEF.md, CHARTER.md, STALE-DOC-REFS.md, FRAME-ORDER.lock, INSTRUMENT-WRITES.baseline, VRLog.cs (whole), INVARIANTS-Hands-Board-Core §6-7, §9 TickGuard, §10 hex, §13 table, §14, §15-16; REVIEW-worldui-frame §9 table; redundancy-audit R14/R30/R42/§6
- read: Board/BoardModule.cs (223), Board/BoardDriver.cs (85) — FRAME-ORDER marker text at :37 byte-identical to lock line; Board/BoardConfig.cs (101), Board/CameraArrivalGuard.cs (83)
- read: Board/BoardPick.cs (393), Board/BoardClickDriver.cs (807), Board/AoeControl.cs (279), Board/TargetingUx.cs (160)

## Findings (appended as found; final ranking at the end)

### E-01 — `BoardClickDriver.DecideTap` swallows a throwing target test at `Warn` = silent at the shipped default
- **File:line:** `src/GloomhavenVR/Board/BoardClickDriver.cs:366-371`
- **Class:** risk-gap
- **Tier:** 3 (one-word tier promotion, no behaviour change)
- **Evidence:** `catch (System.Exception ex) { VRLog.Warn("Board", "[Tap] valid-target test threw — falling back to SELECT ..."); return TapVerdict.Selecting("unknown (target test threw)"); }`. `VRLog.Warn` gates on `Level >= Debug` (`Core/VRLog.cs:152`), so at `[General] LogLevel = Info` a fingertip tap whose decision table threw is silently turned into a click and the log carries nothing. Same shape as redundancy-audit §6.3 (`EscMenuShowSafety`, fixed to `Alert` in ModBuild 439): "a swallowed throw must never be silent, because the swallow is a symptom report, not a fix". Bounded by taps (one line per tap, never per frame), so no flood risk.
- **Proposed action:** `VRLog.Warn(` → `VRLog.Error(` on line 368 (a throw is the `Error` tier's definition: "Something failed"). Wording unchanged.
- **Guard expectation:** `CHANGED` confined to `BoardClickDriver` (one call target).
- **Risk if wrong:** one extra line per throwing tap in a quiet log.
- **Cross-lane:** none

### E-02 — The two fingertip hardware-evidence lines (`[Tap] hex … → SELECT/PING`, `FINGERTIP TOUCH commit:`) print nothing at the shipped default
- **File:line:** `src/GloomhavenVR/Board/BoardClickDriver.cs:565-567` (`LogTapDecision`), `:637` (`LogTouchCommit`), `:592` (`FINGERTIP PING:`)
- **Class:** risk-gap
- **Tier:** 3 (tier promotion only)
- **Evidence:** `LogTapDecision`'s own doc (`:554-558`): "ONE line per fingertip tap on a hex, stating the decision AND the reason, so the next hardware log shows the rule working without guesswork. Not throttled on purpose". `LogTouchCommit`'s doc (`:614-618`): "The hardware-log proof of a fingertip commit". Both are `VRLog.Info` → gated on `Level >= Debug`, i.e. absent from a default-level hardware log. The 2026-09-03 user report quoted at `:266-267` ("Der 'Mit der Fingerspitze auswählen' Test ist nicht erfolgreich obwohl ich mit der Fingerspitze einen Ping ausgelöst habe") was diagnosed by reasoning, and the fix's proof line (`FINGERTIP PING:` / `[Tap] … → PING`) is invisible unless he sets Debug. Rate: bounded by the commit edge + 0.15 s cooldown per hand and 1 s per hand+hex for the commit line; a deliberate tap is a deliberate line.
- **Proposed action:** promote `LogTapDecision` (`:565`) `VRLog.Info` → `VRLog.Note`, and `LogTouchCommit`'s un-throttled branch (`:637`) `VRLog.Info` → `VRLog.Note` (the throttled repeat at `:632` stays `Debug`). `FINGERTIP PING:` (`:592`) is redundant with the `[Tap] → PING` line for the same tap; leave at `Info`. Integrator's call whether the census budget for `Board` shipped lines (currently 0/9 in this file) tolerates two event lines.
- **Guard expectation:** `CHANGED` confined to `BoardClickDriver` (call targets only).
- **Risk if wrong:** two extra lines per deliberate fingertip tap in a default log.
- **Cross-lane:** none

### E-03 — `AoeControl.Reset()` (called from `BoardModule.Shutdown`) leaves `_stickHand` and `_loggedClaimThrow` latched across a hot reload
- **File:line:** `src/GloomhavenVR/Board/AoeControl.cs:91-95` vs `:86`, `:89`; caller `Board/BoardModule.cs:209-212` ("Hot-reload hygiene: everything here is static").
- **Class:** risk-gap (minor)
- **Tier:** 3 (two assignments)
- **Evidence:** `Reset()` clears only `_armed` and `_nextRepeatTime` — deliberately, because `Tick` also calls it on a stick-hand change (`:179`) and must NOT clear `_stickHand` there. But `Shutdown` uses the same method, so after a hot reload `_stickHand` still holds the old side: the edge line "AoE rotation reads the X thumbstick" (`:182`) never re-fires, and `_loggedClaimThrow` stays true so a second throw in `WouldRotate` (`:147`) is never logged again in the new session. No player-visible effect (rotation itself keys off `ResolveRotationHand()` each frame).
- **Proposed action:** none required for behaviour; if acted on, add a separate `ResetForShutdown()` (or two lines in `Shutdown`) that also nulls `_stickHand` and clears `_loggedClaimThrow`. Do NOT add them to `Reset()` itself (that would re-emit the edge line on every hand change and is what `:179` relies on).
- **Guard expectation:** `CHANGED` confined to `AoeControl` (+ `BoardModule` if the call site changes).
- **Risk if wrong:** a spurious edge log line.
- **Cross-lane:** none
- read: Board/BoardPing.cs (265), Board/SelectionReadyHighlighter.cs (252), Board/SelectionOwnershipFallback.cs (155), Board/HexHighlightFix.cs (669)

### E-04 — `BoardPing`: the "SILENT-GATE FIX" rejection lines, the self-disarm line and the swallowed-throw line are all at the debug tier
- **File:line:** `src/GloomhavenVR/Board/BoardPing.cs:111,119` (`[Ping] press rejected —` ×2, `Info`), `:171` (`[Ping] … rejected — neither UIScenarioMultiplayerController nor PingManager`, `Warn`), `:185` (`[Ping] game ping call threw`, `Warn`), `:253,259` (`PingTile … not found — VR pings stay LOCAL-ONLY` / `no ping entry point found … hex ping disabled`, `Warn`)
- **Class:** risk-gap
- **Tier:** 3 (tier promotion only)
- **Evidence:** class doc `:36-39`: "SILENT-GATE FIX (same MP test): every rejection between the A-press and the actual ping call used to be a silent return, which made 'the joining peer cannot ping at all' undiagnosable from logs. A press is an explicit user action now: each rejected press logs its reason exactly once". Every one of those lines is `VRLog.Info`/`VRLog.Warn`, both gated on `Level >= Debug` (`Core/VRLog.cs:152,162`) — at the shipped `LogLevel = Info` the chain is exactly as silent as before the fix. The census for this file is 0 shipped / 10 debug. Two of the lines are not even diagnostics but self-disarms: `:259` "hex ping disabled" and `:253` "VR pings stay LOCAL-ONLY" are the `Error`/`Alert` definition in `VRLog`'s own remarks ("a subsystem that disarmed itself" / "something they care about is not working"). All are one-shot or press-edge bounded.
- **Proposed action:** `:259` `Warn` → `Alert` (feature off, player-actionable: report it); `:253` `Warn` → `Alert`; `:185` `Warn` → `Error` (a throw); `:111,119` `Info` → `Note` (one line per rejected press; a press is deliberate). `:171` `Warn` → `Alert` (same shape as `:259`, reached per press but only while both singletons are dead). Wording untouched (`[Ping]` is a grep token).
- **Guard expectation:** `CHANGED` confined to `BoardPing` (call targets only).
- **Risk if wrong:** a handful of extra lines per session in a default log; the press-rejected lines fire once per A-press that misses the board — pointing the laser into the sky and pressing A repeatedly is the worst case, still one line per press.
- **Cross-lane:** none

### E-05 — Two lines INVARIANTS §15 lists as hardware grep tokens are at the debug tier: `stable hex decal ZTest=` and `HEX PROJECTOR`
- **File:line:** `src/GloomhavenVR/Board/HexHighlightFix.cs:663` (`stable hex decal ZTest=`), `:363` (`HEX PROJECTOR guard [install]`)
- **Class:** risk-gap
- **Tier:** 3 (tier promotion only)
- **Evidence:** `INVARIANTS-Hands-Board-Core.md:2267` lists `stable hex decal ZTest=` under "Log lines that are grep tokens, not debug residue". `ApplyOcclusionKnobs` doc `:657-659`: "only the fact of which occlusion contract this build ships, which a hardware log still needs". `InstallProjectorGuard` doc `:288-289`: "Logs the single HEX PROJECTOR line … the next hardware log needs to prove the guard ran, even when the count is zero". Both are `VRLog.Info` → absent at the shipped default. Both are once-per-session (ZTest: `_lastLoggedZTest` latch; projector: `forceLog` install line + `MaxProjectorLogLines = 4`).
- **Proposed action:** `:663` `Info` → `Note`; `:363` `Info` → `Note`. Nothing else.
- **Guard expectation:** `CHANGED` confined to `HexHighlightFix` (nested patch type for `:663`).
- **Risk if wrong:** ≤ 5 extra lines per session.
- **Cross-lane:** none

### E-06 — `HexHighlightFix` doc still calls `SwapStableShader`/`StableZTest`/`StableDepthBias` "config" and speaks of "live config edits"
- **File:line:** `src/GloomhavenVR/Board/HexHighlightFix.cs:28` ("PROPER FIX (this class, config <c>SwapStableShader</c>)"), `:47-50` ("Config <c>StableZTest</c> … and <c>StableDepthBias</c> … are re-applied on every swap/postfix for on-device experiments"), `:52` ("or <c>SwapStableShader=false</c>"), `:617-618` ("still re-assert the occlusion knobs so live config edits take effect"), `:651` ("Logged when the applied ZTest changes") — against `:170,183,194` (all three are `private const` since the 2026-08-22 settings audit, stated at `:150-158` and `:222-227`) and `:657-659` ("ONE line per session now, not one per change").
- **Class:** doc-drift
- **Tier:** 0 (comment only)
- **Evidence:** quoted above; the constants block and `BindConfig` both say the dials were removed, the class summary and two method docs still describe them as dials.
- **Proposed action:** `:28` "PROPER FIX (this class; <c>SwapStableShader</c> is a constant, always on)"; `:47-50` "<c>StableZTest</c> (4 = LEqual; 8 would be vanilla Always) and <c>StableDepthBias</c> are constants since the 2026-08-22 settings audit and are re-applied on every swap/postfix; the applied ZTest is logged once per session."; `:52` "FALLBACK (old bundle without the shader, or a swap that threw)"; `:617-618` "still re-assert the occlusion knobs (they ride every postfix so a material re-creation can never lose them)"; `:651` "Logged once per session."
- **Guard expectation:** empty (comments are not in the snapshot).
- **Risk if wrong:** none.
- **Cross-lane:** none

### E-07 — `HexHighlightFix.Reset()` does not clear `_knobsBypassLogged`
- **File:line:** `src/GloomhavenVR/Board/HexHighlightFix.cs:456-502` vs `:238`
- **Class:** risk-gap (minor, hot-reload only)
- **Tier:** 3 (one assignment)
- **Evidence:** `Reset()` clears `_materialDumps`, `_errorLogs`, `_lastLoggedZTest`, `_projectorLogLines` but not `_knobsBypassLogged`, so after a hot reload the "stable shader swap active — layer-kill fallback knobs bypassed" line (`:581`) never prints again. Debug tier, cosmetic.
- **Proposed action:** add `_knobsBypassLogged = false;` beside `_lastLoggedZTest = -1;` at `:501`. Optional.
- **Guard expectation:** `CHANGED` confined to `HexHighlightFix.Reset`.
- **Risk if wrong:** none.
- **Cross-lane:** none

### E-08 — R42 for `SelectionReadyHighlighter.cs:223` is NO LONGER TRUE (already on `VRLogThrottle`)
- **File:line:** `src/GloomhavenVR/Board/SelectionReadyHighlighter.cs:84,89,220-221,239-244`
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** the split line goes through `_splitLog = new VRLogThrottle(SplitHeartbeatSeconds = 30f)`; the allocation-free hash prefilter is correctly OR'd with `_splitLog.HeartbeatDue(now)` (`:220`) so the heartbeat is not swallowed; `Reset()` on phase exit (`:167`). Not hand-rolled any more. The heartbeat line itself is `VRLog.Info` (debug tier) — consistent with its role as a tick-liveness probe for a debug log; no promotion proposed.
- **Proposed action:** none. Record in the redundancy-audit R42 row as fixed.
- **Guard expectation:** —
- **Risk if wrong:** —
- **Cross-lane:** none
- read: Board/FocusDriver.cs (651), Board/FocusCue.cs (544; holds 3 top-level types FocusCue/UiRing/WorldFrame)
- read: Board/BoardFrame.cs (1166)

### E-09 — R30(a) / redundancy-audit §6.4 `FocusDriver.Carrier` is NO LONGER TRUE
- **File:line:** `src/GloomhavenVR/Board/FocusDriver.cs:267-283`
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** `Carrier` now calls `Core.TickGuard.NoteThrow("Board", "FocusDriver." + name, e, …)` (`Core/Perf/TickGuard.cs:149` exists), i.e. the shared static store, the shared 10 s repeat window and the `Error` tier. The `HashSet<string> _reported` is gone. The method doc records the change (`:246-265`).
- **Proposed action:** none; mark R30(a) and §6.4 as fixed in the redundancy audit.
- **Cross-lane:** none

### E-10 — `FocusDriver.ScenarioLive` guard against `GlobalData.CurrentGameState` throwing: VERIFIED against decompiled source
- **File:line:** `src/GloomhavenVR/Board/FocusDriver.cs:315-324`; game `decompiled/GH.Runtime/GlobalData.cs:563-569`
- **Class:** leave-alone (comment verified)
- **Tier:** n/a
- **Evidence:** `GlobalData.CurrentGameState`'s Campaign branch reads `AdventureState.MapState.IsInScenarioPhase` with no null check exactly as the doc claims (`if (GameMode == EGameMode.Campaign) { if (AdventureState.MapState.IsInScenarioPhase) …`). The guard is confined to that branch. Correct.
- **Proposed action:** none. (The doc's "The same guard belongs there eventually" for `Net.RevealGate` is a NEEDED-OUTSIDE candidate for lane net if the campaign-menu NRE flood is still observed; not raised here as no evidence it recurs.)
- **Cross-lane:** none

### E-11 — The rectangle FALLBACK board frame is never adopted into the furniture band (sortingOrder 0 — the exact "perspective defect" `BoardFrame`'s doc describes)
- **File:line:** `src/GloomhavenVR/Board/FocusDriver.cs:610-617` (`PlayTray.AdoptFurniture(_boardFrame?.RootObject)` runs BEFORE the `_rectFrame = WorldFrame.Build(...)` fallback and is passed null on that path; `_rectFrame` is never adopted); `Board/FocusCue.cs:533-537` (`WorldFrame.Renderers` exists "for the panel-compositing ladder, where a caller already ranks its own renderers" — nobody ranks this one).
- **Class:** risk-gap (fallback path only)
- **Tier:** 3
- **Evidence:** `BoardFrame.cs:26-54` states the mechanism: a transparent renderer at `sortingOrder 0` is painted BEFORE every converted panel (≥ 100) and the board furniture band, "so a menu or a text plate that is spatially BEHIND the board still painted over it". The traced stroke was fixed by `AdoptFurniture`; the `WorldFrame` rectangle built on the procedural fallback board (`BoardFrame.Build` returns null when `TrayVisual` is absent, `:427-429`) stays at 0. Reachable only when the bundled board asset is missing (no `TrayVisual` child) — a degraded install, not the shipped path.
- **Proposed action:** none this round — the fallback board is itself the degraded path; if acted on, expose `WorldFrame`'s root (or reuse `Renderers`) and call `PlayTray.AdoptFurniture` after `WorldFrame.Build` in the `_boardFrame == null` branch. Design decision (does the fallback board have a furniture band at all?) → integrator.
- **Guard expectation:** `CHANGED` confined to `FocusDriver`/`WorldFrame` if acted on.
- **Risk if wrong:** none on the shipped board.
- **Cross-lane:** none

### E-12 — `FocusCue.cs` holds three top-level types (`FocusCue`, `UiRing`, `WorldFrame`)
- **File:line:** `src/GloomhavenVR/Board/FocusCue.cs:97,344,457`
- **Class:** structure-naming
- **Tier:** 1
- **Evidence:** `UiRing` (98 lines) and `WorldFrame` (88 lines) are independent builders consumed from `Net/Remote*` as well; the file name names only the palette. A reader looking for `WorldFrame` (used by `Net.RemoteFocusOutline`) will not find a `WorldFrame.cs`.
- **Proposed action:** optional pure motion: `UiRing` → `Board/UiRing.cs`, `WorldFrame` → `Board/WorldFrame.cs`, class docs travel verbatim. Recommend NOT doing it this round: CHARTER §2 — nobody is demonstrably lost (the `FocusCue` class doc names both builders in its second paragraph) and the motion touches nothing else.
- **Guard expectation:** empty (whole-type moves are invisible to the snapshot); `MOVED` on `GloomhavenVR.csproj` only if it lists files.
- **Risk if wrong:** none.
- **Cross-lane:** none
- read: Board/CharacterFocus.cs (2079, whole, three reads)
- ran read-only: `bash scripts/check-mirrors.sh` → PASS ("41 mirrored-constant groups agree; 7 shared-expression groups have one implementation each; 2 subset-guard groups keep the game's terms together"); `python3 scripts/check-mirror-dials.py` → PASS (8 reads, 0 owner-violations, 2 OPEN are not in Board/); `loadbearing.py src/GloomhavenVR/Board` → see E-16; `docs/PATCH-INVENTORY.md` rows 41-64 list every Board patch class with its `BoardModule` registration line (all registered once).

### E-13 — STALE-DOC-REFS `Board/CharacterFocus.cs:935 PinRefusal` — CLEARED (corrected sentence)
- **File:line:** `src/GloomhavenVR/Board/CharacterFocus.cs:1075-1077` (the table's line number 935 has drifted; the `<c>PinRefusal</c>` demotion now sits at `:1076`)
- **Class:** doc-drift
- **Tier:** 0 (comment only)
- **Evidence:** `PinRefusal` never existed as a member at HEAD; the job is split across `PinRefuses(CPlayerActor wanted, out CPlayerActor? pinned)` (`:646`, the allocation-free predicate) and `PinReason(CPlayerActor? pinned)` (`:653`, the sentence `LogRefusal` prints), by design (`:642-644`: "Split from PinReason so the predicate allocates NOTHING"). Current sentence: "Exactly TWO reasons can ever appear in that position: the card-selection phase (<see cref="SecretWindowReason"/>, the global gate) and <c>FOCUS PIN</c> (<c>PinRefusal</c>, the actor-dependent one, bounded by a live hex pick belonging to one of this player's characters)."
- **Proposed action:** replace `(<c>PinRefusal</c>, the actor-dependent one, …)` with `(<see cref="PinReason"/>, minted when <see cref="PinRefuses"/> says no — the actor-dependent one, bounded by a live hex pick belonging to one of this player's characters)`. Then delete the `Board/CharacterFocus.cs | 935 | PinRefusal` row from `.planning/refactor/STALE-DOC-REFS.md`.
- **Guard expectation:** empty.
- **Risk if wrong:** none.
- **Cross-lane:** none

### E-14 — `CharacterFocus.Reset()` does not clear `_lastOwnershipCensus`, so the `HW-VERIFY` `[Ownership] HAND FAN` falsifier can stay silent for a whole second scenario
- **File:line:** `src/GloomhavenVR/Board/CharacterFocus.cs:2032-2047` (`Reset`) vs `:1328` (`_lastOwnershipCensus`), `:1417-1419` (the change gate), `:1421-1431` (the `HW-VERIFY` Note)
- **Class:** risk-gap
- **Tier:** 3 (one assignment)
- **Evidence:** `LogOwnershipCensus` is change-gated on a signature of `{actor name, online, IsUnderMyControl, answerable, byList, claimants, localId, readOnly, duplicate}`. It is nulled only on the "no hand" branch (`:1364`) — i.e. only if `ResolveHand(null)` happens to run before teardown. `Reset()` ("scenario teardown, session end, module shutdown") clears every other latch (`_loggedFocusId`, `_loggedFloorId`, `_lastRefusal*`, `_followedTurnId`, both peer dictionaries) but not this one. Second scenario in one session, same party, same assignment ⇒ the first census signature equals the last one of the previous scenario ⇒ the ONE shipped-level line this file has, the one its own doc calls "THE FALSIFIER" for the 2026-09-07 item-10 duplicate-claimant report, does not print for that scenario at all. A missing falsifier reads exactly like "the duplicate did not happen".
- **Proposed action:** add `_lastOwnershipCensus = null;` to `Reset()` (beside `_loggedFloorId = null;`). Wording of the line untouched (`HW-VERIFY`).
- **Guard expectation:** `CHANGED` confined to `CharacterFocus.Reset`.
- **Risk if wrong:** one extra `[Ownership] HAND FAN` line at the start of each scenario — which is the line's stated purpose.
- **Cross-lane:** none (verify who calls `CharacterFocus.Reset()` — grep below)

### E-15 — The character-focus evidence chain (`switch REFUSED`, `now looking at`, `FOCUS PIN engaged`, `SELECTION GUARD`, `[Focus] peer cue`, `[Focus] the game is waiting on`) is entirely at the debug tier
- **File:line:** `src/GloomhavenVR/Board/CharacterFocus.cs:1089` (`switch REFUSED`), `:1051` (`now looking at`), `:694` (`FOCUS PIN engaged`), `:1715` (`SELECTION GUARD`), `:1989,2018` (`peer cue`), `:1171-1207` (`AUTO-FOLLOW …`, 4 variants), `:1215` (`cleared`); `Board/FocusDriver.cs:396,429` (`[Focus] the game is waiting on`), `:344,357` (MR palette)
- **Class:** risk-gap
- **Tier:** 3 (tier promotion only)
- **Evidence:** each line's own doc names it as hardware evidence: `LogRefusal` `:1071-1077` "Format is fixed by the 2026-08-08 ruling … so 'blocked again' is never a guess: grep the log for `switch REFUSED`"; `LogFloor` `:1700-1703` "so the next multiplayer log proves both halves"; `LogPeerCue` `:1973-1978` "so a hardware round can be read from BOTH machines: grep … `[Focus] peer cue` on the observer's"; `FocusDriver.TickAttentionLog` `:375-380` "It exists because this is the exact question the 2026-08-08 hardware report was about … so a future 'it is gone again' is answerable from the log alone"; and `LocalFloorHand`'s evidence paragraph `:1632-1639` quotes `[Focus] cleared` / `[Focus] now looking at` lines read off `LogOutput.log` — lines that, since the 2026-08-30 tier re-decision, no longer appear in a default-level log. Every one is edge- or change-gated (the refusal additionally rate-limited to 5 s); volume is "one line per portrait click / turn hand-off / peer transition", i.e. tens per session — inside `Note`'s stated budget ("Tens of lines in a session, not hundreds").
- **Proposed action:** promote to `VRLog.Note`: `:1089` (`switch REFUSED` — the ruling's own grep token), `:694` (`FOCUS PIN engaged`), `:1715` (`SELECTION GUARD`), `:1051` (`now looking at`). Leave at `Info`: the four `AUTO-FOLLOW` variants (two of them are "not needed" no-ops), `cleared`, the MR palette pair, `LogFocusOnce`. `peer cue` (`:2018`) and `the game is waiting on` (`FocusDriver.cs:429`): integrator's call — both are the observer-side halves of the same evidence and change-gated, but a 4-player session with many hand-offs could reach ~100 lines each.
- **Guard expectation:** `CHANGED` confined to `CharacterFocus` (and `FocusDriver` if `:429` is taken).
- **Risk if wrong:** tens of extra lines per session in a default log.
- **Cross-lane:** none
- read: Board/Patches/HoverPickPatch.cs (625), Board/Patches/PingNameTag.cs (609)

### E-16 — R14 (`PingNameTag` never ranked on the panel ladder) is NO LONGER TRUE
- **File:line:** `src/GloomhavenVR/Board/Patches/PingNameTag.cs:511-528` (`Net.BoardVisual.OrderWithPanels(_tagRenderers, eyeDistance); Net.BoardVisual.OrderWithPanels(_canvas, eyeDistance);` every Tick after the billboard), `:146-150` + `:536-548` (the renderer cache with the same refresh contract as `OwnerTag`/`RemoteNameTag`)
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** the audit's `rg -c 'OrderWithPanels|…'` = 0 is stale: both routes (uGUI clone via the `Canvas` overload, TMP fallback via the `Renderer[]` overload) are ranked, and the comment at `:511-524` is the audit's own R14 text. `Net/Board/BoardVisual.cs:125,143` carry both overloads. The two-line fix is IN.
- **Proposed action:** none in this file. Check `Net/Board/BoardVisual.cs:158`'s exemption list still reads correctly (it names the owner tag as "driven by somebody else"; the ping tag is now driven by itself and could be named there for completeness — lane net, cosmetic; see NEEDED-OUTSIDE note at the end).
- **Cross-lane:** NEEDED-OUTSIDE (doc only, optional): `Net/Board/BoardVisual.cs:~158` — add `PingNameTag` beside `OwnerTag` in the "ranked by somebody else" exemption comment.

### E-17 — `PingNameTag_Patch.TargetMethod` null (feature disabled) and the postfix's swallowed throw are at the debug tier
- **File:line:** `src/GloomhavenVR/Board/Patches/PingNameTag.cs:53` (`[Ping] PingManager.Ping3DElement not found — ping name tags disabled`, `Warn`), `:68` (`name-tag postfix failed (suppressed)`, `Warn`), `:452` (`game tooltip clone unavailable … use the mod-drawn label this session`, `Warn`, once), `:601,606` (reflection fallbacks, `Warn`, once)
- **Class:** risk-gap
- **Tier:** 3 (tier promotion only)
- **Evidence:** same mechanism as E-04: `VRLog.Warn` prints nothing at `LogLevel = Info`. `:53` is a self-disarm of a shipped MP feature ("degrades by design … patch then no-ops" — but a no-op nobody can see in a default log); `:452`'s own doc says "the design regression should be visible in logs" and at `Warn` it is not; `:68` swallows a throw per ping.
- **Proposed action:** `:53` `Warn` → `Alert`; `:68` `Warn` → `Error`; `:452` `Warn` → `Note` (one line per session, a degrade the player may want to report); `:601,606` leave (reflection fallbacks with a working fallback name).
- **Guard expectation:** `CHANGED` confined to `PingNameTag_Patch` / `PingNameTag`.
- **Risk if wrong:** ≤ 2 extra lines per session; `:68` one per throwing ping.
- **Cross-lane:** none

### E-18 — loadbearing census (`CharacterFocus.LogRefusal/LogFloor/LogFocusOnce`, `HoverPickPatch.LogPassthrough`, `BoardClickDriver.LogTouchCommit`, `HexHoverClear.LogShown/LogCleared`, `SelectionReadyHighlighter.LogIfChanged`, `AllCardsViewerBlock.Log`, `SelectionGuardPatches.Log`, `EnemyInfoPhaseSkip.LogOnce`): NONE is load-bearing — nothing for `INSTRUMENT-WRITES.baseline`
- **File:line:** `Board/CharacterFocus.cs:1086-1088,1714,2056` / `Board/Patches/HoverPickPatch.cs:474-480` / `Board/BoardClickDriver.cs:635-636` / `Board/Patches/HexHoverClear.cs:236-250` / `Board/SelectionReadyHighlighter.cs:222` / (AllCardsViewerBlock, SelectionGuardPatches, EnemyInfoPhaseSkip — see their entries below once read)
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** `python3 .planning/refactor/census-2026-08/loadbearing.py src/GloomhavenVR/Board` lists the "outside" readers; every one is a RESET (`CharacterFocus.Reset :2038-2043`, `ResolveHand :1475` re-arming `_loggedFloorId`, `BoardClickDriver.Reset :183-184`, `HoverPickPatch.Reset :208-210`, `HexHoverClear :182-183` (the InScenario-false reset), `SelectionReadyHighlighter.Tick :168`) or a name collision (`EnemyInfoPhaseSkip._logged` vs `FigureClothHands._logged`; `LogPeerCue`'s "`_`" is the discard). No mechanism reads a field a `Log*` method writes. The census is an upper bound and here it is all reset-only.
- **Proposed action:** none; do not add to the baseline.
- **Cross-lane:** none
- read: Board/Patches/PickPhaseInitiativeTrack.cs (539), Board/Patches/EnemyInfoPhaseSkip.cs (447), Board/Patches/SelectionGuardPatches.cs (438), Board/Patches/HexHoverClear.cs (375)

### E-19 — `EnemyInfoPhaseSkip` presses the HOST's "Fortfahren" (a game action, `ConfirmAction` on the wire) and records it only at the debug tier; its disarm line likewise
- **File:line:** `src/GloomhavenVR/Board/Patches/EnemyInfoPhaseSkip.cs:258-274` (`ENEMY-INFO PHASE SKIPPED` via `LogOnce` → `VRLog.Info`, `:368`), `:406` (`the empty-phase skip threw and was DISARMED`, `Warn`, once per session)
- **Class:** risk-gap
- **Tier:** 3 (tier promotion only)
- **Evidence:** the SKIPPED branch calls `button.OnClickInternal()` (`:274`), which runs `Synchronizer.SendGameAction(GameActionType.ConfirmAction, …)` for the whole table (class doc `:61-68`). A mod-initiated game-phase advance that a default-level log cannot show is unattributable when the user asks "why did the enemy-info screen vanish" — the exact shape of `escape-never-measured-its-premise`. One line per reveal at most (`_logged`), and only on the host. `:406` is a self-disarm (`Error` by `VRLog`'s own definition), printed at the debug tier.
- **Proposed action:** split `LogOnce` at the SKIPPED call site: keep `LogOnce` for the three no-action verdicts (`:193`, `:212`, `:224` stay `Info`) and emit the SKIPPED line (`:258`) through `VRLog.Note` — minimal form: add a `bool shipped = false` parameter to `LogOnce` and pass `true` at `:258`, choosing `Note` vs `Info` on it (the `_logged` latch is unchanged). `:406` `Warn` → `Error`. Also `PickPhaseInitiativeTrack.cs:534` (`the decision-flow track refill threw and is backing off for 60 s`, `Warn`, once) → `Error` for the same reason.
- **Guard expectation:** `CHANGED` confined to `EnemyInfoPhaseSkip` (+ `PickPhaseInitiativeTrack` for `:534`).
- **Risk if wrong:** one line per empty enemy-info reveal in the host's default log.
- **Cross-lane:** none

### E-20 — `EnemyInfoPhaseSkip.Reset()` and `PickPhaseInitiativeTrack.Reset()` do not clear `_reportedThrow`
- **File:line:** `src/GloomhavenVR/Board/Patches/EnemyInfoPhaseSkip.cs:125-131` vs `:122,:405`; `Board/Patches/PickPhaseInitiativeTrack.cs:342-349` vs `:335,:533`
- **Class:** risk-gap (minor; hot reload / next scenario)
- **Tier:** 3 (one assignment each)
- **Evidence:** both `Reset()` methods are documented "scenario teardown / hot reload" and clear every latch except the throw-report one, so the SECOND scenario (or the reloaded module) that throws in either tick never reports it — a permanently dead skip/refill after the first throw looks like "it never armed" (`a-held-instrument-reads-as-dead`). Also `HexHoverClear._errorLogs` (`:157`) and `HoverPickPatch` are fine (`HoverPickPatch.Reset` clears `_errorLogs`; `HexHoverClear` has no Reset at all and is not on `BoardModule.Shutdown`'s list — 3 lines per process lifetime, acceptable).
- **Proposed action:** add `_reportedThrow = false;` to both `Reset()` bodies.
- **Guard expectation:** `CHANGED` confined to the two static types' `Reset`.
- **Risk if wrong:** none.
- **Cross-lane:** none

### E-21 — INVARIANTS §7 "The action-phase select guard patches the HUMAN CLICK seam only": still TRUE for target/seam, but its stated RULE is superseded by `CharacterFocus`
- **File:line:** `src/GloomhavenVR/Board/Patches/SelectionGuardPatches.cs:98-104` (focus branch returns false FIRST), `:153-168` (the original action-phase reject, now reachable only for an exhausted hero's stale portrait, as the comment at `:153-157` says); `.planning/refactor/INVARIANTS-Hands-Board-Core.md:1122-1139`
- **Class:** doc-drift (planning doc, integrator's)
- **Tier:** 0
- **Evidence:** the invariant's "Rule: On a non-current locally-controlled player during the action phase, play the game's own invalid-click SFX and return false" is no longer what a laser click does in the action phase — `CharacterFocus.TryFocus` takes the click as a read-only VIEW change (user ruling 2026-08-08) and the guard's own reject is the fourth branch. The "Breaks if" clause (moved to `InitiativeTrack.Select`, or the locally-controlled filter dropped) is still the correct veto, and the patch target is unchanged (`docs/PATCH-INVENTORY.md:60`).
- **Proposed action:** none in code. Integrator: add one sentence to the invariant ("Since the 2026-08-08 free-focus ruling the click is first offered to `CharacterFocus.TryFocus`; the reject below is reached only when focus is refused or the actor is not a focus target") — and note that §13's Board table predates eight registered classes (`HoverPickPatch`, `ProjectorModifier_Awake_Patch`, `InteractabilityManager_PortraitFocusBypass`, `Choreographer_TileHandler_OwnershipGuard`, `AllCardsViewerBlock`, `CharacterManager_OnControlReleased_Fallback`, `PingNameTag_Patch`, `InitiativeTrack_ShowMonsterClasses_ArmSkip`/`_Update_TickSkip`); `docs/PATCH-INVENTORY.md` is the current inventory.
- **Cross-lane:** none
- read: Board/Patches/PlacementDiagnostics.cs (178 — KEEP decision note confirmed at the top, `:12-34`), Board/Patches/AllCardsViewerBlock.cs (176), Board/Patches/PickingPatches.cs (151)
- ran read-only: `python3 scripts/check-hw-verify.py` → PASS (555 marked lines, all at a shipped tier — the `HW-VERIFY` Notes in `CharacterFocus`, `HoverPickPatch`, `PickPhaseInitiativeTrack` included)

### E-22 — Two unreferenced accessors: `BoardFrame.Renderer` (its doc names a caller that uses `RootObject` instead) and `WorldFrame.Renderers`
- **File:line:** `src/GloomhavenVR/Board/BoardFrame.cs:407-410` (`internal MeshRenderer? Renderer`), `Board/FocusCue.cs:533-537` (`internal Renderer[] Renderers`)
- **Class:** dead (Tier 0 candidates) + doc-drift
- **Tier:** 0
- **Evidence (§5 checklist):** (1) not a Harmony target/patch method; (2) not a Unity message, not serialized; (3) no `nameof`/`AccessTools`/`Traverse`/`GetMethod`/string-literal reach over `src/` and `tests/`; (4) not a config key; (5) not a log token (`.planning`/`docs` grep: none); (6) no debug-menu reference; (7) no `tests/` pin; (8) neither is the only writer of an instrument-read field. Callers: `BoardFrame.Renderer` — 0 outside the file (`FocusDriver.cs:610` and `Net/Remote/RemoteFocusOutline.cs:88-95` both go through `RootObject` / `AdoptBoardOrder`); `WorldFrame.Renderers` — 0 (`Net/Avatar/AvatarTurnRing.cs:110` uses `SetLocalSeat` only). `BoardFrame.Renderer`'s doc "for the caller that must seat it on a draw-order ladder (the LOCAL board registers it as furniture …)" is FALSE about the mechanism: the subtree is registered, not the renderer.
- **Proposed action:** Batch-D precedent kept documented API-shaped accessors that cost one line. Recommend: fix `BoardFrame.Renderer`'s doc sentence to "kept for a caller that ranks a single renderer; the local board and the peer board both adopt the SUBTREE (`RootObject` → `PlayTray.AdoptFurniture` / `BoardVisual.AdoptBoardOrder`) and do not read this" — or delete both members in one Tier-0 commit. `WorldFrame.Renderers` becomes the natural seam if E-11 is ever acted on; leaving it costs nothing.
- **Guard expectation:** empty (doc) or "the two members disappear, nothing else" (delete).
- **Risk if wrong:** none.
- **Cross-lane:** none

### E-23 — Correction to E-16's cross-lane note
`Net/Board/BoardVisual.cs:158-165`'s exemption list is for subtrees UNDER a board root (`AdoptBoardOrder` walks `boardRoot`); the ping tag is a free GameObject at the hex and is never walked by it, so no exemption entry is needed. NEEDED-OUTSIDE for E-16: **none**.

---

## Ranked summary (by risk reduced)

| rank | id | class | tier | one line |
|---|---|---|---|---|
| 1 | E-14 | risk-gap | 3 | `CharacterFocus.Reset()` never clears `_lastOwnershipCensus` → the `HW-VERIFY` `[Ownership] HAND FAN` falsifier can be silent for a whole second scenario (one assignment) |
| 2 | E-19 | risk-gap | 3 | `EnemyInfoPhaseSkip` presses the host's Fortfahren (ConfirmAction on the wire) and logs it only at the debug tier; its disarm and `PickPhaseInitiativeTrack`'s back-off are `Warn` (promotions) |
| 3 | E-04 | risk-gap | 3 | `BoardPing`'s "SILENT-GATE FIX" lines and two self-disarms are all `Info`/`Warn` = silent at the shipped default (promotions) |
| 4 | E-01 | risk-gap | 3 | `BoardClickDriver.DecideTap` swallows a throwing target test at `Warn` (→ `Error`) |
| 5 | E-15 | risk-gap | 3 | The whole character-focus evidence chain (`switch REFUSED`, `FOCUS PIN engaged`, `SELECTION GUARD`, `now looking at`, `peer cue`) is debug-tier; four promotions proposed |
| 6 | E-05 | risk-gap | 3 | `stable hex decal ZTest=` (INVARIANTS §15 grep token) and `HEX PROJECTOR` are `Info` (→ `Note`) |
| 7 | E-17 | risk-gap | 3 | `PingNameTag_Patch` self-disarm / swallowed throw at `Warn` (→ `Alert`/`Error`) |
| 8 | E-02 | risk-gap | 3 | Fingertip `[Tap]` / `FINGERTIP TOUCH commit` hardware-evidence lines are `Info` (→ `Note`) |
| 9 | E-20 | risk-gap | 3 | `EnemyInfoPhaseSkip.Reset` / `PickPhaseInitiativeTrack.Reset` keep `_reportedThrow` latched |
| 10 | E-11 | risk-gap | 3 | rectangle FALLBACK board frame never adopted into the furniture band (fallback path only; design call) |
| 11 | E-03, E-07 | risk-gap (minor) | 3 | `AoeControl.Reset` / `HexHighlightFix.Reset` leave edge-log latches set across a hot reload |
| 12 | E-22 | dead / doc-drift | 0 | `BoardFrame.Renderer` (doc names a caller that does not use it) and `WorldFrame.Renderers` unreferenced |
| 13 | E-12 | structure-naming | 1 | `FocusCue.cs` holds three top-level types — recommend leave |
| 14 | E-13 | doc-drift | 0 | STALE-DOC-REFS `PinRefusal` → `PinReason` / `PinRefuses` (corrected sentence given) |
| 15 | E-06 | doc-drift | 0 | `HexHighlightFix` doc still calls three constants "config" / "live config edits" |
| 16 | E-21 | doc-drift (planning) | 0 | INVARIANTS §7 select-guard rule superseded by `CharacterFocus`; §13 Board table predates 8 registered classes |
| — | E-08, E-09, E-10, E-16, E-18 | leave-alone | n/a | R42 / R30(a) / FocusDriver GlobalData guard / R14 / loadbearing census — all verified, nothing to do |

Counts: defect 0 · risk-gap 12 (E-01, E-02, E-03, E-04, E-05, E-07, E-11, E-14, E-15, E-17, E-19, E-20) · parallel-construction 0 · duplication 0 · dead 1 (E-22) · structure-naming 1 (E-12) · doc-drift 4 (E-06, E-13, E-21, and the doc half of E-22) · leave-alone 5 (E-08, E-09, E-10, E-16, E-18) · correction 1 (E-23).

No Tier-3 DEFECT (a wrong output from a stated input) was found in this set; every Tier-3 item above is a log-tier promotion or a one-line reset — none changes a value, an order, a patch target or a wire byte.

## Files read (whole)

`src/GloomhavenVR/Board/`: CharacterFocus.cs 2079 · BoardFrame.cs 1166 · BoardClickDriver.cs 807 · HexHighlightFix.cs 669 · FocusDriver.cs 651 · FocusCue.cs 544 · BoardPick.cs 393 · AoeControl.cs 279 · BoardPing.cs 265 · SelectionReadyHighlighter.cs 252 · BoardModule.cs 223 · TargetingUx.cs 160 · SelectionOwnershipFallback.cs 155 · BoardConfig.cs 101 · BoardDriver.cs 85 · CameraArrivalGuard.cs 83
`src/GloomhavenVR/Board/Patches/`: HoverPickPatch.cs 625 · PingNameTag.cs 609 · PickPhaseInitiativeTrack.cs 539 · EnemyInfoPhaseSkip.cs 447 · SelectionGuardPatches.cs 438 · HexHoverClear.cs 375 · PlacementDiagnostics.cs 178 · AllCardsViewerBlock.cs 176 · PickingPatches.cs 151
Total 11 450 / 11 450. Plus, partially, for cross-checks: `Core/VRLog.cs` (whole), `Core/Perf/TickGuard.cs` (NoteThrow signature), `Net/Board/BoardVisual.cs:125-200`, `decompiled/GH.Runtime/GlobalData.cs:555-575`, `Hands/HandRig.cs` (HandSide = Left 0 / Right 1, so `_near[(int)hand.Side]` is sound), `docs/PATCH-INVENTORY.md` Board rows.

## Verified still true / no longer true

- INVARIANTS §6 `BoardDriver.Update` order — TRUE; marker at `BoardDriver.cs:37` byte-identical to `FRAME-ORDER.lock`; six `TickGuard.Run` calls in that order, static method groups.
- §7 MF choke point (caller's mask, GetComponentInParent, vanilla when inactive) — TRUE (`PickingPatches.cs:43-52`).
- §7 `BoardPick` frame-memoized/lazy, every accessor via `EnsureFresh` — TRUE (`:80-226`; `TryGetHoveredTile`, added since, also goes through `EnsureFresh`).
- §7 `InScenario` ≠ `Active` — TRUE (`:89`, `:99-106`, `:282`). The outer gate is now `VRModeStateMachine.ScenarioBoardExists` (ModBuild 178) rather than the Menu2D/ModalUI mode test the invariant's "Where" implies — behaviourally what the class always meant, argued at `:257-268`.
- §7 near beats far / closer hand wins / `NearOriginLift` 0.03 / far re-raycast 1000f / off-screen (-4096,-4096) / tile-GameObject centre — all TRUE.
- §7 `IsPointerOverUI` predicate identical to `TickFar`'s — TRUE (`PickingPatches.cs:148` vs `BoardClickDriver.cs:659-663`; `TickFar` additionally requires `Held == null` and `HasHit`, as the §7 far-click entry lists).
- §7 CommonLoop postfix: verbatim bookkeeping, always-consume, yields to `__result` — TRUE (`:782-806`).
- §7 pending self-expires on the first line of `Tick` — TRUE (`:191`). Near click suppressed on `Poke.Hovered`/`HoveredUi` — TRUE (`:251-252`). Placement armed under the three game predicates — TRUE (`:713-719`).
- §7 `CameraArrivalGuard` polls, `VRSession.IsRunning`, not phase-gated, calls `OnArrivedToPoint()` — TRUE.
- §7 AoE: repeat ≥ 0.3, melee not handled, redraw matched on `CurrentAbilityDisplayType`, `RearmThreshold` 0.3 — TRUE. CHANGED SINCE (not a break): the stick is the NON-turning hand (`ResolveRotationHand`, ModBuild 138 ruling), no longer `VRHands.Primary`; §13's "AoeControl reads X only in BoardTargeting" still holds.
- §7 `TargetingUx`: suppress = display && !levelEditor && !figureHeld, no TargetSelection exemption, raise only if we lowered — TRUE.
- §7 `HexHoverClear`: postfix, `InScenario` gate, scope = cursor star + two panels, tooltip doubly self-no-op'd — TRUE; the ModBuild 90 "one rule" (revealed + `s_CursorHighlightedTile` identity) and the ModBuild 340 held-prop exemption are consistent additions.
- §7 select guard on the PLAYER override — TRUE for the seam; the rule is superseded (E-21).
- §7 `BoardPing`: dominant `PrimaryDown`, reflection-guarded, 0.2 s cooldown — TRUE. The call is now `UIScenarioMultiplayerController.PingTile` with `Ping3DElementSinglePlayer` as the fallback (MP bug #7); the invariant's "Breaks if: the multiplayer overload is called directly" concerns `Ping3DElementMultiPlayer`, which is still never called directly.
- §7 `SelectionReadyHighlighter`: `IsUnderControlOrSingle` + `!IsCardSelectionReady`, `LateUpdate`, `TickGuard`, `unscaledTime` — TRUE.
- §9 `TickGuard.Run` at `BoardDriver`, `BoardPing.Update`, `SelectionReadyHighlighter.LateUpdate` (also `FocusDriver.LateUpdate`, `PingNameTag.Update`), cached delegates — TRUE.
- §10 `HexHighlightFix`: swap only `OmniDecal_Shd`, queue re-assert, `Swapped` restored in `Reset`, postfix on `ProjectorMaterialAdjustment`, try/caught, error cap 3, `VRZTest` 4 + bias 2e-4 re-applied every postfix, knobs bypassed while swapped — all TRUE (the values are constants now, E-06).
- §13 Board table — every listed row TRUE at HEAD (targets unchanged; `docs/PATCH-INVENTORY.md:41-64` agrees); the table lacks eight later classes (E-21).
- §14.1 `ArmPlacementTile` level-not-latch — TRUE. §14.3 fake-null discipline: `HexHighlightFix.PruneGuardedProjectors`, `FocusDriver.ApplyRing` keep the destroyed reference for `Remove` — TRUE; `TargetingUx._lastHover` `==` compare is safe (a change gate, not a key).
- §15 KEEP list: `PlacementDiagnostics` trio — decision note at the top of the file (`:12-34`), not re-raised; `TargetingUx._suppressionLogged` — still one-shot, harmless; `BoardPick.TryGetCursorWorld` — still 0 callers, still documented P5 API; `PokeInteractor` ↔ `BoardClickDriver` 0.008/0.02 mirror — cross-reference comment present (`BoardClickDriver.cs:110-115`), `bash scripts/check-mirrors.sh` PASS (41 groups agree).
- §15 grep tokens `[Placement] …`, `LASER INFO SUPPRESSION`, `stable hex decal ZTest=`, `Tick '<name>' threw and was ISOLATED` — wording intact; the first three sit at the debug tier (E-05 covers the ZTest one; `[Placement]` and `LASER INFO SUPPRESSION` are deliberately left — answered questions).
- REVIEW-Hands-Board-Core §2.11 (patch classes outside `Board/Patches/` stay where they are) — TRUE, both in place and registered once.
- redundancy-audit R14 — NO LONGER TRUE (E-16). R30(a) / §6.4 — NO LONGER TRUE (E-09). R42 for `SelectionReadyHighlighter` — NO LONGER TRUE (E-08).
- STALE-DOC-REFS `CharacterFocus.cs:935 PinRefusal` — cleared by E-13 (the line is now 1076).
- `INSTRUMENT-WRITES.baseline` — no Board entry needed (E-18).
- `python3 scripts/check-mirror-dials.py` — PASS; no `Net/Remote` reader of a Board dial. `FocusCue.MrActive` is viewer-local by rendering necessity (a peer's cue is drawn against the VIEWER's chroma key); the mark itself is wire-derived (`MarkForPeer` reads only record 22).

## What I did not find

- No per-frame `FindObjectsOfType` in the set: the only one (`HexHighlightFix.SweepProjectors :329`) is event-driven (install / first hex write after a scene load / fresh material) and documented as such.
- No fake-null dictionary hazard: `GuardedProjectors` and the two ring dictionaries prune with the destroyed reference; `PingNameTag.Live` removes on `OnDestroy` and prunes null entries.
- No exception path that latches state: every `try/finally`/`catch` in the set (`SelectionOwnershipFallback.Tick`, `DecideTap`, `WouldRotate`, `TurnActor`, `DecisionOwner`, `PinnedActor`, the Harmony bodies) fails OPEN to the vanilla / no-claim answer and consumes its one-shot.
- No cadence gate advanced only on the logging branch (the ActorPropBody shape): `PickPhaseInitiativeTrack._nextTryAt` (`:432-434`) and `FocusDriver._nextReadOnlyRefreshAt` (`:443-445`) both advance before the work.
- No 1:1 breach: every peer-side derivation (`MarkForPeer`, `FocusIdForPeer`, `AttentionIdForPeer`, `SelectionReadyHighlighter.EnabledEntry` on the wire) reads the OWNER's record.
- No parallel construction inside the set. Examined and rejected: `BoardPing.ResolveClientTile` vs `BoardClickDriver.ResolveTouchedTile` vs `BoardPick.ResolveCursorWorld` — three tile resolutions, deliberately different (`GetComponentInParent` for pick/ping vs `GetComponent` on the interactable for the tap, mirroring `Controller.LateUpdate` verbatim, `BoardClickDriver.cs:311-319`); `PickPhaseInitiativeTrack.VisibleRows` vs `EnemyInfoPhaseSkip.VisibleEnemyRows` — different pools by design (`:352-358`); `SelectionOwnershipFallback.LocallyOwned` vs `CharacterFocus.IsForeign` — the same `LocalControlsActor(…, out answerable) ? byList : IsUnderMyControl` term, one the negation of the other, each carrying its own F5 reason paragraph; a 3-line shared helper would be Tier 2 with no hard-won difference, but it buys nothing and is not proposed.
- No unread config key: `TouchTilesWithFingertip`, `TouchRange`, `SnapToHexCenter`, `HoverHaptics`, `AutoFocusOnTurn`, `AoeFlickThreshold`, `AoeRepeatInterval`, `SelectionReady.Enabled`, `HexHighlight.LogMaterialDump` are all read; no `Bind` description states a default different from the bound one.
- No falsified game-behaviour assertion: `GlobalData.CurrentGameState` (E-10) verified against the decompile; the `Controller.CommonLoop`, `HoverRegisterer`, `WSHD` line citations were not re-derived beyond that (cost).
- `PinRefusal` arrived as prose in `3087dd30` (ModBuild 140) and never existed as a member.
- Minor nits recorded, no finding: `PingNameTag.RefreshTagRenderers` on the clone route always sees an empty `Renderer[]` (CanvasRenderers are not Renderers) and re-fetches every frame for the tag's ~2 s life — a 24-byte allocation per frame per tag; `Placement_UpdateGate_Diagnostics.Prefix` has no try/catch (a throw there would abort `WSHD.Update`), but it is gated on a live `WaitingForCardSelection` state where its reads cannot throw.
