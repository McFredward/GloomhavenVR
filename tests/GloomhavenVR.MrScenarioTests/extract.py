"""Compile the actual scenery-retirement entry point against lifecycle test doubles."""
from pathlib import Path
root = Path(__file__).resolve().parents[2]
s = (root / 'src/GloomhavenVR/Core/MixedReality/MixedReality.cs').read_text()
start = s.index('    private static void RetireSceneryBackings()')
brace = s.index('{', start)
depth = 1
end = brace + 1
while depth:
    depth += (s[end] == '{') - (s[end] == '}')
    end += 1
out = root / 'tests/GloomhavenVR.MrScenarioTests/obj/Retirement.g.cs'
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text('namespace GloomhavenVR.Core;\ninternal static partial class MixedReality\n{\n'
               + s[start:end] + '\n}\n')
