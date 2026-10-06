# Build628: performance after scenario loading

Build628 integrates the reviewed common PC/Frame package into current dev
`a517e0090` (Build627). The maintainer explicitly authorized direct integration
and an origin/dev push after confirming that the parallel main agent is idle.
The pull was fast-forward only and already up to date; the reviewed branch
`work/frame-steady-20261006` at `7239e2e0d` merged without conflicts.

## Evidence and resulting behavior

The supplied PC627 capture has three revealed rooms and no connected multiplayer
peer. Both build banners identify Build627/a517, RTX4090, D3D11 MultiPass and
SteamVR/OpenXR. Fastest has lower frame times in some windows, but different
views/activity and simultaneous changes prevent a per-setting attribution.
After loading, terrain replacement covers few surfaces, submitted environment
groups stay at zero and visible idle sampling stays at zero. Loading/profile
stalls do not decide this package's success; the maintainer accepts loading hitches.

| Area | Result and adjustable compromise |
| --- | --- |
| Terrain | Ten additional exact original Crypt wall/pillar definitions use the existing near/distant detail and distance controls. Floor, foundation and doorway exclusions remain; current native state/material overrides stay live. Settled private proxies skip identical writes. |
| Environment | Existing private mesh, batching, shading and instancing switches may admit authored probe defaults only when actual baked/local lighting is compatible. Late source, probe, lightmap, path and command-buffer changes return original submission before native culling. |
| Idle figures | The existing visible idle interval can cover compatible authored culling and ordinary native LOD groups. The same group retains automatic distance and ForceLOD selection. Actions, holds, clones, active cloth and uncertain ownership restore native rendering. Zero interval disables visible sampling. |
| Disabled clothing | New `Optimize.VisibleIdleDisabledClothApproximation` explicitly permits skeletal clothing during sampled poses. Fresh PC defaults false, fresh Frame true; explicit Fastest enables it, other presets disable it. Live Off restores native clothing; active cloth solvers always remain native. |
| Debug evidence | Actual source reads, reused reads, UI registry visits and surviving camera submissions are reported, including zero rows. These are neither total GPU draw counts nor measured multiplayer savings. |

PC and Frame continue to ship the same code and assets. Device detection seeds
only absent settings; saved/manual values survive until an explicit preset is
applied. Every compromise remains configurable. Resolution is unchanged because
the maintainer performs that comparison himself. No 2.5D board is introduced.
Native gameplay, wire format, transport cadence and NPC627 behavior are unchanged.

## Visibility and lifecycle safeguards

Restoring masked sources at onPreRender is too late once native culling has
excluded them. A shared postfix on the actual Unity2021 Camera.FireOnPreCull
boundary verifies leases after managed callbacks but before native culling.
Unsupported hooks fail open. Known native writes and visual cloning synchronously
restore originals; uncertain native lighting or LOD ownership keeps native output.
Original hierarchy, collision, floor/doorway geometry and tactical contents remain.

The disabled-clothing option is a visible shape compromise. Native BakeMesh can
omit a solver's frozen physical deformation; a native warm-frame Drake fixture
does not prove equivalent output for every garment. The option has independent
English/German help. Native LOD verification still allocates, and a bounded
atomic BakeMesh call cannot be preempted. Bone-write savings alone do not prove
a net CPU/GC or frame-time gain.

## Validation

The clean reviewed worker head passed strict Release and Debug (zero warnings
and errors), all 14 source suites, all 141 local suites, 308,358 final wire/golden
assertions, bilingual documentation, four pinned UnityFS bundles and the figure
bank checks. The exact Build627 compiled comparison has 16 intended changed and
four new types, with no collateral types. Config keys grow 669 to 670, literal
patch registrations 215 to 217, and all 4,790 log tokens remain.

Final integrated source `686c85a3d` passes all 14 source gates, strict Release
and Debug (zero warnings/errors), bilingual documents, the four pinned bundles,
figure bank and the final 308,358 wire/golden assertions. The complete local
attempt records all 141 scopes: 140 direct passes and one retained NPC623 Unity
cold-start timeout. Native project loading took 375.623 seconds against the
unchanged 300-second limit. The exact original compiled manifest subsequently
passes its production case (1,161 assertions) and all eleven negative controls
in the warmed private project. No source/fixture/control change, recompile,
timeout increase or exception suppression was used. All 2,024 frozen tracked
source/test/build inputs and all report/log hashes remain unchanged; successful
unrelated scopes were not repeated. The original complete report remains FAIL;
its exact continuation is recorded separately, not rewritten as a direct pass.

Actual Build627-to-628 compiled output changes 25 types and adds four: sixteen
reviewed behavior types, eight types containing only the new inlined ModBuild,
and BuildInfo containing only the worker-to-dev GitBranch stamp. No type is
removed or unexplained. The original guard exits at the local startup timeout;
its remaining wire/bundle/surface/snapshot operations and the explicit compiled
review pass afterward. A private receipt reader initially expected a colon after
PASS production, and the private compiled reader initially flagged the branch
stamp. Both reader corrections retain their initial logs; native fixtures and
source are unchanged. Config/patch/log surfaces remain 670/217/4,790, with no
removal. Inventory is 190 patch classes and 281 methods.

Fresh integrated native proofs pass: terrain 290 assertions/37 negative controls;
environment 11,372/76; idle 656/40; figure detail 146/30; mirror reads 661/11;
shared UI 1,660 plus 1,659 unsupported-runtime fallback assertions/12; UI inventory
1,021/5. Source manifests, actual callbacks/pixels, verified source bundle and
retained worker failures live in `.planning/debug/frame-steady-handover-20261006/`;
final integrated receipts and continuation boundaries live in
`.planning/debug/frame628/validation-ledger.json`. Only subsequent Markdown
changes reuse this unchanged validated source tree.

One earlier worker attempt raised InvalidCastException in an unchanged NPC
Material cache. All 70 production bindings and normalized production IL match a
historical passing run; baseline production, an independent complete recheck and
the final complete worker gate pass. The cause remains unproven. No NPC cache
change or exception suppression was introduced. Three subsequent harness-only
integration corrections preserve every negative mutation, expected message,
assertion and pixel comparison; focused checks and the final complete gate pass.

## Next hardware test

After complete loading, use a stable viewpoint and compare one setting at a time,
then the combined Frame settings. Check all three rooms in both eyes, floors,
doorways, wall fading, targeting, automatic/forced LOD, actions, local/remote
holding, clones and live option reversal. Compare actual completed-camera
coverage, idle bakes, clothing approximation, PreCull/bake/GC cost and frame
distributions. Include paired same-build host/client logs before attributing
multiplayer headroom. Native Linux editor/GL proofs are not Frame stereo, original
Windows shader or headset FPS evidence.
