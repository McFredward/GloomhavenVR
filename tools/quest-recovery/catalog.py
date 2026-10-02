#!/usr/bin/env python3
"""Read the original Addressables 1.19.19 compact catalog without game code.

The binary layout is verified against Unity's official package source,
Runtime/ResourceLocators/ContentCatalogData.cs and SerializationUtilities.cs.
This produces provenance/dependency associations, not a rebuilt Android catalog.
"""
import argparse
import base64
import json
from pathlib import Path
import re
import struct
import sys

from recover import GUID_PATTERN, RecoveryError, sha256, write_json


def integer(data, offset):
    if offset < 0 or offset + 4 > len(data):
        raise RecoveryError("Truncated original Addressables integer field.")
    return struct.unpack_from("<i", data, offset)[0]


def object_key(data, offset):
    if offset < 0 or offset >= len(data):
        raise RecoveryError("Original Addressables key points outside key data.")
    kind = data[offset]
    offset += 1
    if kind in (0, 1):
        length = integer(data, offset)
        offset += 4
        if length < 0 or offset + length > len(data) or (kind == 1 and length % 2):
            raise RecoveryError("Truncated/malformed Addressables string key.")
        return data[offset:offset + length].decode("ascii" if kind == 0 else "utf-16-le")
    if kind in (2, 3, 4):
        size, format_string = (2, "<H") if kind == 2 else (4, "<I" if kind == 3 else "<i")
        if offset + size > len(data):
            raise RecoveryError("Truncated Addressables numeric key.")
        return struct.unpack_from(format_string, data, offset)[0]
    if kind == 5:
        if offset >= len(data) or offset + 1 + data[offset] > len(data):
            raise RecoveryError("Truncated Addressables Hash128 key.")
        return data[offset + 1:offset + 1 + data[offset]].decode("ascii")
    # Do not resolve arbitrary COM types or instantiate JSON classes while
    # inventorying the player's catalog. Unsupported key kinds fail explicitly.
    raise RecoveryError(f"Unsupported original Addressables key kind {kind}; no runtime code is executed.")


def decode_catalog(catalog):
    try:
        bucket_data = base64.b64decode(catalog["m_BucketDataString"], validate=True)
        key_data = base64.b64decode(catalog["m_KeyDataString"], validate=True)
        entry_data = base64.b64decode(catalog["m_EntryDataString"], validate=True)
    except (KeyError, ValueError) as error:
        raise RecoveryError("Invalid original compact Addressables fields.") from error
    bucket_count = integer(bucket_data, 0)
    key_count = integer(key_data, 0)
    entry_count = integer(entry_data, 0)
    if not 0 <= bucket_count <= 1_000_000 or key_count != bucket_count:
        raise RecoveryError("Original Addressables bucket/key counts do not agree.")
    if not 0 <= entry_count <= 1_000_000 or len(entry_data) != 4 + 28 * entry_count:
        raise RecoveryError("Original Addressables entry count/record width is invalid.")
    keys, buckets = [], []
    offset = 4
    for _ in range(bucket_count):
        data_index = integer(bucket_data, offset)
        count = integer(bucket_data, offset + 4)
        offset += 8
        if count < 0 or offset + count * 4 > len(bucket_data):
            raise RecoveryError("Original Addressables bucket entry list is invalid.")
        entries = [integer(bucket_data, offset + index * 4) for index in range(count)]
        if any(index < 0 or index >= entry_count for index in entries):
            raise RecoveryError("Original Addressables bucket points outside entry table.")
        keys.append(object_key(key_data, data_index))
        buckets.append(entries)
        offset += count * 4
    if offset != len(bucket_data):
        raise RecoveryError("Original Addressables bucket data has an unexpected trailing layout.")
    locations = []
    internal_ids = catalog["m_InternalIds"]
    providers = catalog["m_ProviderIds"]
    prefixes = catalog.get("m_InternalIdPrefixes") or []
    resources = catalog.get("m_resourceTypes") or []
    for index in range(entry_count):
        internal, provider, dependency, dependency_hash, extra, primary, resource = struct.unpack_from("<7i", entry_data, 4 + index * 28)
        if (not 0 <= internal < len(internal_ids) or not 0 <= provider < len(providers)
                or not 0 <= primary < len(keys) or dependency < -1 or dependency >= len(keys)
                or not 0 <= resource < len(resources)):
            raise RecoveryError("Original Addressables entry index is invalid.")
        internal_id = internal_ids[internal]
        if prefixes and "#" in internal_id:
            prefix, suffix = internal_id.rsplit("#", 1)
            if prefix.isdigit():
                prefix_index = int(prefix)
                if prefix_index >= len(prefixes):
                    raise RecoveryError("Original Addressables internal ID prefix is invalid.")
                internal_id = prefixes[prefix_index] + suffix
        locations.append({"index": index, "internalId": internal_id,
                          "provider": providers[provider], "primaryKey": keys[primary],
                          "resourceType": resources[resource],
                          "dependencyEntries": [] if dependency == -1 else buckets[dependency],
                          "dependencyHash": dependency_hash, "extraDataOffset": extra})
    return {"keys": keys, "buckets": buckets, "locations": locations}


def bundle_closure(decoded, entry_indices):
    pending = list(entry_indices)
    visited, bundles = set(), set()
    while pending:
        index = pending.pop()
        if index in visited:
            continue
        visited.add(index)
        location = decoded["locations"][index]
        pending.extend(location["dependencyEntries"])
        internal = location["internalId"].replace("\\", "/")
        if internal.endswith(".bundle"):
            prefix = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/"
            if not internal.startswith(prefix):
                raise RecoveryError("Bundle closure contains a non-local original bundle ID: " + internal)
            bundles.add("StreamingAssets/aa/" + internal[len(prefix):])
    return sorted(bundles)


def build_associations(catalog_path, recovered_project):
    catalog_path = Path(catalog_path)
    project = Path(recovered_project).resolve()
    decoded = decode_catalog(json.loads(catalog_path.read_text(encoding="utf-8-sig")))
    associations = []
    for key, indices in zip(decoded["keys"], decoded["buckets"]):
        if not isinstance(key, str) or not re.fullmatch(r"[0-9a-f]{32}", key):
            continue
        for index in indices:
            location = decoded["locations"][index]
            original_path = location["internalId"].replace("\\", "/")
            if not original_path.startswith("Assets/"):
                continue
            target = (project / original_path).resolve()
            if project not in target.parents:
                raise RecoveryError("Original asset path escapes recovered project.")
            metadata = target.with_name(target.name + ".meta")
            target_guid = None
            if target.is_file() and metadata.is_file():
                match = GUID_PATTERN.search(metadata.read_text(encoding="utf-8"))
                if match:
                    target_guid = match[1]
            associations.append({"originalKey": key, "originalAssetPath": original_path,
                                 "entryIndex": index, "provider": location["provider"],
                                 "resourceType": location["resourceType"],
                                 "recoveredAssetPath": original_path if target_guid else None,
                                 "recoveredGuid": target_guid,
                                 "recoveredSha256": sha256(target) if target_guid else None,
                                 "requiredOriginalBundles": bundle_closure(decoded, [index]),
                                 "status": "exact-source-path-associated" if target_guid else "not-recovered-at-original-path"})
    return {"schema": 1, "originalCatalogSha256": sha256(catalog_path),
            "keyCount": len(decoded["keys"]), "locationCount": len(decoded["locations"]),
            "associations": associations,
            "exactAssociationCount": sum(x["recoveredGuid"] is not None for x in associations),
            "androidCatalogBuilt": False,
            "note": "Retain original keys as aliases in rebuilt Android locations; no filename-only matches."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--project", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        report = build_associations(args.catalog, args.project)
        write_json(args.output, report)
        print("[Quest catalog] Exact source-path associations:", report["exactAssociationCount"],
              "of", len(report["associations"]), "original asset keys.")
        return 0
    except (RecoveryError, OSError, ValueError, KeyError) as error:
        print("[Quest catalog] FAILED:", error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
