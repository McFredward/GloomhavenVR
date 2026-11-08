# Build764 late-join card consumer review

Paired Build763/fb03d1e64 hardware shows the guest controlling Testo while
`CharacterFocus` correctly supplies its read-only presentation floor (remote
LogOutput11976/12006/12007). Its original intro remains natively visible:
the story/overlay gate closes1996 and only reopens9190 on leaving the scenario;
the second attempt repeats10542..22867. Native phase remains card selection.
The story is not a harmless cosmetic panel: original StoryController's
TryBlockedUpdate holds both Choreographer's update blocker and ActionProcessor's
processing lock. ShowNext releases them and retains native client-ready dispatch.

SelectionHandDrift and PumpSelectionHandSwitch deliberately defer to a genuine
blocking story. The floor must remain read-only until the original SwitchHand
seam succeeds. Removing that restriction, granting an arbitrary local hand,
or expiring the blocker would allow callbacks during a still-live native story
and leave the native queue deadlocked. No card production permission/ownership
change is justified by these logs. The repair belongs upstream in the exact
native scenario-start occurrence/continuation handoff; see MP764-RESULTS.md.

## Additional consumer proof

The existing portable `selection-latejoin` container passes **47 production-bound
assertions and six compiling causal controls**. A separately registered local
`--native-story` companion passes **62 assertions and eight compiling controls**.
It imports the actual read-only original StoryController OnFinishShow, ShowNext,
TryBlockedUpdate and TryUnblockedUpdate bodies, alongside the production card
commit blocker, overlay gate, selection recovery pump, CurrentHand and wanted
slot methods. Original native input hashes and generated sources are retained.

The companion executes a sustained native intro lock, successful original finish,
native client-ready sideaction, existing queued presenter recovery and restoration
of both placement overlays without scenario reload. It also tests an unassigned
visitor (native readiness succeeds, card authority remains absent), a genuine
queued successor (both native locks and card refusal stay live), and an original
finish callback that throws (no unlock or fabricated ready notification).
Reverting the native story card gate or the original unlock body compiles and
fails its intended assertion, in addition to all six existing controls.

The native companion is local-only because hosted portable tests do not contain
the read-only decompiled game reference. Its preprocessor/runner flag leaves the
original portable CI obligation independent of those references. It does not
execute a real Photon session, original GUI transitions, head pixels or the
shared occurrence/host authorization algorithm. Window visibility, native roster,
navigation, GUI scheduling and native queue notifications are explicit ports.
The upstream lifecycle fixture qualifies the permission to call native finish;
this companion qualifies its downstream card/readiness consequences, not an
independent automatic permission grant.

An initial fixture build failed on a nullable boundary parameter and an
uninitialized native-message port. Both were corrected without touching runtime
card code; the failure log remains retained. An earlier58-assertion native pass
precedes the additional unassigned/failure cases and is not the final62 result.
Every standalone case reaps its process and calls cleanup_completed_run, removing
only regenerable bin/obj caches. Sources, logs, original hashes and all outcomes
are preserved under ignored `.planning/validation/build764`.
