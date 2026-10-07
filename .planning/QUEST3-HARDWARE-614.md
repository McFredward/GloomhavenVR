# Quest observable startup loading — ModBuild 614

The maintainer reported a logo-only B613 startup and supplied
`quest-capture-20261003T205508Z-64a0ff43.zip`. Quest work remains isolated on
`feature/quest3-standalone`; the concurrent `dev` work is unaffected.

## Evidence and cause boundary

All eight manifested capture files match their size and SHA-256 records. The
installed 1,153,868,013-byte APK has the expected B613 hash
`66733e82c77b1ccfd513a6097845b315c03e83713d47baf9bf241af635dbe226` and input
`99fd0fece7990200066a06a131f729098696bfb347bc68857f5f756e76986393`.
The current startup sink stops at 20:54:20 UTC, immediately after its build banner;
state is `extracting-mod-content`, with no plugin, original bootstrap or retained
managed exception. That old stage precedes both UnityWebRequest completion and
worker extraction, so it cannot establish which operation was waiting.

The observed exit at 20:55:02 follows pause/destroy and process self-termination.
It does not prove a native crash or OOM. Old B609/B611 hardware-probe files and the
B612 previous-run startup sink must not be attributed to this run. No new
screenshot is included in this capture.

The actual B613 startup scene contains no camera. Its neutral camera is created
only after mod delivery, while the archive is 67,563,824 bytes and the extracted
bank is 67,563,656 bytes. Actual Android IL2CPP output selects software
SHA256Managed under Debug/O0; the old cold path hashes approximately 270 MB,
including an additional synchronous bank hash on the Unity main thread.
B612 took 23.83 seconds for its smaller original-content archive, so the 42-second
B613 observation also permits slow legitimate verification. Neither a particular
UnityWebRequest deadlock nor headset crypto throughput has been established.

## Change

The first scene now contains a serialized stereo camera and localized temporary
loading label, including a retained font/material reference. Awake also prepares
that view idempotently before any file check. There are no controls or pointer in
this temporary view; it is removed before the existing real plugin starts.

Both local archives stream directly from their exact `assets/<archive>` APK ZIP
entries on managed workers. This follows Unity's documented Android
[Application.dataPath](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Application-dataPath.html)
APK boundary; this builder produces a monolithic APK without OBB delivery.
Editor execution uses local StreamingAssets files. Size/path/entry/hash gates and
atomic publication/repair remain. No whole APK or archive buffer is allocated.
The final verified bank is no longer hashed again on Unity's main thread.

Copy, archive hash, extraction and file hashes publish separate phase/byte
snapshots. State includes UTC, elapsed time, last progress age, main-thread frame
count and loading-view availability. Worker lifecycle logging uses only the
managed, bounded sink. Diagnostic IO failures do not fail otherwise valid content.
These changes improve first-start observability and frame submission; the cold
Debug/O0 verification can still take time.

The collector adds a separately bounded app logcat history selected by exact
package/process context, including terminated runs. Already evicted Android ring
entries cannot be recovered. It also records only file-size metadata for two
fixed delivery paths; no bundle, archive, save or profile is transferred.

## Hardware procedure

Extract the complete private B614 Windows archive and run `Install-Quest.cmd`.
Package/signing identity and app data are retained. On the first launch allow
roughly two to three minutes for this diagnostic, watching the temporary loading
label and MiB values. This observation interval is not a loading-time prediction.

If progress stops, leave the app open and run `Collect-Quest-Logs.cmd`. Supply its
ZIP and a screenshot under the main ignored `.planning/debug/quest3_probleme/`.
The updated state and delivery stages distinguish progressing work, an idle
worker and a main thread that no longer advances. Relaunch once to compare a
verified warm start after a completed cold run.

If the real mod and Intro/menu appear, follow the existing
[B613 controls, passthrough and menu checks](QUEST3-HARDWARE-613.md#one-hardware-round).
Campaign generation, original saves, the first Android rule-load failure and
required authenticated Android EOS crossplay remain separate gates. This target
still uses the authorized offline dummy identity and no store/cloud services.

## Validation

Validation is scoped to Quest startup/delivery/capture, affected localization and
actual Android outputs, following the maintainer's request to avoid rerunning
unrelated `dev` suites. The new fixture executes the production Bootstrap
IEnumerator and managed content path; lifecycle, Addressables and Unity rendering
are declared seams. A held worker proves that loading yields and Update continues;
corrupt archives must not activate the mod or original game. Real headset camera,
input and native game execution require the next hardware run.

The signed ARM64 IL2CPP B614 build succeeds with Unity 2021.3.5f1 and the five
original startup scenes. The 1,153,837,389-byte APK has SHA-256
`6ac82c27da750b2b3cafb2b3a9cb6c9faa1a370911c141e25bd3829ad221fe52`.
Frozen clean runtime/tool source is `8a12aaff4d01cf71a65f71fbe826b91076a58e47`,
input `6b07372ace46d54e6582e195c72fb33c635272be22b4630083606670f7eacaf8`.
Package `dev.gloomhavenvr.quest` and certificate SHA-256
`1412542b0b4cac2f1bc4941cbb4b01a5556eb8da2c34709086ab9375fd33c012`
retain update continuity. Android signature/manifest tools and the builder's
archive/ARM64 checks pass.

Seven focused Quest suites pass: builder (70 Python tests), installer/capture
(144 Python tests, 23 environment-dependent skips), original weaver (150
assertions), startup logging (38 assertions plus eight defect controls), actual
startup coroutine (159 assertions plus ten defect controls and an inert-comment
control), authored bundle recipe (33 assertions) and archive/content delivery
(299 assertions). The actual B613 APK delivery proof adds a 300th check and
confirms the unmodified mod archive/bank; its host timings are not Quest timings.
All 14 source checks, strict Release with zero warnings/errors, 286,760 direct
protocol/golden assertions, test-registry validation (17 tests, two environment
skips) and paired documentation checks pass. The registry adds the focused
loading fixture to local/CI; no full 116-suite local verdict is claimed.

Independent compiled comparison against reviewed B613 accepts exactly the four
new QuestText keys and build-number changes in NetProtocol plus seven constant
consumers. Serialized resources and reference identity/order remain unchanged;
34 generated project paths differ only by decompiler output location. The
actual APK also matches 999 frozen source/script inputs and the generated build,
API, bundle and content resources. All 48 plugin contracts and eight SDK
assemblies from the actual full Android build match the reviewed player SDK.
Independent API audit checks 537 type/1,441 member references with no remaining
issues; 1,897 protected and 19,520 other types are unchanged.

The exact packaged startup scene has one enabled backbuffer camera with stereo
Both, layer bit 31 and depth -100, plus its correctly linked loading canvas.
Arial's 50,788 font bytes, material and actual GLES3 text-shader payload survive
player packaging. This proves retained rendering inputs, not a correct headset
picture. The Windows archive verifies its source dependencies, embedded APK/hash,
CRC and installer selection outside the checkout. Private scoped/artifact
receipts live beside the handoff. No B614 headset success is claimed here.
