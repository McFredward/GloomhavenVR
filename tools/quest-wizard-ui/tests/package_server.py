"""Real HTTP/package writer browser fixture; barriers only pause measured work."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import time
import zipfile

repo, root = map(Path, sys.argv[1:3])
sys.path.insert(0, str(repo / "tools/quest-wizard"))
import discovery
from state import Store
from server import LocalServer

spec = importlib.util.spec_from_file_location("wizard_browser_package_bytes", repo / "tests/quest-builder/test_portable_package.py")
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
fixture = module.PortablePackageTests(); fixture.setUp()
store = Store(root / "owned")
source = root / "player.apk"
source.write_bytes(fixture.ap.read_bytes())
bank = root / module.portable.CONTENT
bank.write_bytes(fixture.bank.read_bytes())
original = module.portable.export_package
calls = 0

def controlled_export(*args, **kwargs):
    global calls
    calls += 1; number = calls
    report = kwargs.get("progress")
    def progress(value):
        report(value)
        if value["completed"] > 0 and value["percent"] < 100 and not (root / ("paused-" + str(number))).exists():
            (root / ("paused-" + str(number))).write_text(json.dumps(value))
            while not (root / ("continue-" + str(number))).exists():
                if kwargs["cancel"](): break
                time.sleep(.01)
    return original(*args, **{**kwargs, "progress": progress})

module.portable.export_package = controlled_export
helper = discovery.builder(repo); helper.portable_packages = module.portable
discovery.builder = lambda *_: helper
discovery.default_apk = lambda *_: {"path": str(source), "modBuild": 671, "source": "controlled-completed-fixture"}

def discover(*_):
    return {"schema": 1, "event": "discovery", "defaultApk": discovery.default_apk(store),
            "games": [{"id": "owned-ui-fixture", "provider": "gog", "gameRoot": str(root / "PC")}],
            "unityEditors": [], "capabilities": {"browse": False, "spaceEstimate": False}}

server = LocalServer(store, repo / "tools/quest-wizard-ui", discover=discover)
print(json.dumps({"url": server.url, "source": str(source), "root": str(store.root)}), flush=True)
try: server.serve_forever()
except KeyboardInterrupt: pass
finally:
    server.close_owned(); fixture.doCleanups()
