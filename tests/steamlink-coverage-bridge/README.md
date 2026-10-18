# Ordinary-RGBA source capability checks

Run `python3 tests/steamlink-coverage-bridge/run.py --output <evidence>`.
`--suite wire|lifecycle|callback|typecheck` reruns only the affected area.
`--suite lifecycle-retirement` isolates the pending-callback lifetime witness
and its causal control without repeating already-passing lifecycle cases.

The real production parser and observer compile beside explicit process/JSON/log
boundaries. Wire vectors include an independent request golden; malformed
framing, nonce, process identity, source profile, reserved bytes and impossible
flags cannot grant authority. Observer tests cover Off, Reset, stale replies,
nonoverlap, native EOF/refusal and bounded missing-peer attempts. The actual
observer also reproduces the Build708 callback-failure boundary with a real
token registration throwing `ObjectDisposedException`. Both Tick and Reset
revoke authority synchronously; callback execution and CTS disposal remain off
the calling stack. A gated callback proves the tick returns without waiting,
and the owned worker cannot retire or overlap a replacement until cancellation
finishes. Separate causal controls restore synchronous Cancel and premature
worker retirement; each must fail its own semantic assertion. The runtime's
Windows pipe and Mono BCL are not executed by this managed fixture.
The actual
`SteamLinkSceneRuntime`/`SceneHeadOutput` bodies exercise ordinary output,
legacy compatibility, release wait, capability loss and stereo epoch changes.
Eight causal source mutations must fail their named semantic assertions. A strict
net472 compile uses the real Unity2021 metadata. No Windows pipe, Steam process
or headset is contacted by these tests.

The separate real GPU witness is:

```
python3 tests/steamlink-scene-output/run.py --gpu --gpu-mode ordinary-rgba \
  --color-space Gamma --backend vulkan --output-root <evidence>
```

It compares the full current RGB/A output against an independent ordinary resolve
at explicit Left/Right callbacks and different extents/MSAA counts. This is not
proof of D3D11/OpenXR submission, native source/AU association or headset output.
