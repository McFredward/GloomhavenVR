# Shared map-header pose restoration — build 525

## Hardware evidence

The new local logs identify build 524; remote logs remain historical build 500. The prior
`händler_mixed_reality.jpg` shows the reported excess background above the normal shop artwork.
The diagnostic run now names its source directly: `Icon` under `UI Adventure Header`, the
same native Image instance 314858 on every opening, including the temple.

| Local LogOutput.log line | Window/opening | Actual top contributor | Top y, authored pixels |
| --- | --- | --- | ---: |
| 150 | Merchant first | Root Black_Backdrop | 540 |
| 182 | Merchant second | Header Merchant_Icon | 1369.812 |
| 210 | Merchant third | Same header icon | 1938.034 |
| 234 | Merchant fourth | Same header icon | 2317.974 |
| 262 | Merchant fifth | Same header icon | 2572.018 |
| 300 | Merchant sixth | Same header icon | 2741.883 |
| 319 | Temple | Same header, Temple_Icon sprite | 2855.462 |

The icon's own local rectangle remains 74x74 while its host-space height shrinks from
49.48 to 6.61 pixels. The actual MR plate follows the erroneous source, reaching 3411px tall
at temple. This is a reused native transform accumulating conversion scale/position, not
empty text height, a stale MR rectangle or a tooltip. Earlier bounds-only remedies could
not fix the source transform.

## Source cause and change

`GuildmasterDestinations.ReconcileBanner` borrows the game's original shared header into
the floated destination with `SetParent(false)`. Previously it stored only the old parent
and sibling. Native `UIGuildmasterHUD.OnReturnToMap`, `ResetBannerParent`, and temple entry
use `SetParent(true)`, keeping world geometry from the converted VR window. Release then
refused restoration because the native game had already changed the parent. The next borrow
inherited the contaminated local pose, compounding the drift and shrinking.

`GuildmasterBannerBorrow` owns the exact borrowed object and its original root-local
position/rotation/scale and RectTransform anchors/pivot/size/anchored position. Release
always restores this geometry. It changes parent/sibling only while the original is still
under its borrowed host; a native-chosen parent/sibling stays intact. Reacquisition repairs
the previous borrow before capturing another snapshot. A missing home detaches the original
from the disposable host, and repeated release cannot replay stale geometry. Missing-banner
discovery remains bounded rather than adding a scene scan per frame.

Only the original header root is restored. Native child configuration, labels, sprites,
visibility and callbacks remain game-owned. In particular, temple's configured child header
height is preserved. No changes to MR bounds, capture cropping, native continuation, window
animation or multiplayer payloads. Each client uses the same corrected native presentation.

## Validation and hardware limits

The focused harness links the production borrow helper and models rotated/scaled parent
transforms and native world-preserving reparenting. It covers twelve alternating destination
openings, direct handoff, root layout restoration, child configuration, replacement, destroyed
objects, null homes and repeated release. Negative controls reproduce the skipped native-handoff
repair, lost scale restoration, stolen parent ownership and stale release snapshot.

Integrated validation passes: 180 runtime assertions, three integration bindings, four runtime
mutations and one binding negative. Strict Release has zero warnings/errors. Eleven frame-order
locks, bilingual documentation checks, Actionlint, shell syntax and whitespace pass. Config/patch/
log surfaces remain 625 / 172 / 4,733. The focused harness is included in dev CI.
No unrelated full local suite was repeated.

On headset: reopen merchant repeatedly, then open/reopen temple and another destination.
The header should keep its initial size/position and the MR background should retain only
its small margin. Include closing/reopening quickly and a second build-525 peer for remote
confirmation. The automated transform fixture does not prove the final headset picture.
