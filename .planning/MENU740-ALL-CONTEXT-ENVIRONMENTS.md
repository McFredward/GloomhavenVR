# Build740: selected surroundings in every VR context

The maintainer requests the chosen environment in the main menu, 3D map and
scenarios, expressly including the original Default sky. This supersedes the
historical menu exclusion. Build739's confirmation repair is already on dev at
`21701006b`; keep its accepted hover behavior and native prompt changes intact.

## Custom room placement

Remove the table-only surroundings gate. A menu has no native board: use a fixed
1.2 m footprint and the existing 4.5x room proportions, with its floor on the
tracking floor. Map/scenario measurement and normalization remain unchanged.
Rooms stay world-fixed through motion, zoom and same-rig recenter. An actual
RigRoot replacement retires the previous presentation before placing the next
one; ordinary camera rebinding retains the occupied room. Keep Haunt and native
gameplay permissions behind their original table/board gates.

## Original Default sky

The native GH_SkySphere exists in scenario levels8/9/10, not menu/map. Its tree
contains only transforms, MeshFilter and MeshRenderer. An isolated embedded bank
provides the original mesh, material, two encoded textures and three shaders
(AMP_SkyShader plus original Diffuse/VertexLit dependencies), with no external
references or native controllers. Preserve native properties, compiled programs,
vertex/index payloads and encoded mips; relocate only local pointers/streams.
The final bank is 2219921 bytes, SHA256
`9c4c57eb8777fb02b9ba3d53b0a55e768be9aa9b6600151618f743b4e2fd0c16`.

Preserve the original level8 observer origin, offset, flattened scale and yaw.
Uniformly fit this infinite background inside 85% of the existing head far plane;
the room remains world-fixed. Use the existing mod layer and non-occluding
SkyBackdrop treatment, with no camera-mask, clip-plane, lighting or actor writes.
Head-visible native sky takes authority. Scene-loaded notification and head/mask
changes invalidate the bounded scan, including already cached native authority.
Keep the public native-hiding query unfiltered. MR, other styles and VR teardown
release only the owned presentation; actual rig/camera replacement renews it.

The native shader graph retains its original fallback behavior across APIs.
Linux GL reports original Shader.isSupported/cold acquisition true through that
graph. This does not prove the original D3D11 AMP main program's pixels.

## Evidence and integration

The whole custom-room Unity fixture passes 74 assertions and six causal controls;
tiny contract RoomGeo/PlaySpace prefabs qualify artwork coverage. The native-sky
fixture passes 48 assertions/seven controls/eight Unity variants. It loads the
actual shipping resource by canonical names, verifies 1679 mesh vertices and
4096/512 textures, executes an actual serialized scene-loaded event, head-mask
handoffs, MR/style ownership and foreground depth pixels. Its GL graphics witness
preserves native geometry/textures while qualifying original shader fidelity.
Root inspected the cold-menu cloud raster and the foreground-depth motion image.

The first exported bank parsed correctly but still exposed template container
aliases. Actual Unity LoadAsset rejected it. Preserve that failure; the final
export uses the new serialized container data, validates semantic names, and
loads mesh/material/shader through their real aliases. No game reference is written.

Localized help and both player guides describe the new scope. The surface scan
keeps 670 config keys and 263 Harmony entries. Thirteen removed all-caps fragments
were configuration prose, never log calls; authorize only those original paths
through the existing retirement table. No parser rule or runtime marker changes.

Strict Debug/Release and metadata builds pass zero warnings/errors with 1172
stable inputs; all sixteen source checks pass. Compiled scope is three existing
environment/localization types, one new NativeDefaultSky type and nine expected
build-stamp consumers. The embedded resource matches its source byte-for-byte.

Class C and the periodic integration cadence require the complete local/golden
checkpoint. Two initial invocations stopped at the static inventory preflight
(new IDs, then 264-to-266 census); no local/golden pass is claimed for either.
The corrected complete runtime invocation is ignored
`debug/menu740/full-gate/run-nsyomrc0`, on production frozen at `0c39eb00e` with
fixture-only inventory repair through `057704263`. It records 265 passes and one
cleanup failure in 3169.9 seconds. All 74/6 scope behavior checks passed, but its
cleanup correctly refused a Roslyn compiler borrowing the concurrent Default
test's assembly. Preserve that failed invocation, not a complete-pass label.

Isolate both new suites' SDK-named compiler pipes with short private TMPDIRs.
Their complete containing suites then pass together under two-job concurrency:
74/6 and 48/7, in 59.6 seconds. Both final fixture input manifests match exactly;
only those two runner files differ from the complete runtime snapshot. The other
264 execution closures retain their passing bindings. The verified aggregate
therefore covers all 266 current suites without inheriting an older build's pass.
Execute the complete invocation's exact retained wire executable independently:
all 306431 actual golden assertions pass. Its sources remain byte-exact. This is
complete applicable aggregate coverage with fresh goldens, not a newly successful
single complete-gate invocation. See ignored `debug/menu740/validation-final.json`.

New fixture cleanup also handles escaped Unity Roslyn daemons proven by private
file descriptors. Parent process-group termination alone missed two old scope
compilers; targeted pidfd cleanup stopped them before cache deletion. Preserve
the correction and receipts. Once the concurrent borrower exited, stop the old
scope compiler, retain its temporary safety pin as evidence and clean that run's
regenerable caches. Periodic private-FD cleanup also retires escaped compilers from
completed legacy suites, with other active runs explicitly refused. Both clean
workers are archived, SHA256-verified and retired. Keep logs, sources, captures,
manifests, native inputs and deliverables;
stop only owned ADB sockets. Delivery is source on dev, without a local ZIP.
Original D3D11/HMD appearance and pacing remain a maintainer hardware check.
