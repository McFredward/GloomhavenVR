# Equal-resolution Steam Link scene coverage

## Maintainer request and scope

After successful native Frame/Steam Link MR, the maintainer still reports soft
window edges. Build694's C1 packing kernel only narrowed the measured edge by
5.52%; that was never a complete fix. The 2026-10-18 clarification rejects a
window/object classification channel and authorizes first trying a coverage mask
with the same spatial resolution as the scene image. There is no new window
metadata, semantic opacity threshold, extra scene camera or mask render target.

The actual old packer downsamples alpha2048->1024 with a four-sample box, then
reconstructs fractional mask cells with C1 weights. Video/native filtering adds
further sampling. Even a fully opaque source quad has a measurable partially
covered outer band after this packing. Actual source tests also reproduce alpha
loss from a surrogate UI blend, but official UI/Default already preserves alpha;
this does not establish that native materials caused the all-window report.
The experimental window-only late stamp was therefore not integrated.

## General v2 format

Scene RGB and independent coverage both have a2048x2048 logical grid. Pack both
from the same current source RGBA sample, with no special four-sample mask box or
secondary C1 reconstruction. Keep genuine partial opacity, smooth gradients and
additive RGB at zero coverage. Numeric mask data stays neutral grey, because the
receiver consumes raw component G from its actual YCbCr-conversion path; RGB
auxiliary tags would not preserve that contract.

The3168-square virtual canvas has RGB at(560,560), four2048x512 coverage strips,
and16px global-continuation halos. The strips form a windmill around RGB:

| Strip | Data origin | Quarter turns | Source rows |
| --- | --- | --- | --- |
| Top |576,32 |0 |0..511 |
| Right |2624,576 |1 |512..1023 |
| Bottom |544,2624 |0 |1024..1535 |
| Left |32,544 |1 |1536..2047 |

For a rotated strip, physical local(x,y) maps to source(2048-y,512*t+x)
in edge coordinates; inverse center indexing uses2047-sourceX. No halo overlaps
another strip or RGB. Twelve32px grey signature cells lie diagonally in the unused
corner at(64,64), stride(36,36). The frozen profile is
`tests/steamlink-scene-payload/profile-v2.json`, SHA256
`9ea2c9cd304a296d088659e3bc8e9a316365c8b10e3dfddb459bad08ff11ce9f`.
The guard recognizes layout only, never authenticates body, source or freshness.

Legacy v1/profile/shader remain frozen for compatibility and comparison. The
public receiver update must recognize v2 before the new PC producer is tested;
it also retains v1 support. The PC mod never installs or changes Frame files.
No Steam sender modification is introduced.

## Cost and verification boundaries

At an unchanged physical carrier resolution, transport pixel count and render
source resolution remain unchanged. The larger relative mask uses more of the
carrier, so RGB occupies less of it. The square arrangement gives both planes
identical spatial density: at3408-square output each2048-square logical plane
occupies about2203-square video pixels. Legacy RGB occupied about3304x2536 and
its half-size mask about1652x1268. No automatic increase in the user's SteamVR
resolution or stream bitrate is made. Codec bitrate/visible quality and motion
comfort must be checked on the headset; a desktop GPU raster does not establish
those outcomes. Normal source MSAA and genuine transparency are retained; this
is not a promise of zero antialiasing at every geometric edge.

The new owned bank is built with actual Unity2021.3.5f1 for D3D11/GLCore/Vulkan.
No native-material derivative, opaque-depth prefix, shader blend, gameplay,
multiplayer state, source identity or loading-continuity policy is changed.
