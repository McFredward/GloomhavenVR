"""Read the original book mesh for geometry validation; never modify or repackage game data."""
import sys
from pathlib import Path
import UnityPy
base = Path(sys.argv[1])
source = base / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/pcg_databases_assets_assets/pcg/pcg_library.asset.bundle'
for obj in UnityPy.load(str(source)).objects:
    if obj.type.name == 'Mesh':
        mesh = obj.read()
        if mesh.m_Name == 'CR_ST_Shelf_Book_07':
            Path(sys.argv[2]).write_text(mesh.export())
            break
else:
    raise SystemExit('Original temple book mesh is missing; native geometry validation cannot be skipped.')
