"""Keep the pinned Box64 source build independent of a host POSIX shell."""
from pathlib import Path
import re


ORIGINAL = r'''        COMMAND sh -c "echo \\\#define GITREV \\\"$(git rev-parse --short HEAD)\\\">\"${BOX64_ROOT}/src/git_head.h\""'''
ADAPTED = '''        COMMAND "${CMAKE_COMMAND}" "-DHEADER=${BOX64_ROOT}/src/git_head.h" -P "${BOX64_ROOT}/quest_git_head.cmake"'''


def apply(source: Path, revision: str):
    """Retain both native dependency graphs and the pinned revision header."""
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise RuntimeError("Box64 source revision is not the pinned full commit ID.")
    path = source / "CMakeLists.txt"
    text = path.read_text(encoding="utf-8")
    if text.count(ORIGINAL) != 2:
        raise RuntimeError("Pinned Box64 host header generator differs from its audited source.")
    (source / "quest_git_head.cmake").write_text(
        'if(NOT DEFINED HEADER OR HEADER STREQUAL "")\n'
        '  message(FATAL_ERROR "Box64 header destination is missing")\n'
        'endif()\n'
        'file(WRITE "${HEADER}" "#define GITREV \\\"' + revision[:7] + '\\\"\\n")\n',
        encoding="utf-8")
    path.write_text(text.replace(ORIGINAL, ADAPTED), encoding="utf-8")
