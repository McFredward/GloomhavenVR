# 3D-Weltkarte — Plan

> ### ⚠ SUPERSEDED — audited 2026-09-08 against `dev` = `49ceab21` (ModBuild 483)
>
> **The "PLAN ONLY" line below is no longer true. This plan SHIPPED.** The 3D map room is built
> and lives in `src/GloomhavenVR/WorldUI/MapRoom/` (22 files, including `MapRoomDriver.cs`,
> `MapRoomSeat.cs`, `MapRoomHand.{1.Core,2.Fan,3.Wrist}.cs`, `MapTableLegs.cs`,
> `MapParchment.cs`), with the multiplayer half in `src/GloomhavenVR/Net/Remote/RemoteMapRoom.cs`.
>
> Read this file as **the design rationale for a feature that exists**, never as a proposal, and
> never for its file paths — see `.planning/INDEX.md` §5 for the refactor path translation. The
> standing rulings it quotes (no UI that re-anchors or re-positions itself as the head turns;
> game-asset environments are abandoned) are still correct and still govern.

> Status: **PLAN ONLY.** Nothing in `src/` was changed to write this. Every claim below is
> either marked *read in the code* (file + line) or *inferred* (with the reason). Where I could
> not decide something from the sources I say so instead of guessing.

---

## 0. The request (verbatim, user, 2026-08)

> Ich möchte ein weiteres größeres Feature: Komplett überarbeiten wie die world-map funktioniert.
> Die world map steht auf einem Tisch mit Bänken an beinden Enden. Statt einen flat screen vor einem,
> möchte ich das Spieler direct schon in die Umgebung Spawnen und der Tisch mit der Map als asset in
> der Raum gestellt wird (bei den beiden richtigen Umgebungen das er dort auf dem Boden steht und bei
> mied reality aus und default in der Mitte schwebt.). Im Multiplayer soll man sich also auch schon in
> der Map phase voll sehen können. Man soll mit der Map bzw den Icons dort mit dem laser oder mit
> physischem Drücken interagieren können. Die flat UI-Elemente sollen als verschiebare Fenster daneben
> schweben wie alle anderen UI Elemente auch. Diese hier sollen nur nicht schließbar sein. Da jeder
> seine eigene UI sieht, sollen diese UI Element nicht synchronisiert werden. Die buttons unten, um zu
> Bestätigen die map zu wechseln etc sollen als physische buttons am Rand auf dem Tisch vorhanden sein.
> Der Händler. etc soll erstmal auch nur als verschiebares Fenster angezeigt werden (das aber
> geschlossen werden können soll). Während dieser Phase soll keiner der Spieler ein Controllboard
> haben, das soll dann erst nachspawnen wenn man in das level / scenario lädt. Das ganze Feature soll
> optional deaktivier bzw. aktivierbar sein. Spieler die das Feature nicht aktiviert haben sind auch
> nicht im Raum sichtbar sondern sehen bei sich ganz normal die bereits implementierte 2D map wie jetzt
> auch und kommen erst dann dazu wenn das Level geladen wird. Es soll möglich sein ohne probleme -
> auch während die Karte offen ist - zwischen den Modi (2D vs 3D) hin und her zu wechseln und wenn man
> in einer Multiplayer session ist somit aufzutauchen als Maske oder nicht. Mach die hierbei erst einen
> Plan und gehe dann strukturiert vor. In diesem neuen Modus ist also nur noch das Hauptmenu wirklich
> als flatscreen zu sehen alles andere findet bereits in 3D Umgebungen statt. Die Infos über die
> jeweiligen Orte sollen über dem jeweiligen ort Symbol schwebend angezeigt werden und sich immer zum
> Spieler drehen. Auch hierbei auf Probleme dir wir schon behoben hatten für ähnliche Element zB dass
> der Text nicht flach auf dem Element liegt.

---

## 1. The architecture decision, in one paragraph

**Do not move, re-parent, clone or rebuild the campaign map. Move the PLAYER to it.** The mod's
rig root is already a *scaled* transform — `VRRigDriver` sets
`_rigRoot.transform.localScale = Vector3.one * scale` (`src/GloomhavenVR/Rig/VRRigDriver.cs:676`)
with `WorldScale` documented as "game units per real meter … so the board reads as a table"
(`VRRigDriver.cs:32-35`). The scenario diorama has never been a shrunken board; it is a giant
player. The campaign map is *already* a horizontal parchment mesh lying in world space with real
`BoxCollider`s on every location, so the identical mechanism produces the requested picture with
no game-state surgery: seat the menu rig above the parchment at a `WorldScale` that makes it read
as a ~1.2 m tabletop, widen the head camera's culling mask from mod-layer-only to the map camera's
mask, make the already-existing `MapUnlit` material override *persistent for the mode* instead of
scoped to one camera's render, promote the already-existing icon `CommandBuffer` from the mod's
private albedo camera onto the head camera, and put a mod-owned table+benches prop *underneath*
the parchment in the map's own world coordinates. The game's `MapChoreographer`, `MapLocation`
transforms, save state and host/client selection protocol are untouched — which is what makes the
feature multiplayer-safe by construction and what makes the 2D↔3D switch a *presentation* toggle
rather than a state migration.

**The main alternative I rejected: re-seat the map geometry onto a mod-owned table transform.**
It looks like the obvious move ("put the parchment on the table") and it is a trap. The game writes
**absolute world positions** into every map location at build time —
`mapLocation.transform.position = new Vector3(item.Location.MapLocation.X, …)` repeated at
`decompiled/GH.Runtime/MapChoreographer.cs:602, 612, 638, 642, 662, 666, 687, 702`, and the party
token likewise (`m_PartyToken.transform.position = startingMapLocation.CenterPosition`, `:740/:749`,
with `CenterPosition => base.transform.position` at `decompiled/GH.Runtime/MapLocation.cs:211-219`).
A mod transform on a shared ancestor is therefore correct only until the next `InitMap`, quest
unlock, city↔world switch or travel animation, after which fresh locations land at raw save
coordinates far from the moved parchment. It would need a Harmony postfix on every spawn site plus
a per-frame drift invariant — permanent maintenance against a 3 967-line game class, to buy nothing
that scaling the rig does not buy for free.

**The second alternative I rejected: keep photographing the map and paste the photo on a tabletop
quad.** That is the flat screen lying down. No parallax, no depth, no physical press, and the
laser would still have to be translated through a virtual mouse. The user asked for the opposite.

---

## 2. The finding I was asked to verify — confirmed, with a correction

The briefing's finding holds: the map is real 3D geometry, not a canvas.

- The mod renders "the REAL parchment mesh" with a mod-owned forward camera that clones the game
  map camera's render-time view+projection "so the mesh, drawn at its true GPU vertex positions,
  projects to exactly the same screen coordinates as the game's own map"
  (`src/GloomhavenVR/WorldUI/FlatScreenStereo.3.Map.cs:263-275`).
- Picking is a horizontal-plane intersection: `TryMapPlaneHit(cam, u, v, planeY, …)` at
  `FlatScreenStereo.3.Map.cs:277-288`, and the pan path intersects the focal plane `y = 0`
  explicitly (`TryMapPixelToPlane`, `:397-424`, `float t = (0f - _mapDrivenPos.y) / dir.y`).
- The parchment is one `MeshRenderer` with four quadrant submeshes, each carrying a 4096² albedo
  read off the material's `_Alb`/`_MainTex` (`GatherMapTextures`, `:828-863`).

**Correction to the framing.** It is not *merely* a re-seating problem, because of two facts that
only show up when you ask "what happens if the head camera looks at it directly":

1. **The parchment has no forward pass.** The class doc states it plainly: "every OTHER object on
   those layers uses a deferred material with no forward pass, so it stays invisible in our forward
   camera — only the parchment (whose materials we override with `MapUnlit` for exactly our render)
   draws" (`:270-273`). Our head camera is forward too
   (`src/GloomhavenVR/Rig/VRRigDriver.HeadCamera.cs:335`, `_camera.renderingPath = RenderingPath.Forward`).
   So looking at the map with the head camera and no override gives a **black parchment**. The
   `MapUnlit` override is not an optimisation, it is the only reason anything is visible.
2. **The location icons are deferred decals and draw nothing at all in a forward pass.** They are
   `ThreeEyedGames.Decalicious` `Decal` components reached by reflection (`:1424-1427`) and the mod
   already re-draws each one as a textured quad into a `CommandBuffer` attached at
   `CameraEvent.AfterForwardAlpha` (`DrawMapIcons`, `:1368-1651`).

The good news is that both problems are *already solved* in this file — the solutions are simply
scoped to the wrong camera and the wrong lifetime. That is the whole shape of the work.

**Is the material swap safe?** Yes, and the code says why: `ApplyWorldMapOverride` re-captures the
live originals every time so "the restore always puts back exactly what the game currently has",
and bails and rebuilds if the submesh count changed (`:1888-1907`); `RestoreWorldMapOverride`
(`:1910-1917`) and `ReleaseAlbedo` (`:1932-1971`) guarantee the renderer is never left overridden.
That the game tolerates a per-frame swap-and-restore at all is evidence (*inferred*, but strong)
that nothing re-drives `sharedMaterials` per frame; a *held* override for the duration of the mode
is a strictly weaker demand than what already ships.

---

## 3. What is already true (i.e. free)

### 3.1 The location icons are laser-pointable and finger-pressable *today*, with no new input path

This is the single best piece of news in the investigation.

```
// decompiled/GH.Runtime/MapLocation.cs:21
public class MapLocation : CInteractable, IPointerEnterHandler, IEventSystemHandler,
                           IPointerExitHandler, IPointerClickHandler
```

with real colliders:

```
// decompiled/GH.Runtime/MapLocation.cs:49-54
[Header("Colliders")]
private BoxCollider _boxCollider;
private BoxCollider _snappingBoxCollider;
```

and the game's own gamepad path dispatches through Unity's `ExecuteEvents`:

```
// decompiled/GH.Runtime/MapLocation.cs:266-269  (OnGamepadClick)
ExecuteEvents.Execute(base.gameObject, new PointerEventData(EventSystem.current),
                      ExecuteEvents.pointerClickHandler);
```

That is *exactly* the seam `WorldUI/ButtonCluster` already uses for Ready/Undo/Skip
(`src/GloomhavenVR/WorldUI/ButtonCluster.cs:16-24`, which cites the game's own
`BaseButtons.clickButton(GameObject)` as the precedent). So:

- **Laser**: `RayInteractor` (`src/GloomhavenVR/Hands/Interact/RayInteractor.cs`) already does
  physics picks with a settable `Mask` (`:39`). The game hit-tests locations on **layer 15**
  (`private readonly LayerMask _layerMask = 32768;` — `decompiled/GH.Runtime/Assets.Script.AdventureMap/MapLocationSelector.cs:11`).
  Point the ray at that layer, `TryGetComponent<MapLocation>`, dispatch enter/exit/click.
- **Poke**: `VRInteractables` is an *explicit registry*, not layer discovery
  (`src/GloomhavenVR/Hands/Interact/VRInteractables.cs:8-18`), so a tiny adapter component
  registered per `MapLocation` with its `_boxCollider` makes fingertips work
  (`IPokeable`, `Hands/Interact/IPokeable.cs`).

**One thing must be neutralised.** `MapLocationSelector.Update` raycasts the **screen centre**
every frame (`_screenCenterPosition = new Vector2(Screen.width/2f, Screen.height/2f)`, `:15`, used
at `:44`) through `Camera.main`, and on a hit drives
`UINavigation.StateMachine.Enter(CampaignMapStateTag.LocationHover, new MapLocationStateData(...))`.
In VR the game map camera is frozen (the mod prefix-skips `CameraController.LateUpdate` —
`FlatScreenStereo.3.Map.cs:428-430`), so screen centre points at a fixed arbitrary spot and this
component would fight our hover. Plan: **prefix `MapLocationSelector.Update` to `return false`
while 3D map mode is on and run the identical logic from the VR ray** (same `OnPointerEnter`/
`OnPointerExit` calls, same `StateMachine.Enter` tags). It is a UI-seam patch on a mod-visible
MonoBehaviour, not `ScenarioRuleLibrary` and not Bolt, and it is reversible by construction
(patch active only while the mode is on).

### 3.2 The control board already does not exist on the map — requirement #5 is free

*Read in the code, not inferred.* `CardsDriver.RebuildFakeOrClear`
(`src/GloomhavenVR/Cards/CardsDriver.6.Flows.cs:1940-1959`) takes the else-branch whenever there is
no active local hand — exactly the map situation — and the branch is
`_tray.SetVisible(false); _factory.Clear();`. The predicate is
`CardsGameApi.InScenario => Choreographer.s_Choreographer != null`
(`src/GloomhavenVR/Cards/CardsGameApi.cs:3186`), the same expression as
`VRModeStateMachine.ScenarioBoardExists` (`Core/Events/VRModeStateMachine.cs:109`). The remote side
is gated too: `RemoteControlBoard.Tick` hides on `!_owner.HasBoard`
(`Net/RemoteControlBoard.cs:412-462`), and `HasBoard` is only set when the sender transmitted a
tray pose (`Net/NetAvatarDriver.cs:1528-1531`, sampled from `PlayTray.Current?.Root`).

**One caveat that must be checked on hardware, not assumed.** `PlayTray.Destroy()` is called from
only two sites (`CardsDriver.2.Update.cs:303` driver-OnDestroy, `CardsDriver.4.Rebuild.cs:231`
board-style switch). Leaving a scenario calls neither — only `SetVisible(false)` — so the tray
*instance* and the static `PlayTray.Current` survive. Correctness of the remote hide therefore rests
entirely on Unity destroying the hands root (the tray root's parent) on scene load, which
Unity-nulls `Root` and makes `HasBoard` false. That is asserted in a comment
(`CardsDriver.2.Update.cs:507-513`) but `HandsDriver` has both a `TearDown` and a `Reparent` path
(`Hands/HandsDriver.cs:245-252, 268`). **If the hands root is reparented rather than destroyed, a
stale board pose keeps being broadcast and peers would see a floating control board on the map.**
→ verification item in Phase 9, with an assertion added if it turns out to be real.

### 3.3 The physical-button pattern exists, including the no-control-board fallback

`ButtonCluster` docks on the `PlayTray` when one exists and otherwise uses "the floating table-edge
slot … the no-tray fallback" (`src/GloomhavenVR/WorldUI/ButtonCluster.cs:70-83`). The map's bottom
bar is a set of `UIGuildmasterButton`s held by `UIGuildmasterHUD` — `enhanceButton`, `shopButton`,
`trainerButton`, `mapButton`, `templeButton`, `cityButton`, `townRecordsButton`,
`mercenaryLogButton` (`decompiled/GH.Runtime/UIGuildmasterHUD.cs:52-73`), plus
`UIGuildmasterConfirmActionPresenter ConfirmActionPresenter` (`:124`). Same `ExecuteEvents`
dispatch, same state-mirroring pattern (label from the live TMP, interactability from the live
`Selectable.IsInteractable()`).

### 3.4 The movable-window machinery is complete and generic

- `ModalFallback` floats a game window's root RectTransform onto a world-space host via
  `CanvasConversion` and registers it with `UguiPokeSurfaces`, so poke *and* laser drive the real
  uGUI (`src/GloomhavenVR/WorldUI/ModalFallback.1.Core.cs:60-76`). Detection is a data table
  (`FallbackIds`), which is where map window IDs get added.
- `GrabbableModal` makes such a window movable+scalable through the shared
  `PanelGrabHandle`/`IPanelGrabOwner` core (`src/GloomhavenVR/WorldUI/GrabbableModal.cs:6-33`).
- `ModalCloseButton` attaches the X and already carries an **exclusion list** for windows that must
  not be closable ("never attached to the Sieg/Niederlage results windows",
  `src/GloomhavenVR/WorldUI/ModalCloseButton.cs:29-32`). The user's "diese hier sollen nur nicht
  schließbar sein" is one more entry in that list — plus suppressing the modal escape chord
  (`ModalFallback.TickEscapeChord`) for those windows.
- `PanelPlacement` clamps every spawn/heal into the forward FOV
  (`src/GloomhavenVR/WorldUI/PanelPlacement.cs:6-25`).
- Draw order is per-frame by eye distance, farthest = lowest `sortingOrder`, no depth writes
  between panels (`src/GloomhavenVR/WorldUI/CanvasConversion.8.Order.cs:12-60`). Several persistent
  map windows floating at once is exactly the case this pass was built for.

### 3.5 The billboard convention and the "text lies flat" trap are both already solved

**Billboarding.** `Net/OwnerTag.cs:94-101` and `Net/RemoteNameTag.cs:236-243` both do:

```
Vector3 away = _billboard.position - head.transform.position;
_billboard.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
```

i.e. aim **+Z away from the head**, up = **world up** (`RemoteNameTag.cs:276-277` explains why
world-axis up is right for a viewer billboard). Reuse this verbatim.

**The trap the user is pointing at** is documented in `WorldUI/WorldTooltips.cs:52-60`: the game's
tooltip content "carries baked local-z / local rotation (subtle styling under the perspective UI
camera) that becomes literal geometry on a world-space host — the text protruded in 3D past the
panel". The fix already in the tree is `CanvasConversion.FlattenSubtree` — zero the baked local-z
and local rotation on every descendant, every frame, plus a `RectMask2D` to clip 2D overflow, all
reversed on `Restore`. The location placards must go through the same flattening, **and** keep the
established anti-coplanarity offsets (`ModalCloseButton.ViewerNudgePx`, `:44-47`;
`ButtonCluster` cites a 2 mm gap because "the co-planar TMP + opaque cap face z-fought per-eye",
`:912`, `:987-993`).

**Reuse recommendation: `Board/Patches/PingNameTag.cs`, not `WorldTooltips`.** PingNameTag is
already the exact shape being asked for — a world-space placard floating above a world object,
billboarded to the local head, built as a **clone of the game's own tooltip prefab** so it looks
native (`src/GloomhavenVR/Board/Patches/PingNameTag.cs:70-78`), cosmetic-only, no wire bytes,
degrading to a silent no-op if reflection fails. `WorldTooltips` is the wrong shape: it *relocates
the game's single shared tooltip canvas* to one fixed reading spot, deliberately NOT anchored to
the target ("the user wants a stable reading spot, not a spot that jumps around",
`WorldTooltips.cs:36-40`). For the map the user explicitly wants the opposite — info **over its
own icon**.

**Why "faces the player" does not violate "nothing may re-orient with head movement".** The
standing ruling forbids UI that *re-anchors or re-positions* itself as the head turns — content
that chases you, so that turning your head cannot bring anything new into view and the world stops
being a place. A billboard does not move: its **position is fixed in the world** (it hangs over its
icon and stays there when you walk around the table), only its **facing normal** tracks the eye so
the glyphs stay legible. It is the same exception the project already ships four times over
(`OwnerTag`, `RemoteNameTag`, `RemoteEmptyFanHint`, `PingNameTag`). The invariant to write into the
code comment: *a placard's world POSITION is a pure function of its icon; only its rotation reads
the head, and nothing else about it does.*

### 3.6 The environment style set and its room-local convention

`Core/SkyAlternative.cs:33-65` — `Default = 0` (game's own sky), `Cellar = 1`, `SwampNight = 2`
(a night **forest** since ModBuild 132; the file name `Env_Swamp.prefab` is a runtime contract),
`OffBlack = 3` (nothing at all — no prefab loaded, no GameObject, no particles). Plus mixed reality
as an orthogonal dial that takes precedence over all four. That maps onto the user's sentence
exactly: *"bei den beiden richtigen Umgebungen"* = `Cellar` + `SwampNight` (table on the floor);
*"bei mixed reality aus und default"* = MR / `Default` / `OffBlack` (table floats centred).

The room-local frame is `GloomhavenVR.SkyAlternative.Room.<style>`, and its convention is stated
verbatim at `SkyAlternative.cs:1112-1116`: *"they are authored around the origin with the floor at
y = 0 — **the room frame's origin IS its floor**"*. The single placement write (`TryPlaceRoom`,
`:1403-1517`) is:

```
float playWorld = PlaySpaceToBoardRatio * extent;   // 4.5 × board world extent
float roomScale = playWorld / _roomAuthoredPlayExtent;
float floorY    = undersideY - FloatGapToBoardRatio * extent;   // 0.75 × board extent
room.transform.SetPositionAndRotation(new Vector3(center.x, floorY, center.z), boardYaw);
room.transform.localScale = Vector3.one * roomScale;
```

`_roomAuthoredPlayExtent` comes from an empty child named exactly `PlaySpace` whose `localScale.x`
is the authored diameter of the usable open area — a hard contract with the bake
(`unity/.../Editor/BuildEnvironments.cs:2656-2681`, `AddPlaySpace`), read then destroyed at spawn.
Authored: `ForestPlaySpaceDia = 9.0f`, `CellarPlaySpaceDia = 6.5f`
(`unity/.../Editor/BuildEnvironmentRooms.cs:55-56`).

The never-re-seat ruling is enforced structurally, not by discipline
(`SkyAlternative.cs:1058-1076`): `_roomPlaced` latches on the one first placement and only the SKY
branch has a re-seat path (gated on `VRRigDriver.RigPoseVersion`, i.e. "the player was teleported"
events). If the board is not yet measurable the room root is `SetActive(false)` rather than placed
provisionally, because *"a stand-in room would be a lie that then has to be re-seated, i.e. a
teleport"* (`:290-296`).

**A correction to the briefing.** `Board/BoardFrame.cs` and `Net/IBoardAnchor.cs` are *not* the
room seating. `BoardFrame` is a decorative stroke mesh baked around the mod's own card-tray face,
derived from anchor empties on `PlayTray.prefab`. `IBoardAnchor` is the multiplayer *wire* frame,
and its own doc records that the `PlayTray._root` guess was wrong and that the shared frame is world
space (`Net/IBoardAnchor.cs:5-16`). The room-local frame is the `_roomGo` transform above and
nothing else.

### 3.7 The opt-in switch already exists, reserved, with its failure mode documented

`[Rig] Experimental3DMap` is bound at `src/GloomhavenVR/Plugin.cs:303-311`, defaults false
(`Defaults/Defaults.Plugin.cs:30`), has a German description
(`Core/Loc.ConfigDescriptions.German.cs:682`) and is suppressed from the config catalog
(`WorldUI/ConfigCatalog.cs:772`). Its description is the plan's own prehistory:

> "RESERVED — CURRENTLY UNIMPLEMENTED placeholder … Today this switch has NO effect: everything
> before an actual combat scenario … deliberately stays in Menu2D on the floating screen, because
> the map scene was never authored for a free VR camera (test #8: giant map below the player, black
> flat window). The wish is saved here so it survives into a later phase."

with the guard rail at `Rig/VRRigDriver.cs:494-499`: *"it must never silently re-enable the broken
orbit-camera anchoring"*.

**This is the switch the feature uses** — no new config key. And test #8 names the exact mistake the
architecture in §1 avoids: test #8 anchored the rig to `CameraController.s_CameraController` on the
map scene and got a giant map below the player. The plan does not anchor to the game camera at all;
it seats the rig from the **parchment renderer's world bounds** and scales `WorldScale` from them.
Write that contrast into the code comment, because the next person will otherwise re-try the camera
anchor.

---

## 4. What must actually be built — the real problems

### 4.1 The head camera currently cannot see the map at all, on purpose

`VRRigDriver`: "SCENARIO rig = anchor camera's mask OR'd with `VRLayers.ModLayerMask`, never 0;
**MENU rig = the mod layer ONLY** (test #10 — Menu2D shows the world through the FlatScreen RT,
never directly)" (`src/GloomhavenVR/Rig/VRRigDriver.cs:45-49`), "Re-asserted every frame".

So 3D map mode needs a **third rig flavour: the MAP rig** — mask = the game map camera's culling
mask (the same mask `ReconcileAlbedoCamera` clones, `FlatScreenStereo.3.Map.cs:473+`) OR the mod
layer, seated over the parchment, at a map-appropriate `WorldScale`. This is the single highest-risk
edit in the feature because that mask assertion is per-frame and test-#10-load-bearing: get it wrong
in the *main menu* and the menu renders twice or not at all. **Mitigation: the map rig flavour must
be gated on a positively-decided map-open signal, never on "not a scenario".** That signal already
exists and is trusted three times over in this file: a `MapChoreographer` whose `worldMap` or
`cityMap` is `activeInHierarchy` (`TickFastMapEngage`, `:158-187`; `TickNonBlackMapDetect`,
`:105-128`).

### 4.2 The environment system is hard-gated on a scenario board and measures itself off hex tiles

`SkyAlternative.cs:783-787`:

```
// SCENARIO-ONLY SCOPE (class doc, second ruling): outside a live scenario board the
// feature stands down entirely …
if (!Events.VRModeStateMachine.ScenarioBoardExists) { Deactivate(); return false; }
```

and the placement is derived from the board: source `ObjectCacheService.GetTileBehaviors()`, extent
from tile world positions widened by half a hex, underside from renderer bounds and room-chunk
volumes (`SkyAlternative.cs:272-294`); room scale `= (PlaySpaceToBoardRatio × boardWorldExtent) ÷
authoredPlayExtent` (`:208`); yaw from the board's transform (`:226-228`); and then **zero per-frame
writes and no re-seat ever** — "ModBuild-131 ruling: re-seating a room the player stands in IS a
teleport" (`:233-238`).

The map phase has no `Choreographer`, no `TileBehaviour`s and no board. So the environment needs an
**anchor abstraction**: today "the board" is hard-wired as *the thing a room is sized, positioned and
yawed against*; it must become an interface with two implementations — `BoardAnchor` (today's hex-tile
measurement, unchanged, byte-for-byte) and `MapTableAnchor` (footprint = the mod's table prop,
underside = the table's foot plane, yaw = the table's yaw). The never-re-seat ruling survives
unchanged and in fact becomes *easier*: unlike the board, the table's pose is chosen by the mod, so it
is known before the room is placed, and both are placed once at map entry.

**And the bake will actively refuse the obvious layout.** `AssertPlaySpaceClear` / `PlaySpaceCheck`
(`unity/.../Editor/BuildEnvironmentRooms.cs:2386-2470`) **fail the build** if any standing geometry
intrudes into the PlaySpace disc, and `PlaySpaceCarpetR = 1.70f` / `PlaySpaceCarpetY = 0.75f`
(`:104-139`) reserve a hard clearance directly under the floating board. A table with benches *under
the map, at the room's centre* is precisely the shape that gate exists to reject
(`PlaySpaceCarpetGateSelfTest`, `:2474`, proves the gate fires both ways every build).

This is not a bug to route around — it is the design speaking. The gate encodes "the board floats
above an empty clearing you stand in". The 3D map is the opposite picture: **the table IS the floor
the map stands on.** So the resolution is a modelled one, not an exemption:

- The table is **not** baked into `RoomGeo`. It is a standalone prefab under `Assets/Bundle/Table/`,
  loaded through `WorldUI/WorldUIAssets.TryLoadPrefab` (the same path `PlayTray.prefab` uses), and
  seated by the mod at runtime. `AssertPlaySpaceClear` never sees it.
- `MapTableAnchor` therefore reports the table's own footprint and its **top plane** as the reference,
  and the room's `floorY` becomes "one table height below the parchment" instead of
  `0.75 × extent` below a floating board. The room still gets an empty clearing around the table;
  the table simply occupies the middle of it, standing on the floor, which is what the user asked for.

That also keeps the two rooms' bakes byte-identical — nothing in `BuildEnvironmentRooms.cs` changes,
which is worth a lot given it is a 19 283-line file.

### 4.2b Mixed reality destroys the environment, and a floating table there is a NEW ruling

`Core/MixedReality.cs:681-686` calls `SkyAlternative.StandDown()` unconditionally when MR turns on,
and `SkyAlternative` never hides — it destroys ("there is no disabled-renderer path and no
active-but-invisible path anywhere in this file"). So in MR there is no room, no floor and no props.
The MR class doc also records the ruling *"no geometry over passthrough … the ART's to keep"*
(`MixedReality.cs:657-659`).

The user's request explicitly asks for the table to **float centred** in MR. That is a *new*
decision, not an extension of an existing one, and it should be put to him in those words before the
MR branch is built. The mechanism is available — the MR sky-hiding sweep is written to skip anything
that is not a surrounding dome, and its own doc names "a flat floor tile or a table prop" as the
negative example (`MixedReality.cs`, `HideSkyGeometry`) — so the table would survive the sweep. But
no mod-built 3D prop has ever existed in MR, so this is untested territory.

### 4.2c In `Default`/`OffBlack` (no environment, no MR) the table also floats

Same mechanism as MR, minus passthrough: no room is spawned, the table is seated from the parchment,
nothing is placed under it. This falls out of the design rather than needing a branch — which is
itself the argument for seating the table off the parchment rather than off a floor.

### 4.3 The map's flat UI is a screen-space HUD with no per-window entry points yet

`ModalFallback`'s whole detection layer is scenario-shaped (`FallbackIds` = "in-scenario
interactive/blocking window IDs", `ModalFallback.1.Core.cs:28-31`). The map-phase windows are a
different, mostly **persistent** set. From `decompiled/GH.Runtime/UIWindowID.cs` the map-relevant
ids are: `Village`, `Shop`, `PartyPanel`, `MapNodeInfoPanel`, `EventsPanel`, `HeroLevelUpPanel`,
`RewardsPanel`, `EnhancementShop`, `QuestTracker`, `MapObjectiveManager`, `PartyRosterPanel`,
`PartyAssemblyWindow`, `EquipmentItemsPanel`, `MutiplayerHeroAssignPanel`, `QuestPopup`,
`UnlockQuestPopup`, `CompendiumPanel`, `TownRecordsPanel`-ish entries.

Two classes, per the user:
- **Persistent, non-closable, movable** — the always-on map HUD (party panel, quest tracker,
  objectives). No X, no escape chord, respawned if destroyed.
- **Transient, closable, movable** — `Shop` ("der Händler"), `EnhancementShop`, `Village`,
  `HeroLevelUpPanel`, `RewardsPanel`, events. Ordinary `ModalFallback` behaviour.

**OPEN QUESTION (must be answered before the UI phase is planned in detail):** I did **not**
establish which concrete canvases/GameObjects the persistent map HUD lives on, nor whether
`UIGuildmasterHUD`'s bar and its mode windows are separate `UIWindow`s with usable ids or one
composite canvas. `UIGuildmasterHUD` holds `templeWindow`, `shopWindow`, `enhancementWindow`,
`trainerWindow` as fields (`decompiled/GH.Runtime/UIGuildmasterHUD.cs:126-134`) — so at least the
guildmaster sub-windows are separable. A one-session hierarchy dump on hardware while the map is
open (there is already a precedent: `LogMapSceneRenderers`, `FlatScreenStereo.3.Map.cs:1125`)
would settle it definitively. **Budget a spike for this; do not plan the UI phase blind.**

### 4.4 Multiplayer: only the HOST may pick a location

This constrains the feature more than anything else and it is not obvious from the request.

```
// decompiled/GH.Runtime/UIMapMultiplayerController.cs:183-186
public void OnSelectedLocation()
{ if (!FFSNetwork.IsOnline || !FFSNetwork.IsHost || !InQuestSelectionPhase()) return; … }

// :259-266
public void ConfirmSelectedLocation()
{ if (FFSNetwork.IsOnline && FFSNetwork.IsHost && InQuestSelectionPhase())
  { … Synchronizer.SendGameAction(GameActionType.SelectQuest, ActionPhaseType.MapHQ, …); } }
```

Clients do not select; they **ready up** (`UpdateReadyPlayer(NetworkPlayer, bool)`, `:296`;
`EReadyUpToggleStates`). Same for city events and town records (`:269-285`). So:

- The 3D map must not *pretend* every player can pick. A client's laser on an icon may hover and
  show the placard (local, harmless) but its click reaches a game path that no-ops.
- The physical table buttons must mirror the game's own live interactability, exactly as
  `ButtonCluster` does — a client's confirm cap is simply not interactable, which is *the truth the
  flat game shows too*. Do not invent a VR-only affordance here.
- **Consequence for the design of the table**: benches at both ends is right for 2 players; for 3–4
  the seats must be distributed around it. `Rig/SpawnRing.cs` already solves "seat an arriving peer
  across from the others, at the table edge, facing the centre" from a footprint — feed it the
  table footprint instead of the board footprint and it works unchanged.

### 4.5 Remote avatars are visible in every scene already — and on the map they will pile up

Two facts from `Net/`, both of which cut against the request:

**(a) There is no visibility gate to reuse, and none to fight.** A remote avatar is created lazily on
the first rig packet from a peer (`NetAvatarDriver.GetOrCreate`, `Net/NetAvatarDriver.cs:2900-2918`)
and destroyed on a 3 s staleness timeout (`TickAvatars`, `:2922-2960`,
`NetProtocol.StaleTimeoutSeconds = 3f`). There is **no** scene, board, character-assignment or
config gate on creation, and the only per-part gate is validity/tracking
(`Net/RemoteAvatar.cs:1443-1460`). The `[Net] RemoteBoards` dial is explicitly *not* an avatar gate —
`Net/RemoteBoardVisibility.cs:49-54`: *"Hiding a peer's hands would be a different feature."* The
broadcast deliberately does not even wait for character assignment, *"so two players see each other
from the lobby on (user requirement, second MP test)"* (`NetAvatarDriver.cs:757-762`).

So **today, on the campaign map, peers already see each other's heads and hands** — floating in a
scene neither of them is rendering. The user's "Spieler die das Feature nicht aktiviert haben sind
auch nicht im Raum sichtbar" therefore needs a *new* suppression, in both directions, and it is the
one genuinely new piece of net behaviour in the feature.

**(b) All menu rigs sit at the same world point.** `BuildMenuRig`
(`Rig/VRRigDriver.cs:737-770`) puts the rig root at `_menuAnchorPos` / `_menuAnchorYaw` — the menu
camera's authored vantage — at `localScale = Vector3.one`. Since every client is in the same scene
looking at the same authored camera, *(inferred, not measured — nothing in the repo measures it)*
every player's head lands at roughly the same world coordinate. Avatars would interpenetrate rather
than sit around a table. `Rig/SpawnRing.cs` is the seat solver and it is board-derived, so it does
nothing here. **The map room needs its own seat assignment**, and the benches make that easy: fixed
seats, deterministic order (stable player index), so every client agrees who sits where without a
single byte on the wire.

**The existing precedent for "this peer is not here".** `NetSession.FlatNetMode`
(`Net/NetSession.cs:24`) makes a peer simply stop sending — *"our packets simply stop, which to every
peer looks exactly like a flat player (that is the point)"*. And `NetProtocol.FlagHasBoard`
(`NetProtocol.cs:120`) is the established per-packet shape for "this peer has no X right now".
The map-room presence bit should follow `FlagHasBoard`'s shape, not `FlatNetMode`'s sledgehammer —
a 2D-mode player must still be visible in the *scenario*, so we cannot stop sending.

---

## 5. Phases

Sizes are rough implementation effort for one worker in one worktree lane: **S** ≈ a few hundred
lines, **M** ≈ a module-sized change with a hardware round, **L** ≈ multiple rounds.

Each phase is independently shippable and **independently testable on hardware** — the user works
in hardware rounds, so no phase depends on a later phase to produce a visible result.

---

### Phase 0 — Spike: what is actually on the map, and where (S, no ship)

Not a feature. One instrumented build that, while the campaign map is open, dumps:
the map root hierarchy and layers; the `MapChoreographer` parents and their world bounds; every
active canvas with its render mode, its `UIWindowID` (where it has one) and its owner component;
the `UIGuildmasterHUD` bar's button GameObjects; the map camera's culling mask; the parchment
renderer's world bounds and thickness; **and, in a two-client session, each peer's received head
world position on the map** (risk 7 — are all the menu rigs really at the same point?). Extends the
existing `LogMapSceneRenderers` / `MAP ICON GEOMETRY` diagnostics
(`FlatScreenStereo.3.Map.cs:1125`, `:1623`) rather than inventing a new logger.

**Why first:** §4.3 is a genuine unknown and §4.1's culling mask must be read, not guessed. Every
later phase's file list depends on this dump. It costs one round and removes the largest single
source of replanning.

**Files:** `src/GloomhavenVR/WorldUI/FlatScreenStereo.3.Map.cs` (diagnostics only).

---

### Phase 1 — The map rig: stand in the map, see the parchment (M)

The first shippable, and the one that proves or kills the architecture.

- New `WorldUI/MapRoom/MapRoomDriver.cs` (or `Core/MapRoom.cs` — name to settle at review) owning
  the mode: **on** when `[Rig] Experimental3DMap` is on (§3.7 — the switch already exists, reserved;
  no new config key) **and** a `MapChoreographer` has an active `worldMap`/`cityMap` (reuse the
  positive signal from `TickFastMapEngage`). Un-suppress it in `WorldUI/ConfigCatalog.cs:772` and
  rewrite its description in `Core/Loc.ConfigDescriptions.German.cs:682` from "RESERVED —
  UNIMPLEMENTED" to what it now does.
- New rig flavour in `VRRigDriver`: map-rig culling mask (map camera mask | mod layer), seat pose
  over the parchment, `WorldScale` derived from the parchment's world extent so it reads ~1.2 m
  across. Guarded so the main menu and scenario paths are byte-identical when the mode is off.
- Lift `BuildOverrideMaterials` / `ApplyWorldMapOverride` / `GatherMapTextures` out of
  `FlatScreenStereo` into a shared owner so the override can be held for the mode's lifetime.
  `RestoreWorldMapOverride` on every exit path — the teardown guarantee is non-negotiable.
- Promote `DrawMapIcons`' command buffer onto the head camera (it already attaches to whatever
  camera it is handed, `:1412-1418`), keeping the depth-clear trick and the party-token re-draw.
- Suppress the `FlatScreen` while the mode is on.

**Testable on its own:** the user stands over the campaign map in VR and sees the parchment,
the icons and the party token in stereo, with no table, no windows, no interaction. That is a real
hardware verdict on the whole approach.

**Files owned:** `Rig/VRRigDriver*.cs`, `WorldUI/FlatScreenStereo.3.Map.cs`,
`WorldUI/FlatScreen.4.Lifecycle.cs`, new `WorldUI/MapRoom/*`, `Plugin.cs` (description only),
`Core/Loc.ConfigDescriptions.German.cs`, `WorldUI/ConfigCatalog.cs`.

---

### Phase 2 — The table and benches (M)

- A **standalone** `MapTable.prefab` under `unity/GloomhavenVR.Assets/Assets/Bundle/Table/`,
  loaded at runtime through `WorldUI/WorldUIAssets.TryLoadPrefab` — the same path `PlayTray.prefab`
  already uses. **Deliberately not baked into `RoomGeo`**, for the reason in §4.2: the PlaySpace
  clearance gate would fail the build. Geometry: procedural planks textured with the already-imported
  CC0 `dark_wooden_planks` set (§8), sized from measured furniture dimensions the way the cellar
  stool is (`BuildEnvironmentRooms.cs:5510-5520` scales to a measured 0.45 m seat height — do the
  same for a bench seat and a 0.75 m table top).
- Seat it under the parchment in the map's own world coordinates, sized from the parchment bounds.
- Fixed bench seats by stable player index become the local seat and the peer seats (risk 7).
  `Rig/SpawnRing.cs` is the reference for "at the edge, facing the centre", but the map case is
  simpler: the seats are authored, not solved.

**Testable on its own:** the map now sits on a table you can walk around.

**Files owned:** `unity/.../Assets/Bundle/Table/**`, a new
`unity/GloomhavenVR.Assets/Assets/Editor/BuildMapTable.cs`, `WorldUI/WorldUIAssets.cs`,
`WorldUI/MapRoom/*`. **Verifiable without hardware** via the preview-render harness
(`unity/.../Editor/PreviewEnvironments.cs`) under `xvfb-run` — this phase should ship with preview
renders in the PR. Note `BuildEnvironmentRooms.cs` is deliberately **not** in this list: keeping the
two room bakes byte-identical is a feature of the design.

---

### Phase 3 — The room around the table (M)

- Introduce the anchor abstraction described in §4.2. `BoardAnchor` must be a pure refactor with an
  empty behavioural diff for scenarios (the refactor guard baseline is the check).
- `MapTableAnchor` for the map phase, reporting the **table's** footprint, yaw and top plane; widen
  the `ScenarioBoardExists` gate at `SkyAlternative.cs:783` to "a scenario board **or** a seated map
  table exists". The `floatGap` term does not apply in the map case: the table *is* the floor the map
  stands on, so `floorY` = table foot plane, not `undersideY − 0.75 × extent`.
- Preserve the never-re-seat structure exactly: one `_roomPlaced` latch, no per-frame writes, and no
  provisional placement before the table is measurable.
- Mixed reality / `Default` / `OffBlack`: no room, table **floats centred** (§4.2b/§4.2c). The MR
  case is gated behind an explicit user ruling — build the `Default`/`OffBlack` branch first, which
  needs no ruling, and hold the MR branch until he answers.

**Testable on its own:** switch the environment dial on the map and get cellar / night forest /
black / passthrough, with the table standing on the floor in the first two.

**Files owned:** `Core/SkyAlternative.cs`, `Core/MixedReality.cs`, `Core/Haunt*.cs` (scope check
only), new `Core/EnvAnchor*.cs`.

---

### Phase 4 — Icon interaction: laser and finger (M)

- `MapLocationInteractor` adapter: registers each live `MapLocation` with `VRInteractables` using
  its `_boxCollider`; dispatches `pointerEnter`/`pointerExit`/`pointerClick` via `ExecuteEvents`.
- Ray path: `RayInteractor.Mask` includes layer 15 while the mode is on; hover/click routing
  mirrors `Cards/CardsDriver.3.Laser.cs`'s arbitration conventions.
- Prefix `MapLocationSelector.Update` off while the mode is on and reproduce its
  `UINavigation.StateMachine.Enter(LocationHover/WorldMap, …)` transitions from the VR hover.
- Re-scan on the same cadence `DrawMapIcons` already uses (`_iconCacheFrame`,
  `IconCacheIntervalFrames`) — locations are destroyed and respawned by `InitMap`
  (`MapChoreographer.cs:585-596`), so registrations must be lifecycle-safe.

**Testable on its own:** point at a location, it highlights and plays the game's own hover sound;
press it with a fingertip, it selects (host) or no-ops (client) exactly as the flat game would.

**Files owned:** new `WorldUI/MapRoom/MapLocationInteractor.cs`, `Board/Patches/` (one new patch
class), `Hands/Interact/RayInteractor.cs` (mask plumbing only).

---

### Phase 5 — Floating location placards (S–M)

- Clone the game's own location marker/info presentation the way `PingNameTag` clones the ping
  tooltip; position = icon world position + a fixed world-up lift; rotation = the `OwnerTag`
  billboard formula; **position never reads the head.**
- Route the content through `CanvasConversion.FlattenSubtree` and give the text the established
  anti-coplanar viewer nudge, so it does not lie flat on its backing.
- The game's own screen-space followers (`UIFollowMapLocation`, which projects through
  `CameraController.s_CameraController.m_Camera.WorldToScreenPoint`,
  `decompiled/GH.Runtime/UIFollowMapLocation.cs:26-51`) are useless in VR and must be hidden while
  the mode is on, then restored.

**Testable on its own:** names and quest info hover over their icons and stay legible from any
side of the table.

**Files owned:** new `WorldUI/MapRoom/MapLocationPlacard.cs`, `WorldUI/WorldTooltips.cs` (read-only
reference; edit only if the shared flatten helper must move).

---

### Phase 6 — Physical table-edge buttons (M)

Port the `ButtonCluster` pattern to the table rim for the `UIGuildmasterHUD` bar (§3.3): label from
the live TMP, colour from state, collider disabled when the real `Selectable` is not interactable,
click via `ExecuteEvents.pointerClickHandler` on the real button GameObject. Geometry is config and
lives in the `[RoundButtons]`-style family so it can be tuned live.

**Testable on its own:** press a wooden button on the table rim to switch world↔city map.

**Files owned:** new `WorldUI/MapRoom/MapButtonRail.cs`, `WorldUI/ButtonTuning.cs`,
`WorldUI/NativeButtonSkin.cs` (shared skin, read-mostly).

---

### Phase 7 — The floating windows (L)

Depends on Phase 0's dump. Two window classes (§4.3), the non-closable set added to
`ModalCloseButton`'s exclusion list and to an escape-chord exclusion, the closable set behaving like
every other floated modal. Spawn poses through `PanelPlacement`, order through
`CanvasConversion.8.Order`, grab through `GrabbableModal`.

**Testable on its own:** the merchant opens as a movable window you can close; the party panel and
quest tracker float beside the table and cannot be dismissed.

**Files owned:** `WorldUI/ModalFallback.*.cs`, `WorldUI/ModalCloseButton.cs`,
`WorldUI/CanvasConversion.*.cs`, `WorldUI/PanelPlacement.cs`.

---

### Phase 8 — Multiplayer presence and the live mode switch (M)

The wire work (§6) and the switch sequences (§7). Last, because it is only meaningful once there is
a room to be present in. Claim record id **18** (or 19/20/21) per the parallel-work reservation rule
and say which in the change report; bump `ModBuild` (156 → next).

Note this phase does two things that are easy to conflate: it adds the presence bit *and* it adds
the **suppression** — today peers are visible on the map unconditionally (§4.5), so a 2D-mode player
who does nothing is currently *more* visible than the feature wants, not less.

**Testable on its own:** two headsets in one session, one toggling the feature live and appearing/
disappearing at the table without either client hitching.

**Files owned:** `Net/NetProtocol.cs`, `Net/PresenceState.cs`, `Net/NetAvatarDriver.cs`,
`Net/RemoteAvatar.cs`, `WorldUI/MapRoom/*`. **This phase must not run in parallel with another lane
that also edits `NetProtocol.cs`/`PresenceState.cs`** — the record-id collision the reservation rule
exists to prevent happened once already at merge.

---

### Phase 9 — Verification sweep (S)

Confirm §3.2 on hardware (no control board on the map, board appears on scenario load), confirm the
teardown guarantee (no override left on the game renderer, no orphan prop, no rig-mask residue)
using `Core/TeardownReport.cs`, and confirm a 2D-mode player is genuinely invisible to a 3D-mode
peer and vice versa.

---

## 6. The wire contract

**Almost nothing travels, and that is the design.**

- **The flat panels: nothing.** The user's ruling ("Da jeder seine eigene UI sieht, sollen diese UI
  Element nicht synchronisiert werden") matches what the project already does for every converted
  panel; there is no existing panel-position sync to disable.
- **The map state itself: nothing new.** The game already syncs it, host-authoritatively, through
  `Synchronizer.SendGameAction(GameActionType.SelectQuest, ActionPhaseType.MapHQ, …)`
  (`decompiled/GH.Runtime/UIMapMultiplayerController.cs:265`). Adding a second channel for the same
  facts would be a second source of truth. Do not.
- **Player pose in the map room: existing embodiment packets, unchanged.** Heads and hands already
  ride `NetAvatarDriver`'s rig packets (`Rig/SpawnRing.cs:36-41` cites
  `NetAvatarDriver.CollectPeerHeads`). Nothing about a head pose changes because the room is a map
  room rather than a scenario room.
- **The one genuinely new fact: "I am in the 3D map room."** One bit per peer, so that a peer with
  the feature off is not drawn standing in a room they cannot see, and so that a live toggle makes
  someone appear or disappear cleanly. This belongs in the presence channel, not a new packet type.

**Shape.** One extension record, `ExtIdMapRoom`, payload 1 byte: bit 0 = "I am in the 3D map room".
It rides the extras extension tail, which is a self-describing TLV — `[count]` then
`count × [id][len][payload]` (`Net/PresenceState.cs:1301-1304` write, `:2435-2452` read). The read
loop's own comment states the property that makes this safe:

> "THE SKIP IS THE POINT: a record whose id this build does not know is stepped over by its own
> length, so a newer peer may add fields without this reader being taught about them and without
> breaking."

so a peer on an older build silently reports nothing → treated as **not present** → not drawn in the
room, which is exactly correct. **No wire `Version` bump** (that byte is 3 and a mismatch hard-drops
the packet, `NetProtocol.cs:38`, `PresenceState.cs:2356-2358`; additive records never touch it).
`NetProtocol.ModBuild` is bumped per the standing rule but is not a rejection either — it drives the
`VersionGuard` dialog (`Net/VersionGuard.cs:134-141`), whose answers are "join as a flat player"
(`FlatNetMode`) or "cancel".

**Record id — VERIFIED, and the briefing was wrong.** On this branch every `ExtId*` in
`Net/NetProtocol.cs` is 1..17 and 22..32; **there is no "story sync" record and 19 is not taken**.
`NetProtocol.cs:3457-3460` is the most recent claim note: *"Ids in use are now 1..17, 22..32; 18..21
stay reserved for the parallel round that claimed them; 33+ are free."* And `:4170-4181` explains
why the reservation exists — two workers once both took "the next free id" independently and it was
caught only at merge. **Since this feature will be built while other lanes are in flight, take one of
18..21 and say so in the change report.** `ModBuild` is currently 156 (`NetProtocol.cs:419`), not
148/152.

**The six edits, per the established pattern** (record 15 `ExtIdPileCounts` is the smallest complete
worked example): declare `ExtIdMapRoom` + `MapRoomRecordBytes` in `NetProtocol.cs` with the claim
comment; add `HasMapRoom` + the field to `struct PresenceState`; write it in `PresenceSerializer.Write`
**in id order** with a bounds check and `records++`; read it in `TryRead` with a length check;
**add its worst case to the `MaxSize` sum comment in the same commit** (standing rule,
`PresenceState.cs:1131-1134`; currently 1600 with worst case 1326); sample it in
`NetAvatarDriver.TickExtrasSend` and consume it on `RemoteAvatar`.

**Rate.** Extras ride at `ExtrasSendRateHz = 5f` (`NetProtocol.cs:52`), which is right for a sticky
bit; nothing here needs the 15 Hz promotion the moving-board pose uses
(`NetAvatarDriver.cs:924-931`). A *toggle* should pre-empt the rate gate and send on the frame it
happened, the way fan-count changes already do (`:817-828`) — the switch must not take 200 ms to be
seen.

**Receiver rules the codebase enforces everywhere and this must honour:** mask to the bits this build
defines (`PresenceState.cs:2493-2498`); never trust the wire, re-clamp; and fail **closed to the
pre-record picture** rather than to a clamped extreme (`:2571-2592`) — i.e. an absent bit means
"not in the room", the exact picture a peer predating the record renders.

**Standing rule that constrains this:** local settings take precedence and a player with the
feature off is never corrected. Nothing arriving on the wire may switch the 3D map on for someone
who switched it off; the flag is *reporting*, never *instructing*. The suppression is symmetric and
purely receiver-side: a 3D-mode client draws only peers whose bit is set; a 2D-mode client draws
none of them, because it has no room to draw them in.

---

## 7. The mode switch, exactly

Both directions must work **while the map is open**, in a session, with no reload.

### 7.1 2D → 3D

1. Latch the local config on. Broadcast `MapRoomPresent = true` on the next presence tick.
2. `FlatScreen.Hide()` — its documented restore path puts every captured camera's `targetTexture`
   and clear flags back (`FlatScreen.1.Core.cs:44-46`). The flat map's own render is left alone
   because it was never the source of truth.
3. `EngageMapCore()` equivalent for the mode: acquire the parchment renderer, build the `MapUnlit`
   override materials, **apply and hold**.
4. Rebuild the rig into map flavour: mask, seat pose, `WorldScale`. This bumps `RigPoseVersion`,
   which is the established signal for "re-derive rig-cached poses"
   (`VRRigDriver.cs:80-86`) — panels re-heal through `PanelPlacement`, which is idempotent inside
   the view cone.
5. Instantiate the table; seat it; seat the environment room off the table anchor.
6. Attach the icon command buffer to the head camera; register `MapLocation` pokeables; disable
   `MapLocationSelector`; hide the game's screen-space location followers.
7. Convert + float the map windows.
8. Remote avatars whose `MapRoomPresent` is true become visible.

### 7.2 3D → 2D

Strict reverse, and every step is an existing teardown that already exists and is already tested:

1. Broadcast `MapRoomPresent = false`.
2. Release the floated windows (`CanvasConversion.Release` restores each to its exact 2D home).
3. Re-enable `MapLocationSelector`, restore the game's followers, unregister pokeables, detach the
   command buffer, destroy the placards and the button rail.
4. Despawn the environment room and the table.
5. `RestoreWorldMapOverride()` + `DestroyOverrideMaterials()` — the game renderer must end holding
   exactly its own materials.
6. Rebuild the rig into menu flavour (mod layer only).
7. `FlatScreen.Show()`; the existing probe/engage path takes the map back over from where it left
   off. **The game's map state was never touched, so there is nothing to migrate.**
8. Hide every remote avatar.

### 7.3 The property that makes this safe

The switch touches **presentation only**. No step above writes game state, sends a game action, or
alters `MapChoreographer`/`AdventureState`. A player toggling mid-selection loses nothing, and two
players on different settings are looking at the same map. That is the argument for the whole
architecture in one sentence, and it is why I would not accept any design that re-parents the map.

---

## 8. Assets — a table with benches at both ends

**Standing rulings that apply:** game-asset environments (Apparance / map prefabs) are abandoned —
custom style-matching assets only, built in the bake; search for assets rather than hand-building;
**CC0 / public domain only**, licence verified at the source and quoted in `License.md`; raw sources
never committed.

**The pipeline already exists and is exactly right for this.**
`unity/GloomhavenVR.Assets/Assets/Bundle/Environments/License.md` documents a **Poly Haven (CC0 1.0)**
import pipeline with a reproducible script (`Assets/Editor/polyhaven_pipeline.py` — python3 +
pymeshlab + Pillow, doing UV-preserving decimation, AO multiplied into albedo, opacity merged into
albedo alpha, resolution reduction). Imported models already in the tree include
**`small_wooden_table_01.obj`** and **`wooden_stool_02.obj`**
(`unity/GloomhavenVR.Assets/Assets/Bundle/Environments/Imported/Models/`), both Poly Haven CC0, both
already style-matched to the cellar.

### Candidates (pages fetched and read this session; licence quoted as stated)

Poly Haven's licence page (<https://polyhaven.com/license>) states verbatim: *"Our assets are all
licensed as CC0, which is effectively Public Domain even in jurisdictions that do not support the
Public Domain."* … *"You do not need to give credit or attribution when using them (although it is
appreciated)."* … *"You can redistribute them, share them around, include them when sharing your own
work, or even in a product you sell."* That covers 1–4 below.

1. **`wooden_picnic_table` — <https://polyhaven.com/a/wooden_picnic_table>** — CC0, 10 210 tris,
   2 241 × 3 022 × 746 mm, textures 1K/2K/4K, formats incl. FBX and glTF. **This is literally a
   weathered wooden table with attached benches at both ends, as one mesh with one material set.**
   Geometrically it is the request, exactly. The honest trade: it reads *rustic countryside picnic*,
   not *medieval tavern* — attached benches with a cross-brace rather than a trestle table with free
   benches. Recommended as the **starting point**, decimated to ~3–4 k tris through the existing
   `polyhaven_pipeline.py`, then judged on hardware.
2. **`wooden_table_02` — <https://polyhaven.com/a/wooden_table_02>** — CC0, **196 polys**,
   1 134 × 706 × 800 mm, tagged `wooden, worn, village, rural`. Absurdly cheap; the closest Poly
   Haven comes to "tavern".
3. **`dining_table` — <https://polyhaven.com/a/dining_table>** — CC0, **1 048 polys**,
   2 256 × 1 390 × 877 mm — a genuine long table. Ships with a checkered cloth whose geometry would
   need dropping.
4. **`painted_wooden_bench` — <https://polyhaven.com/a/painted_wooden_bench>** — CC0, **630 polys**,
   1 165 × 497 × 889 mm, `farmhouse, rustic, aged`. Has a back and a lower shelf, so it is an end
   piece rather than a plain trestle bench.
5. **Medieval Benches — <https://opengameart.org/content/medieval-benches>** — author AnyRPG,
   licence as stated on the page: **"CC0"**. 1 024 verts / 852 tris. `.blend` only, so it needs a
   Blender export step that the current pipeline does not have.
6. **Quaternius Fantasy Props MegaKit — <https://quaternius.com/packs/fantasypropsmegakit.html>** —
   page states *"Free to use in personal, educational and commercial projects." (CC0 License)*.
   FBX/OBJ/glTF. Listed for completeness only: it is flat-shaded low-poly, which is the look the
   2026-08-13 ruling explicitly rejected (*"aber nicht low-poly"*). **Do not use.**

### The cheapest option, and probably the right first move

**Build the table procedurally and texture it with `dark_wooden_planks`, which is already imported.**
`PlayTray` and the button caps are already built this way (`WorldUI/ButtonCluster.cs:60-62`:
"Visuals: procedural base+cap meshes now; bundle prefabs … are probed first and used when present"),
the repo already ships `dark_wooden_planks_alb.jpg` / `_nrm.jpg` from Poly Haven for the cellar
ceiling, and the `Prop`/`Rest`/`PaintContactAO` helpers give vertex-exact grounding and contact
shading for free. That is **zero new licence work, zero new bundle bytes**, and it makes the table's
dimensions a tunable rather than a download. Ship that in Phase 2; swap in `wooden_picnic_table` in a
later round if the user wants the photoscan.

### Provenance discipline

If any new asset is imported, `unity/GloomhavenVR.Assets/Assets/Bundle/Environments/License.md` (or a
sibling `Bundle/Table/License.md`) is the template, and it is strict: one `###` section per asset
with **Asset** (name, id, direct URL, resolution/format), **License** (quoted verbatim from the
source page, with the page URL and the date it was *re-verified at source immediately before
download*), **Why this one**, **Not used, but evaluated** (rejected candidates and why, "so a future
round need not repeat the search"), and **Modifications** (exact and reproducible, naming the
pipeline script and its dependencies). The raw-sources rule is stated verbatim in that file: *"Raw
source not committed, per this project's standing rule: the zip is downloaded, keyed and discarded;
only `ivy_alb.png` ships."*

Note also the one non-CC0 exception already in the tree (a fire atlas from the Unity Asset Store,
whose section says *"Read this section before adding any further Asset Store material"* and *"The raw
source files may never enter this repository"*). Nothing in this feature should go near that class of
licence.

---

## 9. Risks and open questions, ranked

1. **The per-frame head culling-mask assertion (§4.1).** `VRRigDriver` re-asserts "MENU rig = the
   mod layer ONLY" every frame, and test #10 is the reason. A map-rig flavour that leaks into the
   main menu breaks the menu — the loudest possible regression. *Mitigation:* gate on the positive
   `MapChoreographer`-active signal, never on the absence of a scenario; log the mask every
   transition; make the main-menu path provably unchanged.

2. **Everything on the map except the parchment may be invisible.** The forward/deferred split
   (`FlatScreenStereo.3.Map.cs:270-273`) says only overridden materials draw. The parchment and the
   icons are handled; **the mountains, borders, sea, city buildings, labels and travel-route props
   are not, and I do not know what they are made of.** If a large amount of the map's visual identity
   lives in other deferred renderers, Phase 1 will show a bare parchment and each of those will need
   its own `MapUnlit` override. *This is the risk most likely to turn an M into an L.* Phase 0's
   hierarchy dump is what sizes it. `LogMapSceneRenderers` (`:1125`) already exists for exactly this
   question — it just has to be run in this new context and read.

3. **Only the host can pick (§4.4).** The request reads as if every player interacts with the map.
   Physically they can point and hover, but selection is host-only in the game's own protocol. If
   the user expects client-side picking, that is a *game-rules* change, not a VR change, and it is
   out of scope by the "never patch `ScenarioRuleLibrary`, commit through UI seams only" rule.
   **Flag this to the user before Phase 4.**

4. **The map HUD's canvas topology is unknown (§4.3).** Phase 7 cannot be planned without Phase 0.

5. **The PlaySpace bake gate rejects the obvious layout (§4.2).** `AssertPlaySpaceClear` fails the
   *build* if geometry stands within the clearing, and reserves a 1.70 m no-carpet disc under the
   floating board. §4.2's answer — keep the table out of `RoomGeo` entirely, load it as a standalone
   `Bundle/Table/` prefab and seat it at runtime — resolves it without an exemption and without
   touching a 19 283-line bake file, but it means the *room* and the *table* are placed by two
   different mechanisms and must agree on one floor plane. Get that agreement wrong and the table
   sinks into or hovers above the floor. `Rest`/`PaintContactAO` exist for the baked case only; the
   runtime case needs its own grounding check.

6. **Mixed reality is a new ruling, not an extension (§4.2b).** MR destroys the environment
   outright and the project's MR ruling is "no geometry over passthrough". The user has asked for a
   floating table there. **Put this to him explicitly before building the MR branch** — it is a
   deliberate reversal of a standing art decision, and only he can make it.

7. **Every client's menu rig sits at the same world point (§4.5).** Avatars will pile up unless the
   map room assigns seats. Fixed bench seats by stable player index solve it with zero wire bytes,
   but the seat assignment must be deterministic across clients or two players will each think they
   have the same bench. *This conclusion is inferred from `BuildMenuRig`, not measured — nothing in
   the repo measures it.* Phase 0 should measure it.

8. **Scale.** The parchment is a large world-space mesh with 4×4096² textures viewed today from a
   camera at 50–105° FOV. What `WorldScale` makes it read as a table, and whether 4096² textures at
   reading distance on a Quest 3 are legible or a memory problem, are both empirical. The mip bake
   machinery exists (`WorldUI/PanelMipBake.cs`, `Cards/CardFaceMipBake.cs`) if they need help.

9. **The environment anchor refactor (§4.2) touches a file with an unusually load-bearing class
   doc.** `SkyAlternative.cs` documents a ruling history (ModBuild 128/130/131) in prose. The
   refactor must keep the scenario branch behaviourally identical and must extend, not rewrite, that
   doc. The refactor guard baseline is the objective check.

10. **A stale control-board pose could leak onto the map (§3.2).** Low severity, cheap to check,
    embarrassing if it ships: a floating control board next to the map table in multiplayer.

11. **The wind/cloud particles.** `TuneMapWindParticles` (`FlatScreenStereo.3.Map.cs:1191`) dims
    them for the photographed render and restores them on exit. Standing over the map in stereo,
    drifting cloud sprites at table height may read very differently. Expect a tuning round; the dial
    (`[WorldUI] MapWindOpacity`) already exists.

---

## 10. What I would NOT do

- **I would not re-parent or transform the game's map geometry** (§1). The absolute-world-position
  writes make it a permanent maintenance liability for zero gain over scaling the rig.
- **I would not build a mod-side copy of the map** — a second source of truth for quest state,
  visibility spheres and travel, forbidden by the project's own invariants and guaranteed to drift.
- **I would not add a wire record for panel positions, map selection, or hover state.** The first is
  explicitly excluded by the user, the second is already synced by the game, the third is local by
  nature.
- **I would not make the info placards re-position with the head.** Only the facing normal reads the
  head (§3.5). A placard that slides as you turn would violate the standing ruling and, worse, would
  destroy the one thing this feature is for: that the map is a *place*.
- **I would not add settings beyond the single on/off switch.** The rule is that settings may only
  configure optional content or comfort; the 3D map is optional content, so it gets exactly one
  toggle. Geometry tuning belongs in the existing `[RoundButtons]`-style tuning families, not in new
  user-facing options.
- **I would not touch `MapLocationSelector` by any means other than a mode-scoped prefix.** Deleting
  or permanently disabling a game component that drives the `UINavigation` state machine would strand
  the flat game for a player who toggles back.
- **I would not ship Phases 1–8 as one branch.** Each one has a hardware verdict of its own, and
  Phase 1's verdict can still change the plan.

---

## 11. Three questions for the user, before Phase 1

1. **Mixed reality.** The project's standing art ruling is "no geometry over passthrough", and MR
   currently *destroys* the environment outright (`Core/MixedReality.cs:681-686`). You have asked for
   the table to float centred there. That is a deliberate reversal of that ruling — do you want it?
   (`Default` and `OffBlack` need no ruling; those branches can be built either way.)

2. **Who may pick a location.** The game's own protocol lets **only the host** select and confirm a
   map location (`decompiled/GH.Runtime/UIMapMultiplayerController.cs:183-186, 259-266`); clients
   ready up instead. In the 3D room every player can *point* at an icon, but only the host's press
   does anything. Is that acceptable, or did you expect all players to be able to pick? (If the
   latter, that is a game-rules change and out of scope under "never patch `ScenarioRuleLibrary`".)

3. **The table's look.** The cheapest and most style-consistent option is a procedurally built plank
   table + benches textured with the CC0 `dark_wooden_planks` set already in the bundle. The
   alternative is Poly Haven's `wooden_picnic_table` — geometrically exactly what you asked for
   (table with attached benches at both ends) but reading *rustic picnic* rather than *medieval
   tavern*. Which first?
