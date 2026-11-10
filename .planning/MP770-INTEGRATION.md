# Results responsiveness after shared-picture acceptance

## Intake and scope

Both supplied logs identify Build769/726634d5c, built2026-11-10 17:37:37 UTC.
The guest now receives and displays the complete17part/403node common picture.
The user confirms identical visible content. Their clarification is decisive:
the host immediately sees guest scrolling; only that guest sees its own scroll
after2–3seconds. Host-originated scrolling appears promptly to the guest.
Shared Results window movement also lags behind established board/window motion.
Hardware inputs are frozen in `validation/build770/hardware769`.

ClassB, bounded to the existing Results presentation/control paths. No new codec,
TLV, native gameplay action, global scheduler or capture behavior is planned.
The maintainer explicitly requests targeted tests only. Reuse qualified769
complete checkpoint for unchanged scope, with final source/compiled comparisons.
If implementation scope expands, reassess and record the exact affected selection;
do not silently restart all297suites after this user's explicit instruction.

## Source findings

- The small ordered scroll state updates only the guest's hidden original
  ScrollRect/Scrollbar. Its visible inert copy has no live ScrollRect, and its
  content pose is still painted from the slower full Results snapshot. Project
  accepted common scroll state onto that exact visible content/handle geometry,
  after painting and between picture arrivals, without replaying native callbacks.
- Results frame poses are carried by the same fragmented content snapshots.
  Reuse the existing small pose-only Results grammar and common move register/
  interpolation, with independently bounded direct motion delivery and separate
  receiver ordering. Keep full picture dependencies reliable and unchanged.

## Ownership and selected checks

Scroll lane owns SharedWindowScrollSync/Bridge plus shared-scroll runtime coverage.
Motion lane owns ScenarioResultsPose, NetAvatarDriver.Results, one validated
FfsNetTransport motion entry point and affected transport/pose fixtures. Root owns
build number, integration, final review and source/compiled qualification.

Required proof includes guest request→author action→accepted common state→visible
copy, stale picture repaint resistance, scrollbar/joystick and competing inputs;
and continuous frame movement under slow/incomplete picture traffic, original
grab ownership, independent zoom, source changes, close/reopen and stale packets.
Use affected suites/controls, strict Release and all source gates. No full local
gate, asset rebuild, ZIP, release or device action. Clean every completed run and
retire only these owned worktrees after verified preservation and dev push.

The supplied Info-level guest log proves picture acceptance but contains no
per-action latency trace. Source-proven routing defects and controlled fixture
latency must be distinguished from measured live network/hardware timing.

## Integrated implementation and review

Scroll checkpoint cdf72090d integrates as4fc718866. Motion checkpoint58f31c436
integrates asc1a2ca3a6; its fixture checkpoint8f8528974 integrates aseced0976f.
The final ModBuild is770. Both participants must update and restart the game.
No asset, configuration, preloader or wire-layout change is required.

The focused scroll proof executes actual native Results clone construction,
neutralization, geometry binding and original Unity UI. It checks visible content
and rendered pixel changes from guest wheel, joystick and scrollbar inputs;
different private row heights; author input; stale graph repaint; and concurrent
opposite input ordering. Production184 assertions and3 compiling causal controls
pass. An initial new linked-handle assertion exposed Unity's later native bar
update; the bounded repair uses the same accepted content state for the linked
handle. All four attempts, including that failure, remain preserved.

The transport proof executes actual production producer, codec, receive queues,
native-reflection emission and content scheduler with declared socket/clock ports.
Production2,438 assertions and16 compiling causal controls pass. In the five-second
loaded producer trace,67 small poses are emitted,55 received with deliberate loss
and jitter, and41 complete picture updates still progress. The worst source gap is
0.200s. Motion cannot replace a pending picture or suppress its sparse dependency.

That exact decoded trace is copied and hash-pinned into the actual pose/track
consumer proof. Results-only44 assertions and2 controls pass, including350 moving
90Hz frames, a delayed full picture, bootstrap, local grip, mover arbitration,
foreign authors, close/reopen and peer forgetting. Maximum observed frame step is
0.044 world units; maximum tracking error is0.367 world units at2 units/second.
These are controlled-fixture values, not measured network/HMD latency. The source
and consumer preserve the same interpolation as other existing shared motion.

Independent read-only motion review found no concrete blocker. The final compiled
review confirms four changed behavior types, one internal projection helper,
eight inlined build stamps, no removed types and32 byte-identical resources.
Native continuation, original animation/audio and shared geometry remain intact.
See MP770-REVIEW.md for the exact contracts and evidence limits.

## Final qualification and reuse

Fresh strict Release: zero warnings/errors,22.94s. All16 source suites pass in
24.2s. Surface comparison retains all672 config keys,289 Harmony patches and
4,862 log tokens. The9,831 runtime/reference/build inputs are frozen and unchanged
after final integration. Worker scope pins bind the actual tested runtime files;
the sole scroll/protocol difference is the reviewed769→770 build stamp and notes.

Total affected runtime coverage is2,666 assertions and21 compiling controls.
Do not label this a fresh full local gate. Reuse Build769's qualified aggregate
297 local scopes and307,211 wire assertions for unchanged scope. Its original
complete invocation remains a recorded failure with15 private-ADB startup
refusals; only those refused scopes were repaired and passed. Existing actual-DLL
head/picture proof remains applicable to unchanged sampling, assets and painter;
the new projection has its own actual Unity visible-pixel proof. The final fixture
entrypoint changes are exercised in their affected proof, not an excuse to repeat
all unrelated successful suites or the unchanged strict build.

All failed fixture-development attempts, exact source/native/toolchain pins,
producer trace and cleanup receipts are retained. Evidence is under
`.planning/validation/build770/`. Root strict-build obj caches were retired through
the canonical cleanup API (33,677,197 bytes), preserving current DLLs. Completed
worker caches are cleaned before archive verification; only these two owned
worktrees are removed after preservation and successful dev push. Other agents'
worktrees and all supplied hardware logs remain untouched.

No new HMD, Photon, audible timing or native final-game layout acceptance is
claimed. The next paired test checks guest-own scrolling, host-originated updates,
simultaneous input, and grabbing/moving/resizing from either participant while
both continue seeing the accepted full Results contents and continuation buttons.
