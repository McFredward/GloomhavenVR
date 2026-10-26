"""Exercise real Bash archive code with tiny valid companions and causal controls."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET
import zipfile

PREFIX = 'BepInEx/plugins/GloomhavenVR/MenuDepth/'


def verify_project_items(source: Path, root: Path, evidence: Path, execute, check) -> dict:
    """Build the exact bounded production item declarations with the real SDK."""
    dotnet = shutil.which('dotnet')
    if dotnet is None and (candidate := Path.home() / '.dotnet/dotnet').is_file():
        dotnet = str(candidate)
    if dotnet is None:
        raise RuntimeError('A real .NET SDK is required to verify menu depth output/resource items')
    production = ET.parse(source / 'src/GloomhavenVR/GloomhavenVR.csproj').getroot()
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    properties = ET.SubElement(project, 'PropertyGroup')
    for name, value in [('OutputType', 'Exe'), ('TargetFramework', 'net8.0'),
                        ('AssemblyName', 'MenuDepthItemProof'), ('EnableDefaultCompileItems', 'false'),
                        ('GenerateDocumentationFile', 'false'), ('TreatWarningsAsErrors', 'true')]:
        ET.SubElement(properties, name).text = value
    group = ET.SubElement(project, 'ItemGroup')
    declarations = []
    for item in production.findall('.//Content') + production.findall('.//EmbeddedResource'):
        if item.get('Include', '').startswith('Assets\\MenuDepth\\'):
            group.append(item)
            declarations.append(ET.tostring(item, encoding='unicode'))
    ET.SubElement(group, 'Compile', Include='MenuDepthItemProgram.cs')
    folder = root / 'src/GloomhavenVR'
    filename = folder / 'MenuDepthItemProof.csproj'
    ET.ElementTree(project).write(filename, encoding='utf-8', xml_declaration=True)
    (folder / 'MenuDepthItemProgram.cs').write_text(
        'using System; using System.Reflection;\n'
        'static class ItemProof { static int Main() { var a = Assembly.GetExecutingAssembly();\n'
        'using var s = a.GetManifestResourceStream("GloomhavenVR.MenuDepth.Rendering.bundle");\n'
        'if (s == null || s.Length != 23 || a.GetManifestResourceNames().Length != 1) return 1;\n'
        'Console.WriteLine("Exact menu depth resource identity and bytes present."); return 0; } }\n')
    bundle = folder / 'Assets/MenuDepth/Rendering/menu-depth.bundle'
    bundle.parent.mkdir(parents=True, exist_ok=True)
    bundle.write_bytes(b'opaque renderer fixture')
    environment = dict(os.environ, MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
    code, log = execute([dotnet, 'build', str(filename), '-c', 'Release', '--nologo', '-v', 'quiet',
                         '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false'],
                        evidence / 'depth-project-build.log', environment)
    check(code == 0, 'Real SDK builds exact production Content/EmbeddedResource declarations: ' + log)
    output = folder / 'bin/Release/net8.0'
    payloads = json.loads((folder / 'Assets/MenuDepth/Story/manifest.json').read_bytes())['images']
    expected = ['Story/manifest.json'] + ['Story/' + item['depth_file'] for item in payloads]
    actual = {path.relative_to(output / 'MenuDepth').as_posix() for path in (output / 'MenuDepth').rglob('*') if path.is_file()}
    check(actual == set(expected), 'Actual MSBuild output has exactly 516 external relief companions')
    for name in expected:
        check((output / 'MenuDepth' / name).read_bytes() == (folder / 'Assets/MenuDepth' / name).read_bytes(),
              'Actual MSBuild copies exact native companion: ' + name)
    code, log = execute([dotnet, str(output / 'MenuDepthItemProof.dll')], evidence / 'depth-project-run.log', environment)
    check(code == 0 and 'Exact menu depth resource identity' in log, 'Actual SDK assembly embeds only renderer under exact runtime resource name')
    retained = evidence / 'actual-content-proof-artifact'
    retained.mkdir()
    for path in output.glob('MenuDepthItemProof.*'):
        if path.is_file():
            shutil.copyfile(path, retained / path.name)
    return {'production_item_declarations': declarations,
            'executed_artifact_sha256': hashlib.sha256((retained / 'MenuDepthItemProof.dll').read_bytes()).hexdigest(),
            'external_companions': len(expected),
            'scope': 'Actual SDK builds the unchanged bounded production item declarations using tiny companion/bundle substitutes. Full plugin compile and real bundle behavior remain integration checks.'}


def verify_bash(root: Path, evidence: Path, package, check, payloads: dict[str, bytes]) -> dict:
    source = root / 'src/GloomhavenVR/Assets/MenuDepth'
    roster = root / 'packaging/menu-depth-files.txt'
    script = root / 'scripts/package-release.sh'
    archive = root / 'dist/GloomhavenVR-0.0.0-test.zip'
    code, log = package('depth-complete-baseline')
    check(code == 0, log)
    original_archive = archive.read_bytes()
    with zipfile.ZipFile(archive) as package_zip:
        actual = {name for name in package_zip.namelist() if name.startswith(PREFIX) and not name.endswith('/')}
        check(actual == {PREFIX + name for name in payloads}, 'Bash ZIP has exactly the 516 finite menu depth companions')
        for name, data in payloads.items():
            check(package_zip.read(PREFIX + name) == data, 'Bash ZIP contains exact depth companion: ' + name)
    fields = sorted(name for name in payloads if name.endswith('.gmd'))
    controls = [fields[0], fields[len(fields) // 2], fields[-1], 'Story/manifest.json']
    for index, name in enumerate(controls):
        path = source / name
        path.unlink()
        try:
            code, log = package('depth-missing-' + str(index))
            check(code != 0 and 'Missing required menu depth companion: ' + name in log, 'Bash refuses missing required companion: ' + name)
            check(archive.read_bytes() == original_archive, 'Missing companion preserves previous ZIP: ' + name)
        finally:
            path.write_bytes(payloads[name])

    manifest = source / 'Story/manifest.json'
    document = json.loads(payloads['Story/manifest.json'])
    changed = json.loads(payloads['Story/manifest.json'])
    changed['images'][0]['depth_file'] = 'f' * 32 + '.gmd'
    variants = [('manifest-roster-mismatch', manifest, (json.dumps(changed) + '\n').encode(), 'manifest and roster disagree')]
    changed = json.loads(payloads['Story/manifest.json'])
    changed['images'][1] = changed['images'][0]
    variants.append(('manifest-duplicate', manifest, (json.dumps(changed) + '\n').encode(), 'duplicate menu depth source identity'))
    lines = roster.read_text()
    variants.append(('roster-path-escape', roster, lines.replace(fields[0], 'Story/../escape.gmd').encode(), 'Invalid menu depth roster path'))
    variants.append(('roster-duplicate', roster, lines.replace(fields[1], fields[0]).encode(), 'exactly 516 distinct companions'))
    changed = json.loads(payloads['Story/manifest.json'])
    changed['images'][0]['depth_dimensions'] = [513, 2]
    variants.append(('manifest-axis-cap', manifest, (json.dumps(changed) + '\n').encode(), 'Invalid menu depth dimensions'))
    changed = json.loads(payloads['Story/manifest.json'])
    changed['images'][0]['source_dimensions'] = [16385, 8]
    variants.append(('manifest-source-axis-cap', manifest, (json.dumps(changed) + '\n').encode(), 'Invalid menu depth dimensions'))
    variants.append(('corrupt-source', source / fields[0], payloads[fields[0]][:-1] + b'\xff', 'size/checksum mismatch'))
    for label, path, replacement, expected in variants:
        original = path.read_bytes()
        path.write_bytes(replacement)
        try:
            code, log = package('depth-' + label)
            check(code != 0 and expected in log, 'Bash rejects ' + label + ': ' + log)
            check(archive.read_bytes() == original_archive, 'Invalid companions preserve previous ZIP: ' + label)
        finally:
            path.write_bytes(original)
    check(json.loads(manifest.read_bytes()) == document, 'All original fixture manifest bindings are restored')

    original_script = script.read_text()
    needle = 'cp "$MENU_DEPTH_ROOT/$companion" "$PLUGDIR/MenuDepth/$companion"'
    check(original_script.count(needle) == 1, 'Depth causal controls bind the actual production staging copy')
    for index, name in enumerate(controls):
        replacement = 'if [[ "$companion" != "' + name + '" ]]; then ' + needle + '; fi'
        script.write_text(original_script.replace(needle, replacement))
        try:
            code, log = package('depth-omitted-copy-' + str(index))
            check(code != 0 and 'Missing required menu depth companion: ' + name in log, 'Real Bash ZIP validator rejects omitted staged companion: ' + name)
        finally:
            script.write_text(original_script)
    name = fields[0]
    script.write_text(original_script.replace(needle, needle + '\n    if [[ "$companion" == "' + name + '" ]]; then printf x >> "$PLUGDIR/MenuDepth/$companion"; fi'))
    try:
        code, log = package('depth-corrupted-stage')
        check(code != 0 and 'size/checksum mismatch' in log, 'Real ZIP validator rejects corrupt staged field bytes')
    finally:
        script.write_text(original_script)
    code, log = package('depth-restored-complete')
    check(code == 0, log)
    for name, data in payloads.items():
        check((source / name).read_bytes() == data, 'Archive controls preserve original source companion: ' + name)
    return {'companions': len(payloads), 'missing_source_controls': len(controls),
            'missing_staged_controls': len(controls), 'invalid_set_controls': len(variants),
            'corrupt_staged_controls': 1,
            'fixture_payload_sha256': {name: hashlib.sha256(data).hexdigest() for name, data in payloads.items()}}
