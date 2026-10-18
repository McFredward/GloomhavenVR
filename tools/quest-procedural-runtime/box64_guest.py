"""One Android-only upstream adaptation: readable guest ELF is interpreted data."""
from pathlib import Path

ORIGINAL = """    if (flags & IS_EXECUTABLE) {
        if ((sb.st_mode & S_IXUSR) != S_IXUSR)
            return 0; // nope
    }
"""
ADAPTED = """    if (flags & IS_EXECUTABLE) {
        if ((sb.st_mode & S_IXUSR) != S_IXUSR) {
#ifdef ANDROID
            // Quest: interpreted x64 ELF is readable payload, never execve'd.
            unsigned char header[20];
            FILE* guest = fopen(filename, "rb");
            size_t length = guest ? fread(header, 1, sizeof(header), guest) : 0;
            if (guest) fclose(guest);
            if (length != sizeof(header) || header[0] != 0x7f || header[1] != 'E'
                || header[2] != 'L' || header[3] != 'F' || header[4] != 2
                || header[5] != 1 || header[18] != 0x3e || header[19] != 0)
                return 0;
#else
            return 0;
#endif
        }
    }
"""


def apply(source: Path):
    path = source / "src/os/os_linux.c"
    text = path.read_text()
    if text.count(ORIGINAL) != 1:
        raise RuntimeError("Pinned Box64 readable-guest boundary differs from its audited source.")
    path.write_text(text.replace(ORIGINAL, ADAPTED))
