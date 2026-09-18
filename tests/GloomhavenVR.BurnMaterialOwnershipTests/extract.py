from pathlib import Path
import sys
root, dest = map(Path, sys.argv[1:])
s = (root/'src/GloomhavenVR/Cards/Art/CardHalfTone.cs').read_text()
def method(name):
    start = s.index(name)
    start = s.rfind('\n', 0, start) + 1
    opening = s.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (s[end] == '{') - (s[end] == '}')
        end += 1
    return s[start:end]
body = '\n'.join(method(n) for n in ['void Observe(', 'bool IsModOwnedCopy(', 'void NormalizeCardFx('])
assert 'g.material = rest;' in body
assert body.index('if (IsModOwnedCopy(face))') < body.index('NormalizeCardFx(face);')
dest.write_text('using UnityEngine; using UnityEngine.UI; using GloomhavenVR.Core; namespace GloomhavenVR.Cards; partial class CardHalfTone {\n'+body+'\n}')
print('Burn material ownership: 2 production write-route bindings passed.')
