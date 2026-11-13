# Embedded Quest startup shader delivery — 2026-10-21

The dedicated initial-handoff shader from loading checkpoint `b4a5c3745` needs
new Android bytecode. Rebuilding all four main/town/voice/environment banks for
this one authored shader would repeat unrelated art imports and package work.

Use the project's existing small embedded shader-bank pattern instead. The exact
shader body and fixed importer GUID move under `src/GloomhavenVR/Assets/QuestLoading`.
The plugin embeds the independently compiled Android bank and its provenance.
There is no extra fifth mod-content bank, no new installed content path and no
change to original Campaign/figure/audio/movie/environment production.

The final bank is16,936bytes with SHA-256
`9bcb74cf6f90f03dee60a1e8d159d3efee2781011f40e9c836ed1e2bd11f408a`.
Two fresh private projects with the fixed original shader GUID produce identical
bank bytes. The exact source body remains
`d3f8e82fe07d1d63f78acf976f38ed959e1d3dd52c825f7a2c6d09187415d490`.
The new source, bank, importer and provenance are explicit required release inputs.

`QuestLoadingAssets.ResolveShader()` reads nothing before Quest platform adoption,
requires Vulkan and the current MultiPass routing, validates bounded provenance
plus exact resource size/hash, then checks actual loaded bank/asset/shader identity
and support. It caches a successful shader and reports a permanent failure once
while retaining the caller's native-artwork fallback. An early unconfigured call
does not latch a failure. Desktop never loads this Android resource.

The minimal developer producer imports only this authored shader in an isolated
Unity2021.3.5f1 project. Its actual bank audit performs14checks, including Android
serialized target13, Vulkan program platform18, the real bank container and exact
asset path, surviving mono SPIR-V programs, premultiplied blend and depth/cull
state. MultiPass invokes that program separately per eye. Unused shared-eye
variants were stripped by the minimal non-XR project; this is explicitly not
claimed as SinglePassInstanced/Multiview support. Installing full XR packages just
to retain unused variants was unnecessary. Shader source retains its XR macros.

Resource tests execute the real loader, existing bounded JSON parser, real
embedded bytes and SHA qualification. Only native Android AssetBundle/Shader APIs
are explicit models. Thirteen cases /64assertions cover adoption/cache, missing
bank/provenance, wrong target/hash/API/routing, native load failure, wrong bank,
wrong asset path, missing/wrong/unsupported shader and bounded fallback/unload.
After the final artifact-only compiler/GUID cleanup, success and hash-refusal
cases plus actual compiled-plugin PE resource/hash closure were rerun. The exact
final plugin passes strict Release with zero warnings/errors. The existing
release overlay tests pass10cases.

This is source/resource/binary-metadata evidence. The separate loading lane tests
the complete drawing path; Android GPU/headset pixels remain hardware acceptance.
No full APK, original asset conversion or large mod-bank build occurred.

Every Unity producer invocation adopts, terminates and reaps its own remaining
compiler/Xvfb children before the common completed-run cleanup removes generated
caches. Source, metadata, outputs and logs are preserved under the root evidence
`quest717-review-20261021/embedded`; no borrowed game input is a cleanup target.
