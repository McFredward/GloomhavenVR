"""Original CAB ownership includes nested Campaign PCG bundle paths."""
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import campaign_shaders
from storage import BuildError


class CampaignCabOwnershipTests(unittest.TestCase):
    def test_nested_bundle_native_owner_is_preserved(self):
        with tempfile.TemporaryDirectory() as folder:
            game = Path(folder)
            directory = game / "StreamingAssets/aa/StandaloneWindows64"
            nested = directory / "pcg_databases_assets_assets/pcg/pcg_cave.asset.bundle"
            nested.parent.mkdir(parents=True)
            nested.write_bytes(b"fixture nested bank")
            (directory / "root.bundle").write_bytes(b"fixture root bank")
            def members(path):
                return [{"name": "CAB-" + ("1" if path == nested else "2") * 32}]
            with patch.object(campaign_shaders, "load", return_value=SimpleNamespace(serialized_members=members)):
                owners = campaign_shaders.original_cab_bundles(Path(folder), game)
            self.assertEqual(owners["cab-" + "1" * 32], nested.relative_to(game).as_posix())
            self.assertEqual(len(owners), 2)
            with patch.object(campaign_shaders, "load", return_value=SimpleNamespace(serialized_members=lambda _: [{"name": "CAB-" + "1" * 32}])):
                with self.assertRaisesRegex(BuildError, "conflicting"):
                    campaign_shaders.original_cab_bundles(Path(folder), game)


if __name__ == "__main__":
    unittest.main()
