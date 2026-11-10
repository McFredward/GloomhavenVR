# Shared public window scrolling

`run.py` compiles the exact production coordinator, codec, public-root and input
proxy implementations. Original `UguiPointer.Scroll`, `Press`, `Drag`, `Release`, wheel-owner
lookup, nested `TryRaycast` dispatch and event-data creation execute. Original
`RayUguiDriver.TickStickScroll` and `ModalFallback` Results stick scrolling run.
Original `RayUguiDriver.TrySurface`, `TryIntersect` and `VisibleUiSurface` run
before the registered nested picker can settle hover. Every common Results
viewport, scrollbar and header entry comes from that physical ray and real Unity
`GraphicRaycaster` hits; a failed surface entry cannot inject a downstream hover.
The nested-canvas registry is unmodified. Converted committed hit rectangles are
explicit inputs; the hardware's outboard scrollbar is inside that committed rect.

Three independent coordinator instances model separate process state. Original
Unity `ScrollRect`, `Scrollbar`, `Canvas`, `CanvasGroup`, `RectMask2D`, graphics,
camera projection and native UI callbacks execute in Unity2021.3.5f1/GLCore.
The fixture explicitly ports the game's singleton/window/session registry and
does not load a full Victory game controller or claim Photon/HMD acceptance.
Tick's existing clock parameter samples an intermediate rendered UI state without
relying on a software renderer completing a frame within33milliseconds.

The observer executes the actual production `ScenarioResultsPresentation.Build`,
`Prune`, `SetLayer` and `RootPose` methods. Source/bootstrap discovery and the
converted-panel/state-header fields are explicit ports. Original module bindings
read, validate and apply the inert picture; `TownServiceNeutralize` removes native
controllers while inactive. Results, header and repeated row modules are cloned
separately, including legitimate `(Clone)` names. This catches lost original
paths that the earlier monolithic prototype could not expose.
The complete picture is mounted below the same extra observer root and remains
inactive while input is attached, then is revealed without a new descriptor
version. Original inert groups block raycasts. Only an own metadata input
surface under the actual common viewport/scrollbar can receive events. Its exact
canvas joins and leaves the production nested-pointer registry. Private client
lists deliberately have different content heights from the source prototype.
Small ordered author state is projected onto the actual inert content and handle
geometry before a new full Results graph arrives. Guest wheel, joystick and
scrollbar actions change its own visible picture, including final rendered pixel
checks for wheel input. Repeated stale graph transforms are repaired by the real
post-paint hook even when input descriptors are unchanged. Linked handles use the
same accepted content position to avoid a pre-LateUpdate native handle lag.
Direct differential cases compare both axes, undersized pivot alignment and all
four scrollbar directions against original Unity UI geometry. No cloned native
controller or callback is restored, and opposite/concurrent and stale/foreign
requests retain existing author ordering.
One test fits the outer host133pixels short of the original scrollbar and picks
and drags that unchanged common scrollbar through the registered nested canvas.
This includes the original surface-admission methods; it does not replace the
generic converted-window fit/depth or final compositor tests.

Coverage includes pre-descriptor consumption, authoritative wheel and scrollbar
input, native group permissions, common interpolation, source/nonce/generation
validation, same-frame reopening, private composed-loadout exclusion,2D-host/3D
visitor election, unmodded-host surrogate behavior and unchanged input reuse.
Whole-window header hover works without a private native list or with an inactive
one, while actual proxy hover has one generic delivery and no duplicate fallback.
A common list without travel does not claim scroll-versus-locomotion focus.
Header hover follows the exact original graphic rectangles, masks and current
visibility, leaves empty space and separate footer buttons alone, and does not
invent a viewport pointer-down/drag gesture. Original source raycaster and group
denial retire viewport, scrollbar and header input together; unchanged state
does not repeat normal lifecycle/anomaly logs.
Opposed wheel/position requests converge in existing author order; a newer
explicit scrollbar press takes control and stale former-owner drag/release cannot
move or terminate its gesture. Private windows and points outside Results retain
their original paths. Initiative-depth picking, desktop coordinate-transpiler
scope, unrelated keyboard/initiative click side effects, native window bootstrap
permission and tracking/focus bookkeeping are explicit boundary ports. Native
scroll callbacks and all Results pointer-down/drag/up paths execute. Reliable
packet delivery is a three-coordinator mailbox port, not a Photon/Bolt socket.
Compiling counterfactuals must fail at their exact causal assertion. A compile
failure never qualifies as a negative-control pass.

Use a private ADB server and at most one editor for this worker. The producer
reaps editors/compiler processes and calls `cleanup_completed_run` on every
completed or failed invocation, preserving source pins, assemblies and results.
`--presentation-source-root` can pin the exact disjoint producer checkout while
the input lane owns the coordinator/bridge. The default uses `--source-root`.
