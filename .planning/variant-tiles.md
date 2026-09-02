# The option picture tiles — what they are and how to rebuild them

User request, 2026-09-02, verbatim:

> "Ich möchte nochmal eine kleine Umstrukturierung des Optionmenus. Hierbei will ich, dass das
> umstellen der Assets etwas präsenter wird. Am Besten will ich dass die Umgebung (Wald, Keller,
> Default, Schwarz, Mixed Reality) als Kacheln mit einem Bild darin angeboten werden. Genau für die
> Hände und Masken. Nutze die Renders als inhalt der Kacheln die du schon gemacht hattest für die
> README oder generier dir neue."

Three settings stop being dropdowns and become a strip of picture tiles:

| Setting | Config file | Tiles |
| --- | --- | --- |
| `[Sky] Style` + `[MixedReality] Enabled` | `rig` + `mixedreality` | 5 |
| `[Hands] HandStyle` | main | 3 |
| `[Net] MaskId` | `net` | 3 |

Code: `src/GloomhavenVR/WorldUI/Options/VariantTiles.cs` (the widget) and `VariantTilesTable.cs`
(what each picker offers). Art: `src/GloomhavenVR/WorldUI/Options/VariantTiles/*.png`, eleven
320x240 PNGs, **425,715 bytes** for the set.

## Why the art ships in the DLL and not in the bundle

`gloomhavenvr.bundle` is 74,558,728 bytes and every install since ModBuild 296 has been a
plugin-DLL drop. Putting eleven thumbnails in it would turn this build into a full re-install for
every user, and would rewrite the whole 74.5 MB blob in git. They ship as `<EmbeddedResource>`
instead — the route `Core/EmbeddedTexture.cs` exists for and argues for, already used by the
main-menu wordmark, the four grab-bar strips and the shared-window badge.

**Measured cost: the plugin DLL grows 426,496 bytes** (11,843,072 -> 12,269,568 with the eleven
entries added). The bundle is untouched, byte for byte. No rebake.

## The five environment tiles are two config keys

`SkyStyle` has four members. Mixed reality is a separate dial that OVERRIDES the sky, so the player
experiences five alternatives while the code has 4 + 1. The strip presents the five states and keeps
both keys consistent; see the class doc on `VariantTiles.cs` for the exact rule (short version: a
sky tile clears MR only if it was on, and the MR tile leaves `[Sky] Style` alone so switching MR back
off restores the chosen room).

Two of the five have no room to photograph, and both show **the same play tray at the same size and
pose** so the difference between them is the only thing that changes:

* **Aus (schwarz)** — the tray in pure black. Deliberately *not* an empty black rectangle: that is
  exactly what a tile whose art failed to load looks like, so the one state that really is "no
  surroundings" has to prove it is a picture.
* **Mixed Reality** — the same tray over an open, daylight-neutral field with a soft contact shadow.
  There is nothing to photograph (the surroundings are the player's own room, delivered by the
  compositor's chroma key), and a stock living room would be a picture of somebody else's flat. The
  contrast with the black tile beside it is the message.

**Standard** is a crop of a real in-game frame of `docs/img/control-board.mp4`, framed to the
scenario diorama and the game's own dark forest behind it, with every piece of mod UI cropped out.

Nothing is brightened. Both mod rooms are night scenes and that IS the product — the same ruling
`docs/img/README.md` records for the README env heroes. Legibility at tile size is bought with the
CROP, never with a curve that advertises a room the player never gets.

## Regenerating the art

Blender renders the hands, the masks and the play tray from the shipped bundle assets with the exact
flags `unity/asset-preview/build_asset_strips.sh` uses for the README strips — same shader
arithmetic, same side, same cull/unlit decisions. Only `--res` differs (512, not 900).

```bash
B=unity/GloomhavenVR.Assets/Assets/Bundle
BL=/home/claw/blender-4.2/blender
r() { "$BL" --background --python unity/asset-preview/render_asset.py -- "$@" >/dev/null; }

# hands — back of the hand (--yaw 215), --cull because the materials are _Cull: 2 (Back)
r $B/Hands/VRHand_L_rig.fbx       $B/Hands/VRHand_albedo.png       raw/hand_0_glove.png \
  --normal $B/Hands/VRHand_normal.png       --cull --res 512 --yaw 215
r $B/Hands/VRHandPlate_L_rig.fbx  $B/Hands/VRHandPlate_albedo.png  raw/hand_1_plate.png \
  --normal $B/Hands/VRHandPlate_normal.png --mrs $B/Hands/VRHandPlate_mrs.png --cull --res 512 --yaw 215
r $B/Hands/VRHandArcane_L_rig.fbx $B/Hands/VRHandArcane_albedo.png raw/hand_2_arcane.png \
  --normal $B/Hands/VRHandArcane_normal.png --cull --res 512 --yaw 215

# masks — --unlit (GloomhavenVR/HeadUnlit, the texture is pre-lit), no --cull (_Cull: 0)
r $B/Head/Mask_0.fbx $B/Head/Mask_0_albedo.png raw/mask_0.png --unlit --res 512
r $B/Head/Mask_1.fbx $B/Head/Mask_1_albedo.png raw/mask_1.png --unlit --res 512
r $B/Head/Mask_2.fbx $B/Head/Mask_2_albedo.png raw/mask_2.png --unlit --res 512

# the play tray, for the two environment tiles with no room to photograph
r $B/Table/PlayTray_prepped.fbx $B/Table/PlayTray_albedo.png raw/board_oak.png \
  --normal $B/Table/PlayTray_normal.png --res 512 --yaw 35 --pitch 50 --scale 0.78

# the Standard tile's source frame
ffmpeg -v error -ss 0.2 -i docs/img/control-board.mp4 -frames:v 1 frames/std_cb.png
```

The composition step (crop, scale, backdrop, contact shadow) is a short PIL script; its decisions are
all written down in the comments of `VariantTiles.cs` and above. Re-deriving it needs only these
rules:

* every tile is **320x240**, opaque;
* hands and masks are scaled by ONE shared 512 -> 230 factor per family and centred on the strips'
  own near-black (a vertical 34,32,30 -> 16,15,14 lift). They are **not** re-fitted to each
  subject's alpha box — `render_asset.py` already auto-fitted each asset inside its own frame, and
  re-fitting here would normalise away the difference between a cuffed gauntlet and a glove that
  stops at the wrist;
* Keller and Nachtwald are 4:3 crops of `docs/img/env-cellar.jpg` and `docs/img/env-forest.jpg`,
  centred on the lit part of the room (0.50/0.56 at 0.92 height, and 0.50/0.46 at 0.94);
* Standard is `crop43(std_cb.png, cx=262, cy=312, h=364)` — the window that excludes the control
  board (x >= 760), the tooltip (x >= 573) and the card fan (x >= 511, y >= 441).

## Legibility

The tile's layout rect is 232 x 204 px, of which 222 x 172 is the picture and 27 px the label; the
strip shrinks tiles toward a 116 px floor when the pane is narrower than five of them. The angular
size that works out to in the headset **has not been measured** — it depends on the options panel's
world size and the player's distance, neither of which is verifiable without hardware. What can be
said without a headset: the label is a TMP with auto-sizing between 10 and 16 pt and never
ellipsises (standing ruling for this pane), and the selected tile is marked three redundant ways —
a 5 px gold border, a full-brightness picture against dimmed neighbours, and a bold gold label — so
no single cue has to survive on its own.

## Integration — the two edits outside this lane's paths

The widget is committed but **not yet reachable**: the hook that calls it and the `<EmbeddedResource>`
entries that carry its art both live in files this lane does not own. Until both are applied the
three settings keep their dropdowns and the tile code is inert.

### 1. `src/GloomhavenVR/WorldUI/Options/VROptionsTab.2.Rows.cs` — the one-line hook

In `BuildRow` (around line 856), immediately **before** the existing `TryBuildSpecialRow` call:

```csharp
        // …and three that are not edited with a CONTROL at all: the environment, the hand style and
        // the head mask are picked from a strip of pictures. See WorldUI/Options/VariantTiles.cs.
        if (component == 0 && TryBuildVariantTiles(parent, item, caption, hintKey))
            return;

        // A few entries' stored type says nothing useful about how they should be edited.
        if (component == 0 && TryBuildSpecialRow(parent, item, caption, hintKey))
            return;
```

It has to run *before* `TryBuildSpecialRow`, because `[Sky] Style` and `[Net] MaskId` are already in
`HasSpecialRow` and would otherwise get their dropdowns. Those definitions are deliberately left in
place: they are the working fallback if this hook is ever removed.

`BuildRow` is the single funnel for every page (curated, topic, board tree, topic tree), so this one
line covers all of them. `[Hands] HandStyle` and the other two are all `Components == 1`, so the
`component == 0` guard cannot build a strip twice.

### 2. `src/GloomhavenVR/GloomhavenVR.csproj` — the eleven resources

Insert immediately after the existing `net_shared.png` entry, inside the same `<ItemGroup>`:

```xml
    <!--
      THE ELEVEN OPTION PICTURE TILES (user request 2026-09-02: the environment, hand and mask
      pickers become tiles with a picture in them, not dropdowns). 320x240 each, 425,715 bytes for
      the set, and they ship the same way the wordmark and the grab-bar strips do and for the same
      reason: gloomhavenvr.bundle is 74,558,728 bytes, so eleven thumbnails in the bundle would
      cost every user a FULL RE-INSTALL where a DLL drop does. See Core/EmbeddedTexture.cs.

      They are BUILT, not hand-painted. The hands, the masks and the play tray come out of
      unity/asset-preview/render_asset.py with the exact flags build_asset_strips.sh uses for the
      README strips (same shader arithmetic, same side, same cull/unlit decisions); the two room
      tiles are crops of the committed docs/img/env-*.jpg heroes and the Default tile is a crop of a
      frame of docs/img/control-board.mp4. .planning/variant-tiles.md carries the commands.

      LogicalName is pinned on every one, so these files may be moved (e.g. to src/GloomhavenVR/
      Assets/ with the rest of the embedded art) by changing only the Include path — the manifest
      names WorldUI/Options/VariantTilesTable.cs asks for do not move with them.
    -->
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_env_default.png"
                      LogicalName="GloomhavenVR.Assets.tile_env_default.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_env_cellar.png"
                      LogicalName="GloomhavenVR.Assets.tile_env_cellar.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_env_swamp.png"
                      LogicalName="GloomhavenVR.Assets.tile_env_swamp.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_env_offblack.png"
                      LogicalName="GloomhavenVR.Assets.tile_env_offblack.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_env_mr.png"
                      LogicalName="GloomhavenVR.Assets.tile_env_mr.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_hand_glove.png"
                      LogicalName="GloomhavenVR.Assets.tile_hand_glove.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_hand_plate.png"
                      LogicalName="GloomhavenVR.Assets.tile_hand_plate.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_hand_arcane.png"
                      LogicalName="GloomhavenVR.Assets.tile_hand_arcane.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_mask_0.png"
                      LogicalName="GloomhavenVR.Assets.tile_mask_0.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_mask_1.png"
                      LogicalName="GloomhavenVR.Assets.tile_mask_1.png" />
    <EmbeddedResource Include="WorldUI\Options\VariantTiles\tile_mask_2.png"
                      LogicalName="GloomhavenVR.Assets.tile_mask_2.png" />
```

Both edits were applied together on this lane's tree and verified: `build.sh` and
`EXPECT_WARNINGS=0 ci-build.sh` came back **0 errors, 0 warnings**, all eleven manifest names were
present in the built DLL, and the DLL grew from 11,843,072 to 12,269,568 bytes. They were then
reverted, because neither file belongs to this lane.

**No bundle rebake is needed.** `prebuilt/gloomhavenvr.bundle` is untouched at 74,558,728 bytes and
`check-bundle-format.sh` passes against it unchanged. This build stays a plugin-DLL drop.
