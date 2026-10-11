"""Bind the runtime ancestry fixture to exact shipped map/ProcGen coordinator objects.

The ordinary runtime harness checks the retained contract and its causal controls.
--game-data additionally reparses all 129 original procedural/editor map roots and
the addressed ProcGen Maps root. Only MonoBehaviour script-reference headers are
needed from the stripped scene; trailing game-specific fields are not decoded.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

FIXTURE = Path(__file__).with_name("native-world-scope.json")
MAP_TYPES = ["ApparanceEntity", "ProceduralMapTile", "ProceduralStyle",
             "RoomVisibilityTracker", "ProceduralMapConfig", "ApparanceMap"]
ROOT_TYPES = ["ProceduralScenario", "ProceduralStyle",
              "ProceduralPlacementNotifierHandler", "LightShadowsModifierController"]


def verify(fixture=FIXTURE, game_data=None, native_code=None):
    data = json.loads(Path(fixture).read_text())
    checks = 0

    def check(ok, message):
        nonlocal checks
        checks += 1
        if not ok:
            raise AssertionError(message)

    def contract(value):
        check(value["schema"] == 1, "native coordinator contract schema")
        check(len(value["maps"]) == 2, "both original map source bundles retained")
        for source, expected in zip(value["maps"], (15, 114)):
            check(len(source["maps"]) == expected, "all original map roots retained")
            check(len(source["sha256"]) == 64, "native map bundle identity retained")
            for root in source["maps"]:
                check([c["class"] for c in root["components"]] == MAP_TYPES,
                      "exact original map coordinator tuple")
                check(all(len(c["rawSha256"]) == 64 for c in root["components"]),
                      "every addressed native map coordinator raw object retained")
        roots = value["procGenRoot"]["roots"]
        check(len(roots) == 1 and roots[0]["pathId"] == 2,
              "exact native ProcGen Maps root retained")
        check([c["class"] for c in roots[0]["components"] if c["type"] == "MonoBehaviour"] == ROOT_TYPES,
              "exact original ProcGen coordinator tuple")

    contract(data)
    controls = 0
    for section, class_name in (("maps", "ApparanceMap"), ("maps", "ProceduralMapConfig"),
                                ("procGenRoot", "ProceduralPlacementNotifierHandler"),
                                ("procGenRoot", "LightShadowsModifierController")):
        changed = copy.deepcopy(data)
        components = (changed["maps"][0]["maps"][0]["components"] if section == "maps"
                      else changed["procGenRoot"]["roots"][0]["components"])
        next(c for c in components if c.get("class") == class_name)["class"] = "UnknownNativeAnimation"
        try:
            contract(changed)
        except AssertionError as error:
            check("exact original" in str(error), "causal tuple control fails its intended contract")
            controls += 1
        else:
            raise AssertionError("native tuple control escaped: " + class_name)

    if game_data:
        import UnityPy
        game_data = Path(game_data)
        for source in data["maps"]:
            path = game_data / source["source"]
            check(hashlib.sha256(path.read_bytes()).hexdigest() == source["sha256"],
                  "actual original map bundle SHA matches retained fixture")
            environment = UnityPy.load(str(path))
            actual = []
            for obj in environment.objects:
                if obj.type.name != "GameObject":
                    continue
                go = obj.read()
                components = []
                for pointer in go.m_Component:
                    ptr = pointer.component
                    if ptr.type.name != "MonoBehaviour":
                        continue
                    component = ptr.read()
                    reader = component.object_reader
                    script = component.m_Script.read()
                    tree = reader.read_typetree()
                    components.append({"class": script.m_ClassName, "componentPathId": ptr.path_id,
                                       "rawSha256": hashlib.sha256(reader.get_raw_data()).hexdigest(),
                                       "objectType": tree.get("m_ObjectType")})
                if any(c["class"] == "ApparanceMap" for c in components):
                    actual.append({"name": go.m_Name, "pathId": obj.path_id, "components": components})
            check(actual == source["maps"], "every original map root reparses exactly")
        source = data["procGenRoot"]
        path = game_data / source["source"]
        check(hashlib.sha256(path.read_bytes()).hexdigest() == source["sha256"],
              "actual original ProcGen scene SHA matches retained fixture")
        environment = UnityPy.load(str(path))
        for root in source["roots"]:
            matches = [o for o in environment.objects if o.type.name == "GameObject" and o.path_id == root["pathId"]]
            check(len(matches) == 1, "exact original ProcGen root identity reparsed")
            go = matches[0].read()
            check(go.m_Name == root["name"], "actual addressed ProcGen root name")
            for pointer, component in zip(go.m_Component, root["components"]):
                reader = pointer.component.deref()
                check(reader.path_id == component["pathId"] and reader.type.name == component["type"],
                      "actual original root component identity")
                check(hashlib.sha256(reader.get_raw_data()).hexdigest() == component["rawSha256"],
                      "actual original root component bytes")
                if reader.type.name == "MonoBehaviour":
                    # This stripped scene's base MonoBehaviour tree describes the
                    # header only. Script PPtr/class remains exact; custom payload
                    # is retained by its complete raw hash rather than invented.
                    header = reader.read_typetree(check_read=False)
                    script = reader.parse_as_object(check_read=False).m_Script.read()
                    check(header["m_Script"] == component["script"] and script.m_ClassName == component["class"],
                          "actual original root script reference and class")
    if native_code:
        for name, digest in data["codeSources"].items():
            check(hashlib.sha256((Path(native_code) / name).read_bytes()).hexdigest() == digest,
                  "reviewed original coordinator/load method source SHA: " + name)
    return {"assertions": checks, "controls": controls, "actualMapRoots": 129 if game_data else 0,
            "actualProcGenRoots": 1 if game_data else 0,
            "coverage": "native bytes and script references" if game_data else "retained original ancestry contract",
            "limits": "Runtime component types/controller callbacks are explicit harness boundaries; hardware generated geometry and pixels remain separate."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", type=Path)
    parser.add_argument("--native-code", type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    report = verify(game_data=args.game_data, native_code=args.native_code)
    if args.report:
        args.report.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report))
