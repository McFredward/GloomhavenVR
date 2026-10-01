# Steam Frame: large-scenario performance program

The subsequent Build 599 hardware review is in
[STEAM-FRAME-TWELFTH-HARDWARE.md](STEAM-FRAME-TWELFTH-HARDWARE.md).
It rules out the nine sampled native callbacks as the main CPU wall; actual
Unity draw counters remain unavailable. The scope and limitations below still
apply, including the need for a loaded-scenario inventory and matched views.

## Evidence and correction

The 2026-10-01 ModBuild 598 log in `.planning/debug/steam_frame/LogOutput.log`
loads `ProcGen` directly. It does not contain a 3D-map test. The apparent
36 ms/frame interval was inside the scenario, with the HMD untracked and only
seven visible renderers; it is not an interactive map or scenario baseline.

After tracking returned, scenario windows report 107.74–130.84 ms/frame.
The 107.74 and 130.84 ms windows each contain an eye-resolution change and
cannot be labelled as single-scale samples. Only the later 127.78 and
130.82 ms windows remain at the full 3408-pixel eye target throughout; their
view positions and visible populations still differ. At full scale the final
window has 70.76 ms Update→LateUpdate, 32.40 ms
main-thread render cull/submission and 27.65 ms blocked time. The measured
mod total is 26.22 ms/frame. The XR `gpu` field mirrors the frame interval and
is not GPU busy time. The owner also tried turning wall fade off and found no
material subjective improvement. The log shows a few wall-off frames with
lower mod time but still roughly 125–135 ms total; the open options window
prevents attributing an exact A/B delta.

The scene census reports 5,689 renderers visible to at least one camera and
5,786 material slots on those renderers; 2,851 slots use `Amp_Basic_Foliage`
and 1,601 use `Amp_Basic_WallFade`. These are candidates, not measured draw
calls: `Renderer.isVisible` is not specific to the head camera, and batching
changes how many actual submissions occur. The head camera costs about 28 ms
main-thread cull/submission over two MultiPass eye renders. CPU Update also
has a large unassigned native-game component. The available Unity marker
recorders return no samples in this release player.

## Optimization order

1. Attribute the two large unknowns: actual CPU costs of selected native
   game methods and real render counters/GPU versus compositor time. Keep
   sampling bounded at Debug and report unavailable counters as unavailable.
   Use a tracked, fixed-view scenario window for every A/B.
2. Prototype draw-submission reduction only for groups proven static and
   visually independent. Room/tile reveal, Apparance material replacement,
   LOD, wall fade property blocks, collider/gameplay ownership and multiplayer
   must retain their native lifecycles. Blanket `StaticBatchingUtility.Combine`
   is already falsified by Build 15: newly revealed rooms became invisible.
3. Target significant native CPU work identified by timing, especially work
   that can be prepared during loading or skipped when truly unchanged. Do
   not disable native controllers simply because their UI is not drawn.
4. Once the structural costs fall, consider optional Frame-only visual
   compromises such as a conservative distance/size limit for decorative
   foliage. Never hide gameplay geometry, change interaction or reduce
   multiplayer presentation silently. Maintain PC behavior and offer a
   reversible Frame control for a visible compromise.
5. Recheck the 240–262 ms wall-table rebuilds as a separate hitch problem.
   Their elimination improves smoothness but the owner's wall-off A/B means
   they cannot by themselves solve the sustained frame rate. Transient
   `HexCenter_Proj` and `HexHighlight` renderers may trigger rebuilds, but
   excluding only their signature without excluding them from wall membership
   is not proven safe. The staleness ceiling also covers renderer-enabled
   changes not represented in the scene signature.

## Hardware acceptance

For each candidate build, hold the same tracked head position and board pose
in the same large scenario after loading. Record 30-second windows with the
same eye scale and graphics settings. Compare frame mean/p50/p95, main-thread
logic, cull/submission, mod time, visible population, and SteamVR CPU/GPU
times if a real recording is available. Check normal scenario interaction,
room reveal, wall/foliage appearance, original map modes, and a remote peer's
view. A faster microbenchmark or an untracked-HMD window is not acceptance.
