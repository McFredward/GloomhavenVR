# Embedded Quest loading shader

`ghvr-quest-loading.bundle` contains only the authored `QuestLoadingSpinner.shader`.
It is compiled with Unity2021.3.5f1 for Android/Vulkan, linear colour and the current
Quest MultiPass route. Its mono SPIR-V program executes separately for each eye;
this bank does not claim future SinglePassInstanced/Multiview support.

The exact source, compiled bank and provenance ship together. The plugin embeds
the bank plus provenance JSON and `Core/QuestLoadingAssets.cs` validates their
identity and hash before resolving the exact shader. Failure retains the caller's
native-artwork fallback with one bounded diagnostic. Desktop never loads it.

This delivery allows a compact native code/metadata update to preserve all four
existing large Quest mod banks and all owned game/audio/movie/mesh data. The
Builder never recompiles this bank on the player's PC.

Developer regeneration:

```sh
python3 scripts/build-quest-loading-shader.py
bash scripts/ci-build.sh Release
python3 scripts/quest-loading-assets-tests.py
```

The producer creates only a private one-shader Unity project. Its metadata audit
checks the actual Android container, exact Vulkan program inventory, alpha blend
and depth state before publishing. Its runtime failure tests use explicit modeled
Android AssetBundle APIs; metadata and host tests do not certify headset pixels.
