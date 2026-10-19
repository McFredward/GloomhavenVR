# Revised cinematic courtyard

The maintainer rejected the first trailer study's visible board cues, disconnected
wall panels, static bodies and aimless player gestures. Revision 02 opens inside
an inhabited film world: the original Brute has a living native pose, joined
ancient masonry surrounds a walkable arch and passage, warm torch pools contrast
with cold roof light, and subtle dust reveals depth. The original Grimhorn then
appears above the open courtyard with its gaze directed toward the character.

This is cinematic staging, not gameplay footage. Architecture and lighting may
serve the film, as explicitly requested by the maintainer; source character mesh,
UV, textures, native animation, weapons, masks and hand rigs remain original.

## Current review boundary

Produce one corrected short review showing film-world discovery, purposeful
intervention and the second player's attentive gaze. Share that video and pause
all further trailer production until the maintainer approves its direction. The
full table/gameplay transition belongs to the later approved work. No paid API
calls are made by this world-builder.

All outputs remain in the main checkout's gitignored directory:

```text
.planning/debug/trailer/revision-02/world/
```

The final source handoff for the motion driver is:

```text
approved-reference/god_presence_base.blend
approved-reference/film-world-manifest-opening-god_presence-companion_lift.json
approved-reference/render-film-world-source-901913bed363.py
```

The packed base contains the exact native idle frame 0, original Grimhorn,
continuous courtyard, source-textured props, torches, dust and cloudy night sky.
It has no player hand rigs, Runeveil or previously applied animation keys. Load
this base and apply the reviewed player-motion driver once. Other reference
Blender files already include motion rigs and are unsuitable as clean inputs.

## Rendering a source reference

Use Blender 4.2 and an initialized current-development worktree. A 960-pixel,
32-sample render is sufficient for composition review; final references were
rendered at 1920 by 1080 with 96 Eevee samples.

```bash
xvfb-run -a /home/claw/blender-4.2/blender -b --python-exit-code 1 \
  --python scripts/trailer/render_film_world.py -- \
  --output /home/claw/gloomhaven_vr/.planning/debug/trailer/revision-02/world/source \
  --shots opening,god_presence --width 1920 --samples 96 --save-base --save-blend
```

`--pose melee --pose-frame 6` supplies the approved active still composition.
The God scene always uses original idle frame 0, which supports the independently
checked native-motion driver. Optional `--player-motion-helper <path>` supplies
that reviewed driver's source-rig poses for reference images; `companion_lift`
requires it. The driver determines the final review camera and shot timing.

The agreed close endpoint is camera `(-2.394, -18.370, 8.099)`, target
`(-0.089, 0.789, 4.039)`, lens `27.018 mm`, with at most a small further dolly.
The rejected distant overhead endpoint must not be used for the next review.

## Source geometry and scene contracts

Native motion archives provide 12,034 original vertices, original triangles,
material indices and UVs. The builder creates its mesh directly from these arrays
at source scale, retaining the exact order required by native-motion validation.
The original weapon attachments remain part of that exported motion. Original
Brute material slots retain separate diffuse, normal, packed PBR and fur-alpha
textures; cinematic roughness remapping bounds sharp specular highlights.

The courtyard walls join at their corners and surround the complete archway.
Side walls extend to `y = -12.8`; the continuous floor ends at `y = -13` behind
the initial camera. The floor footprint is **13.1 by 25 units**, closely enclosing
the courtyard. It does not leave an oversized dark rectangle outside the walls.
The restrained wooden support measures **21 by 32 units** and remains outside
early framing. There is no visible initial hex grid, tabletop, stage edge or
black void. The render coordinate gauge is consistent through the discovery.

Grimhorn is at `(-1.1, 2.2, 6.9)`, source scale 26, with anatomical face axis
`-Y` directed toward Brute. Projection checks widen reference framing enough to
retain the complete original horn silhouette. The source-rig motion lane owns
loose reach, overlapping wrist/finger closure, contact, lift and meaningful
Runeveil attention. The rejected pointing/palm-claw placeholder and distant
static companion alternative have been removed from the generator.

## Evidence and limits

The source handoff includes packed textures, captured executed Python source,
SHA-256 input/output receipts, actual native pose metadata and reference motion
traces. Local Python compilation and actual Blender renders pass. Visual checks
cover original character identity, source material/UV assignments, continuous
world geometry, light motivation, night air and safe mask framing. The motion
lane checks original idle topology and contact/gaze traces separately.

These checks establish cinematic artifact provenance, not headset output or a
new runtime gate. Source inputs, logs, manifests and current references remain
preserved; only owned Python caches and obsolete Blender backups are removed
after render processes exit.
