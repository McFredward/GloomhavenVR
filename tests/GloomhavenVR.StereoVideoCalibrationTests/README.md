# Calibration owner lifecycle proof

Run `python3 scripts/stereo-video-calibration-tests.py --self-test` from a
checkout containing the calibration runtime, or pass `--source-root` to the
checkout under review. The wrapper copies and hashes the complete actual
`StereoVideoCalibration.cs`, `StereoVideoCalibrationPolicy.cs` and
`Loc.StereoVideo.cs`, compiles them without extraction, and refuses certification
if any source changes during the run.

The actual `FlatScreen.Tick` and `FlatScreen.Shutdown` sources are also copied
and hashed. A method-scoped lexical check requires exactly one calibration call
as the first statement of each method, before native continuation and early
gates. Twelve hook controls remove, comment, quote, relocate, condition or
duplicate those calls; extra comments alongside a real call remain accepted.
This is a source binding check, separate from executing the calibration owner.

The small API substitutes model Unity object lifetime (including destroyed
objects comparing equal to null), GameObjects, native menu/session state,
`File.Exists`, decoder events, a surface attachment and pointer registration.
The assembly-relative movie path is real. Each legacy command-line token case
runs in a fresh process and must stay inert until the explicit menu request. Reflection resets private static
owner fields only between independent lifecycle cases within that process.
The surface itself is substituted, so no GPU/eye-routing proof is claimed.

The cases cover normal launch and legacy argv doing no file/shader/decoder work,
explicit repeatable menu requests, live localized status, offline VR/native-menu gates (including `MainMenu_gamepad`), first
prepared texture/frame and exact dimensions, the mono companion fallback,
single active decoder, explicit restart after close or failure, exact picture click ownership, English/German close hints, fixed
window pose/aspect, errors, repeated close, shutdown before/after an attempt,
opening/manual-closing alpha transitions on the shared update clock,
scene or session loss, external root/player destruction, stale queued callbacks
callbacks from an earlier attempt after a new request, and untouched native players. Source mutations must fail at their named causal
assertion; compilation errors and unrelated failures never count as detections.

The fake-null stale-callback case reproduced a real defect in the first owner:
after Stop destroys the decoder and clears `_player`, `player == _player` can
be true for an old destroyed Unity object. Both callbacks therefore require
live objects plus exact managed identity. Separate prepared/error mutations
restore the old guard and must reproduce that failure.

Logs, source copies, source hashes, results and cleanup receipts are retained
under `.planning/debug/stereo-video/lifecycle/run-*`. After every child exits,
fails or is killed/reaped, the wrapper calls `cleanup_completed_run` so generated
`bin`/`obj` caches are removed while source and evidence remain intact.
