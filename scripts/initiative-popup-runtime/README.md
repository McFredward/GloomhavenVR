# Initiative enemy popup proof

`check-initiative-popup-runtime.py` executes the complete production popup placement
and material-treatment helpers in Unity 2021.3.5 with real uGUI/TMP and final camera
pixels. Local and observer call sites bind to the same explicit world-depth mode
and portrait-relative seat. The fixture checks three board rotations/scales,
portrait reorder, non-central pivots, own-rim clearance, occlusion by a nearer
board, idempotent placement and exact native pose/material restoration. Both opaque and depthless board
materials participate; every font glyph must respect the same occlusion.

The original game's exported font/atlas/material and canonical TMP shader source
are pinned. Board geometry, popup rectangles, surrounding component type identities
and logging are declared ports. This is not a paired headset or full native
initiative-controller replay. Six compiling controls restore Always depth, the
shared holder's horizontal origin, stale board draw band, Always-only font depth,
missing rim clearance or missing pose restoration;
each must fail its named geometry/pixel assertion. Every run retains snapshots,
executed assemblies, images, logs and a canonical cache-cleanup receipt. A run
without an inherited task-private ADB socket creates and retires its own socket.
