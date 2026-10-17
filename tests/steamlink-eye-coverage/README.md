# Actual same-eye coverage component fixture

The runner copies the production SceneEyeCoverage component and its shader into
an isolated Unity2021.3.5 project. It executes actual Camera.Render/OnRenderImage
under Linux Vulkan/Gamma/SDR UNorm at supported1/2/4/8 samples, compares complete
RGB bit patterns against the no-hook equivalent, and checks independent opaque,
cutout, AlphaToMask and stencil silhouettes. A depthless causal control is
explicitly excluded rather than admitted. Lifecycle, OFF, nested foreign-camera
and exact camera/epoch controls use the production component directly.

```sh
python3 tests/steamlink-eye-coverage/run.py --output .planning/debug/eye-coverage
```

The fixture is not XR/Windows or native all-game transparent-coverage evidence.
It freezes source hashes through child reap, preserves results/failures, calls
the shared cleanup API and removes its own complete stopped Unity project.
