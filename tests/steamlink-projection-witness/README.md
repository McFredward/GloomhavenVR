# Zero-carrier projection witness

This focused local GPU fixture executes the unchanged actual v3 pack shader
and `SceneEyeOutput` with a constant1x1 zero-RGB/zero-alpha owned input. It checks
both explicit eye labels, all12 format cells, independent coverage strips and
the central RGB region in the complete3168-square carrier. An analytic
source-over control explains why opaque black in a central flattened RGB-only
overlay cannot occlude a bright camera while coverage stays zero.

Run `python3 -B tests/steamlink-projection-witness/run.py`. The pinned Unity
2021.3.5 editor and an owned Intel Vulkan test context are required. Source,
carrier PNGs, logs, result and cache-cleanup receipts remain in the evidenced
run. No game source image, XR session, OpenVR, Steam setting or Frame is touched.

The nominal[0.4,0.6] overlay chart is inside the v3 RGB rectangle with a large
margin. This is not a proof of every PC pre-warp or clipping behavior. A headset
positive witness additionally requires black occlusion beside interior real
camera visibility, correct eye marks and a stable image. Translucency alone,
recommended render size, or a successfully created2048-square texture does not
prove independent transport, delivered resolution or full-scene image quality.
