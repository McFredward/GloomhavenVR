# Synthetic Steam Link presenter source v2

This developer-only profile prepares the next receiver output experiment. It does
not enable native passthrough, produce real game coverage, modify the Steam
sender, or prove a source video-frame/physical-eye association.

`PresenterSource.Stamp` wraps the unchanged `RegistrationSource.Stamp`. It keeps
both logical planes and all three full CRC headers byte-identical, paints only
twelve neutral-gray 32x32 footer rectangles, sets the returned source profile to
`dense-asymmetric-registration-presenter-v2` / revision2, and recomputes the whole
canvas SHA256. The old manifest grammar and header layout revision remain1;
this wrapper profile explicitly distinguishes the new canonical canvas.

The frozen cross-project contract is copied into
`tests/steamlink-presenter-source/profile.json`. Its signature preimage names the
exact 688x548 canvas, color rectangle `(16,244,320,192)`, coverage rectangle
`(352,244,320,192)`, straight numeric RGB, linear coverage, native-G component,
cell placement and tolerance. SHA256 is
`6f999b336f525cdb66e379bd3038c50fc9d1237f534a66323ab0215b503ed586`;
the first12 bytes provide codes
`111,153,155,51,111,82,92,219,102,227,121,189`.
Cell i starts at `(16+52*i,464)`. The painted cells end at y495 and x619, so
neither original asymmetric L marker at `(4,448)` / `(644,502)` is touched.
The receiver's declared recognition tolerance is2/255; source pixels themselves
use the exact code in all three channels.

`RegistrationTextures` defaults to the existing profile. Only an explicit
`presenterProfile:true` chooses v2. The existing settings-controlled PC source
uses that explicit choice when graphics fences are supported. Its Debug receipt
reports the actual profile, revision and guard hash. The existing static fallback
on providers without CPU fences remains the old static diagnostic and is never
reported as v2.

Preparation still belongs exclusively to `Prepare` in Update, before publishing
either texture. Both eye images share the immutable frozen pair context. No new
render-callback work, allocation, source preparation, timer, pool lifetime or
fence policy is introduced. Opening options or switching off closes admission
and removes the complete source through the existing lifecycle. This source
change alone does not prove how a receiver handles already recorded commands.

A compact guard recognizes an agreed format; it cannot authenticate the body,
prove liveness, detect a copied guard, or distinguish a frozen valid image from
continued mode. Coverage stays numeric and independent of visible RGB. An intact
guard with a changed body deliberately remains recognizable. Ordinary output
must remove the entire source image; a future receiver's OFF behavior can only
change once the post-OFF image arrives.

## Focused checks

Run `python3 scripts/check-steamlink-presenter-source.py` for pure production C#
controls. Add `--compile-runtime` to type-check the changed runtime against the
actual local Unity/net472 references. These checks compare every canvas byte
against v1 and allow changes only in the12 exact rectangles; they preserve
headers/planes/markers, independently verify frozen preimage/hash/codes, validate
actual-profile JSON and refuse invalid original pattern contexts. Every cell has
independent tolerance-boundary and causal mismatch controls. A v1 Stamp after
v2 must restore the original bytes and profile, proving the reusable pattern
workspace was not polluted by footer painting.

`--gpu-only` runs a new, bounded actual Unity2021.3.5 Vulkan fixture. It exercises
only the new profile's production upload/blit/pool/fence/Off path, both eyes,
passed-fence texture reuse and whole-image readback. It reports still-pending
checks explicitly; zero means that condition was not exercised. This is not an
XR callback, stream, native-mask or headset-picture proof. The runner pins
production inputs and refuses certification if they change during its run.
Processes are reaped before the shared retention API removes generated caches;
source/receipts/readback PNGs and failure evidence remain.

The first focused CPU run passes151 assertions across8 phase/eye cases with24
single-cell causal mutations. Actual Unity reference compilation passes with
zero errors/warnings. Production generator and fence-pool behavior outside the
explicit profile branch inherit the existing checks; this is not a new complete
local/wire gate. Two bounded GPU attempts failed with native Unity signal11
before any source stamp; their evidence is retained. In the second attempt the
first default constructor completed, followed by a failure before preparation
of the second pool. The final fixture removes that unrelated double-construction;
only its real-reference compilation is verified, with no third GPU execution.
Therefore no new upload/blit/fence/OFF GPU pass is claimed. The old fence-pool
evidence is inherited for its unchanged lifetime logic. Use `--gpu-compile-only`
to recheck the final fixture's actual Unity-reference types without launching Unity.
