# Robust full-resolution producer pair

This is one bounded Unity2021.3.5/Vulkan proof, not the old45-case coverage matrix.
It executes the actual shipping v3 compiled shader through production
`SceneEyeOutput`, with two independent4040-square8xMSAA sources labelled Left/Right.
The retained compiled v2 shader packs the exact same two sources as controls.

```sh
python3 -B tests/steamlink-robust-coverage/run.py --output /private/evidence
```

The test verifies all24actual format cells, contribution RGB, fractional glass,
positive emission at zero alpha, all four strip orientations and both sides of
all three seams. It rechecks the retained left output after the right draw.
Actual resolved source and packed v3/v2 carrier PNGs, numeric source/atlas probe
metadata, source hashes, frozen profile and compiled bank pins remain available
for independent lossy-codec/native-decoder replay. The PNG header centers are
checked again after encoding to establish their top-down file convention.
There is no ideal repaint or synthetic header substitution in these images.

The source controls bind the exact active production loader to frozen v3 and
refuse actual loader mutations to v1/v2. The v3 header is format recognition only:
it does not prove exact R1 registration, image authenticity, freshness or physical
stereo association. This fixture does not execute XR callbacks, Steam Link,
codec transport, native receiver, camera blending or a headset. Actual8xMSAA is
required and read back; the request alone cannot pass.

All children are bounded and reaped. The central completed-run cleanup API runs
for success/failure, and the exact owned generated Unity project is removed.
PNG inputs, compiled bank, source/manifest evidence and logs remain untouched.
