# Advanced stereo-video action

This focused fixture runs inside the existing `vr-options` suite, or directly:

```bash
python3 scripts/vr-options-stereo-video-tests.py --self-test
```

It executes the actual `BuildAdvancedIndex`, complete `BuildLinkRow` and new
`VROptionsTab.StereoVideo.cs` partial. The real Button listener dispatches the
runtime request. Unity hierarchy/art helpers, localization values and the
runtime request/status API are explicit substitutes; runtime decoder lifetime,
packaging and actual menu/headset appearance require their separate checks.

The fixture covers direct Advanced placement, action versus toggle, no start on
construction, immediate and asynchronous status, repeated presses on the same
row, runtime refusals/retries, language refresh, dynamic note height, unchanged
text deduplication, reopening and row destruction. Eight source mutations must
fail at the expected causal assertion. Compilation or unrelated assertion
failures never count as successful controls.

Source snapshots/hashes, logs and results remain in ignored
`.planning/debug/vr-options-stereo-video/run-*`. The wrapper kills/reaps owned
subprocesses on interruption and calls `cleanup_completed_run` after every run,
removing only its generated `bin`/`obj` caches.
