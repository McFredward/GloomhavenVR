#!/usr/bin/env python3
"""Source-bound original menu illustration mesh, GPU and typed native binding proof."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers


def execute(argv, log, timeout=240):
    with log.open('w') as stream:
        process = subprocess.Popen(argv, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=timeout)
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()


def replace_once(code, before, after):
    assert code.count(before) == 1, (before, code.count(before))
    return code.replace(before, after, 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    output = ROOT / '.planning/debug/menu-depth-graphic'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n'); print('Evidence: ' + str(run), flush=True)
    status = 1
    try:
        source = ROOT / 'src/GloomhavenVR/WorldUI/MenuDepth'
        names = ('IMenuDepthSource.cs', 'MenuDepthEye.cs', 'MenuDepthSources.cs', 'MenuDepthProjection.cs',
                 'MenuDepthDisplay.cs', 'MenuDepthHeightfield.cs', 'MenuDepthGraphic.cs', 'MenuDepthCatalogue.cs', 'MenuDepthDriver.cs')
        sources = {name: (source / name).read_text() for name in names}
        fixture = ROOT / 'tests/GloomhavenVR.MenuDepthGraphicTests'
        cases = [
            ('production-corpus', '', '', '', '', 'Corpus'),
            ('production', '', '', '', ''),
            ('old-json-array-parser', 'MenuDepthCatalogue.cs',
             'using (Stream input = File.OpenRead(file))\n                manifest = new DataContractJsonSerializer(typeof(Manifest),\n                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 100000 }).ReadObject(input) as Manifest;',
             'manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(file));',
             'the complete shipped original-image manifest loads through the actual production parser', 'Corpus'),
            ('flat-relief', 'MenuDepthGraphic.cs', 'float depth = _image.rectTransform.rect.width * _depthRatio;',
             'float depth = 0f;', 'prepared depth really gives the original native Image nonzero spatial relief'),
            ('lost-native-uv', 'MenuDepthGraphic.cs', '_field.Sample(vertex.uv0.x, vertex.uv0.y)',
             '_field.Sample(u, v)', 'relief samples the unchanged native sprite subrect UV in its full original texture'),
            ('lost-eye-projection', 'MenuDepthGraphic.cs', 'return MenuDepthProjection.ProjectToPlane(point, _eye);',
             'return point;', 'actual root relief renders distinct tracked eye views through the original native Image'),
            ('stale-native-tint', 'MenuDepthGraphic.cs',
             '|| a.tangent != b.tangent || a.color.r != b.color.r || a.color.g != b.color.g\n                || a.color.b != b.color.b || a.color.a != b.color.a',
             '|| a.tangent != b.tangent', 'native animated vertex tint and alpha invalidate the relief mesh cache'),
            ('flat-decoration', 'MenuDepthGraphic.cs',
             'float depth = Mathf.Min(_image.rectTransform.rect.width, _image.rectTransform.rect.height) * _depthRatio;',
             'float depth = 0f;', 'decorative native layers keep their exact mesh and receive the configured shallow plane separation'),
            ('unbound-native-story', 'MenuDepthDriver.cs', 'new("StoryImageViewer", "imageHolder", Role.Narrative, .03f)',
             'new("MissingOriginalStoryType", "imageHolder", Role.Narrative, .03f)',
             'actual driver discovers the real GH.Runtime StoryImageViewer typed image field'),
            ('async-depth-lost', 'MenuDepthDriver.cs', 'art = binding.Artwork; // The original old sprite is still visible during its async replacement.',
             'return; // injected: visible old sprite loses its matching relief',
             'an async new story key retains the corresponding old depth while its original old colour remains visible'),
            ('lost-native-fallback', 'MenuDepthDriver.cs',
             'return configs[0].id; // Same original fallback as UIInfoTools.GetNarrativeImageConfig.',
             'return string.Empty;', 'actual driver follows original native first-config fallback for an unknown story identity'),
            ('unchecked-depth-hash', 'MenuDepthCatalogue.cs', 'if (actual != expected.depth_sha256)',
             'if (actual.Length == 0)', 'a length-correct CRC-correct companion with the wrong declared SHA is refused'),
        ]
        if args.case:
            selected = set(args.case); assert selected <= {item[0] for item in cases}
            cases = [item for item in cases if item[0] in selected]
        game = ROOT / 'ressources/GH_Data/Managed'
        harmony = Path('/home/claw/.nuget/packages/lib.harmony/2.4.2/lib/net472')
        bundle = ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle'
        corpus = ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Story'
        corpus_data = json.loads((corpus / 'manifest.json').read_text()) if any(len(case) == 6 for case in cases) else None
        manifest = {'evidence': str(run), 'result': str(run / 'results.txt'), 'gameManaged': str(game),
                    'harmonyRuntime': str(harmony), 'cases': []}
        project = run / 'unity'
        for directory in ('Assets/Editor', 'Packages', 'ProjectSettings'): (project / directory).mkdir(parents=True)
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0'}}) + '\n')
        shutil.copyfile(Path(__file__).parent / 'Editor/Runner.cs', project / 'Assets/Editor/Runner.cs')
        pins = [*(source / name for name in names), *fixture.glob('*'), Path(__file__),
                Path(__file__).parent / 'Editor/Runner.cs', ROOT / 'scripts/menu-depth-character/project_compilers.py',
                bundle, game / 'GH.Runtime.dll', game / 'UnityEngine.UI.dll']
        if corpus_data:
            pins += [corpus / 'manifest.json', *(corpus / entry['depth_file'] for entry in corpus_data['images'])]
            shutil.copyfile(corpus / 'manifest.json', run / 'shipped-original-manifest.json')
        hashes = {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in pins if path.is_file()}
        (run / 'source-pins.json').write_text(json.dumps({'sha256': hashes,
            'boundaries': 'Surrounding VR session/config, active-panel list and unrelated character hook are fixture inputs. Actual original GH.Runtime StoryImageViewer/UITab fields, actual production Driver/Catalogue/Heightfield/Graphic, actual Sprite.Create subrect/native uGUI UV generation, native masks/fades, camera rendering and shipping shader execute.',
            'limits': 'Synthetic original artwork/depth, actual native subrect sprite rather than publisher fractional trim/packing metadata, explicit eye inputs and a source-bound active-panel list. No native game prefab/addressable loading, actual story art, D3D11, XR/headset picture, multiplayer or comfort claim.'}, indent=2) + '\n')
        for item in cases:
            name, filename, before, after, expected = item[:5]
            method = item[5] if len(item) == 6 else 'Run'
            case = run / name; bound = case / 'production'; bound.mkdir(parents=True)
            for actual, code in sources.items():
                if actual == filename: code = replace_once(code, before, after)
                (bound / actual).write_text(code)
            csproj = case / 'MenuDepthGraphic.csproj'; shutil.copyfile(fixture / csproj.name, csproj)
            assembly = 'MenuDepthGraphic_' + name.replace('-', '_')
            argv = ['/home/claw/.dotnet/dotnet', 'build', str(csproj), '-c', 'Release', '-v', 'quiet', '--nologo',
                    '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                    '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(bound),
                    '-p:UnityManaged=/home/claw/unity-2021.3.5/Editor/Data/Managed', '-p:GameManaged=' + str(game),
                    '-p:DepthBundle=' + str(bundle)]
            if execute(argv, case / 'build.log'):
                raise RuntimeError('Compilation failure is not a control pass: ' + str(case))
            retained = run / 'assemblies' / name / (assembly + '.dll'); retained.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(case / 'bin/Release/net472' / retained.name, retained)
            if method == 'Corpus':
                staged = retained.parent / 'MenuDepth/Story'; staged.mkdir(parents=True)
                shutil.copyfile(corpus / 'manifest.json', staged / 'manifest.json')
                for entry in corpus_data['images']:
                    # Mono FileInfo.Length reports a symlink's own byte length.
                    # Hardlinks retain real-file packaging semantics without a
                    # second regenerable copy of the 515 prepared companions.
                    os.link(corpus / entry['depth_file'], staged / entry['depth_file'])
                counts = {'fields': len(corpus_data['images']), 'narratives': len(corpus_data['narrative_configs']),
                          'artwork': len(corpus_data['artwork']),
                          'defaults': len({a['native_field'] for a in corpus_data['artwork'] if a.get('native_field')}),
                          'guids': len({a['addressable_guid'].lower() for a in corpus_data['artwork'] if a.get('addressable_guid')})}
                (retained.parent / 'expected-corpus.json').write_text(json.dumps(counts, indent=2) + '\n')
            manifest['cases'].append({'name': name, 'dll': str(retained), 'expected': expected, 'method': method})
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        argv = ['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-glcore',
                '-projectPath', str(project), '-executeMethod', 'MenuDepthGraphicRunner.Start',
                '-menuDepthGraphicManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')]
        (run / 'command.json').write_text(json.dumps(argv, indent=2) + '\n')
        status = execute(argv, run / 'launch.log', 420)
        (run / 'exit-code.txt').write_text(str(status) + '\n')
        print((run / 'results.txt').read_text() if (run / 'results.txt').exists() else 'No Unity results', flush=True)
        stable = all(hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == sha for path, sha in hashes.items())
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(hashes)}) + '\n')
        if not stable:
            status = 1
            raise RuntimeError('Pinned sources changed during the proof')
        return status
    finally:
        compilers = cleanup_project_compilers(run / 'unity', editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']:
            raise RuntimeError('Project compiler survived reaping; its generated caches are retained.')
        cleanup = cleanup_completed_run('menu-depth-graphic', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__': raise SystemExit(main())
