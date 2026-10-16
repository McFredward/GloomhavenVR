#!/usr/bin/env python3
"""Execute the real release packager with small substitute build/assets, then inspect its ZIP."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile


def main():
    source = Path(sys.argv[1])
    with tempfile.TemporaryDirectory(prefix="ghvr-movie-package-") as directory:
        root = Path(directory)

        def write(name, data):
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            return path

        for name in ("package-release.sh", "check-package-text.py", "check-frame-launchers.py", "check-figure-mesh-bank.py"):
            write("scripts/" + name, (source / "scripts" / name).read_bytes())
        # The native MOV is opaque to packaging. Real video encode/decoder evidence belongs
        # to media QC; only the expensive compiler and other binary assets are substituted.
        payload = b"approved mod-local intro\0\xff\x11"
        movie = write("src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.mov", payload)
        write("src/GloomhavenVR/GloomhavenVR.csproj", b"<Project><PropertyGroup><Version>0.0.0-test</Version></PropertyGroup></Project>\n")
        write("src/GloomhavenVR/bin/Release/net472/GloomhavenVR.dll", b"plugin substitute")
        write("src/GloomhavenVR.Preload/bin/Release/net472/GloomhavenVR.Preload.dll", b"preloader substitute")
        for name in ("UnityOpenXR.dll", "openxr_loader.dll"):
            write("libs/Natives/" + name, b"native substitute")
        write("libs/RuntimeDeps/Unity.XR.OpenXR.dll", b"dependency substitute")
        for name in ("gloomhavenvr.bundle", "ghvr-town.bundle", "ghvr-town-voices.bundle", "ghvr-environment.bundle"):
            write("prebuilt/" + name, b"asset substitute")
        part = "ghvr-figure-meshes-5-00.bundle"
        write("prebuilt/" + part, b"UnityFS\0" + (7).to_bytes(4, "big") + b"5.x.x\0" + b"2021.3.5f1\0")
        write("prebuilt/ghvr-figure-meshes-index.json", json.dumps({"entries": [{
            "mesh": "figure-0000000000000000-5", "bank": part}]}).encode())
        for name in ("LICENSE", "packaging/INSTALL.txt.in", "packaging/INSTALL.de.txt.in", "packaging/THIRD-PARTY.txt", "packaging/licenses/SOURCES.txt"):
            write(name, b"Fixture notice @VERSION@.\n")
        for name in ("install-steam-frame.sh", "steam-frame-config.py", "frame-boot-config.py"):
            write("scripts/" + name, (source / "scripts" / name).read_bytes())
        write("GloomhavenVR-Setup.desktop", (source / "GloomhavenVR-Setup.desktop").read_bytes())
        for name in ("library_600x900.png", "library_header.png", "library_hero.png", "logo.png", "icon.png"):
            write("src/GloomhavenVR/Assets/SteamFrameArtwork/" + name, b"\x89PNG\r\n\x1a\nfixture")
        compiler = write("tools/dotnet", b"#!/usr/bin/env bash\nexit 0\n")
        compiler.chmod(0o755)
        environment = dict(os.environ, PATH=str(compiler.parent) + os.pathsep + os.environ["PATH"], GHVR_USE_LOCAL_BUNDLE="0")

        def run():
            return subprocess.run(["bash", str(root / "scripts/package-release.sh")],
                                  env=environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)

        result = run()
        if result.returncode != 0:
            raise AssertionError(result.stdout)
        archive_path = root / "dist/GloomhavenVR-0.0.0-test.zip"
        with zipfile.ZipFile(archive_path) as archive:
            expected = "BepInEx/plugins/GloomhavenVR/Movies/CP_Intro/GH_CP_Intro.mov"
            assert archive.read(expected) == payload, "release ZIP must carry exact movie bytes beside plugin"
            assert [name for name in archive.namelist() if name.endswith(".mov")] == [expected]
            assert all("StreamingAssets" not in name for name in archive.namelist()), "release must never overwrite game movies"
        assert movie.read_bytes() == payload, "packager must leave movie source unchanged"
        movie.unlink()
        result = run()
        assert result.returncode != 0 and "missing approved campaign intro" in result.stdout, result.stdout
        assert archive_path.exists(), "missing movie preflight must preserve existing package"
        print("Native movie packaging: exact plugin-local ZIP bytes verified; missing-asset negative control rejected.")
        shell = shutil.which("pwsh")
        if not shell:
            candidate = Path.home() / ".dotnet/tools/pwsh"
            if candidate.is_file():
                shell = str(candidate)
        if shell:
            movie.write_bytes(payload)
            shell_environment = dict(os.environ)
            if "DOTNET_ROOT" not in shell_environment and (compiler_runtime := shutil.which("dotnet")):
                shell_environment["DOTNET_ROOT"] = str(Path(compiler_runtime).resolve().parent)
            result = subprocess.run([shell, "-NoProfile", "-NonInteractive", "-File",
                                     str(source / "tests/GloomhavenVR.NativeVideoTests/MovieInstallerTests.ps1"),
                                     "-Installer", str(source / "scripts/install.ps1"), "-Fixture", str(root)],
                                    env=shell_environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            if result.returncode != 0:
                raise AssertionError(result.stdout)
            print(result.stdout.strip())
        else:
            print("Native movie PowerShell installer: pwsh unavailable; deployment execution not checked on this host.")


if __name__ == "__main__":
    main()
