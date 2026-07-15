# Bundle content: Test/

`VRTestCube.prefab` — Phase 0b acceptance asset: a 10 cm cube referencing the
built-in Cube mesh and Default-Material. Spawning it in-game via

```csharp
var bundle = AssetBundle.LoadFromFile(pathToGloomhavenvrBundle);
Object.Instantiate(bundle.LoadAsset<GameObject>("Assets/Bundle/Test/VRTestCube.prefab"));
```

proves the bundle pipeline end-to-end (ROADMAP Phase 0b "Done when").

Note: the material reference resolves against the GAME's built-in resources at
load time. If it comes in pink (shader variant stripped by the game), the load
still succeeded — reassign any game material at runtime
(`Resources.FindObjectsOfTypeAll<Material>()`), per TOOLCHAIN.md §4.1.

This file was hand-authored as text YAML; Unity re-saves it (and adds a .meta)
on first import — commit those changes.
