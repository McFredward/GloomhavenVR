# Build701: make the edge-quality policy run and stabilize source startup

## Hardware evidence and attribution

The maintainer reports no visible change in Build700: blur/veil grows with object
distance, and both eyes briefly differed while loading before the main menu.
The fresh Player.log and LogOutput.log are copied and hashed under
`.planning/debug/steamlink-701-review/hardware-inputs/`.

The log confirms Build700, genuine Deckard MP/cv Steam Link identity and five v3
source epochs with both4040-square8xMSAA eyes. All three quality lease attempts
were refused at `helper:snapshot`; no new driver policy was applied. Therefore
this run does not test uniform/no-CAS quality and cannot reject that approach.
The old receipt did not name the failing key or public API error. Do not claim
that error5 was retrospectively measured in this run.

The only logged UnknownEye is late at line10726 near XR session stop/resume,
not proof of the reported pre-main-menu glitch. The source nevertheless had two
unsafe startup paths: admission without a stereo-ready Both attachment, and a
new component starting atlas output in the remaining eye of an existing frame.
No actual failed receiver eye verdict or startup pixels were captured here.

## Settings repair

Public IVRSettings_003 error5 means UnsetSettingHasNoDefault. A previously unused
nonfoveated width can legitimately be absent; treating every such state as a
snapshot error incorrectly refuses the entire policy. The helper now snapshots
absence independently from effective false/zero. It still snapshots all four
settings before any mutation, explicitly writes absent desired false/zero keys,
and applies automatic=false, width, sharpening=false, foveation=1 in that order.

On rollback/EOF it compares each still-owned value and restores an absent key
through the public typed RemoveKeyInSection slot10, then verifies error5.
Originally effective values use the existing compare/restore behavior. Manual
changes, including removing a key, are preserved. Every non5 API failure still
refuses; remaining snapshot refusals name the bounded key and API code. There
is no guessed fallback default, config-file parsing, timer or sender binary patch.
The acquired receipt carries an explicit four-bit absence mask; managed decoding
validates the mask and its placeholders without allowing a missing baseline to
justify an inherited higher width.

The shipping helper is46080bytes, SHA256
`4dae192d836d989e78a878e97abd6976b8ee3db03718b17122704e7745db99d4`.
Its managed verifier and reproducible provenance match. The existing default
quality policy and3072 requested width remain unchanged; successful application
is identified by `Steam Link stream quality: active`. Readback still does not
prove actual encoded dimensions or a safe client decoder maximum.

## Stereo startup boundary

Current head attachment must be stereo-enabled and target Both at admission,
pre-cull and image output. A temporary Mono/non-stereo render closes normally
and can prepare again; actual HDR/format/attachment faults remain latched.
A fresh output observes explicit Left and Right labels within one frame, keeping
that whole frame ordinary. Only at the next frame boundary may atlas publication
begin. A remaining Right after mid-frame AddComponent cannot independently turn
packed while Left stayed ordinary. Different frames and duplicate labels cannot
complete readiness. No eye is inferred from order, and no previous image or mask
is stored. Current permissions still apply per callback.

This prevents that source startup class; it cannot guarantee atomic transitions
inside the independent native video encoder/receiver, or identify the exact
reported hardware frame. All image shaders, coverage/scene bank, v3 layout and
installed receiver stay unchanged. No Frame update, contact or restart occurred.

## Focused evidence and previous blind spots

- 18271 actual C API-table/lifecycle checks under ASan/UBSan, all16 presence
  combinations, false/zero distinction and120 partial-apply rollback cases.
  Remove failures, false-success removal, user removal and non5 errors are
  covered. Shipping PE rebuild is byte-identical. Both blind-restore and the
  exact old missing-setting-refusal mutations fail the same fixture.
- 85 managed lifecycle/protocol checks, strict net4720warnings/errors and real
  acquired/missing/API-error C transcripts. Previous tests supplied every key
  as valid, so they could not expose a legitimately absent SteamVR value.
- 100 production callback/cohort checks and six compiled semantic mutants;
  actual Unity2021/net4720warnings/errors. The real Gamma/Vulkan loading test
  passes499checks,27renders,6automaticresumes and96headercells. Its old-owner
  negative fails after66checks. XR eye, stereo-enabled and frame-count metadata
  are explicit fixture inputs because the Editor is not a real XR runtime;
  actual ownership, materials, command buffers, packing and pixels execute.
- A small independent raster study rejects a new source prefilter:48 controls
  at24retained actual raster edges and1162 checks across576analytical thin-edge
  cases show additional contrast loss without reliable phase elimination. No
  speculative extra blur, opacity cutoff, window tag or shader rewrite ships.
- Independent reviews pin the real helper/parser and the final startup boundary.
  Ordinary logs remain bounded; detailed startup cohort context is Debug only.
- CI700 also exposed two fixture boundaries missing the new MixedReality config
  port and a stale registry inventory. Those test boundaries are corrected
  without changing production predicates or weakening negative controls.

Final integrated builds/artifacts/source gates and cleanup receipts are retained
under `.planning/debug/steamlink-701-review/`. New inventory is235local/104CI/16source.
Unchanged699 renderer/codec and existing wire evidence are inherited, not renamed
a new complete gate. Actual Windows/Proton settings behavior, negotiated video
quality and the corrected headset picture remain hardware verification.
