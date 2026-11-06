# Shared public window scrolling

`run.py` compiles the exact production coordinator, codec, public-root and input
proxy implementations. Original `UguiPointer.Scroll`, `Press`, `Drag`, wheel-owner
lookup and event-data creation execute; settled hover is supplied after a real
Unity `GraphicRaycaster` hit. The existing nested-canvas registry is unmodified.

Three independent coordinator instances model separate process state. Original
Unity `ScrollRect`, `Scrollbar`, `Canvas`, `CanvasGroup`, `RectMask2D`, graphics,
camera projection and native UI callbacks execute in Unity2021.3.5f1/GLCore.
The fixture explicitly ports the game's singleton/window/session registry and
does not load a full Victory game controller or claim Photon/HMD acceptance.
Tick's existing clock parameter samples an intermediate rendered UI state without
relying on a software renderer completing a frame within33milliseconds.

The observer prototype is mounted below the same extra synthetic observer root
as the production picture. Native scroll controllers and raycasters are removed
while inactive. Original inert groups block raycasts. Only an own metadata input
surface under the actual common viewport/scrollbar can receive events. Its exact
canvas joins and leaves the production nested-pointer registry. Private client
lists deliberately have different content heights from the source prototype.

Coverage includes pre-descriptor consumption, authoritative wheel and scrollbar
input, native group permissions, common interpolation, source/nonce/generation
validation, same-frame reopening, private composed-loadout exclusion,2D-host/3D
visitor election, unmodded-host surrogate behavior and unchanged input reuse.
Compiling counterfactuals must fail at their exact causal assertion. A compile
failure never qualifies as a negative-control pass.

Use a private ADB server and at most one editor for this worker. The producer
reaps editors/compiler processes and calls `cleanup_completed_run` on every
completed or failed invocation, preserving source pins, assemblies and results.
