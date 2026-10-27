# Build726 common character units, rear views and travel sound

## Hardware review and inputs

The maintainer accepts the direction of Build725 but requests consistent character
alignment and a common scale, with intrinsically shorter bodies remaining shorter.
Some previews were smaller/right-shifted while others were larger/centered. The
back of a physical window must show the actual character's back with tracked 3D
perspective. Independently, road-event continuation sometimes produced overlapping
travel sound until arrival. The delivery remains the normal Debug installer ZIP.

Both fresh local logs identify725 /6ee56ff48, built2026-10-27 12:30:47 UTC. This is
the same source commit as the earlier delivered725, with a different compile time.
Remote717 logs are stale. No new screenshot or audio recording was supplied. Inputs
are byte-pinned under ignored debug/menu726/hardware725, not modified in place.
The new log records sparse New Party display fallbacks naming the rejected
front-facing portal at lines5777 onward. This directly supports the rear-eye
admission defect; log rows alone do not establish the count of playing AudioObjects.

## Source causes and implementation boundaries

Build725 fits each completed actor/equipment/companion envelope independently to
82% width/75% height, places its bounds center on the aperture and compresses depth
to a per-envelope limit. A weapon or companion therefore changes the main actor's
scale/center, while short/tall bodies lose their intrinsic size relationship.
Use a single native-unit metric and the authored main actor's ground/axis instead.
Native model/camera/holder transforms, original animation, skins, companions and
authored child scale remain original. No first head pose or animation phase may
establish the reference frame, and no recurring renderer census may return.

The native audit covers34 forms,21 configs and12 exact native camera-to-RT-to-image
apertures. Manager roots have zero local position/unit scale; native child scale
still varies intentionally. Imported skinned bounds and equipment extrema are not
ground corrections or per-body fit rules. Actual mesh coordinates, authored TRS,
GUID/path identities and source hashes remain reproducible read-only evidence.

Rear cabinet eyes need a reflected off-axis view through the same fixed pane UVs,
with finally-restored culling state. Native-screen eye mapping must also admit
the rear half-space. Preserve the725 stable direct-target priming, sample budget,
hysteresis, capture ownership and all original native effects/restoration. The
accepted story/video resources and native input authority remain unchanged.

Native MapTimedMovementFlow starts StartMoving followed by a scheduled Moving item
on the encounter leg. Its continuation starts Moving again while the first loop
remains alive. MapPointsMovementFlow has the same producer asymmetry. The serialized
moving item is a loop with unlimited instances and no stop events; the encounter
item has no stop event either. The repair owns concrete native AudioObjects for
the exact flow's configured moving item, not all sounds sharing an ID. Before the
next native leg it retires that previous request and lets every original statement,
callback and new native layer execute. Pending starts and pool reuse must be
qualified against the actual ClockStone lifecycle. Flat calls stay native.

## Integration and verification plan

Three disjoint worktrees start at currentdev6ee56ff48: character math/native-scene
projection, native travel-audio ownership, and a read-only native authoring audit.
The primary owns registration, ModBuild726, manifests/inventories, final review,
dev integration/push and normal archive delivery. Focused affected checks and full
golden/source/build checks will reuse the passing725 evidence for unchanged areas;
they must not be described as a new complete local-suite invocation. Source/GPU/PCM
proof and new headset/audible outcomes remain separate. Final results follow the
integrated checkpoints.
