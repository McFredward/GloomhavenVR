# Steam Link observer retirement and legacy menu containment — Build709

## Scope and captured evidence

The complete paired input is retained under
`.planning/debug/steamlink-live-20261018-220355/`, including immutable PC-input
copies and Frame read-only SSH envelopes. The PC identifies Build708 at8f25f7925.
No device files, settings or services were changed during this investigation.

The current sender run starts at00:02:01, not the older23:39 run in the same
vrserver log. Real peer negotiation and the coded-AU roundtrip succeed at
00:02:03.334/351. The receiver's local UNIX channel is connected. However, every
coverage sample still has zero D3D draws/maps/copies despite one native device
and context. Thus there is no independent mask and no ordinary-RGBA grant;
legacy atlas is genuinely still selected. This is separate from the now-fixed
orphan-socket failure. A subsequent source/control loss at00:02:50 is not enough
to assign which endpoint failed; native cold diagnostics now distinguish it.

## Game observer retirement

The supplied game log also reports a SafeHandle/AggregateException escaping
CancellationTokenSource.Cancel in the XR tick. Cancellation executes registered
callbacks synchronously; the supplied Mono implementation was inspected read-only.
The new monitor revokes source authority immediately but cancels/disposes off the
XR stack. Its owned lifetime includes completion of cancellation callbacks, so no
replacement can race them or revive an obsolete snapshot. Existing wire/path
validation and the three-attempt/two-second policy remain unchanged.

Actual production lifecycle37 checks and the isolated retirement7 checks pass;
causal stale-epoch, synchronous-cancellation and premature-retirement controls
fail their intended assertions. The retirement test itself needed a bounded
continuation observation rather than an immediate IsCompleted sample; that
fixture gap and the failed initial control are recorded honestly in
`monitor-review/REVIEW.md`. Actual Unity/net472 typechecking passes0W/0E.

## Steam menu containment

Secured PC evidence ties the full Steam dashboard opening at00:03:03.834 to
OpenXR Focused->Visible at03.866. Legacy atlas depends on a transported image
header which the existing native dashboard path damages. It now requires
explicit current XR input focus at admission and both render-time seams. Lost
or unknown focus temporarily restores ordinary presentation, without selecting
chroma key, revoking the user's setting or retiring coverage/quality observation.
A regained focus needs a new complete stereo epoch. A late second-eye callback
cannot revive a previously completed pair.

The independent ordinary-RGBA path explicitly ignores this legacy focus gate.
It carries original colour and certified coverage separately; the existing exact
native video-image/descriptor/lease join must never apply a game mask to unknown
menu/intermediate images. Original UI draws remain untouched. This removes the
known atlas-header dependency but is not yet a headset demonstration of combined
game+menu mixed reality. Full/quick-access menu variants remain hardware checks;
only the actual full-dashboard focus correlation is established by current logs.

The production startup/callback port passes194 checks, including56 focused menu
checks and four semantic controls. Actual Unity Tick/camera/pixel63 and projection
callback44 checks pass; real Unity/net472 typing is0W/0E. A previously stale
frame-MR fixture boundary was repaired to current production signatures, rather
than treating compile failure as a new product defect. All own fixture children
were reaped and completion cleanup removed their regenerable caches.

## Integration evidence and limits

The complete Release solution compiles with zero warnings/errors. The actual
source census versus8f25f7925 preserves all669 config keys,241 Harmony bindings
and4806 log tokens. No multiplayer record, payload format, RGB/mask geometry,
resolution, foveation or standalone path changes. Already-passing unchanged
checks are inherited; this is not a repeated complete local gate. Raw evidence,
source snapshots and current binaries are under
`.planning/debug/steamlink-sender721/{monitor-review,menu-review,game}/`.

The native sender dispatch repair and its real Windows rendering tests are a
separate delivery requirement. Do not infer sharpness or a correct headset/menu
picture from the game lifecycle tests or a successful paired handshake.
