Offline-only UnityMeshSimplifier core from https://github.com/Whinarn/UnityMeshSimplifier at commit 53fdb3122645bcd3ad2c258235200dff3dfbaa9b (MIT).

Only integration changes: disable nullable analysis on unchanged upstream code and escape a generic type in one XML comment. FacialHybrid keeps original facial triangles separately; it does not modify the upstream algorithm. No runtime simplification or LOD renderer/material generation is shipped.
