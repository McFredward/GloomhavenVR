#!/usr/bin/env python3
"""Compile the actual floated-window close slice and explicit-close scope, without Unity."""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    fixture = Path(__file__).resolve().parent
    root = fixture.parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=root)
    parser.add_argument('--no-negative-controls', action='store_true', help='Run only the production fixture')
    parser.add_argument('--only-mutation', help='Run production and one selected causal control')
    parser.add_argument('--skip-production', action='store_true', help='Resume --only-mutation after its production source passed')
    args = parser.parse_args()
    if args.skip_production and not args.only_mutation:
        parser.error('--skip-production requires --only-mutation')
    if args.no_negative_controls and args.only_mutation:
        parser.error('--no-negative-controls cannot select a mutation')
    source = args.source_root / 'src/GloomhavenVR/WorldUI'
    complete = (source / 'Modal/ModalFallback.7.Close.cs').read_text()
    signature = '    internal static void CloseFloatedWindow(UIWindow? window)'
    if complete.count(signature) != 1:
        raise SystemExit('Actual CloseFloatedWindow source binding drift')
    start = complete.index(signature)
    end = complete.index('\n    /// <summary>', start)
    close = ('using System;\nusing GloomhavenVR.Core;\nusing UnityEngine.UI;\n'
             'namespace GloomhavenVR.WorldUI;\ninternal static partial class ModalFallback {\n'
             + complete[start:end] + '\n}\n')
    scope = (source / 'MapRoom/TownWindowCloseScope.cs').read_text()
    entry = ('        using var closeScope = MapRoom.TownWindowCloseScope.Enter(\n'
             '            MapRoom.GuildmasterDestinations.IsDestination(window));')
    leave = '        MapRoom.GuildmasterDestinations.LeaveMode(window, "X button");'
    permanent = '        if (IsMapRoomPermanent(window))'
    capture = '        var merchantClose = TownServiceTutorialPatches.CaptureConvertedMerchantClose(window);'
    completion = '        TownServiceTutorialPatches.CompleteConvertedMerchantClose(window, merchantClose);'
    menu_reset = '        ResetEscMenuToggleGroup(window);'
    cleanup = 'if (_entered) _depth--;'
    thread = '[ThreadStatic] private static int _depth;'
    for original, text in ((close, entry), (close, leave), (close, permanent), (close, capture),
                           (close, completion), (close, menu_reset), (scope, cleanup), (scope, thread)):
        if original.count(text) != 1: raise SystemExit('Explicit close mutation source binding drift: ' + text)
    controls = {
        'missing-scope': (close.replace(entry, ''), scope, 'native destination leave observes explicit close intent'),
        'late-scope': (close.replace(entry, '').replace(leave, leave + '\n' + entry), scope,
                       'native destination leave observes explicit close intent'),
        'ungated-scope': (close.replace(entry, '        using var closeScope = MapRoom.TownWindowCloseScope.Enter();'), scope,
                          'ordinary native menu does not borrow destination close intent'),
        'before-guards': (close.replace(entry, '').replace(permanent, entry + '\n' + permanent), scope,
                          'permanent and mandatory guards run before explicit destination scope'),
        'missing-dispose': (close, scope.replace(cleanup, 'if (_entered) _depth -= 0;'),
                            'explicit scope disposal returns to unscoped context'),
        'unconditional-dispose': (close, scope.replace(cleanup, 'if (_entered || !_entered) _depth--;'),
                                  'disabled nested scope preserves enclosing explicit close intent'),
        'shared-thread-scope': (close, scope.replace(thread, 'private static int _depth;'),
                               "thread-local scope cannot borrow another native close's intent"),
        'missing-merchant-capture': (close.replace(capture, '        TownServiceTutorialPatches.MerchantClose? merchantClose = null;'), scope,
                                   'opaque merchant continuation is captured before native LeaveMode cleanup'),
        'late-merchant-capture': (close.replace(capture, '').replace(leave, leave + '\n' + capture), scope,
                                'opaque merchant continuation is captured before native LeaveMode cleanup'),
        'unscoped-merchant-capture': (close.replace(capture, '').replace(entry, capture + '\n' + entry), scope,
                                    'converted merchant snapshot and continuation remain inside explicit close intent'),
        'missing-merchant-completion': (close.replace(completion, ''), scope,
                                      'converted merchant continuation follows native close or final VR release'),
        'early-merchant-completion': (close.replace(completion, '').replace(capture, capture + '\n' + completion), scope,
                                    'converted merchant continuation follows native close or final VR release'),
        'late-merchant-completion': (close.replace(completion, '').replace(menu_reset, menu_reset + '\n' + completion), scope,
                                   'converted merchant continuation precedes unrelated menu-reset failures'),
    }
    if args.only_mutation and args.only_mutation not in controls:
        parser.error('Unknown causal control: ' + args.only_mutation)
    chosen = [] if args.no_negative_controls else [args.only_mutation] if args.only_mutation else list(controls)
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    with tempfile.TemporaryDirectory(prefix='ghvr-town-close624-') as scratch:
        build = Path(scratch)
        for original in fixture.iterdir():
            if original.suffix in ('.cs', '.csproj'): shutil.copy2(original, build / original.name)
        project = build / 'GloomhavenVR.TownWindowCloseTests.csproj'
        close_path, scope_path = build / 'Close.fixture', build / 'Scope.fixture'

        def run(name, actual_close, actual_scope, expected=''):
            close_path.write_text(actual_close); scope_path.write_text(actual_scope)
            result = subprocess.run([dotnet, 'run', '--project', str(project), '--configuration', 'Release',
                '--property:CloseSource=' + str(close_path), '--property:ScopeSource=' + str(scope_path)],
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            if expected:
                if result.returncode == 0 or ('Town window close assertion: ' + expected) not in result.stdout:
                    raise SystemExit(result.stdout + '\nFAIL causal control: ' + name)
                print('Town window close negative control: ' + name + ' rejected (' + expected + ').', flush=True)
            else:
                if result.returncode: raise SystemExit(result.stdout + '\nFAIL actual production close')
                print(result.stdout, end='', flush=True)

        print('Town close source: ' + str(args.source_root.resolve()) + '; SHA256 '
            + hashlib.sha256((close + scope).encode()).hexdigest(), flush=True)
        if not args.skip_production: run('production', close, scope)
        for name in chosen: run(name, *controls[name])


if __name__ == '__main__':
    main()
