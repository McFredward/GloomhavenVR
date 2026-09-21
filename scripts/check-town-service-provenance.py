#!/usr/bin/env python3
"""Execute production-bound item provenance guards without game/Unity callbacks.

Native models/pool/art requests are explicit adversarial fixtures. This verifies ID
validation and resource ownership, not Unity rendering. Every negative control must
compile and fail the intended behavioral assertion.
"""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
base = root / 'src/GloomhavenVR/WorldUI/TownServices'
raw = {name: (base / name).read_text() for name in ('TownServiceNativeAssets.cs', 'NativeTemplates.cs')}

def method(text, signature):
    start = text.index('    ' + signature)
    return text[start:text.index('\n    }', start) + 6]

assets = '\n'.join(method(raw['TownServiceNativeAssets.cs'], signature) for signature in (
    'internal static void PrepareItem(ItemCardUI? source)',
    'internal static ItemCardYMLData? FindItemData(int id)',
    'internal static void PrepareItemId(int id)'))
borrow = method(raw['NativeTemplates.cs'], 'private static void EnsureCard(string key)')
fixture = (root / 'scripts/town-service-provenance-runtime/Program.cs').read_text()
mutations = [
    ('unsafe-getter', 'assets', 'ItemCardYMLData? data = FindItemData(id);', 'ItemCardYMLData? data = new CItem(id).YMLData;', 'valid item art uses immutable lookup'),
    ('uninitialized-borrow', 'borrow', 'borrowedItem.item = new CItem(id);', '// retain the stale pooled model', 'borrow receives requested ID'),
    ('missing-validation', 'borrow', 'if (item && TownServiceNativeAssets.FindItemData(id) == null)', 'if (item && id == -1)', 'missing item rejected before native pool'),
    ('pool-model-leak', 'borrow', 'if (borrowedItem != null) borrowedItem.item = previousItem;', '// omit restoration', 'borrow restores original pooled model'),
]
output = root / '.planning/debug/town-service-provenance'
output.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
(run / 'Test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs" /></ItemGroup></Project>')
dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
results = []
for name, target, before, after, expected in [('positive', '', '', '', '')] + mutations:
    a, b = assets, borrow
    if target:
        text = a if target == 'assets' else b
        if text.count(before) != 1:
            raise RuntimeError('Production binding drift: ' + name)
        text = text.replace(before, after)
        if target == 'assets': a = text
        else: b = text
    source = fixture + '\nstatic partial class TownServiceNativeAssets {\n' + a + '\n}\nstatic partial class NativeTemplates {\n' + b + '\n}\n'
    (run / 'Program.cs').write_text(source)
    build = subprocess.run([dotnet, 'build', str(run / 'Test.csproj'), '--nologo', '-v:q'], capture_output=True, text=True)
    (run / (name + '-build.log')).write_text(build.stdout + build.stderr)
    if build.returncode: raise RuntimeError('Fixture failed to compile: ' + name + '\n' + build.stdout)
    result = subprocess.run([dotnet, str(run / 'bin/Debug/net8.0/Test.dll')], capture_output=True, text=True)
    log = result.stdout + result.stderr
    (run / (name + '.log')).write_text(log)
    if name == 'positive':
        if result.returncode: raise RuntimeError(log)
        print(log.strip())
    elif result.returncode == 0 or 'ASSERT: ' + expected not in log:
        raise RuntimeError('Negative control did not fail intended assertion: ' + name + '\n' + log)
    else: print('PASS negative control: ' + name)
    results.append(name)
(run / 'manifest.json').write_text(json.dumps({'sha256': {name: hashlib.sha256(text.encode()).hexdigest() for name, text in raw.items()}, 'cases': results}, indent=2))
print('Evidence:', run)
