# Build700: streamed native-passthrough silhouettes

## Report and evidence

The maintainer confirms Build699 shows both eyes correctly, but rejects remaining
shimmering outlines at passthrough boundaries. Four 20261018025*.jpg screenshots,
Player.log and LogOutput.log are retained with hashes under
`.planning/debug/steamlink-700-edge-review/hardware-inputs/`. JPEGs document visible
softness; they cannot measure temporal shimmer or uniquely assign its cause.
No new Frame read, write, restart or capture was performed for this correction.

Independent reviews inspected the actual compiled 699 carrier rasters, retained
native metadata, original native fragment and exact supplied PC Steam Link driver:

- Direct standalone composes RGBA without separated video regions, lossy YUV or
  foveated video reconstruction. Equal virtual 2048-square fields do not establish
  equal physical sampling. Actual 4040 carrier probes show a maximum normalized
  colour/coverage boundary mismatch of 0.358288 and up to 0.509143 original source
  pixels of half-crossing displacement before video compression.
- Retained real R0/R1 metadata yields central colour density 3.507 times that of
  peripheral coverage. Earlier flat/interior codec controls disabled the inner
  patch, so they missed this class. Current hardware per-pixel weights remain
  unknown. This is stronger evidence for a transport-quality repair than for an
  unpremultiply, opacity threshold, window tag or another format redesign.
- YUV/video filtering can introduce additional edge error. Software 8-bit codec
  surrogates are not amplitude measurements of the current P010 stream.
  RGB_IDENTITY Vulkan sampling ignores YCbCr range; retained black/white values
  do not justify a limited-range expansion. No such correction is applied.
- The supplied host driver reads public `driver_vrlink` settings and reacts live
  to public settings-change event 872. `foveationMode=1` disables foveation;
  `useSharpening=false` disables pre-encode CAS. A uniform manual width is needed
  because automatic policy derives a reduced width from the former foveated mode.

Small private reports live under `seams700-source-review/` in the GH evidence
folder and `private-seams700-{receiver,codec,range}-review/` in the native repo's
ignored build folder. Proprietary inputs/disassembly are not published.

## Implementation and tradeoff

New visible World → Scenery controls, limited to native Frame/Steam Link MR:

- `MixedReality/SteamLinkEdgeQuality=true`: uniform encoding without sharpening.
- `MixedReality/SteamLinkEncodeWidth=3072`: request 1024..4096 pixels, rounded up to
  64; retain an existing higher manual uniform width within bounded readback.

An embedded isolated Windows PE uses the public Background OpenVR client, also
usable through the existing Proton bridge on Linux hosts. It snapshots all four
settings, then applies automatic=false, uniform width, sharpening=false and
foveation=1. It does not patch the Steam sender binary. Width readback is not
actual negotiated resolution, bandwidth, decoder load or a proven safe maximum.
The visible help explains the higher network/decoder cost and lower-width escape.

One async managed worker owns stdin for the lease. Camera/menu/loading replacement
does not end it. Off, link loss or game shutdown requests EOF and reverse-order
compare-before-restore. Observed manual edits are retained. A stale generation
cannot become Active or overlap a replacement lease; unknown restoration latches
unsafe. No active expiry. Startup may take up to five seconds, including pending
Off. Emergency cleanup bounds only follow explicit release. Helper crash/kill can
prevent restore, same-value/simultaneous external edits are not distinguishable,
and public IVRSettings restores effective values, not absent override entries.

The exact PE is 45568 bytes, SHA256
`e54894b92d98c72bfed45079c70b2771aa63d348a4fcb88d111c7dd4aeb68d2f`.
Its reproducible source/build/compiler provenance ships beside the resource.

## Review and verification boundary

New managed state/protocol fixture: 76 checks, strict net472 typecheck 0 warnings
and errors, including exact native serializer transcripts and typed-token negative
controls. Native fixture: 3747 checks with ASan/UBSan, real stdin EOF, rollback,
manual conflicts, apply order, rejected blind-restore mutant and identical PE
rebuild. Availability fixture: 69 assertions and six rejected semantic mutants.
Independent review fixed unsafe restoration retries, mismatched clamped-width
validation, post-process-start cleanup and abandoned mutex classification. Final
follow-up adds bounded ordinary warning context for safe startup refusal.

These are not actual Windows/Proton kernel-mutex, live IVRSettings, network or
headset-picture checks. Source/receiver/scene-bank files remain byte-identical to
accepted 699. Inherited 699 GPU/codec/wire evidence is not relabelled as a new
complete gate. Final strict Debug/Release builds, actual resource/default/build
identity checks, source checks and cleanup are retained under
`.planning/debug/steamlink-700-edge-review/`. No Frame update is needed.

The quality lease removes the identified foveation-quality split and sharpening,
but cannot promise lossless streaming or mathematically identical standalone
boundaries. No coverage threshold or semantic classification hides real glass,
MSAA or additive light. Headset edge quality and streaming cost remain the next
hardware judgment; disable edge quality to compare against the unchanged 699
rendering path without updating the receiver.
