#!/usr/bin/env python3
"""Compile and execute real OpenXR composition/lifecycle tests with defect controls."""
import argparse
import importlib.util
from pathlib import Path
import shutil
import subprocess
import tempfile
try:
    import resource
except ImportError:
    resource = None

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT / ".planning/debug/quest-tools/openxr-headers")
    args = parser.parse_args()
    if resource is not None:
        resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    specification = importlib.util.spec_from_file_location("quest_native_build", ROOT / "scripts/build-quest-native.py")
    native = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(native)
    headers = native.fetch_headers(args.cache)
    compiler = shutil.which("c++")
    if not compiler:
        raise RuntimeError("The native test requires a host C++17 compiler")
    source = ROOT / "tools/quest-native/passthrough.cpp"
    fixture = ROOT / "tests/quest-native/PassthroughTests.cpp"
    with tempfile.TemporaryDirectory(prefix="quest-native-tests-") as folder:
        scratch = Path(folder)
        def compile_and_run(contents, label):
            path = scratch / (label + ".cpp")
            path.write_text(contents, encoding="utf-8")
            executable = scratch / label
            subprocess.run([compiler, "-std=c++17", "-Wall", "-Wextra", "-Werror",
                            "-Wno-missing-field-initializers", "-pthread", "-I", str(headers),
                            str(path), str(fixture), "-o", str(executable)], check=True)
            return subprocess.run([str(executable)], capture_output=True, text=True)
        contents = source.read_text(encoding="utf-8")
        result = compile_and_run(contents, "production")
        print(result.stdout, end="")
        if result.returncode:
            raise RuntimeError("Production native fixture failed: " + result.stderr)
        mutation = "projections.back().layerFlags |= XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;"
        if contents.count(mutation) != 1:
            raise RuntimeError("Alpha defect-control seam changed")
        control = compile_and_run(contents.replace(mutation, "// Deliberately missing projection alpha."), "missing-alpha")
        if control.returncode == 0:
            raise RuntimeError("Fixture failed to detect missing projection alpha")
        print("Quest native negative control: omitted projection alpha was detected.")


if __name__ == "__main__":
    main()
