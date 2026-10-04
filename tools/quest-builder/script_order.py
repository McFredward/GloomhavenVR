"""Recover owned Unity 2021.3 MonoScript orders without loading game code.

The narrowly supported serialized-file reader uses only the Python standard
library. Managed type identities come from the existing metadata-only .NET tool;
neither UnityPy nor an executed original game assembly is a builder dependency.
"""
from __future__ import annotations

from collections import defaultdict
import importlib.util
import json
from pathlib import Path
import re
import struct
import subprocess

from storage import BuildError, digest, record_file, value_hash, write_json

MANIFEST = "Assets/QuestOriginalStartup/script-orders.json"
RECEIPT = "QuestStartupEvidence/script-orders-source.json"
POINTER = re.compile(r"m_Script:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}")


class _Reader:
    def __init__(self, data: bytes, start: int = 0, end: int | None = None, endian: str = "<"):
        self.data, self.position, self.end, self.endian = data, start, len(data) if end is None else end, endian

    def take(self, size: int) -> bytes:
        if size < 0 or self.position + size > self.end:
            raise BuildError("Original MonoScript serialized data is truncated or out of bounds.")
        value = self.data[self.position:self.position + size]
        self.position += size
        return value

    def number(self, form: str):
        return struct.unpack(self.endian + form, self.take(struct.calcsize(form)))[0]

    def align(self):
        self.take((-self.position) % 4)

    def count(self, maximum: int = 262144) -> int:
        value = self.number("i")
        if not 0 <= value <= maximum:
            raise BuildError("Original MonoScript serialized count is invalid.")
        return value

    def string(self) -> str:
        try:
            value = self.take(self.count(4096)).decode("utf-8", errors="strict")
        except UnicodeError as error:
            raise BuildError("Original MonoScript contains invalid UTF-8.") from error
        self.align()
        return value


def read_script_orders(path: Path) -> list[dict]:
    """Read player MonoScripts in the pinned 2021.3 serialized v22 format."""
    if path.is_symlink() or not path.is_file() or not 48 <= path.stat().st_size <= 64 * 1024 * 1024:
        raise BuildError("Owned globalgamemanagers.assets is missing, linked or unexpectedly large.")
    data = path.read_bytes()
    header = _Reader(data, endian=">")
    header.take(8)
    version = header.number("I")
    header.take(4)
    little = header.number("B")
    if version != 22 or little not in (0, 1) or header.take(3) != b"\0\0\0":
        raise BuildError("Unsupported original MonoScript serialized format; audit the new game version.")
    metadata_size, file_size, data_offset = header.number("I"), header.number("q"), header.number("q")
    header.take(8)
    if file_size != len(data) or not 48 < 48 + metadata_size <= data_offset <= file_size:
        raise BuildError("Original MonoScript serialized header sizes are invalid.")
    reader = _Reader(data, 48, 48 + metadata_size, "<" if little == 0 else ">")
    unity = bytearray()
    while len(unity) <= 64:
        byte = reader.take(1)
        if byte == b"\0":
            break
        unity.extend(byte)
    if bytes(unity) != b"2021.3.5f1":
        raise BuildError("Original MonoScript Unity version changed; audit before rebuilding.")
    reader.take(4)  # Target platform does not change player MonoScript layout.
    type_tree = reader.number("B")
    if type_tree not in (0, 1):
        raise BuildError("Invalid original MonoScript type-tree flag.")
    types = []
    for _ in range(reader.count(4096)):
        class_id = reader.number("i")
        reader.take(3)  # isStrippedType and scriptTypeIndex
        if class_id == 114:
            reader.take(16)
        reader.take(16)  # oldTypeHash
        if type_tree:
            nodes, strings = reader.count(262144), reader.count(16 * 1024 * 1024)
            reader.take(nodes * 32 + strings)
            reader.take(reader.count(262144) * 4)
        types.append(class_id)
    objects, ids, intervals = [], set(), []
    for _ in range(reader.count()):
        reader.align()
        path_id, start, size, type_index = reader.number("q"), reader.number("q"), reader.number("I"), reader.number("i")
        start += data_offset
        if path_id in ids or not 0 <= type_index < len(types) or not data_offset <= start <= start + size <= file_size:
            raise BuildError("Original MonoScript object identity or bounds are invalid.")
        ids.add(path_id)
        intervals.append((start, start + size))
        if types[type_index] == 115:
            objects.append((path_id, start, size))
    previous = data_offset
    for start, end in sorted(intervals):
        if start < previous:
            raise BuildError("Original MonoScript serialized objects overlap.")
        previous = end
    result = []
    for path_id, start, size in objects:
        item = _Reader(data, start, start + size, reader.endian)
        item.string()  # m_Name
        order = item.number("i")
        item.take(16)  # m_PropertiesHash
        name, namespace, assembly = item.string(), item.string(), item.string()
        if (item.position != item.end or not name or not re.fullmatch(r"[A-Za-z0-9_.-]+\.dll", assembly) or
                any("\0" in value for value in (name, namespace))):
            raise BuildError("Original MonoScript identity, order or player layout is unsupported at object " + str(path_id) + ".")
        result.append({"assembly": assembly, "namespace": namespace, "name": name,
                       "executionOrder": order, "sourcePathId": path_id})
    if not result:
        raise BuildError("Owned serialized bank contains no MonoScripts.")
    return result


def _managed_types(game: Path, source: Path, cache: Path, dotnet: Path) -> dict:
    tool = source / "tools/quest-recovery/ManagedInventory"
    sources = [record_file(p, p.name) for p in sorted(tool.glob("*")) if p.suffix in (".cs", ".csproj")]
    originals = [record_file(p, p.name) for p in sorted((game / "Managed").glob("*.dll"))]
    if not sources or not originals or not dotnet.is_file():
        raise BuildError("Script-order recovery requires owned managed DLLs and the builder's .NET SDK.")
    key = value_hash({"sources": sources, "originals": originals})
    folder = cache / "script-order-metadata" / key
    inventory = folder / "managed-types.json"
    proof = folder / "receipt.json"
    if inventory.is_file() and proof.is_file():
        receipt = json.loads(proof.read_text(encoding="utf-8"))
        if receipt.get("sha256") == digest(inventory):
            return json.loads(inventory.read_text(encoding="utf-8"))
        raise BuildError("Cached script-order managed metadata was modified; remove its cache and retry.")
    folder.mkdir(parents=True, exist_ok=True)
    commands = ([str(dotnet), "build", str(tool / "ManagedInventory.csproj"), "--output", str(folder / "tool"),
                 "--nologo", "--verbosity", "quiet", "-p:BaseIntermediateOutputPath=" + str(folder / "obj") + "/"],
                [str(dotnet), str(folder / "tool/ManagedInventory.dll"), str(game / "Managed"), str(inventory)])
    for command in commands:
        result = subprocess.run(command, text=True, capture_output=True)
        with (folder / "metadata.log").open("a", encoding="utf-8") as log:
            log.write(result.stdout + result.stderr)
        if result.returncode:
            raise BuildError("Metadata-only script-order inventory failed; inspect " + str(folder / "metadata.log"))
    write_json(proof, {"schema": 1, "sha256": digest(inventory), "sources": sources, "originals": originals})
    return json.loads(inventory.read_text(encoding="utf-8"))


def _file_id_function(source: Path):
    spec = importlib.util.spec_from_file_location("quest_script_order_md4", source / "tools/quest-recovery/md4.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.script_file_id


def _replace_orders(text: str, entries: list[dict]) -> str:
    pattern = re.compile(r"^  executionOrder:(?:[ \t]*\{\}[ \t]*\r?\n|[ \t]*\r?\n(?:^    [^\n]*\n)*)", re.MULTILINE)
    matches = list(pattern.finditer(text))
    if not re.search(r"^PluginImporter:\r?$", text, re.MULTILINE) or len(matches) != 1:
        raise BuildError("Recovered DLL has no unique PluginImporter execution-order map.")
    newline = "\r\n" if matches[0].group(0).endswith("\r\n") else "\n"
    lines = "".join("    " + json.dumps(e["fullName"], ensure_ascii=False) + ": " + str(e["executionOrder"]) + newline
                    for e in sorted(entries, key=lambda e: e["fullName"]))
    replacement = "  executionOrder:" + newline + lines if lines else "  executionOrder: {}" + newline
    match = matches[0]
    return text[:match.start()] + replacement + text[match.end():]


def _static_utility(type_record: dict) -> bool:
    # ECMA-335 TypeAttributes.Abstract | Sealed identifies static classes only
    # with a System.Object base. Do not infer lifecycle eligibility from a name
    # or exclude an abstract/sealed component with an unknown base identity.
    flags = type_record.get("attributes")
    return (type(flags) is int and flags & 0x180 == 0x180 and flags & 0x20 == 0 and
            type_record.get("baseNamespace") == "System" and type_record.get("baseName") == "Object" and
            type_record.get("baseAssemblyName") in ("mscorlib", "System.Runtime", "System.Private.CoreLib"))


def stage_script_orders(project: Path, game: Path, source: Path, cache: Path, dotnet: Path,
                        *, metadata: dict | None = None) -> dict:
    """Before weaving/import, stage all original orders and their exact identity proof.

    The optional metadata value is a test seam; production uses the metadata-only
    original DLL reader. Source DLLs must still match the owned installation.
    """
    bank = game / "globalgamemanagers.assets"
    bank_record = record_file(bank, "globalgamemanagers.assets")
    records = read_script_orders(bank)
    metadata = _managed_types(game, source, cache, dotnet) if metadata is None else metadata
    file_id = _file_id_function(source)
    plugins, guids, plans = {}, {}, []
    for dll in sorted((project / "Assets").rglob("*.dll")):
        original = game / "Managed" / dll.name
        if dll.name not in metadata:
            continue  # Mod/platform DLLs are not original scripts.
        if metadata[dll.name].get("name") != dll.stem:
            raise BuildError("Original script-order assembly name differs from its recovered plugin filename: " + dll.name)
        if dll.name in plugins or dll.is_symlink() or not original.is_file() or digest(dll) != digest(original):
            raise BuildError("Original script-order plugin identity is duplicate, linked or no longer original: " + dll.name)
        meta = Path(str(dll) + ".meta")
        if meta.is_symlink() or not meta.is_file():
            raise BuildError("Original script-order plugin has no recovered importer: " + dll.name)
        text = meta.read_bytes().decode("utf-8")
        hits = re.findall(r"^guid: ([0-9a-f]{32})\r?$", text, re.MULTILINE)
        if len(hits) != 1 or hits[0] in guids:
            raise BuildError("Original script-order plugin has an ambiguous recovered GUID: " + dll.name)
        plugins[dll.name] = (dll, meta, text, hits[0])
        guids[hits[0]] = dll.name
    binding_path = project / "Assets/QuestOriginalStartup/script-bindings.json"
    try:
        bindings = json.loads(binding_path.read_text(encoding="utf-8"))
        disabled = bindings["disabledPluginGuids"]
    except (OSError, ValueError, KeyError, TypeError) as error:
        raise BuildError("Original script-order recovery requires its declared SDK binding manifest.") from error
    if (bindings.get("schema") != 1 or not isinstance(disabled, list) or
            any(not isinstance(guid, str) or not re.fullmatch(r"[0-9a-f]{32}", guid) for guid in disabled) or
            len(disabled) != len(set(disabled))):
        raise BuildError("Original script-order SDK exclusion identities are invalid or duplicated.")
    disabled = set(disabled)
    for _, _, text, guid in plugins.values():
        enabled = re.findall(r"^\s+enabled:\s*([01])\s*$", text, re.MULTILINE)
        if not enabled or (guid in disabled and set(enabled) != {"0"}) or (guid not in disabled and "1" not in enabled):
            raise BuildError("Script-order provider availability disagrees with declared SDK exclusion.")
    assets, references, pointer_guids = [], set(), set()
    for asset in sorted((project / "Assets").rglob("*")):
        if not asset.is_file() or asset.suffix not in (".unity", ".prefab", ".asset", ".controller", ".overrideController", ".anim", ".playable"):
            continue
        if asset.is_symlink():
            raise BuildError("Recovered script-order assets must not be symbolic links.")
        with asset.open("rb") as stream:
            if stream.read(5) != b"%YAML":
                continue
        text = asset.read_text(encoding="utf-8")
        assets.append(record_file(asset, asset.relative_to(project).as_posix()))
        for match in POINTER.finditer(text):
            pointer_guids.add(match.group(2))
            if match.group(2) in guids:
                references.add((guids[match.group(2)], int(match.group(1))))
    unused_disabled = disabled - set(guids)
    if unused_disabled & pointer_guids or any(b.get("oldGuid") in unused_disabled for b in bindings.get("bindings", [])):
        raise BuildError("Declared excluded SDK provider is missing but still referenced in the startup closure.")
    grouped = defaultdict(list)
    for item in records:
        grouped[(item["assembly"], item["namespace"], item["name"])].append(item)
    entries, excluded, by_plugin, mapped = [], [], defaultdict(list), set()
    for (assembly, namespace, name), rows in sorted(grouped.items()):
        if len({r["executionOrder"] for r in rows}) != 1:
            raise BuildError("Conflicting original script execution orders: " + assembly + ":" + name)
        identifier = file_id(namespace, name)
        referenced = (assembly, identifier) in references
        types = metadata.get(assembly, {}).get("types", [])
        matches = [t for t in types if t["namespace"] == namespace and t["name"] == name and not t["nested"]]
        if len(matches) > 1:
            raise BuildError("Ambiguous original type metadata: " + assembly + ":" + name)
        base = {"assemblyName": assembly[:-4], "fullName": namespace + "." + name if namespace else name,
                "executionOrder": rows[0]["executionOrder"], "sourcePathIds": [str(r["sourcePathId"]) for r in rows]}
        if assembly not in plugins or len(matches) != 1:
            if referenced:
                raise BuildError("Referenced original script has no exact retained DLL type: " + assembly + ":" + base["fullName"])
            excluded.append({**base, "reason": "assembly-outside-startup-closure" if assembly not in plugins else
                             "no-original-top-level-type", "referenced": False})
            continue
        identity = (assembly, identifier)
        if identity in mapped:
            raise BuildError("Unity script file-ID collision in original assembly: " + assembly)
        mapped.add(identity)
        plugin = plugins[assembly]
        entry = {**base, "originalGuid": plugin[3], "originalFileId": str(identifier), "referenced": referenced,
                 "pluginPath": plugin[0].relative_to(project).as_posix(),
                 "package": plugin[3] in disabled}
        if _static_utility(matches[0]):
            if referenced:
                raise BuildError("A static original utility cannot be a serialized startup component: " + assembly + ":" + base["fullName"])
            excluded.append({**base, "reason": "original-static-utility", "referenced": False,
                             "typeAttributes": matches[0]["attributes"],
                             "baseAssemblyName": matches[0]["baseAssemblyName"],
                             "baseNamespace": matches[0]["baseNamespace"], "baseName": matches[0]["baseName"],
                             "pluginPath": entry["pluginPath"], "originalAssemblySha256": digest(game / "Managed" / assembly)})
        else:
            entries.append(entry)
        # Keep even a static utility's authored value in its original plugin
        # importer. It has no instantiable Awake/Update target for Editor order
        # verification, but deleting its original importer evidence is unnecessary.
        by_plugin[assembly].append(entry)
    if references - mapped:
        raise BuildError("Recovered startup contains script pointers absent from the original serialized script bank.")
    for assembly, (_, meta, text, _) in plugins.items():
        after = _replace_orders(text, by_plugin[assembly])
        plans.append((meta, after, {"path": meta.relative_to(project).as_posix(), "beforeSha256": digest(meta),
                                   "originalAssemblySha256": digest(game / "Managed" / assembly)}))
    if bank_record != record_file(bank, "globalgamemanagers.assets"):
        raise BuildError("Original script bank changed while staging execution orders.")
    # No project mutation occurs before all source identities/pointers validate.
    for meta, text, _ in plans:
        meta.write_bytes(text.encode("utf-8"))
    manifest = {"schema": 1, "sourceSha256": bank_record["sha256"], "entries": entries, "excluded": excluded}
    write_json(project / MANIFEST, manifest)
    receipt = {"schema": 1, "source": bank_record,
               "sourceRecordCount": len(records), "sourceDistinctCount": len(grouped),
               "orderedRecordCount": sum(r["executionOrder"] != 0 for r in records),
               "manifest": record_file(project / MANIFEST, MANIFEST), "assetIdentityEvidence": assets,
               "unusedDisabledPluginGuids": sorted(unused_disabled),
               "excluded": excluded, "plugins": [{**proof, "afterSha256": digest(meta)} for meta, _, proof in plans],
               "scope": "original startup closure; exact imported scripts verified after SDK remapping"}
    write_json(project / RECEIPT, receipt)
    return receipt
