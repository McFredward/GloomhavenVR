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

## Exact depthless screen-backdrop subset

```sh
python3 tests/steamlink-eye-coverage/run.py --backdrop-only --output .planning/debug/eye-backdrop
```

This subset exercises the new production third pass without repeating the older
opaque-prefix matrix. An owned queue1000, ZWriteOff, ZTestAlways screen writes
RGB with alpha0 before the existing reset/stamp. The actual renderer is then
drawn alpha-only after those two commands. Complete RGB bit patterns must remain
identical, including an opaque hand drawn in front and a separate half-opacity UI
surface. Independent red silhouette coverage checks include resolved MSAA edges;
pixels outside actual surfaces must remain clear. No second scene camera or
render target is added by the component.

The fixture also compiles the exact production `FlatScreen.OpaqueBackdrop` getter
in a minimal owner excerpt. It checks split versus nonsplit ownership, foreign
materials, hidden/disabled/inactive/destroyed surfaces and exact old-owner release.
This is not execution of the complete game's Show/Hide methods. Component tests
exercise disabled/hidden/force-off/layer changes, replacement, destruction,
movement, repeated show/hide without a cold refresh, foreign-camera exclusion,
getter failure and disposal. The getter is created cold and queried only for the
actual armed camera at `Camera.onPreRender`; the existing command buffer is
rebuilt only when its selected renderer changes. Its `DrawRenderer` follows the
current transform. A destroyed managed renderer is distinguished from actual
null so Unity's destroyed-object equality cannot retain its old command.

Root integration calls `SetOpaqueBackdropSource(() => FlatScreen.OpaqueBackdrop)`
once before `Arm`; `Disarm` detaches the camera event and commands, while `Dispose`
also releases the callback reference and owned objects. UI glass and arbitrary
depthless scene renderers are never inferred from their shader names. Exact
owned material identity is necessary but not sufficient: only its confirmed
opaque `Hidden/BlitCopy` path is admitted. The alpha-blended Sprites/UI fallback
materials are refused. A destroyed split background cannot be mistaken for the
absence of a split and thereby fall through to another quad.

2026-10-17 focused worker evidence: Linux Vulkan/Intel, Gamma UNorm,
four1/2/4/8-sample cases,118875 checks,9 owner checks and5 component lifecycle
checks. Strict net472 nullable compilation passes with warnings as errors. The
first retained run completed its four raster cases but failed the disposed→Arm
control because the new Disarm path tried to clear a released command buffer;
the final passing run includes the disposed guard. This was an offline component
failure, not a headset cause. Earlier opaque/cutout/stencil evidence is inherited;
no fresh full suite, Windows, XR or native video-decoder proof is claimed.

The final owner-admission delta runs independently with `--owner-only`:20 checks,
including11 exact getter checks and no raster rows. It adds explicit positive
Hidden/BlitCopy and negative Sprites/Default/UI/Default controls. The retained
first owner-only attempt exposed the destroyed-object fallthrough described
above; the corrected attempt passes. The four unchanged GPU cases and strict
component compilation are inherited rather than repeated for this getter-only
repair.
