# Build 610: Steam Frame CPU and rendering budget

The supplied `steam_frame/LogOutput.log`, `Player.log` and OpenXR log identify
**610 / 3edbcb284**, not the later NPC multiplayer Build 611. Player and BepInEx
logs duplicate one run. Input hashes and parser output are preserved under
`.planning/debug/frame612-analysis/`. No new image or video accompanies this run.

## Loaded gameplay

The trace contains both CampaignMap and ProcGen. Exclude initial generation,
preparation and loading: the first ProcGen window is still mixed with setup.
The following later tracked windows permit normal head motion rather than
requiring a stationary VR camera. Means are frame-weighted; p95 ranges are
per-window values, not a pooled percentile.

| Context | Windows / frames | Frame mean | Mod scopes | Logic | Render loop | Window-p95 range |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| CampaignMap, windows 6–10 | 5 / 1234 | 56.91ms | 31.41ms | 39.50ms | 5.97ms | 84.67–186.60ms |
| ProcGen, windows 13–23 | 11 / 3280 | 61.21ms | 19.60ms | 34.66ms | 9.72ms | 95.63–120.76ms |

These observations support the user's marginal playability report. They do not
measure Build 612, isolate NPC cost, or prove a GPU bottleneck. FrameTimingManager
GPU-busy data remains unavailable. OpenXR's `gpu` trace field is a compositor
frame interval/wait and must not be presented as application GPU-busy time.

## Concrete work to remove or reduce

At the final loaded scenario split, four render passes remain: two headset-eye
passes and one each for ScenarioCamera and UI Camera. ScenarioCamera brackets
about 1.15ms of CPU culling/submission; UI Camera about 0.27ms. Both already draw
into a discarded sink, so the additional optional budget disables their Camera
components completely. This is a plausible CPU saving, not a promised hardware
FPS improvement. Native `Camera.main` and UI camera enumeration require an
identity-preserving managed bridge: blindly disabling them breaks gameplay
projection, recovery and UI continuation. The native movie camera is an active
decoder/continuation lease; visible menu/preview captures remain necessary.

The later scope window contains ActorBars.Late around 2.01ms and
CanvasConversion around 1.15ms (including Fit around 0.91ms). Optional static
panel fitting and verified original-loop bar sampling reduce repeated checks,
while reveal, input, movement and non-loop animation transitions retain their
immediate paths. The original card census previously repeated synchronous
resource-heap discovery during interaction. A startup seed and actual widget
lifetimes replace that repeated query for every platform, including remote
original artwork. Debug diagnostics remain bounded; normal logging still does
not run the census.

Each resident NPC body has over 100,000 vertices and explicitly forces FourBones.
A global skin-weight setting alone cannot affect those renderers. Mesh-only
NPC quality, projected-size figure LOD and a renderer-level skinning cap are
reversible quality compromises. Native renderer/material/skeleton identity,
original facial expressions and shared animation clocks remain in use. The new
far bank must be judged using actual triangle counts and native rendered poses;
asset size alone says nothing about FPS.

Wall fading remains significant: the final window reports WallFade.Late
around 5.33ms, including Pipeline around 3.95ms and three Rescan events averaging
about 171ms. These scopes are inclusive and must not be added together. Existing
wall inventory/check interval settings can lower frequency; this round does not
claim those expensive native classification events are eliminated. Unbracketed
engine time and transient native action effects remain further constraints.

## Next hardware evidence

Install the complete Build 612 archive; DLL-only installation omits the new mesh
parts. On an existing Frame profile, inspect the new controls under Graphics:
unused-camera suspension, distance LOD, NPC detail, skinning bone limit, static
window maintenance interval and idle health-bar pose check interval. New Frame
profiles select the cheaper defaults; saved existing keys remain intact.

Compare the same scenario during ordinary play with distance LOD and camera
suspension toggled separately. Verify the dragon bar through sleep/wake/flight,
long-rule hover/click and remote board expansion, campaign videos and skipping,
2D-map/non-NPC windows, and close inspection of all three NPC faces. Debug logs
must show camera ownership/restoration, actual derivative usage and bar
verification/skip counts. Editor timings are causal implementation evidence,
not a substitute for the next Steam Frame measurements.
