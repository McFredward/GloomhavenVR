# Build688: remove the hidden Steam Link availability lookup failure

## Hardware evidence and cause boundary

The latest main-checkout `LogOutput.log` and `Player.log` identify Build687,
Windows/D3D11 and active SteamVR/OpenXR2.17.10. The connection reader reports
`RuntimePath`; tracking/model/registered fields are empty. Thus its own initial
DLL/registry search rejected before an isolated public HMD query, and the cached
menu predicate correctly hid the setting for that unavailable snapshot.

The maintainer then supplied a valid single-runtime `openvrpaths.vrpath` and
confirmed `bin/win64/openvr_api.dll` exists there. The old blanket exception/null
handling cannot establish which exact path, JSON, file or PE check refused. Do
not describe a missing DLL or multiple runtime roots as this user's proved cause.

## Replacement

Delete that unnecessary managed lookup instead of adding guessed search paths
or bypassing the headset/transport condition. Valve's public SDK loader belongs
to an application; the runtime client it locates is `bin/vrclient_x64.dll`.
Embed the exact public Windows-x64 OpenVR1.26.7 redistributable, its source/blob
provenance and BSD3 notice. Extract it by its own SHA256 alongside the separately
pinned owned helper. Only the bounded Background child loads it, never Unity's
existing OpenXR process. Valve resolves registry history and public path
overrides; the child retains those overrides while stripping unrelated injection
variables. No installed runtime/provider binary is redistributed or overwritten.

Connected HMD0/class plus exact Steam Frame model and `vrlink` transport remain
required. Other headsets, standalone and other transports keep their previous
paths. Availability is still cached and can refresh an already open menu. Saved
preferences, the MR master and all source/raster/wire behavior are unchanged.
Hardware identity is not proof that the Frame receiver is installed.

Diagnostics retain the existing tokens and add finite extraction/reader/API
failure stages. Normal failures are deduplicated per active session; full public
device metadata stays at Debug. No image capture, Frame write or restart occurs.

Primary contracts:

- [Valve SDK runtime client loading](https://github.com/ValveSoftware/openvr/blob/v1.26.7/src/openvr_api_public.cpp)
- [Ordered registry history and environment selection](https://github.com/ValveSoftware/openvr/blob/v1.26.7/src/vrcore/vrpathregistry_public.cpp)
- Exact binary, tag commit/blob and license pins are in
  `src/GloomhavenVR/Assets/SteamLink/openvr-loader-provenance.json`.

## Validation and remaining hardware step

- Focused registered local suites: connection and availability,2/2 pass.
- Actual production connection source:101 managed checks, including both real
  embedded payloads, missing/altered resources, PE kind, finite child failures,
  override inheritance, timeout/reaping and unsupported-device controls.
- Existing production C metadata controls:98; no Windows DLL execution claimed.
- Existing production availability predicates:37 assertions and4 causal controls.
- Affected source suites: instrument-writes, desync-surface, its parser and
  options-coverage,4/4 pass. No wire layout change; prior goldens are inherited.
- Strict Release:0 warnings/errors. A separate read of the actual built plugin
  proves both resource names/bytes/SHA256 and ModBuild688. Its first private
  inspector failed nullable compilation; only that inspector was repaired/rerun.
- Worker SDK/license lane:30 checks plus4 real embedded-resource checks; exact
  official exports/imports and staged license verified.

Sources/results/logs remain in `.planning/debug/steamlink-688-integrated/`,
`steamlink-688-source/`, the two focused suite run directories and
`steamlink-688-loader-worker/`. Test caches and the completed worker checkout
were removed. This is composed focused evidence, not a new complete local gate
or a live Windows/Frame recognition result.

Rebuild/install current dev on the PC, reconnect the ordinary Frame Steam Link
VR stream, and open VR Options → World & sound → Mixed Reality. The native
control should appear when identity becomes ready; no Frame patch reinstall is
required. The next Debug log should show `Ready` and the actual public identity,
or an actionable bounded failure stage. Verify the real image separately.
