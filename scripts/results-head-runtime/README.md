# Results head capture integration

The complete `check-results-presentation-runtime.py` suite runs this integration
in addition to its original rendering and negative cases. Before head rendering,
it sends that exact native sampler's enabled/disabled snapshots through the
transport starvation regression and its cancellation/boundedness controls. The
ordinary transport suite is not repeated by this follow-up. `--case`, `--footer-only`
and `--head-only` are explicit partial selections. The head selection takes about
one minute on the current llvmpipe machine, including a fresh strict Release build
and Unity import; it does not replace the complete local gate.

The fixture loads the freshly built **complete production GloomhavenVR assembly**.
It executes `CanvasConversion.Convert`, the real reveal decision/completion,
`PanelSupersample.Tick`/`LateTick`, the allocator, private layer sweeps, camera
callbacks, capture/resolve/mip resources and final head-camera raster. An iterator
advances across actual Unity play frames, so cadence is not a supplied frame count.
The source and observer retain independent windows and capture allocations. The accepted integration keeps its supplied conversion frame. An unmerged
1474x157-to-1474x1024 growth experiment has passing and unexplained failing
sampler evidence; it is not qualified main coverage.

The original Results hierarchy, artwork, fonts, materials, masks and standard
uGUI layout components are imported from the game. Additional instances of the
original accomplishment row make its table scrollable without substituting colored
geometry. Victory, three sampled intermediate header alpha/position states, the complete
table and three settled native scroll positions cross
the existing sampler/codec/playback path before final head pixels are compared.
The negative control suppresses the actual production `ResolveAndMip` method after
the observer's first picture and must expose the retained heading at the final head.
Nonplaying AudioSource/Animator sentinels retain their source identities and enabled
state, while inert copies contain neither component. They do not certify audible
playback or the timing/curve of the native animation authoring.

The visible test output is deliberately a supplied prefab/widget state: repeated
Enemies Killed template labels, unpopulated right columns, a white viewport backing
and a Victory band whose final native GUIAnimator transform is not executed. Equal
pixels prove capture/presentation parity for that state; they do not prove that
this is the correct final native Results layout or appearance.

Explicit boundaries remain the native gameplay/bootstrap controllers, scenario
values, animation clock, readiness and network transport. The imported standard
layout engines execute; native gameplay controllers do not. The test supplies a
native conversion clock deadline and head camera, and calls enabled cameras in
their normal capture-before-head order. It uses mono desktop GL/llvmpipe, without
an XR device. This proves the exercised composition; it does not certify paired
headset pixels, native gameplay authoring, stereo backends or subjective legibility.

A focused command requires a task-private ADB server, for example:

```sh
ADB_SERVER_SOCKET=tcp:localhost:5083 python3 scripts/check-results-presentation-runtime.py --head-only
```

The full wrapper invokes the head selection automatically. Logs, source/DLL/input
pins, native inputs, pixel captures and cleanup receipts are kept for every run;
regenerable project caches are removed through `cleanup_completed_run`.
