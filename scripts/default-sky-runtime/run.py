#!/usr/bin/env python3
"""Run the complete original default-sky lifecycle with a declared GL shader port.

The packaged native Windows bank is rebuilt/audited and loaded by actual Unity.
Its original shader programs are not executable in the Linux GL editor. Only
that graphics-API boundary is replaced for the render/lifecycle cases; headset,
original D3D11 fragment appearance and complete MR admission are not certified.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import select
import time
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers, cleanup_project_editors


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def cleanup_run_compilers(run, *, editor_reaped=False):
    """Retire only VBCSCompiler processes still borrowing this completed run.

    Unity Bee can launch Roslyn outside the editor process group with a global
    working directory and PWD. An open private Bee descriptor is the ownership
    proof in that case. A compiler with another private run open is left alone.
    pidfds bind signals to the rechecked process; no global daemon shutdown runs.
    """
    if editor_reaped is not True:
        raise ValueError('Reap the private editor before its compiler cleanup.')
    run = str(Path(run).absolute())
    if not hasattr(os, 'pidfd_open') or not hasattr(signal, 'pidfd_send_signal'):
        raise RuntimeError('Private compiler cleanup requires Linux pidfds.')
    result = {'run': run, 'editor_reaped': True, 'matched': [], 'refused': [],
              'term_sent': [], 'kill_sent': [], 'remaining': []}
    handles = {}

    def inside(path):
        return path == run or path.startswith(run + '/')

    def ownership(proc):
        try:
            if proc.stat().st_uid != os.geteuid():
                return None
            args = [a for a in (proc / 'cmdline').read_bytes().split(b'\0') if a]
            compilers = {b'VBCSCompiler.dll', b'VBCSCompiler.exe'}
            if not args or args[0].rsplit(b'/', 1)[-1] not in {b'dotnet', b'mono', b'mono-sgen', *compilers}:
                return None
            if not any(a.rsplit(b'/', 1)[-1] in compilers for a in args):
                return None
            cwd = os.readlink(proc / 'cwd').removesuffix(' (deleted)')
            files = []
            for fd in (proc / 'fd').iterdir():
                try:
                    files.append(os.readlink(fd).removesuffix(' (deleted)'))
                except FileNotFoundError:
                    continue
            owned = [path for path in [cwd, *files] if inside(path)]
            if not owned:
                return None
            other = [path for path in [cwd, *files]
                     if '/.planning/debug/' in path and not inside(path)]
            if other:
                return {'private_paths': owned, 'conflicting_private_paths': sorted(set(other))}
            return {'private_paths': sorted(set(owned)), 'conflicting_private_paths': []}
        except (OSError, ValueError):
            return None  # Exit/race/unknown ownership must never authorize a signal.

    def wait(pending, seconds):
        deadline = time.monotonic() + seconds
        while pending:
            ready, _, _ = select.select(pending, [], [], max(0, deadline - time.monotonic()))
            pending = [fd for fd in pending if fd not in ready]
            if time.monotonic() >= deadline:
                break
        return pending

    try:
        for proc in Path('/proc').iterdir():
            if not proc.name.isdecimal() or int(proc.name) == os.getpid():
                continue
            proof = ownership(proc)
            if proof is None:
                continue
            if proof['conflicting_private_paths']:
                result['refused'].append({'pid': int(proc.name), **proof})
                continue
            try:
                fd = os.pidfd_open(int(proc.name), 0)
            except ProcessLookupError:
                continue
            checked = ownership(proc)
            if checked is None or checked['conflicting_private_paths']:
                os.close(fd)
                continue
            handles[fd] = int(proc.name)
            result['matched'].append({'pid': int(proc.name), **checked})
        for fd, pid in handles.items():
            try:
                signal.pidfd_send_signal(fd, signal.SIGTERM)
                result['term_sent'].append(pid)
            except ProcessLookupError:
                pass
        pending = wait(list(handles), .4)
        for fd in pending:
            try:
                signal.pidfd_send_signal(fd, signal.SIGKILL)
                result['kill_sent'].append(handles[fd])
            except ProcessLookupError:
                pass
        result['remaining'] = [handles[fd] for fd in wait(pending, 1)]
        return result
    finally:
        for fd in handles:
            os.close(fd)


def method(source,signature):
    start=source.index('    '+signature);end=source.index('\n    }',start)+len('\n    }')
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', choices=('production',))
    parser.add_argument('--output', type=Path, default=ROOT / '.planning/debug/default-sky-runtime')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n')
    world = ROOT / 'src/GloomhavenVR/Core/Environment'
    files = {name: world / name for name in ('SkyBackdrop.cs', 'NativeDefaultSky.cs')}
    originals = {name: path.read_text() for name, path in files.items()}
    screen_core=ROOT/'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.1.Core.cs'
    screen_lifecycle=ROOT/'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.4.Lifecycle.cs'
    world_menu=ROOT/'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.9.WorldMenu.cs'
    core=screen_core.read_text();lifecycle=screen_lifecycle.read_text();menu=world_menu.read_text()
    begin=lifecycle.index('            Shader? shader = Shader.Find("Hidden/BlitCopy")')
    end=lifecycle.index('            _quadRenderer.sharedMaterial = _screenMaterial;',begin)+len('            _quadRenderer.sharedMaterial = _screenMaterial;')
    coverage=core[core.index('    internal static Renderer? OpaqueBackdrop'):core.index('    // Shader.name marshals')]
    originals['ScreenBackdrop.cs']='using GloomhavenVR.Core; using GloomhavenVR.Core.Events; using GloomhavenVR.Hands; using UnityEngine; namespace GloomhavenVR.WorldUI; internal sealed partial class FlatScreen {\n'+coverage+'\n'+method(core,'private void RefreshOpaqueBackdropMaterial()')+'\n'+method(core,'private void TickBackdropDepth()')+'\n'+method(menu,'private void TickWorldMenuBackdrop()')+'\n'+method(menu,'private void RestoreWorldMenuBackdrop()')+'\ninternal void BindOpaque(GameObject quad,RenderTexture rt) { _coverageOwner=this; _worldMenuHost=quad; _quadRenderer=quad.GetComponent<Renderer>();_rt=rt;\n'+lifecycle[begin:end]+'\n}\n}'
    bank = ROOT / 'src/GloomhavenVR/Assets/DefaultSky/native-default-sky.bundle'
    exporter = ROOT / 'tools/default-sky-native/export.py'
    overlay = ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader'
    inputs = [*files.values(), screen_core, screen_lifecycle, world_menu, bank, bank.with_suffix('.provenance.json'), exporter, overlay,
              *[p for p in HERE.iterdir() if p.is_file()]]
    pins = {str(p.relative_to(ROOT)): sha(p) for p in inputs}
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    cases = [('production', None, None, '')]
    controls = [
        ('world-window-depth-lost', 'ScreenBackdrop.cs', '        TickWorldMenuBackdrop();', '        /* world window material omitted */',
         'actual world-menu opaque RT survives original Default sky and later room geometry'),
        ('world-window-before-sky-reset', 'ScreenBackdrop.cs', '+ (_worldMenuBackdropDepth ? 2 : 0)', '+ 0',
         'actual world-menu opaque RT survives original Default sky and later room geometry'),
        ('world-window-zwrite-lost', 'ScreenBackdrop.cs', '_screenMaterial.SetFloat("_ZWrite", 1f);', '_screenMaterial.SetFloat("_ZWrite", 0f);',
         'actual world-menu opaque RT survives original Default sky and later room geometry'),
        ('world-window-alpha-blend-lost', 'ScreenBackdrop.cs', '_screenMaterial.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);',
         '_screenMaterial.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);',
         'actual world-menu opaque RT survives original Default sky and later room geometry'),
        ('world-window-rear-cull', 'ScreenBackdrop.cs', '_screenMaterial.SetFloat("_Cull", (int)UnityEngine.Rendering.CullMode.Off);',
         '_screenMaterial.SetFloat("_Cull", (int)UnityEngine.Rendering.CullMode.Back);',
         'actual world-menu opaque RT survives original Default sky and later room geometry'),
        ('world-window-coverage-lost', 'ScreenBackdrop.cs', '|| (_worldMenuBackdropDepth && shader == _worldMenuBackdropShader)', '|| false',
         'world-menu depth material remains authoritative opaque MR coverage'),
        ('world-window-intro-restore-lost', 'ScreenBackdrop.cs', '            RestoreWorldMenuBackdrop();', '            /* original intro shader restore omitted */',
         'leaving ready menu restores the original intro material identity shader and queue'),
        ('direct-intro-admission-lost', 'NativeDefaultSky.cs', '!Events.MenuWorldContext.AllowsSurroundings',
         'bool.Parse("false")', 'direct default acquisition cannot reveal surroundings before the actual menu is ready'),
        ('backdrop-intro-admission-lost', 'SkyBackdrop.cs', 'if (!Events.MenuWorldContext.AllowsSurroundings)',
         'if (!Events.MenuWorldContext.AllowsSurroundings && _sky == null)',
         'intro admission removes the actual default environment while restoring the stock screen'),
        ('cold-acquisition-lost', 'SkyBackdrop.cs', 'FindNativeSky(headVisibleOnly: true) ?? NativeDefaultSky.Renderer ?? NativeDefaultSky.Acquire()', 'FindNativeSky(headVisibleOnly: true) ?? NativeDefaultSky.Renderer',
         'cold main menu acquires its original default sky without a native scene'),
        ('native-handback-lost', 'SkyBackdrop.cs', 'arrived = FindNativeSky(headVisibleOnly: true);', 'arrived = null;',
         'late native scenario sky takes authority without a second generated dome'),
        ('scene-notification-lost', 'SkyBackdrop.cs', 'SceneManager.sceneLoaded += OnSceneLoaded;', '/* scene notification omitted */',
         'late native scenario sky takes authority without a second generated dome'),
        ('cached-visibility-lost', 'SkyBackdrop.cs', 'if (_sky != null && !NativeDefaultSky.Owns(_sky) && !HeadCanRender(_sky))\n            FullReset();',
         '/* cached authority visibility omitted */', 'cached native sky gives way when the menu head cannot render its layer'),
        ('rig-lifetime-lost', 'SkyBackdrop.cs', 'if (NativeDefaultSky.RefreshLifetime()) FullReset();', '/* lifetime omitted */',
         'real menu-to-map rig replacement retires the old default presentation'),
        ('ownership-release-lost', 'SkyBackdrop.cs', 'else if (_applied)\n                RemoveEffects();\n            NativeDefaultSky.Release();',
         'else if (_applied)\n                RemoveEffects(); /* generated presentation retained */',
         'mixed reality and non-default styles remove the generated default sky'),
        ('depth-reset-lost', 'SkyBackdrop.cs', '            EnsureResetObject();', '            /* depth reset omitted */',
         'default sky depth reset preserves original foreground pixels during head motion'),
    ]
    if not args.case:
        for name, file, before, after, expected in controls:
            assert originals[file].count(before) == 1, 'Control binding drift: ' + name
            cases.append((name, file, (before, after), expected))
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    managed = unity.parent / 'Data/Managed'
    framework = Path.home() / '.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
    # Keep SDK-named Roslyn pipes local to this suite even when Unity editors run
    # concurrently. A short /tmp path also respects Unix socket path limits.
    compiler_temp = Path(tempfile.mkdtemp(prefix='ghvr-defaultsky-', dir='/tmp'))
    (run / 'compiler-temp.json').write_text(json.dumps({'TMPDIR': str(compiler_temp)}) + '\n')
    env = dict(os.environ, DOTNET_ROOT=str(Path.home() / '.dotnet'), DOTNET_CLI_USE_MSBUILD_SERVER='0',
               MSBUILDDISABLENODEREUSE='1', UseSharedCompilation='false',
               TMPDIR=str(compiler_temp),
               ADB_SERVER_SOCKET='localfilesystem:/tmp/ghvr-default-sky-' + run.name + '.sock', ADB_MDNS_AUTO_CONNECT='')
    dotnet = str(Path.home() / '.dotnet/dotnet')
    status = 1
    process = None
    try:
        command = [str(Path.home() / 'unitypy-venv/bin/python'), '-B', str(exporter), '--game-data', str(ROOT / 'ressources/GH_Data'),
                   '--output', str(run / 'rebuilt-native-default-sky.bundle')]
        with (run / 'native-rebuild.log').open('w') as log:
            result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
        assert result.returncode == 0 and sha(run / 'rebuilt-native-default-sky.bundle') == sha(bank), 'Native bank does not reproduce original assets exactly'
        for name, changed, mutation, expected in cases:
            case = run / name
            frozen = case / 'sources'; frozen.mkdir(parents=True)
            for file, value in originals.items():
                if changed == file:
                    value = value.replace(*mutation)
                (frozen / file).write_text(value)
            assembly = 'DefaultSky_' + name.replace('-', '_')
            build = case / 'build'; build.mkdir()
            shutil.copy2(HERE / 'Fixture.csproj', build / 'Fixture.csproj')
            command = [dotnet, 'build', str(build / 'Fixture.csproj'), '-c', 'Release', '--nologo', '-v', 'quiet',
                       '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                       '-p:ProductionDir=' + str(frozen), '-p:FixtureDir=' + str(HERE), '-p:SkyBank=' + str(bank),
                       '-p:UnityManaged=' + str(managed), '-p:FrameworkReferenceDir=' + str(framework)]
            with (case / 'compile.log').open('w') as log:
                result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, env=env)
            assert result.returncode == 0, 'A compile failure is not a control success: ' + str(case / 'compile.log')
            project = case / 'unity'
            for path in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'):
                (project / path).mkdir(parents=True)
            dll = build / 'bin/Release/net472' / (assembly + '.dll')
            retained = case / (assembly + '.dll'); shutil.copy2(dll, retained)
            shutil.copy2(retained, project / 'Assets/Plugins' / retained.name)
            shutil.copy2(HERE / 'EditorRunner.cs', project / 'Assets/Editor/EditorRunner.cs')
            for shader in HERE.glob('*.shader'): shutil.copy2(shader, project / 'Assets' / shader.name)
            shutil.copy2(overlay, project / 'Assets/OriginalOverlay.shader')
            (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {name: '1.0.0' for name in
                ('com.unity.modules.assetbundle', 'com.unity.modules.imageconversion', 'com.unity.modules.jsonserialize', 'com.unity.modules.physics')}}))
            (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
            command = [str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod', 'DefaultSkyRunner.Start',
                       '-skyEvidence', str(case), '-skyAssembly', assembly, '-logFile', str(case / 'unity.log')]
            if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a', *command]
            process = subprocess.Popen(command, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                result = process.wait(timeout=240)
            finally:
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait()
                editor = cleanup_project_editors(project, unity, launcher_reaped=True)
                escaped = cleanup_run_compilers(run, editor_reaped=True)
                if escaped['refused'] or escaped['remaining']:
                    (run / '.keep').write_text('Compiler ownership unresolved; protect borrowed inputs.\n')
                    raise RuntimeError('Run-owned compiler could not safely stop: '+str(escaped))
                compiler = cleanup_project_compilers(project, editor_reaped=True)
                assert not editor['remaining'] and not compiler['remaining']
                (case / 'process-cleanup.json').write_text(json.dumps({'editor': editor, 'compiler': compiler, 'escaped_compiler': escaped}, indent=2) + '\n')
            failure = (case / 'failure.txt').read_text() if (case / 'failure.txt').exists() else ''
            if not expected:
                assert result == 0 and (case / 'result.txt').exists(), 'Production failed: ' + str(case / 'unity.log')
                print((case / 'result.txt').read_text().strip(), flush=True)
            else:
                assert result != 0 and expected in failure, 'Control escaped or failed outside its oracle: ' + str(case)
                print('PASS negative control ' + name + ': ' + expected, flush=True)
            receipt = cleanup_completed_run('default-sky-runtime', result, completion, ROOT, process_reaped=True)
            (case / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        assert all(sha(ROOT / p) == digest for p, digest in pins.items()), 'Execution inputs changed during the suite'
        status = 0
        print(('PARTIAL PASS' if args.case else 'PASS') + ': default sky native assets, production rendering/lifecycle and ' + str(len(cases) - 1) + ' controls.', flush=True)
    finally:
        if process is not None:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()
        escaped = cleanup_run_compilers(run, editor_reaped=True)
        (run / 'escaped-compiler-cleanup.json').write_text(json.dumps(escaped, indent=2)+'\n')
        if escaped['refused'] or escaped['remaining']:
            (run / '.keep').write_text('Compiler ownership unresolved; preserve inputs.\n')
        subprocess.run(['adb', 'kill-server'], env=env, capture_output=True, timeout=20)
        receipt = cleanup_completed_run('default-sky-runtime', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        (run / 'status.json').write_text(json.dumps({'exit_code': status, 'case_count': len(cases), 'new_complete_local_gate': False}) + '\n')
        shutil.rmtree(compiler_temp)
    return status


if __name__ == '__main__':
    raise SystemExit(main())
