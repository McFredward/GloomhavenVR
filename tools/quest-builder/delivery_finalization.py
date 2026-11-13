"""Independent, byte-qualified checkpoints for an already compiled Player.

The 075701 retry failed during signing after Unity had finished. A later
failure in the Compute gate must not discard successful APK/graphics gates.
Only small receipts and the exact files read by each validator are stage
outputs; the multi-GB APK/bank stay in their existing final-build witness owner.
"""
from __future__ import annotations

import importlib.metadata
import json
from pathlib import Path
import zipfile


def _row(builder, path):
    path = builder._ordinary_owned(Path(path))
    return {"path": str(path), "sha256": builder.digest(path), "size": path.stat().st_size}


def _file_rows(builder, paths):
    return [_row(builder, path) for path in paths]


def _decoder_versions():
    versions = {}
    for package in ("UnityPy", "PyYAML"):
        try:
            versions[package] = importlib.metadata.version(package)
        except importlib.metadata.PackageNotFoundError:
            versions[package] = None
    return versions


def artifact_rows(builder, output, key, apk, target):
    """Read bytes only without an existing reliable, exact change-stamp proof."""
    with builder.Stages(output)._witnesses("build", key) as witnesses:
        rows = [{"path": apk.relative_to(output).as_posix(),
                 "sha256": witnesses.observe(apk, hasher=builder.digest), "size": apk.stat().st_size}]
        if target != "game":
            return rows
        with zipfile.ZipFile(apk) as archive:
            name = "assets/Quest/content-delivery.json"
            if archive.namelist().count(name) != 1 or archive.getinfo(name).file_size > 65536:
                raise builder.BuildError("Completed Player lacks one bounded content-delivery contract.")
            value = json.loads(archive.read(name))
        files = value.get("files")
        if not isinstance(files, list) or len(files) != 1 or files[0].get("file") != "GloomhavenVR-Quest-content.zip":
            raise builder.BuildError("Completed Player content bank identity is invalid.")
        row = files[0]
        bank = builder._ordinary_owned(apk.parent / row["file"])
        if not witnesses.qualify(bank, row.get("sha256"), row.get("size"), hasher=builder.digest):
            raise builder.BuildError("Complete Campaign content bank changed or is missing.")
        rows.append({"path": bank.relative_to(output).as_posix(), "sha256": row["sha256"], "size": row["size"]})
    return rows


class Gates:
    def __init__(self, builder, output, key, apk, source, project):
        self.builder = builder
        self.output, self.apk, self.source, self.project = map(Path, (output, apk, source, project))
        self.key = key
        self.stages = builder.Stages(self.output)
        self.here = Path(builder.__file__).resolve().parent

    def _identity(self, name, artifacts, code, dependencies, extra=None):
        return {"schema": 1, "owner": "completed-player-final-gate", "gate": name,
                "buildKey": self.key, "artifacts": artifacts,
                "validators": _file_rows(self.builder, [Path(__file__), *code]),
                "dependencies": _file_rows(self.builder, dependencies), "parameters": extra}

    def _run(self, name, identity, receipt, action, *, selected_files=None):
        builder = self.builder
        key = builder.value_hash(identity)
        stage = "delivery-" + name
        dependencies = identity["dependencies"]

        def qualify_artifacts():
            with self.stages._witnesses("build", self.key) as witnesses:
                for row in identity["artifacts"]:
                    path = self.output / row["path"]
                    if not witnesses.qualify(path, row["sha256"], row["size"], hasher=builder.digest):
                        raise builder.BuildError("Delivered Player/content changed during final validation: " + path.name)

        def run():
            result = action()
            # Audits publish their own actual-byte evidence atomically. Never
            # infer success from a receipt left behind by a failed invocation.
            builder.write_json(receipt, result)
            paths = [receipt]
            for row in dependencies:
                path = Path(row["path"])
                if builder.digest(path) != row["sha256"]:
                    raise builder.BuildError("Delivery audit source changed while validated: " + path.name)
                paths.append(path)
            if selected_files is not None:
                paths.extend(selected_files(result))
            qualify_artifacts()
            return paths, {"gate": name, "buildKey": self.key}

        qualify_artifacts()
        retained = self.stages.valid(stage, key)
        if retained:
            print("delivery: reusing completed " + name + " gate", flush=True)
        else:
            self.stages.run(stage, key, run)
        # Read the hashed small evidence, not an unqualified details member in
        # a mutable stage receipt. A corrupt gate receipt reruns only that gate.
        return json.loads(receipt.read_text(encoding="utf-8"))

    def apk_validation(self, inputs, tools, report, provenance, action):
        builder = self.builder
        artifacts = artifact_rows(builder, self.output, self.key, self.apk, inputs["target"])
        identity_path = self.output / "identities" / inputs["profileKey"] / "quest-profile.json"
        dependencies = [identity_path]
        certificate = self.output / "signing/certificate.json"
        code = [self.here / name for name in (
            "builder.py", "storage.py", "native_plugins.py", "installation_manifest.py", "dependencies.py")]
        code.extend(Path(tools[name]) for name in ("java", "apksigner", "aapt"))
        metadata = json.loads(report.read_text(encoding="utf-8"))
        # Driver/retry annotations are host diagnostics, not Unity evidence.
        # A retry changes those fields without changing the signed Player.
        compiler_report = {name: value for name, value in metadata.items() if name != "deliveryDriver"}
        identity = self._identity("apk", artifacts, code, dependencies,
                                  {"inputs": inputs, "tools": tools, "provenance": provenance,
                                   "compilerReport": compiler_report})
        receipt = self.apk.parent / "apk-delivered-validation.json"

        def validate():
            result = action()
            actual = [{"path": self.apk.relative_to(self.output).as_posix(),
                       "sha256": result["apkSha256"], "size": self.apk.stat().st_size}, *result["contentFiles"]]
            if actual != artifacts:
                raise builder.BuildError("Delivered Player/content changed during final validation.")
            return result

        result = self._run("apk", identity, receipt, validate,
                           selected_files=lambda result: [certificate])
        result["buildReport"] = metadata
        return result

    def shaders(self, artifacts, receipt, action):
        dependencies = [self.project / "Assets/QuestOriginalCampaign/campaign-shaders.json",
                        self.project / "Assets/Resources/QuestCampaignShaderVariants.shadervariants.meta"]
        dependencies.extend(self.source / path for path in (
            "tools/quest-recovery/catalog.py", "tools/quest-recovery/recover.py", "tools/quest-recovery/md4.py",
            "tools/quest-builder/progress.py"))
        code = [self.here / name for name in ("campaign_native_shaders.py", "campaign_shaders.py", "storage.py", "dependencies.py")]
        identity = self._identity("shaders", artifacts, code, dependencies,
                                  {"materialSampleCount": 16, "decoders": _decoder_versions()})

        def materials(result):
            paths = []
            for row in result["materials"]:
                relative = row.get("assetPath")
                if (not isinstance(relative, str) or not relative.startswith("Assets/") or "\\" in relative
                        or ":" in relative or any(part in ("", ".", "..") for part in relative.split("/"))):
                    raise self.builder.BuildError("Delivered Shader gate has an invalid source material path.")
                path = self.builder._ordinary_owned(self.project / relative)
                if self.builder.digest(path) != row["sourceSha256"]:
                    raise self.builder.BuildError("Delivered source material changed during graphics validation: " + path.name)
                paths.append(path)
            if len(paths) != result["materialSampleCount"]:
                raise self.builder.BuildError("Delivered Shader material evidence is incomplete.")
            return paths

        return self._run("shaders", identity, receipt, action, selected_files=materials)

    def compute(self, artifacts, receipt, action):
        dependencies = [self.project / "Assets/QuestOriginalCampaign/campaign-computes.json"]
        dependencies.extend(self.source / ("tools/quest-compute/" + name) for name in
                            ("compiled.py", "native.py", "vulkan_compiled.py", "spirv.py"))
        code = [self.here / name for name in ("campaign_compute.py", "campaign_shaders.py", "storage.py", "dependencies.py")]
        identity = self._identity("compute", artifacts, code, dependencies, {"decoders": _decoder_versions()})
        return self._run("compute", identity, receipt, action)
