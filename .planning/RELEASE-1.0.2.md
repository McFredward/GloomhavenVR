# Release 1.0.2 — build 513

## Authorization and tested source

The maintainer reported no observed issues in the build-513 retest and authorized release
if log review found no blocker. The tested DLL identifies commit `66df2561c`, version 1.0.2,
ModBuild 513. Runtime source was not changed after that test. The current local log also
observes the other player on build 513; files under `debug/remote` remain historical build 500
and are not new observer evidence.

## Hardware log review

The reviewed files were supplied on 2026-09-16:

| File | SHA256 |
|---|---|
| LogOutput.log | 5ca0c9c5a999bf931e043ba1977f4409f45541da8282e8201961b4def03519ce |
| Player.log | 98c0903aec976ccc8a12f7cbbfc2ec5e1235537e6cffe5f6927c66406a25cfb7 |
| openxr-diagnostics.log | 15c6af68fa85a607ceb18c75f6269de0809922d071d2463d39f5e67e5fea707b |

- All six recorded burns have a subsequent Burnt-pile flight. Four local native holds finish
  at `_GreyOut=1.00` after 2.00–2.05 seconds. Two observed peer holds release after the owner's
  completion frame has played, after 3.21 and 2.34 seconds from local discovery. No orphaned
  burn or expired claim is evidenced. Character presentation changes do not stop progression.
- All recorded phase-stall episodes clear. Outgoing animation telemetry ends at 16/16 events,
  with zero dropped and none queued. No GloomhavenVR error-level entry is present.
- The render-phase warning at LogOutput line 245 has a real `WindowMaterialiseRunner.LateUpdate`
  caller. The phase-scope marker is missing; the stack does not show a render callback.
- Control-coverage warnings compare HostRect alone and ignore native clipping/visibility.
  The actual input/capture bounds include content. The result window subsequently closes and
  the map starts; those warnings alone do not prove inaccessible controls.
- Unity really refuses result-window reparenting during deactivation (Player 5224–5225).
  The mod defers host destruction, preserving native content. No completed retry is recorded;
  scene teardown may destroy the host first. No subsequent gameplay block is evidenced.
- The rescue screen at LogOutput 2275 is released at 2279 after native close. The mutual-hold
  intervention at 2400 resolves at 2429, two ticks after its lift.
- The only NullReferenceException is the game's `VoiceChat.BoltVoiceChatService.OnDestroy`
  after XR shutdown (Player 202324), rather than a gameplay or mod exception.

This is a successful maintainer retest with no evidenced release blocker. Logs do not establish
that every character-switch edge or consumed-item effect was exercised.

## Performance limitation

The multiplayer board exists for approximately 14m58s and 72,681 Unity frames, averaging 80.95
frames/s. Across 179 five-second scenario monitoring windows, coarse cadence declines from
approximately 88.4 to 81.6 to 73.2 ticks/s across equal thirds. The measured WallFade cost rises
from approximately 0.266 to 0.401 to 0.514 ms/frame. These are game-loop observations, not headset
or compositor FPS. No continuous memory or GPU telemetry establishes the cause or a memory leak.
The maintainer reports acceptable play; this trend remains an observation for a dedicated
performance capture, not a claimed fix or proof of stable frame rate.

## Candidate validation

- All 17 local guard checkers and production suites pass; 254,019 wire assertions.
- Strict Release: zero warnings and errors; bilingual docs and whitespace checks pass.
- The compiled comparison was reviewed; the retained build-502 baseline was not replaced.
- CI for tested commit 66df2561 succeeded (run 35142004594).
- Release topology simulation: 36 assertions pass, including protected-main PR provenance
  and post-release dev ancestry/version handling.
- The candidate package has 29 entries and the complete 74,943,763-byte committed asset
  bundle. Its DLL reports 1.0.2, commit 66df256 and `IsDevBuild=false`.
- Reviewed bilingual release highlights focus on player-visible fixes and installation.

## Publication

PR [#5](https://github.com/McFredward/GloomhavenVR/pull/5) was merged as
`11107a29e94db25f0329c89b09862d4bc75e5256`. Its tree exactly matches the hardware-tested dev
commit (`c3741e7bd8da707af6a0df706839a28109ac4ee8`); the production provenance check passes.
PR CI run 35145441355 succeeded. Main-only Release run
[35146255179](https://github.com/McFredward/GloomhavenVR/actions/runs/35146255179)
completed successfully and published [v1.0.2](https://github.com/McFredward/GloomhavenVR/releases/tag/v1.0.2)
on 2026-09-16 at 20:35:13 UTC. The annotated tag resolves to that exact main merge.

The public, unauthenticated latest-release endpoint returns `v1.0.2`, with both draft and
prerelease false. The ZIP was downloaded through its public browser URL and verified:

- File: `GloomhavenVR-1.0.2.zip`, 85,115,414 bytes, 29 entries, all CRC checks pass.
- SHA256: `3c6d47a9a6001c3b5cc0c2a2e3c60db9d7ca8d35bf7da5e7e5bcf93eefd1465a`, matching
  the GitHub asset digest.
- Asset bundle: 74,943,763 bytes, matching the complete committed bundle's size.
- Extracted DLL: version `1.0.2`, ModBuild `513`, commit `11107a2`, `IsDevBuild=false`.

The workflow preserved main ancestry in dev through merge `9ea58bbe` and advanced the next
development version to `1.0.3` in `947f0586`. The local dev checkout fast-forwarded to that
commit. Release publication and bookkeeping required no source edits or force-pushes.
