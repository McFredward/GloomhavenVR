# Native Steam Link connection admission and video coverage

Prepared on 2026-10-17 from real-game Build686 evidence. The independent stereo
intro owner reserves Build687, NetProtocol and STATE; this lane supplies a source
checkpoint and focused evidence for that combined handover, not another686 binary.

## User request and policy

The native MR backend is available only after a connected Steam Frame HMD using
the `vrlink` driver is identified. A running SteamVR/OpenXR runtime alone is not
sufficient, because other streaming transports can also use it. New preferences
are true, but the effective source gate is false until identity is established;
existing saved false preferences are preserved. The general MR switch still
starts false. Frame standalone keeps its separate OpenXR backend.

An unsupported saved-on preference cannot pack the scene atlas or hide chroma-key
controls. An already open options pane rebuilds once on a changed capability,
without a per-frame menu rebuild. HMD changes revoke the cached identity before
another source image; controller-only changes do not unnecessarily flicker it.

The native receiver is NOT detected by this identity check. With the correct
Frame/Steam Link hardware but no matching active receiver, enabling MR can expose
the packed transport image. This is a wrong-image/fallback limitation, not proof
of a crash, no-op or installed receiver. Player help states the prerequisite.
There is no new network discovery, modified Steam sender or device-side write.

## Isolated public identity reader

The actual OpenVR ABI/lifetime belongs in a bounded Windows-x64 child process,
not the game's existing OpenXR process. The child starts only against an already
running SteamVR session, uses public Background-client APIs and reads connected
HMD0/class plus tracking-system, model and registered-device-type strings.
It never initializes or shuts down OpenVR inside Unity. A failed or ambiguous
read stays unavailable. Only our own child can be cancelled/killed; refresh is
cold, bounded and asynchronous. No permanent background process is introduced.

The helper is embedded and extracted by its exact source-build hash. It contains
no Valve DLL, private shader or camera image. Public API identity is cooperative
runtime metadata, not hardware attestation or receiver attestation.

## Transparent main-menu video

The actual686 logs show MainMenuVideo captured into FlatScreen. Its exact owned
Hidden/BlitCopy backdrop writes RGB before AfterForwardOpaque without writing
depth. The real-scene coverage reset therefore erased its alpha; a depth-only
stamp could not recover it. The new third alpha-only BACKDROP pass stamps that
exact visible opaque renderer after reset/depth coverage. RGB, original early
video order, hand occlusion and the split UI-glass renderer remain untouched.
Hide, disable, layer removal, material replacement and destruction remove the
stamp before the next head draw. Unknown alpha-blended fallback shaders are not
admitted. Shader-name inspection occurs only on material/shader changes, outside
warm eye getters. The small17-shader bank is rebuilt with Unity2021.3.5f1.

## Focused evidence and limits

- Actual production backdrop GPU:118875 checks across MSAA1/2/4/8, with independent
  silhouette/RGB oracles, early-depthless failure control and lifecycle cases.
- Final owner getter:26 checks,15 owner cases,0 managed bytes in256 warm reads.
  Shader raster is unchanged by the final getter/cache repair, so GPU proof is
  inherited rather than needlessly repeated.
- Exact production request/menu predicates:37 assertions and4 causal controls.
- Actual Unity MR presentation:38 assertions, including unsupported saved-on
  fallback and the existing open-menu/camera/HDR restoration behavior. Native XR,
  identity lookup and image packing remain explicit boundaries in that fixture.
- Public identity reader:87 managed/process checks plus98 actual-production C
  metadata checks under ASan/UBSan, local net472 Unity typecheck0W/0E and a
  byte-identical42,496-byte MinGW helper rebuild. This does not execute the
  Windows helper or recognize actual headset hardware.
- New compiled shader bank:64 metadata/container assertions,173134 bytes,
  SHA256 fbe5f8eadd92ac43d3c94bcf0353cf5965b77af0c7a7c50d2c15499e9308f71b.
- Source gates16/16 passed before the final reader checkpoint; reader additions
  are separately typed/reviewed. The merged source Release build passed0W/0E before the final help text was
  shortened to the player-help limit; final help lookup2308 and docs parity pass.
  The combined687 handover owner performs its final versioned build.
- Registered local/CI reader and availability suites passed2/2. The reader was
  repeated alone after fixing its output wrapper to allocate a fresh child run;
  policy/runtime sources were unchanged. Its87/98 checks pass after that bounded
  runner repair. Existing passing availability/MR/GPU scopes were not repeated.

Private receipts are under `.planning/debug/steamlink-capability-20261017/` and
`.planning/debug/steamlink-video-coverage-worker-20261017/`. Test-run completion
cleanup preserves sources/results and removes owned stopped project caches.
The first fixture compile failed on an uninitialized boundary field; its failure
is retained and not counted as a successful negative control.

No new headset capture, receiver installation/restart, full gate, golden-wire run
or hardware identity recognition is claimed by these focused tests. Build686
real-game headset success remains the prior visible proof, not proof of this
new connection reader's live result.

## Window/hand edges remain separate

All three supplied private room screenshots were inspected. The exact game
UI/Default shader writes correct alpha-over, not the A-squared described by old
PanelSupersample comments. Its known premultiplied capture RT is, however,
multiplied by texture alpha again in RGB when displayed through that shader.
That darkening defect, authored HeaderShadow alpha, the1024 coverage resolution
and downstream filters are distinct. The private hash-pinned UI audit describes
an exact-consumer PMA correction, but this patch does not introduce a global
threshold, opaque panel rectangle or claim the photographed fringe solved.
Narrow anti-aliased hand silhouettes are not alone evidence of incorrect opacity.

## Receiver permanence and provenance

The currently approved Frame package remains one consumed compositor-process
lease. Files can remain in the user directory, but this is not a persistent
installer: a restarted compositor refuses a consumed lease, and the explicit
camera-blending override requires recorded rollback. SteamVR updates require
compatibility validation; do not bypass the original-provider/shader pins.
The ordinary fork install script still installs the old chroma-key backend.

The new explicit-opacity receiver/source is mostly new work, while compositor
entry knowledge, retained original helpers and history come from spyjacob's
MIT-licensed steam-frame-chroma-key. Keep its attribution/license/history and
clearly distinguish the new backend. A general receiver still requires each
game's compatible image/coverage producer; it cannot infer arbitrary games'
correct glass opacity from an ordinary RGB stream. Do not publish private
replacement SPIR-V containing original Valve shader bodies.
