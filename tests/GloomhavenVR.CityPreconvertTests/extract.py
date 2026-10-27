"""Run actual pre-conversion helper and actual TryConvertWindow entry prefix."""
from pathlib import Path
import re,sys

def method(source, signature):
    assert source.count(signature)==1,signature
    start=source.index(signature);opening=source.index('{',start);depth=1;end=opening+1
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start:end]

source,out=Path(sys.argv[1]).read_text(),Path(sys.argv[2])
entry=method(source,'private static bool TryConvertWindow(UIWindow window)')
# Pin the real conversion entry, rather than certifying a helper nobody calls.
# This ordering assertion is repeated for every causal-control source snapshot.
code=re.sub(r'//[^\n]*|/\*[\s\S]*?\*/','',entry)
assert code.count('PrepareCityReadinessForConversion(window);')==1,'city preparation must run exactly once in conversion entry'
assert code.index('PrepareCityReadinessForConversion(window);')<code.index('var rect = window.transform as RectTransform;'),'city preparation must precede first rect snapshot'
assert code.index('PrepareCityReadinessForConversion(window);')<code.index('CanvasConversion.Convert('),'city preparation must precede first conversion'
prefix=entry[:entry.index('            // Story-box sanity:')]
# The untouched prefix includes the real try, native render-owner release and
# first RectTransform guard. The fixture stops at the first-content boundary;
# later chrome/render stages are intentionally outside this focused test.
prefix+='            FirstContentSnapshot(window,rect);return true;\n        } finally { }\n    }\n'
text='using System;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI {internal static partial class ModalFallback {\n'
text+=method(source,'private static void PrepareCityReadinessForConversion(UIWindow window)')+'\n'+prefix+'\n}}\n'
out.write_text(text)
