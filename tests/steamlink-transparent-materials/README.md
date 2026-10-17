# Actual head-only owned material integration fixture

The fixture uses the production SceneTransparentMaterials component and the
actual shipping SceneCoverage shader bank. It imports unchanged originals for
RGB/alpha comparison, executes real nested cameras and covers lifecycle,
manual foreign writes, state-MPB refusal and allocation-free warmed entry/exit.
The variants.json manifest is source-pinned input to Root's separate bank build.

Use run.py --output <ignored-evidence>; --lifecycle-only skips the previously
passing raster matrix for narrow cold-cache controls. No existing shader or
game asset is edited. All source inputs remain pinned through child reap and
owned Unity projects are cleaned afterwards. Scope is finite owned Linux/Vulkan,
not native VFX, XR, Windows D3D11 or game-wide coverage.
