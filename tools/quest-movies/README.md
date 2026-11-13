# Owned-source Quest stereo movie recipe

This directory distributes numeric geometry and shader/converter source only.
It contains no original RGB movie frames, encoded movie packets or audio. The
155 Zstandard chunks retain all 7,167,744,000 original half-depth bytes from the
65 accepted shots, using a reversible two-dimensional uint16 spatial predictor.
Every decoded half-float bit is checked against its retained source digest.
There is no quantization, temporal resampling, neural inference or model download.

`recipe.json` retains the original Windows movie hashes, exact source intervals,
per-shot normalizations, hero print protection and accepted geometry. It covers
11 hero unlocks, seven ambient films and the embedded Twin Sails studio mark.
The gameplay trailer remains explicitly excluded. The project's separately
approved campaign-intro mono/SBS files remain authored mod inputs.

The local producer in `tools/quest-builder/movie_banks.py` validates the selected
owned original installation. Its finite movie jobs reconstruct the right eye
with the approved projection and preserve original left pictures, native frame
counts, frame rates, endings and AAC packets. Movies use a timecode-free Android
MP4 container under their canonical MOV names. The two Android shaders form a
separate small bank; no original Campaign bundle is rebuilt.

Each film's key covers its own source, numeric recipe, rendering code and pinned
converter packages. General builder orchestration and the mod build number do
not invalidate completed films. Signed APK metadata also retains those keys, so
an APK-only update can leave unchanged compressed movie entries in the APK and
reuse its small shader bank without Unity. The headset uses separate per-file
receipts and copies only changed movie files after an update.

The source recipe is game-free. Reconstructed movies stay in the player's local
workspace and finished Quest package. Real Quest decoder throughput, UI/XR
raster, fusion, comfort and all native movie spawn paths require hardware tests;
local media and GPU checks do not establish those outcomes.
