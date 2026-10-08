"""Retain both guest entry ABIs on the pinned Android/Bionic Box64 host."""
import hashlib
from pathlib import Path


SOURCE_SHA256 = "6ca1f434d92ee0da661281fe4756116939420f2c021019a08c35cb22df060c6a"
ENTRYPOINT = "#else\nEXPORT int32_t my___libc_start_main("
REQUIRED_EXPORTS = frozenset(("my___libc_init", "my___libc_start_main", "my___libc_init_first"))


def adapt(original: bytes) -> bytes:
    """Enable upstream glibc guest startup without changing either entry body.

    B624's actual worker failed before Wine main: ANDROID selected only the
    Bionic guest entry, while our interpreted Wine ELF imports glibc startup.
    The previous Linux/glibc-host proof compiled the other preprocessor branch
    and could not establish Android guest ABI completeness.
    """
    if hashlib.sha256(original).hexdigest() != SOURCE_SHA256:
        raise RuntimeError("Pinned Box64 dual guest entry source differs from its audited revision.")
    text = original.decode("utf-8")
    if text.count(ENTRYPOINT) != 1 or not text.endswith("#endif\n#endif\n#endif\n"):
        raise RuntimeError("Pinned Box64 guest entry conditional boundaries differ.")
    # Close ANDROID after its original 64-bit Bionic entry. Its unchanged
    # upstream glibc entry also belongs in the Android host; the final outer
    # closing directive is consequently redundant. BOX32 remains untouched.
    return (text.replace(ENTRYPOINT, "#endif\nEXPORT int32_t my___libc_start_main(")[:-len("#endif\n")]).encode("utf-8")


def apply(source: Path):
    path = source / "src/emu/entrypoint.c"
    path.write_bytes(adapt(path.read_bytes()))


def require_exports(actual):
    missing = REQUIRED_EXPORTS - set(actual)
    if missing:
        raise RuntimeError("Android Box64 original Wine guest entry exports are incomplete: " + ", ".join(sorted(missing)))
