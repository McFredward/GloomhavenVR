# Focused actual-Unity registration output proof

This local developer fixture compiles the exact current production generator,
pair/texture owner and blit method against Unity2021.3.5, runs real graphics and
CPU fences, and validates the GPU output with the existing independent payload
header decoder. It does not simulate real XR callbacks or prove Steam Link,
Windows D3D11, native video/eye mapping, material alpha or passthrough.

```bash
PYTHONDONTWRITEBYTECODE=1 python3 tests/steamlink-registration-gpu/run.py \
  --source-root /home/claw/gloomhaven_vr \
  --payload-tools /home/claw/steam-frame-chroma-key/tools
```

The default uses Vulkan and both Linear and Gamma rendering in two minimal
Editor sessions. Missing graphics/fence capability fails explicitly. The actual
production caller's expected sRGB output state is supplied and recorded; this
does not establish an unobserved XR callback's transfer state. Pool observations
precede every readback so ReadPixels cannot conceal unsafe reuse. A fast GPU may
complete a fence before the pending branch is observed: that limitation is
reported rather than counted as a native pending proof.

The only callback-boundary stub disables the unused XR callback; all generator,
slot ownership, Unity upload, blit, readback and fence calls are real production
code. Reflection only observes private slot/fence state and never writes it.
Retained lossless PNGs, source records, source hashes and numeric results identify
the exact tested bytes. Processes are reaped before completion cache cleanup.

After a bounded fixture repair, use `--color-space linear` or `gamma` for only
the affected profile. A remaining profile can use `--reuse-build <passing-run>`;
this refuses unless all production and compiled fixture hashes still match.
