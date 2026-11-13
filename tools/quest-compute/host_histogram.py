"""Execute the actual cooked original EyeHistogram kernel on a host GLES driver.

This bounded developer proof requires Linux EGL/GLES and the builder's UnityPy.
It tests the original integer histogram/centre-weight formula on binary-exact
input colours; it is not headset, visual or complete shader pixel-parity proof.
"""
from __future__ import annotations

import argparse
import ctypes as C
import hashlib
import json
import math
from pathlib import Path


def api(library, name, returns, *arguments):
    function = getattr(library, name)
    function.restype, function.argtypes = returns, list(arguments)
    return function


def expected_histogram(width=16, height=16, source_width=None, source_height=None) -> list[int]:
    source_width = width if source_width is None else source_width
    source_height = height if source_height is None else source_height
    result = [0] * 64
    for y in range(height):
        for x in range(width):
            # Audited original DXBC: max RGB, log2, scale+offset, saturation,
            # uint truncation into 64 bins. Weight is the original centre mask.
            brightness = 1.0 if (x + y) % 2 else 0.125
            if x >= source_width or y >= source_height:
                bucket = 0  # Original D3D LD returns all-zero colour, log2 -> -inf.
            else:
                bucket = int(max(0.0, min(1.0, math.log2(brightness) / 12.0 + 8.0 / 12.0)) * 63.0)
            radius2 = (x / width - 0.5) ** 2 + (y / height - 0.5) ** 2
            weight = int(max(1.0 - radius2, 0.0) ** 2 * 64.0)
            result[bucket] += weight
    return result


def expected_waveform(width=16, height=16) -> list[int]:
    result = [0] * (width * height * 4)
    for y in range(height):
        for x in range(width):
            colours = (1.0 if (x + y) % 2 else 0.125, 0.25, 0.5)
            for channel, value in enumerate(colours):
                bucket = round(value * (height - 1))
                element = x * height + bucket
                if element != 0: result[element * 4 + channel] += 1
    return result


def run(bundle: Path, name="EyeHistogram", source_width=16, source_height=16) -> dict:
    if not 1 <= source_width <= 16 or not 1 <= source_height <= 16 or name != "EyeHistogram" and (source_width, source_height) != (16, 16):
        raise ValueError("Bounded edge fixture accepts a 1..16 EyeHistogram source; Waveform uses its original 16x16 fixture.")
    import UnityPy
    matching = [obj.read_typetree() for obj in UnityPy.load(str(bundle)).objects
        if obj.type.name == "ComputeShader" and obj.read_typetree()["m_Name"] == name]
    if len(matching) != 1:
        raise RuntimeError("Actual bank must contain one original " + name + ".")
    shader = matching[0]
    if len(shader["variants"]) != 1 or shader["variants"][0]["targetRenderer"] != 11:
        raise RuntimeError("Expected real cooked GLES compute bytes.")
    kernel = shader["variants"][0]["kernels"][0]
    expected_kernel = "KEyeHistogram" if name == "EyeHistogram" else "KWaveformGather"
    if kernel["name"] != expected_kernel: raise RuntimeError("Original integer-counter kernel changed.")
    code = bytes(kernel["variantMap"][0][1]["code"]).rstrip(b"\0")

    egl, gl = C.CDLL("libEGL.so.1"), C.CDLL("libGLESv2.so.2")
    pointer, integer, unsigned = C.c_void_p, C.c_int, C.c_uint
    display = api(egl, "eglGetPlatformDisplay", pointer, unsigned, pointer, C.POINTER(integer))(0x31DD, None, None)
    major, minor = integer(), integer()
    if not api(egl, "eglInitialize", unsigned, pointer, C.POINTER(integer), C.POINTER(integer))(display, C.byref(major), C.byref(minor)):
        raise RuntimeError("Surfaceless EGL initialization failed.")
    try:
        if not api(egl, "eglBindAPI", unsigned, unsigned)(0x30A0): raise RuntimeError("GLES API bind failed.")
        config, count = pointer(), integer()
        attributes = (integer * 5)(0x3040, 0x40, 0x3033, 1, 0x3038)
        if not api(egl, "eglChooseConfig", unsigned, pointer, C.POINTER(integer), C.POINTER(pointer), integer, C.POINTER(integer))(
                display, attributes, C.byref(config), 1, C.byref(count)) or count.value != 1:
            raise RuntimeError("EGL GLES3 compute config unavailable.")
        context = api(egl, "eglCreateContext", pointer, pointer, pointer, pointer, C.POINTER(integer))(
            display, config, None, (integer * 3)(0x3098, 3, 0x3038))
        surface = api(egl, "eglCreatePbufferSurface", pointer, pointer, pointer, C.POINTER(integer))(
            display, config, (integer * 5)(0x3057, 1, 0x3056, 1, 0x3038))
        if not context or not surface or not api(egl, "eglMakeCurrent", unsigned, pointer, pointer, pointer, pointer)(display, surface, surface, context):
            raise RuntimeError("EGL GLES3 current context failed.")
        get_string = api(gl, "glGetString", C.c_char_p, unsigned)
        renderer, version = get_string(0x1F01).decode(), get_string(0x1F02).decode()
        compiled = api(gl, "glCreateShader", unsigned, unsigned)(0x91B9)
        source = C.c_char_p(code)
        api(gl, "glShaderSource", None, unsigned, integer, C.POINTER(C.c_char_p), C.POINTER(integer))(compiled, 1, C.byref(source), None)
        api(gl, "glCompileShader", None, unsigned)(compiled)
        status = integer()
        api(gl, "glGetShaderiv", None, unsigned, unsigned, C.POINTER(integer))(compiled, 0x8B81, C.byref(status))
        if not status.value:
            log = C.create_string_buffer(4096)
            api(gl, "glGetShaderInfoLog", None, unsigned, integer, C.POINTER(integer), pointer)(compiled, len(log), None, log)
            raise RuntimeError("Actual cooked GLES kernel failed host compile: " + log.value.decode())
        program = api(gl, "glCreateProgram", unsigned)()
        api(gl, "glAttachShader", None, unsigned, unsigned)(program, compiled)
        api(gl, "glLinkProgram", None, unsigned)(program)
        api(gl, "glGetProgramiv", None, unsigned, unsigned, C.POINTER(integer))(program, 0x8B82, C.byref(status))
        if not status.value: raise RuntimeError("Actual cooked GLES kernel failed host link.")
        api(gl, "glUseProgram", None, unsigned)(program)

        texture = unsigned()
        api(gl, "glGenTextures", None, integer, C.POINTER(unsigned))(1, C.byref(texture))
        api(gl, "glActiveTexture", None, unsigned)(0x84C0)
        api(gl, "glBindTexture", None, unsigned, unsigned)(0x0DE1, texture)
        api(gl, "glTexStorage2D", None, unsigned, integer, unsigned, integer, integer)(0x0DE1, 1, 0x8814, source_width, source_height)
        green, blue = (0.0625, 0.03125) if name == "EyeHistogram" else (0.25, 0.5)
        pixels = (C.c_float * (source_width * source_height * 4))(*[value for y in range(source_height) for x in range(source_width)
            for value in ((1.0 if (x + y) % 2 else 0.125), green, blue, 1.0)])
        api(gl, "glTexSubImage2D", None, unsigned, integer, integer, integer, integer, integer, unsigned, unsigned, pointer)(
            0x0DE1, 0, 0, 0, source_width, source_height, 0x1908, 0x1406, pixels)
        parameter = api(gl, "glTexParameteri", None, unsigned, unsigned, integer)
        parameter(0x0DE1, 0x2801, 0x2600); parameter(0x0DE1, 0x2800, 0x2600)
        location = api(gl, "glGetUniformLocation", integer, unsigned, C.c_char_p)(program, b"_Source")
        if location < 0: raise RuntimeError("Original texture input was deleted from actual GLSL.")
        api(gl, "glUniform1i", None, integer, integer)(location, 0)

        buffers = (unsigned * 2)()
        api(gl, "glGenBuffers", None, integer, C.POINTER(unsigned))(2, buffers)
        bind = api(gl, "glBindBuffer", None, unsigned, unsigned)
        data = api(gl, "glBufferData", None, unsigned, C.c_ssize_t, pointer, unsigned)
        base = api(gl, "glBindBufferBase", None, unsigned, unsigned, unsigned)
        params = (C.c_float * 4)(*( (1.0 / 12.0, 8.0 / 12.0, 16, 16) if name == "EyeHistogram" else (16, 16, 0, 0)))
        bind(0x8A11, buffers[0]); data(0x8A11, C.sizeof(params), params, 0x88E4); base(0x8A11, 0, buffers[0])
        expected = expected_histogram(source_width=source_width, source_height=source_height) if name == "EyeHistogram" else expected_waveform()
        zeros = (unsigned * len(expected))()
        bind(0x90D2, buffers[1]); data(0x90D2, C.sizeof(zeros), zeros, 0x88E8); base(0x90D2, 0, buffers[1])
        api(gl, "glDispatchCompute", None, unsigned, unsigned, unsigned)(1 if name == "EyeHistogram" else 16, 1, 1)
        api(gl, "glMemoryBarrier", None, unsigned)(0x2200)
        api(gl, "glFinish", None)()
        error = api(gl, "glGetError", unsigned)()
        if error: raise RuntimeError("Host GLES compute dispatch failed: " + hex(error))
        bind(0x90D2, buffers[1])
        address = api(gl, "glMapBufferRange", pointer, unsigned, C.c_ssize_t, C.c_ssize_t, unsigned)(0x90D2, 0, C.sizeof(zeros), 1)
        if not address: raise RuntimeError("Host GLES histogram readback failed.")
        observed = list((unsigned * len(expected)).from_buffer_copy(C.string_at(address, C.sizeof(zeros))))
        api(gl, "glUnmapBuffer", unsigned, unsigned)(0x90D2)
        if observed != expected:
            raise RuntimeError("Actual original histogram formula mismatch: " + json.dumps({"expected": expected, "actual": observed}))
        return {"schema": 1, "kernel": name + "/" + expected_kernel, "hostRenderer": renderer, "hostGlesVersion": version,
            "actualGlslSha256": hashlib.sha256(code + b"\0").hexdigest(), "actualHistogram": observed,
            "expectedOriginalInstructionHistogram": expected, "binaryExactInputPixelCount": source_width*source_height,
            "sourceSize": [source_width, source_height], "originalViewportSize": [16,16],
            "originalOutOfBoundsZeroReadCount": 256-source_width*source_height,
            "weightedHistogramSum": sum(observed), "actualHostDispatchPassed": True,
            "completeOriginalPixelParityVerified": False, "hardwareVerified": False}
    finally:
        api(egl, "eglTerminate", unsigned, pointer)(display)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--shader", choices=("EyeHistogram", "Waveform"), default="EyeHistogram")
    parser.add_argument("--source-width", type=int, default=16)
    parser.add_argument("--source-height", type=int, default=16)
    args = parser.parse_args()
    receipt = run(args.bundle, args.shader, args.source_width, args.source_height)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({key: receipt[key] for key in ("hostRenderer", "weightedHistogramSum", "actualHostDispatchPassed", "hardwareVerified")}))
