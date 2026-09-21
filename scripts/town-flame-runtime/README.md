# Remote native flame playback check

Run `python3 scripts/check-town-flame.py`.

This compiles the production asset registry, material pool, binding, retained frame
representation and flame clock into genuine Unity 2021.3.5. Only the receive-time
clock is injected. Rendering uses the actual `TownFlame.shader` with a diagnostic
eight-frame colour atlas so skipped or incorrect intermediate frames are measurable.
It does not claim that the diagnostic atlas is original game artwork or verify a
headset's stereo appearance.

The check covers independent per-renderer clock epochs sharing one immutable
material, all other material fields, 1,000 clock updates without material replacement,
10,000 steady ticks without managed allocation, coalesced delayed samples, old samples,
session reset, shader replacement and property-block restoration. Intermediate owner
and observer pixels must match exactly. Six compiled negative controls break the
relevant production paths and must fail their named assertions.

The clock adapter is restricted to the owned `GloomhavenVR/TownFlame` shader's verified
rate-one `_TownAnimationTime`; it does not predict arbitrary native shader properties.
The existing multiplayer frame/session guards continue to reject stale sessions before
binding. Indexed property blocks keep clock output out of pooled immutable materials.
