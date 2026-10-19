# Larger RGB/coverage carrier without PC Steam changes

The maintainer proposes preserving the selected scene resolution while making
the transmitted Steam Link image larger to carry coverage beside that scene.
The Frame would extract scene and coverage and retain the original apparent
view. This is a valid spatial-budget strategy if the actual encoded carrier can
grow. It is distinct from supersampling the scene into an unchanged video.

## Necessary budget and existing hardware evidence

The frozen v3 logical canvas is3168-square with a2048-square scene. Restoring
the same uniform scene raster therefore requires99/64=1.546875 times each
carrier axis, or2.392822265625 times its area. A4040-square scene would need an
ideal6249.375-square carrier, rounded upward to6272 at64-pixel alignment.
This is an allocation calculation, not a measured2.39-fold bitrate increase
or equivalence to MR-Off's different foveated sample distribution.

The actual Build703 PC targets increase4040/5050/6060. Matching retained Frame
logs continue to report3264x6400 MR decode allocations despite width requests
4096/5056/6080. The exact active chart versus decoder-padding width is not
established. Off restores1152x4608 in another, foveated layout. Neither the
public setting readback nor the application's larger render target proves a
larger transmitted scene.

## Why the inspected host still reduces the carrier

The supplied2026-10-11 Windows driver is pinned privately at SHA256
`1e846be041039798580a91452371f6288c614fb4c152126b137e2c693fb77eef`.
It is not proved byte-identical to the current user's PC SteamVR2.17.10 driver.
Static claims below apply to that supplied file; the current hardware geometry
is independently consistent with the same limit, not proof of version identity.

Its requested square size passes through an encoder layout fitter:

| Layout | Complete image from inputW/H | Internal complete-image maximum | Maximum square input axis |
| --- | --- | --- | ---: |
|1|2W x2H|6400 x6400|3200|
|2|W x2H|3200 x6400|3200|
| Other |W x4H|3200 x12800|3200|

The larger complete dimensions in another layout are spent on that layout's
own packing. They are not a larger square input plane. The inspected request
path passes the same requested width asW andH, so simply requesting another
aspect ratio in the app does not change this square sizing policy either.
At3200 square input, the present uniform v3 scene has approximately2069
samples per axis before any subsequent filtering; that is a model budget,
not an exact current visible-resolution measurement.

The manual `nonFoveatedStreamFormatWidth` request is already clamped against
the encoder's width bound; the fitter independently enforces its height bound.
The min/max settings constrain automatic/default selection and do not replace
those internal limits. Queried GPU encoder limits can reduce them, not raise
them. A larger acknowledged setting, recommended render extent or application
texture cannot undo that later fit.

The canonical host and receiver research records live in the native repository:

- `docs/research/larger-carrier-host-limits-20261018.md`
- `docs/research/larger-carrier-receiver-negotiation-20261018.md`

## Receiver sizing is downstream of encoded capacity

The independently inspected retained Frame client, collected2026-10-12 with
SHA256 `31addef0c9553d3770b5570487699e96d70271d4073cd6c03b080fdee6ae2153`,
queries `VIDIOC_G_FMT` on the capture-multiplanar queue. Its capture setup copies
the decoder-reported dimensions into its allocation and subsequent format
request. That is received-stream-to-buffer dataflow, not a demonstrated request
for a larger encoder image. This retained client also predates later updates.

No outgoing capability-to-host-maximum override has been established in the
bounded review. Even a larger advertised decoder capability would not by
itself remove the independently selected host fit above. Companion settings
names and larger local GPU/decoder buffers are not evidence of such an override.
This is not an exhaustive proof that every version lacks another negotiation
path, nor a maximum-capability claim about the Frame's hardware.

## Integration decision and evidence boundaries

Do not ship another width multiplier as a picture-quality repair. The relevant
property is the actual selected encoded/decoded carrier, with correct stereo
and reconstruction, rather than an accepted configuration value. Preserving
the ordinary foveated scene distribution is a separate obligation from making
the mask fit. A new carrier and receiver mapping would need to preserve both.

The proposed simple game/receiver-only enlargement has no demonstrated route
through this host. Raising its implementation constants would be a PC sender
change and is outside the user's instruction. A newer official version or a
real supported negotiation branch could reopen this route, but would require
matching source identity and actual delivered dimensions, not another settings
receipt. A more compact in-band coverage representation is a different strategy:
it could use the existing Steam Link channel, but its compression, sharp-edge
fidelity and native mapping are not proved by this size audit. Do not silently
switch to an independent channel or another speculative hardware diagnostic.

No production code, helper binary, shader, runtime setting or game build is
changed by this investigation. Runtime remains707. No Frame connection, file
write, restart or new capture is performed. No whole-game test gate is repeated
for this research-only change. Private source pins and arithmetic receipts live
under `.planning/debug/steamlink-carrier708/`; they establish their stated
static/offline scope, not a new successful headset picture.
