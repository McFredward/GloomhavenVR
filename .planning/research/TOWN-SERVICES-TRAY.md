# Movable town work tray

`TownServiceTray` owns a mod-authored `townworktray` furniture instance, the existing
Generic window grab rod, and a world-space uGUI instruction label. It contains no
native service button or transaction logic.

## Integration contract

```csharp
var tray = new TownServiceTray(nativeText, position, rotation, scale);
// Retain tray.Root as TownServiceToken's mat transform.
tray.Tick();
tray.LateTick();
// Optional: the presentation's shared appearance clock supplies 0..1.
tray.SetVisibility(visibility);
// During native close/fallback: dispose tokens before their mat.
tray.Dispose();
```

- `Root` is the centred worktop contact plane, authored at local y=0. Width is
  .44 m and depth .32 m before its uniform world/user scale. The existing sample
  drop predicate stays `abs(x)<.22`, `abs(z)<.16`, `y>-.06`, `y<.20` in this frame.
  No renderer-bounds normalization changes the prefab's authored pivot.
- `Content` is the instruction Canvas transform. `CaptionSource` is the actual
  authored **TextMeshProUGUI**, so the existing original-Graphic sampler can see it.
  It uses the supplied native font (or the existing native HUD font helper), the
  `town_sample_hint` EN/DE localization entry and a language-change subscription.
  The 400×72 px canvas is scaled to .40×.072 m and sits horizontally just above the
  front part of the worktop. It has no GraphicRaycaster; its label and CanvasGroup
  reject raycasts and cannot cover original service controls with an input surface.
- `HandleRoot` / `OriginalHandleTemplate` expose the actual local rod hierarchy.
  `CreateTemplate(nativeText)` builds the identical hierarchy, then deactivates it
  synchronously for original-template lookup without opening a local service.
  The inactive template has no registered handle or reel claim; callers dispose it.
- `Materials` exposes private furniture material copies and the rod's private
  material. Shared prefab materials, font assets, font materials, textures and
  cached grab-bar meshes are never modified or destroyed.

The grab rod is built through `GrabBarVisual.Build` with Generic style, the shared
radius and the original window Overlay material. `GrabBarLayout.Solve` supplies its
length, thickness, gap and palm-zone proportions. Its only colliders are the normal
palm box and the rod's original laser capsule. Imported furniture colliders are
disabled immediately and removed from the clone.

`PanelGrabHandle` owns palm carry, two-hand resize, laser carry/reel, hover tint and
tracking-loss handling. `PanelCarryMode.Level` and the identity level frame keep the
worktop horizontal. The final release preserves the chosen position and yaw while
discarding pitch/roll; it never turns a shared table toward a client's head. Resize
limits are the existing panel factors relative to the initial world scale. Tick
only refreshes a changed world camera; LateTick adds no competing pose writer.

## Appearance and lifecycle

The presentation owner can tween `SetVisibility` from its shared clock. Materials
which expose `_TownVisibility` receive that scalar; the instruction CanvasGroup
receives its alpha. Zero disables originally visible mesh renderers and refuses new
grabs. A material without the town shader property, including the original rod
material, keeps its original shading and changes visibility at zero. The class
does not replace that material or overwrite the shared handle's hover tint.

Disposal is synchronous for visibility and interaction: deactivating the root
unregisters the handle and releases its laser-reel claim before Unity's deferred
object destruction. Language callbacks and owned material instances are released.
Constructor failure uses the same cleanup. No animation gates native continuation.

## Verification and limits

Strict Release compilation with real game/publicized references passed with zero
warnings and errors (`dotnet build GloomhavenVR.sln -c Release -warnaserror`).
Source review verified the shared handle's registration/disable lifecycle, its
level carry and absolute scale-limit contract, original bar material ownership and
the unchanged local drop-coordinate bounds. No new carry implementation was added.

The primary agent wires this class into `TownServicePresentation` and publishes the
shared root pose/caption. A uGUI-only sampler cannot itself reproduce MeshRenderers;
the rod's mesh presentation still requires that multiplayer integration. Headset
legibility, hand reach, top-surface depth and synchronized appearance remain hardware
checks; compilation alone does not establish those outcomes.
