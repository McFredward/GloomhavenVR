# Synthetic uGUI coordinate seam proof

```sh
TMPDIR=/dev/shm ADB_SERVER_SOCKET=tcp:localhost:5073 \
  python3 scripts/ugui-coordinates760-runtime/run.py
```

The production plane intersection, pointer raycast and complete coordinate seam
run in Unity2021.3.5. Harmony modifies the actual original `GraphicRaycaster`
call. Native Button, CanvasGroup, reversed-graphic, far-clip and blocker behavior,
scope nesting and exceptions remain observable. An explicitly admitted physical
world window uses its committed interaction frame instead of desktop-mono
viewport bounds. The actual raw `ScreenPointToRay` and native graphic permission
filters remain unchanged. Causal controls remove the coordinate, provenance and
lifetime guards. `--case` selects a bounded repair rerun.

The original **game** `RelativeMouseAtScaled` body is extracted and replayed
without changing its algorithm. Only OS/display inputs are explicit: a
representative 1920×1080 window within a 3440×1440 desktop. The Linux editor's UI
assembly has a simpler display utility and lacks physics-blocking delegates;
the fixture records both assemblies and compares its blocker behavior with the
same original editor raycaster. It does not claim to run the Windows display
backend or to prove the maintainer's headset picture. Original game raycaster
source, runtime assemblies, source pins and cleanup receipts are retained.

The backbuffer case fixes the camera and pane at 38 degrees: the original
mono-aspect bounds reject the same button at aspect 1 and accept it at 2.389.
Two explicit 100-degree eye matrices see that button. These matrices are fixture
inputs, not a live OpenXR headset. The production correction admits it without
changing any camera, projection or physical target.

The complementary common-caller proof is:

```sh
TMPDIR=/dev/shm ADB_SERVER_SOCKET=tcp:localhost:5073 \
  python3 scripts/ugui-coordinates760-runtime/run.py --common
```

It executes the game's actual `GetMousePointerEventData(int)` binary, its
original event-data allocation/reset and hover dispatch, the complete production
native-mouse shim, actual processed InputSystem mouse devices and FlatScreen's
click, wheel and drag kernels. Only exact current captured UI pixels qualify;
ordinary mouse takeover, parked/stale input, legacy providers, VR shutdown,
changed render-target ownership and nested foreign pointers retain native input.
The producer's `OwnsNativeUiPixels` body is compiled from its actual source;
capture lifecycle membership is explicit fixture input. No new mouse events or
drag-coordinate patch is introduced by production.
