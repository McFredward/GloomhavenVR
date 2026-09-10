# Multiplayer build 497 — motion and hand ordering

## Evidence and scope

The supplied local `LogOutput.log` is build 496, commit `1a714ee27`. Its one peer announces 496;
no remote-client log belongs to this test. Old remote logs, `Player-prev.log` (463) and earlier
screenshots are not evidence for this run. The detailed measurements and exclusions are in
[MP-497-PERF.md](MP-497-PERF.md).

119 regular scenario windows cover 59.5 minutes at 11.35 ms/frame, with 0.54% over 22.22 ms.
After a room expansion, the settled mean rises from 11.21 to 12.69 ms alongside more game renderers;
mod renderer count stays approximately constant. A separate 90→72→90 Hz episode recovers. This
supports the user's smoother experience but cannot isolate the benefit of 493 from scene complexity.
The managed heap samples rise; they neither prove nor rule out a leak. Four-player scaling and
remote frame pacing remain unmeasured.

## Source changes

- Board pose and scale share the head/hand rig packet through additive record 70. Once received,
  delayed large presence snapshots cannot overwrite that pose or resurrect a removed board.
  Legacy fallback remains until explicit rig board state arrives. Board motion alone no longer
  accelerates large presence snapshots. Receiver smoothing remains identical to avatar smoothing.
- Initial spawn and B+Y recenter derive horizontal heading from the actual clamped board seat
  to the player. Authored tilt and normal follow/fixed placement are retained.
- Owned normal hands allow presentation-only insertion during selection, action and on the map.
  Local scalar character/card order survives widget recreation and scenario entry. Long-rest
  discard-choice fans and other players' characters do not allow reordering.
- A held card stays outside the physical fan when its source rebuilds. Remote position history
  is independent of whether fronts may be shown; source domain/membership changes invalidate
  positional addresses. Map loadout order uses existing positional record 44.
- Review found an old unapproved insertion-gap deferral. Record 71 carries the current insertion
  gap or explicit clear with the same fan snapshot. Remote layout and marker use shared geometry.
  No card identity is added to the wire; existing concealment and public-map rules remain intact.
- Requested defaults: vertical turn-stick movement enabled, board follows, peer board becomes
  transparent when occluding the playfield. Existing configuration values are preserved. English
  and German guides and both controller diagrams describe controls, sorting and left-slot initiative.
- The logged shutdown exception in `RemoteOriginalDecisionPrompt.Destroy` is guarded against
  an already destroyed Unity placement component. This was not an in-session performance cause.

## Verification and remaining hardware checks

Final integration readings are recorded in STATE.md and the integration commit. Board tests
exercise wire golden bytes, malformed/duplicate records, legacy fallback, delayed presence,
removal/reconnect and actual-seat heading. Fan tests cover local order, ownership and positional
reflow; insertion records cover active/clear/legacy/malformed transitions.

Headset checks still needed: smooth follow versus mask, initial/recenter heading, covered
selection pluck with one and two hands, action/map reorder and scenario carry-over, remote gap
and marker appearance, and long-rest exclusion. No automated result establishes visual correctness.
New defaults require a fresh or deliberately updated configuration to appear on an existing install.
Build 497 is DLL-only after the full 483 bundle; all modded peers need 497.
