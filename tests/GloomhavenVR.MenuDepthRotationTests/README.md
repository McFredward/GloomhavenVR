# Original character portrait turntable

Run `python3 scripts/menu-depth-rotation/run.py` with the task-private ADB server.
`--case` selects a focused case. This is a local Unity2021.3.5/OpenGLCore proof;
it does not create an installation archive or establish a headset outcome.

The production receiver and matrix helper execute unchanged. The real original
GraphicRaycaster selects the portrait or an overlaid native control. Unmodified
UguiPointer press/drag/release/cancel methods, RayUguiDriver's tracking/primary-hand
guard and cancellation, and FlatScreen's begin/update/end/pose-loss drag methods
are extracted from the current sources. XR availability, primary hand, native
window/readiness lifetime and unrelated input/audio services are explicit ports.
The fixture does not reimplement the turn, pointer event flow or its matrix.

Three actual uGUI contexts cover world-space, native ScreenSpaceCamera and native
overlay pixels. Checks cover the first drag delta, physical portrait-width scale,
vertical and head-only movement, two simultaneous pointers, native mouse input,
tracking/dominance/pose loss, source replacement, native raycaster lock, visibility,
focus loss and depth-off retirement. Native window focus/selection and the original
focus-disable guard remain authoritative; adjacent original controls retain their
callbacks. A parent window drag canary must remain unused. Native raycast flags,
parent/size/pose are restored, including same-frame rebind and original true flags.

The actual model camera renders asymmetric original-input meshes through the
production native rotation matrix. Front,90-degree side and rear images must show
different authored markers while every original model transform and its ground
origin stay fixed. Meshes and unlit materials are controlled inputs, not native
game artwork or a Beautify visual claim. Integration of the yaw into the actual
character source renderer belongs to the parent/quality lane and requires its own
source-bound capture checks.

Eleven causal controls remove raycast admission/restoration, the first drag delta,
pointer ownership, identity reset, native ground, visible turn, native focus,
private eligibility and both upstream cancellation paths. Compiler failures are
never accepted as controls. Every run retains sources, hashes, actual assemblies,
captures and failures; owned Editor/compiler processes are reaped before the same
completion cleanup API removes generated caches.

`scripts/menu-depth-rotation/audit-native.py --output <path>` independently scans
all13 read-only native scenes. The optional `--native-profile` consumes a pinned
prior census. All12 actual camera-RT consumers have raycastTarget=false. Exactly
eight are under the original interactive UIAdventurePartyAssemblyCharacterDisplay
or UICharacterCreatorWindow. Their ancestry has UIWindow pointer-down/selection
focus, with no native pointer-click/up/drag action. Preserve its original callback
and disabled-focus permission. The four automatic retirement/creation-celebration
consumers remain unchanged. Party/creation content is private under the existing
SharedWindows contract, so this feature introduces no new synchronization exception
and does not modify game/network character state.
