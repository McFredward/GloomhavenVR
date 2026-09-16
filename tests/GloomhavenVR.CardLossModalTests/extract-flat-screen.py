#!/usr/bin/env python3
"""Compile the actual FlatScreen policy without the unrelated rendering implementation."""
from pathlib import Path
import sys

source = Path(sys.argv[1]).read_text()
def method(marker):
    assert source.count(marker) == 1, 'FlatScreen policy signature changed'
    start = source.index(marker)
    end = source.index('\n    }\n', start) + len('\n    }\n')
    return source[start:end]

import re
fallback = re.findall(r'    private static bool MapFallbackActive =>[^;]+;', source)
assert len(fallback) == 1
methods = fallback[0] + '\n' + method('    private void UpdateScreenTakeover()\n') + method('    private bool WantVisible()\n')
Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
Path(sys.argv[2]).write_text(
    'using GloomhavenVR.Core;\nusing GloomhavenVR.Core.Events;\n'
    'namespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\n'
    + methods + '}\n')
