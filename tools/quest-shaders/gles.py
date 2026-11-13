#!/usr/bin/env python3
"""Execute an actual Unity-generated GLES3 bank on a local EGL/GLES device.

No graphics Python package is needed. The original generated equations and
bindings remain intact. Serialized Unity banks use explicit uniform locations
with a GLSL300 header; for a GLES3.1+ local context the probe promotes that header
to GLSL310 and records the exact adaptation and effective source hashes.
Uniform/texture/geometry fixtures are explicit input data. This is native GLES
execution evidence, separate from original Windows pixels and Quest hardware.
"""
from __future__ import annotations

import argparse
import ctypes as c
import ctypes.util
import hashlib
import json
from pathlib import Path
import re


class GlesError(RuntimeError):
    pass


def _function(library, name, result, *arguments):
    function = getattr(library, name)
    function.restype, function.argtypes = result, list(arguments)
    return function


class Device:
    def __init__(self, width=128, height=128):
        self.width, self.height = width, height
        self.egl = c.CDLL(ctypes.util.find_library("EGL") or "libEGL.so.1")
        self.gl = c.CDLL(ctypes.util.find_library("GLESv2") or "libGLESv2.so.2")
        ptr, integer, uint = c.c_void_p, c.c_int, c.c_uint
        get_display = _function(self.egl, "eglGetPlatformDisplay", ptr, uint, ptr, c.POINTER(integer))
        self.display = get_display(0x31DD, None, None)  # EGL_PLATFORM_SURFACELESS_MESA
        initialize = _function(self.egl, "eglInitialize", uint, ptr, c.POINTER(integer), c.POINTER(integer))
        major, minor = integer(), integer()
        if not self.display or not initialize(self.display, c.byref(major), c.byref(minor)):
            raise GlesError("A real surfaceless EGL display is unavailable.")
        bind_api = _function(self.egl, "eglBindAPI", uint, uint)
        if not bind_api(0x30A0):
            raise GlesError("Actual EGL GLES API binding failed.")
        choose = _function(self.egl, "eglChooseConfig", uint, ptr, c.POINTER(integer), c.POINTER(ptr), integer, c.POINTER(integer))
        attributes = (integer * 17)(0x3033, 1, 0x3040, 0x40, 0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8, 0x3025, 24, 0x3026, 0, 0x3038)
        config, count = ptr(), integer()
        if not choose(self.display, attributes, c.byref(config), 1, c.byref(count)) or count.value != 1:
            raise GlesError("Actual EGL GLES3 pbuffer configuration is unavailable.")
        create_surface = _function(self.egl, "eglCreatePbufferSurface", ptr, ptr, ptr, c.POINTER(integer))
        surface_attributes = (integer * 5)(0x3057, width, 0x3056, height, 0x3038)
        self.surface = create_surface(self.display, config, surface_attributes)
        create_context = _function(self.egl, "eglCreateContext", ptr, ptr, ptr, ptr, c.POINTER(integer))
        self.context = create_context(self.display, config, None, (integer * 3)(0x3098, 3, 0x3038))
        make_current = _function(self.egl, "eglMakeCurrent", uint, ptr, ptr, ptr, ptr)
        if not self.surface or not self.context or not make_current(self.display, self.surface, self.surface, self.context):
            raise GlesError("Actual GLES3 context creation failed.")
        self.call = {}
        signatures = {
            "glGetString": (c.c_char_p, uint), "glGetError": (uint,),
            "glCreateShader": (uint, uint), "glShaderSource": (None, uint, integer, c.POINTER(c.c_char_p), c.POINTER(integer)),
            "glCompileShader": (None, uint), "glGetShaderiv": (None, uint, uint, c.POINTER(integer)),
            "glGetShaderInfoLog": (None, uint, integer, c.POINTER(integer), c.c_char_p), "glDeleteShader": (None, uint),
            "glCreateProgram": (uint,), "glAttachShader": (None, uint, uint), "glLinkProgram": (None, uint),
            "glGetProgramiv": (None, uint, uint, c.POINTER(integer)), "glGetProgramInfoLog": (None, uint, integer, c.POINTER(integer), c.c_char_p),
            "glUseProgram": (None, uint), "glDeleteProgram": (None, uint),
            "glGetUniformLocation": (integer, uint, c.c_char_p), "glUniform1i": (None, integer, integer),
            "glUniform1fv": (None, integer, integer, c.POINTER(c.c_float)), "glUniform2fv": (None, integer, integer, c.POINTER(c.c_float)),
            "glUniform3fv": (None, integer, integer, c.POINTER(c.c_float)), "glUniform4fv": (None, integer, integer, c.POINTER(c.c_float)),
            "glGenVertexArrays": (None, integer, c.POINTER(uint)), "glBindVertexArray": (None, uint),
            "glGenBuffers": (None, integer, c.POINTER(uint)), "glBindBuffer": (None, uint, uint),
            "glBufferData": (None, uint, c.c_ssize_t, ptr, uint), "glGetAttribLocation": (integer, uint, c.c_char_p),
            "glEnableVertexAttribArray": (None, uint), "glVertexAttribPointer": (None, uint, integer, uint, c.c_ubyte, integer, ptr),
            "glGenTextures": (None, integer, c.POINTER(uint)), "glBindTexture": (None, uint, uint),
            "glTexParameteri": (None, uint, uint, integer), "glTexImage2D": (None, uint, integer, integer, integer, integer, integer, uint, uint, ptr),
            "glTexImage3D": (None, uint, integer, integer, integer, integer, integer, integer, uint, uint, ptr),
            "glActiveTexture": (None, uint), "glViewport": (None, integer, integer, integer, integer),
            "glClearColor": (None, c.c_float, c.c_float, c.c_float, c.c_float), "glClear": (None, uint),
            "glDrawArrays": (None, uint, integer, integer), "glReadPixels": (None, integer, integer, integer, integer, uint, uint, ptr),
            "glFinish": (None,), "glDisable": (None, uint)
        }
        for name, signature in signatures.items():
            self.call[name] = _function(self.gl, name, *signature)
        self.extensions = set(self.call["glGetString"](0x1F03).decode().split())
        self.version = self.call["glGetString"](0x1F02).decode()
        self.renderer = self.call["glGetString"](0x1F01).decode()
        self.vendor = self.call["glGetString"](0x1F00).decode()
        if not self.version.startswith("OpenGL ES 3"):
            raise GlesError("Native device does not expose GLES3.")

    def error(self, boundary):
        error = self.call["glGetError"]()
        if error:
            raise GlesError(f"Actual GLES error at {boundary}: 0x{error:04x}")

    def stage(self, stage, source):
        shader = self.call["glCreateShader"](stage)
        raw = source.encode()
        text = c.c_char_p(raw)
        self.call["glShaderSource"](shader, 1, c.byref(text), None)
        self.call["glCompileShader"](shader)
        status = c.c_int()
        self.call["glGetShaderiv"](shader, 0x8B81, c.byref(status))
        if not status.value:
            log = c.create_string_buffer(16384)
            self.call["glGetShaderInfoLog"](shader, len(log), None, log)
            self.call["glDeleteShader"](shader)
            raise GlesError("Native GLES stage compiler rejected actual bank: " + log.value.decode(errors="replace"))
        return shader

    def program(self, bank):
        stages = sections(bank)
        prelude = ""
        adaptation = "none"
        effective_hashes = {}
        shaders = []
        for name, kind in (("vertex", 0x8B31), ("fragment", 0x8B30)):
            source = stages[name]
            if "#define UNITY_SUPPORTS_UNIFORM_LOCATION 1" in source and source.startswith("#version 300 es\n"):
                if not re.match(r"OpenGL ES 3\.[12]", self.version):
                    raise GlesError("Local serialized-bank probe needs GLES3.1 explicit uniform locations.")
                source = source.replace("#version 300 es\n", "#version 310 es\n", 1)
                adaptation = "GLSL300-to-310-header-for-serialized-explicit-uniform-locations"
            effective_hashes[name] = hashlib.sha256(source.encode()).hexdigest()
            shaders.append(self.stage(kind, source))
        program = self.call["glCreateProgram"]()
        for shader in shaders:
            self.call["glAttachShader"](program, shader)
        self.call["glLinkProgram"](program)
        for shader in shaders:
            self.call["glDeleteShader"](shader)
        status = c.c_int()
        self.call["glGetProgramiv"](program, 0x8B82, c.byref(status))
        if not status.value:
            log = c.create_string_buffer(16384)
            self.call["glGetProgramInfoLog"](program, len(log), None, log)
            self.call["glDeleteProgram"](program)
            raise GlesError("Actual native GLES bank link failed: " + log.value.decode(errors="replace"))
        self.error("actual stage link")
        return program, {"prelude": prelude, "adaptation": adaptation, "effectiveStageSha256": effective_hashes}

    def uniform(self, program, name, values, width=None, integer=False):
        location = self.call["glGetUniformLocation"](program, name.encode())
        if location < 0:
            return False
        if integer:
            self.call["glUniform1i"](location, int(values[0]))
        else:
            width = width or len(values)
            if width not in (1, 2, 3, 4) or len(values) % width:
                raise GlesError("Fixture uniform shape does not match its explicit native input.")
            data = (c.c_float * len(values))(*values)
            self.call[f"glUniform{width}fv"](location, len(values) // width, data)
        return True

    def texture(self, program, name, kind, unit, pixels):
        if self.call["glGetUniformLocation"](program, name.encode()) < 0:
            return False
        target = {"sampler2D": 0x0DE1, "samplerCube": 0x8513, "sampler3D": 0x806F}.get(kind)
        if target is None:
            raise GlesError("This native fixture needs explicit support for texture type " + kind)
        self.call["glActiveTexture"](0x84C0 + unit)
        texture = c.c_uint()
        self.call["glGenTextures"](1, c.byref(texture))
        self.call["glBindTexture"](target, texture.value)
        for key, value in ((0x2801, 0x2600), (0x2800, 0x2600), (0x2802, 0x2901), (0x2803, 0x2901)):
            self.call["glTexParameteri"](target, key, value)
        data = (c.c_ubyte * len(pixels))(*pixels)
        if kind == "sampler3D":
            self.call["glTexImage3D"](target, 0, 0x8058, 4, 4, 1, 0, 0x1908, 0x1401, data)
        elif kind == "samplerCube":
            for face in range(6):
                self.call["glTexImage2D"](0x8515 + face, 0, 0x8058, 4, 4, 0, 0x1908, 0x1401, data)
        else:
            self.call["glTexImage2D"](target, 0, 0x8058, 4, 4, 0, 0x1908, 0x1401, data)
        self.uniform(program, name, [unit], integer=True)
        self.error("native texture " + name)
        return True

    def vertices(self, program, columns):
        array = c.c_uint()
        self.call["glGenVertexArrays"](1, c.byref(array))
        self.call["glBindVertexArray"](array.value)
        for name, rows in columns.items():
            location = self.call["glGetAttribLocation"](program, name.encode())
            if location < 0:
                continue
            width = len(rows[0])
            if any(len(row) != width for row in rows):
                raise GlesError("Native fixture vertex attributes have inconsistent shape.")
            data = (c.c_float * (len(rows) * width))(*(value for row in rows for value in row))
            buffer = c.c_uint()
            self.call["glGenBuffers"](1, c.byref(buffer))
            self.call["glBindBuffer"](0x8892, buffer.value)
            self.call["glBufferData"](0x8892, c.sizeof(data), data, 0x88E4)
            self.call["glEnableVertexAttribArray"](location)
            self.call["glVertexAttribPointer"](location, width, 0x1406, 0, 0, None)
        self.error("native fixture vertices")

    def picture(self, program):
        self.call["glUseProgram"](program)
        self.call["glViewport"](0, 0, self.width, self.height)
        self.call["glDisable"](0x0B44)
        self.call["glDisable"](0x0BE2)
        self.call["glDisable"](0x0B71)
        self.call["glClearColor"](0.0627451, 0.1254902, 0.1882353, 1)
        self.call["glClear"](0x4000 | 0x0100)
        self.call["glDrawArrays"](0x0004, 0, 3)
        pixels = (c.c_ubyte * (self.width * self.height * 4))()
        self.call["glReadPixels"](0, 0, self.width, self.height, 0x1908, 0x1401, pixels)
        self.call["glFinish"]()
        self.error("actual native draw and pixel readback")
        return bytes(pixels)


def sections(bank):
    vertex = bank.find("#ifdef VERTEX\n")
    fragment = bank.find("#ifdef FRAGMENT\n")
    if vertex < 0 or fragment <= vertex:
        raise GlesError("Native bank is missing actual vertex/fragment sections.")
    result = {}
    for name, value in (("vertex", bank[vertex:fragment]), ("fragment", bank[fragment:])):
        first = value.find("\n") + 1
        last = value.rfind("#endif")
        if last <= first:
            raise GlesError("Actual generated GLES section guard is incomplete.")
        source = value[first:last].strip() + "\n"
        if not source.startswith("#version 300 es\n") and not source.startswith("#version 310 es\n"):
            raise GlesError("Actual generated shader is not a GLES3 bank.")
        result[name] = source
    return result


def native_fixture(bank_path, configuration, output):
    raw = bank_path.read_bytes()
    source = raw.decode("utf-8")
    device = Device()
    program, driver = device.program(source)
    device.call["glUseProgram"](program)
    bound = []
    for name, value in configuration["uniforms"].items():
        values = value["values"] if isinstance(value, dict) else value
        width = value.get("width") if isinstance(value, dict) else None
        if device.uniform(program, name, values, width):
            bound.append(name)
    texture_rows = re.findall(r"uniform\s+(?:(?:highp|mediump|lowp)\s+)?(sampler\w+)\s+(\w+)\s*;", source)
    unique_textures = dict((name, kind) for kind, name in texture_rows)
    for unit, (name, kind) in enumerate(unique_textures.items()):
        pixel = configuration["textures"].get(name)
        if pixel is None:
            raise GlesError("Actual native sampler has no explicit fixture data: " + name)
        if device.texture(program, name, kind, unit, pixel):
            bound.append(name)
    device.vertices(program, configuration["attributes"])
    baseline = device.picture(program)
    output.mkdir(parents=True, exist_ok=True)
    (output / "baseline.rgba").write_bytes(baseline)
    background = bytes((16, 32, 48, 255))
    foreground = sum(max(abs(a - b) for a, b in zip(baseline[index:index + 4], background)) > 4 for index in range(0, len(baseline), 4))
    if foreground < 16:
        raise GlesError("Actual native shader fixture does not render visible geometry.")
    cases = []
    for case in configuration["cases"]:
        for name, value in case.get("uniforms", {}).items():
            if not device.uniform(program, name, value):
                raise GlesError("Fixture hypothesis does not bind its actual uniform: " + name)
        for name, value in case.get("textures", {}).items():
            if name not in unique_textures:
                raise GlesError("Fixture hypothesis does not bind its actual sampler.")
            device.texture(program, name, unique_textures[name], list(unique_textures).index(name), value)
        if case.get("attributes") is not None:
            device.vertices(program, case["attributes"])
        pixels = device.picture(program)
        changed = sum(max(abs(a - b) for a, b in zip(pixels[index:index + 4], baseline[index:index + 4])) > 4 for index in range(0, len(pixels), 4))
        if changed < 4:
            raise GlesError("Actual GLES picture does not observe fixture hypothesis " + case["id"])
        name = case["id"] + ".rgba"
        if Path(name).name != name:
            raise GlesError("Native fixture case path is invalid.")
        (output / name).write_bytes(pixels)
        cases.append({"id": case["id"], "observedChangedPixels": changed, "pixelsSha256": hashlib.sha256(pixels).hexdigest()})
        # Restore every explicit baseline input for the next independent probe.
        for uniform, value in configuration["uniforms"].items():
            device.uniform(program, uniform, value["values"] if isinstance(value, dict) else value,
                           value.get("width") if isinstance(value, dict) else None)
        for unit, (texture, kind) in enumerate(unique_textures.items()):
            device.texture(program, texture, kind, unit, configuration["textures"][texture])
        device.vertices(program, configuration["attributes"])
    # A real driver compiler negative control: a syntactically invalid stage
    # must fail through the same native API used above, not a text-only checker.
    rejected = False
    try:
        device.stage(0x8B31, "#version 300 es\nvoid main(){ gl_Position = MissingActualInput; }\n")
    except GlesError:
        rejected = True
    if not rejected:
        raise GlesError("Native GLES compiler accepted the planted undefined input.")
    receipt = {"schema": 1, "unityGeneratedBankSha256": hashlib.sha256(raw).hexdigest(),
               "graphicsVersion": device.version, "renderer": device.renderer, "vendor": device.vendor,
               "driverSourceAdaptation": driver, "actualVertexAndFragmentLinked": True, "visibleForegroundPixels": foreground,
               "boundInputs": bound, "cases": cases, "actualCompilerNegativeRejected": rejected,
               "originalWindowsDxbcPixelParityVerified": False, "questHardwareVerified": False}
    (output / "gles-pixels.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bank", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        receipt = native_fixture(args.bank, json.loads(args.fixture.read_text()), args.output)
    except (GlesError, OSError, ValueError) as error:
        raise SystemExit(str(error)) from error
    print("Actual GLES picture hypotheses: " + str(len(receipt["cases"])) + "; " + receipt["renderer"])


if __name__ == "__main__":
    main()
