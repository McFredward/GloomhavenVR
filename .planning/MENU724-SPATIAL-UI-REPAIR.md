# Build724 spatial UI hardware repair

## Hardware report and scope

On 2026-10-27 the maintainer reported two failures in Build723: a door
illustration remained flat, and live character previews translated with the
viewer's lateral head movement. The requested character presentation is a fixed
scene inside the window, viewed through glass: head movement changes perspective,
never the model's pose relative to the window.

Both supplied local logs identify ModBuild723, commit `5ddc94991`, built
2026-10-27 07:36:04 UTC. The remote log still identifies Build717 and cannot
certify this round. No new screenshot was supplied. Byte-pinned local inputs and
the exact Build723 source/assemblies are retained under ignored
`debug/menu-depth724/`.

The logs show four character source registrations, the complete 448-identity /
515-field catalogue loading, and bounded `MENU DEPTH DIRECT CAPTURE`
InvalidOperationException reports. They do not identify the pictured door by
themselves or demonstrate visible relief. A later loadout illustration is logged
as `CR_DF_03_Demon (VR-mip)`.

## Proven source gaps

- The original serialized GUI3D camera owns the character holder as its child.
  Build723 moves that camera transform while rendering each tracked eye, which
  also moves its native model and light hierarchy. The earlier character fixture
  used a separate holder root and therefore missed the real hierarchy.
- Illustration matching compares the displayed sprite name literally with the
  original asset name. The existing mip baker creates named replacements and
  maintains a read-only reverse map to the originals. Those replacements fail
  Build723's identity match. Event default comparisons and async old/new sprite
  retention need the same original identity. Reconstructed trimmed sprites also
  need their presentation UVs mapped back to the original texture coordinates.
- Direct-capture failures currently log the exception type without the message
  or source, preventing a reliable attribution from the supplied log alone.

## Repair and validation

Implementation, affected checks, delivery identity and remaining hardware limits
will be recorded here after integration. Existing Build723 evidence remains valid
only for unchanged areas; its prior green source/GPU/CI results did not establish
the requested headset behavior.
