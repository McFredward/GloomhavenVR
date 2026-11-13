"""Execute actual cooked original kernels with odd/padded GLES image extents.

Original MSVO downsample tiles and 3D interpolation are used, including their
real UAV allocation formats and dispatch dimensions. No test shader replaces
the native kernel. This host proof is not a Quest hardware or full-image claim.
"""
from __future__ import annotations

import argparse
import ctypes as C
import hashlib
import json
import math
from pathlib import Path
import struct

from host_histogram import api


class Device:
    def __init__(self, desktop=False):
        self.egl, self.gl = C.CDLL("libEGL.so.1"), C.CDLL("libGL.so.1" if desktop else "libGLESv2.so.2")
        self.pointer, self.integer, self.unsigned = C.c_void_p, C.c_int, C.c_uint
        self.display = self.egl_api("eglGetPlatformDisplay", self.pointer, self.unsigned, self.pointer, C.POINTER(self.integer))(0x31DD, None, None)
        major, minor = self.integer(), self.integer()
        if not self.egl_api("eglInitialize", self.unsigned, self.pointer, C.POINTER(self.integer), C.POINTER(self.integer))(
                self.display, C.byref(major), C.byref(minor)):
            raise RuntimeError("Surfaceless EGL unavailable.")
        if not self.egl_api("eglBindAPI", self.unsigned, self.unsigned)(0x30A2 if desktop else 0x30A0): raise RuntimeError("GL unavailable.")
        config, count = self.pointer(), self.integer()
        if not self.egl_api("eglChooseConfig", self.unsigned, self.pointer, C.POINTER(self.integer), C.POINTER(self.pointer), self.integer, C.POINTER(self.integer))(
                self.display, (self.integer * 5)(0x3040, 8 if desktop else 0x40, 0x3033, 1, 0x3038), C.byref(config), 1, C.byref(count)) or count.value != 1:
            raise RuntimeError("GL compute configuration unavailable.")
        context_attributes = (self.integer * 7)(0x3098, 4, 0x30FB, 3, 0x30FD, 1, 0x3038) if desktop else (self.integer * 3)(0x3098, 3, 0x3038)
        context = self.egl_api("eglCreateContext", self.pointer, self.pointer, self.pointer, self.pointer, C.POINTER(self.integer))(
            self.display, config, None, context_attributes)
        surface = self.egl_api("eglCreatePbufferSurface", self.pointer, self.pointer, self.pointer, C.POINTER(self.integer))(
            self.display, config, (self.integer * 5)(0x3057, 1, 0x3056, 1, 0x3038))
        if not context or not surface or not self.egl_api("eglMakeCurrent", self.unsigned, self.pointer, self.pointer, self.pointer, self.pointer)(
                self.display, surface, surface, context):
            raise RuntimeError("GLES current context unavailable.")
        self.renderer = self.call("glGetString", C.c_char_p, self.unsigned)(0x1F01).decode()
        self.version = self.call("glGetString", C.c_char_p, self.unsigned)(0x1F02).decode()

    def call(self, name, returns, *arguments): return api(self.gl, name, returns, *arguments)
    def egl_api(self, name, returns, *arguments): return api(self.egl, name, returns, *arguments)
    def close(self): self.egl_api("eglTerminate", self.unsigned, self.pointer)(self.display)

    def check(self, where):
        error = self.call("glGetError", self.unsigned)()
        if error: raise RuntimeError(where + " failed: " + hex(error))

    def program(self, code):
        shader = self.call("glCreateShader", self.unsigned, self.unsigned)(0x91B9)
        source, status = C.c_char_p(code), self.integer()
        self.call("glShaderSource", None, self.unsigned, self.integer, C.POINTER(C.c_char_p), C.POINTER(self.integer))(shader, 1, C.byref(source), None)
        self.call("glCompileShader", None, self.unsigned)(shader)
        self.call("glGetShaderiv", None, self.unsigned, self.unsigned, C.POINTER(self.integer))(shader, 0x8B81, C.byref(status))
        if not status.value:
            log = C.create_string_buffer(8192)
            self.call("glGetShaderInfoLog", None, self.unsigned, self.integer, C.POINTER(self.integer), self.pointer)(shader, len(log), None, log)
            raise RuntimeError("Actual cooked original kernel failed host compile: " + log.value.decode())
        program = self.call("glCreateProgram", self.unsigned)()
        self.call("glAttachShader", None, self.unsigned, self.unsigned)(program, shader)
        self.call("glLinkProgram", None, self.unsigned)(program)
        self.call("glGetProgramiv", None, self.unsigned, self.unsigned, C.POINTER(self.integer))(program, 0x8B82, C.byref(status))
        if not status.value: raise RuntimeError("Actual cooked original kernel failed host link.")
        self.call("glUseProgram", None, self.unsigned)(program)
        self.check("Native kernel setup")
        return program

    def texture(self, target, internal_format, width, height, depth=1, *, values=None):
        texture = self.unsigned()
        self.call("glGenTextures", None, self.integer, C.POINTER(self.unsigned))(1, C.byref(texture))
        self.call("glBindTexture", None, self.unsigned, self.unsigned)(target, texture)
        if target == 0x0DE1:
            self.call("glTexStorage2D", None, self.unsigned, self.integer, self.unsigned, self.integer, self.integer)(target, 1, internal_format, width, height)
        else:
            self.call("glTexStorage3D", None, self.unsigned, self.integer, self.unsigned, self.integer, self.integer, self.integer)(target, 1, internal_format, width, height, depth)
        if values is not None:
            pixels = (C.c_float * len(values))(*values)
            if target == 0x0DE1:
                self.call("glTexSubImage2D", None, self.unsigned, self.integer, self.integer, self.integer, self.integer, self.integer, self.unsigned, self.unsigned, self.pointer)(
                    target, 0, 0, 0, width, height, 0x1908, 0x1406, pixels)
            else:
                self.call("glTexSubImage3D", None, self.unsigned, self.integer, self.integer, self.integer, self.integer, self.integer, self.integer, self.integer, self.unsigned, self.unsigned, self.pointer)(
                    target, 0, 0, 0, 0, width, height, depth, 0x1908, 0x1406, pixels)
        parameter = self.call("glTexParameteri", None, self.unsigned, self.unsigned, self.integer)
        for name, value in ((0x2801, 0x2600), (0x2800, 0x2600), (0x2802, 0x812F), (0x2803, 0x812F)):
            parameter(target, name, value)
        if target != 0x0DE1: parameter(target, 0x8072, 0x812F)
        self.check("Original-format texture allocation")
        return {"texture": texture.value, "target": target, "format": internal_format, "width": width, "height": height, "depth": depth}

    def sampler(self, program, name, unit, texture):
        self.call("glActiveTexture", None, self.unsigned)(0x84C0 + unit)
        self.call("glBindTexture", None, self.unsigned, self.unsigned)(texture["target"], texture["texture"])
        location = self.call("glGetUniformLocation", self.integer, self.unsigned, C.c_char_p)(program, name.encode())
        if location < 0: raise RuntimeError("Original sampled resource was removed: " + name)
        self.call("glUniform1i", None, self.integer, self.integer)(location, unit)

    def image(self, unit, texture):
        self.call("glBindImageTexture", None, self.unsigned, self.unsigned, self.integer, C.c_ubyte, self.integer, self.unsigned, self.unsigned)(
            unit, texture["texture"], 0, texture["target"] != 0x0DE1, 0, 0x88B9, texture["format"])

    def constants(self, values):
        buffer = self.unsigned()
        self.call("glGenBuffers", None, self.integer, C.POINTER(self.unsigned))(1, C.byref(buffer))
        pixels = (C.c_float * len(values))(*values)
        self.call("glBindBuffer", None, self.unsigned, self.unsigned)(0x8A11, buffer)
        self.call("glBufferData", None, self.unsigned, C.c_ssize_t, self.pointer, self.unsigned)(0x8A11, C.sizeof(pixels), pixels, 0x88E4)
        self.call("glBindBufferBase", None, self.unsigned, self.unsigned, self.unsigned)(0x8A11, 0, buffer)

    def dispatch(self, x, y, z):
        self.call("glDispatchCompute", None, self.unsigned, self.unsigned, self.unsigned)(x, y, z)
        self.call("glMemoryBarrier", None, self.unsigned)(0x2420)
        self.call("glFinish", None)()
        self.check("Original padded dispatch")

    def read(self, texture):
        framebuffer = self.unsigned()
        self.call("glGenFramebuffers", None, self.integer, C.POINTER(self.unsigned))(1, C.byref(framebuffer))
        self.call("glBindFramebuffer", None, self.unsigned, self.unsigned)(0x8D40, framebuffer)
        result = []
        for layer in range(texture["depth"]):
            if texture["target"] == 0x0DE1:
                self.call("glFramebufferTexture2D", None, self.unsigned, self.unsigned, self.unsigned, self.unsigned, self.integer)(
                    0x8D40, 0x8CE0, texture["target"], texture["texture"], 0)
            else:
                self.call("glFramebufferTextureLayer", None, self.unsigned, self.unsigned, self.unsigned, self.integer, self.integer)(
                    0x8D40, 0x8CE0, texture["texture"], 0, layer)
            if self.call("glCheckFramebufferStatus", self.unsigned, self.unsigned)(0x8D40) != 0x8CD5:
                raise RuntimeError("Original native output format cannot be read on this host.")
            pixels = (C.c_float * (texture["width"] * texture["height"] * 4))()
            self.call("glReadPixels", None, self.integer, self.integer, self.integer, self.integer, self.unsigned, self.unsigned, self.pointer)(
                0, 0, texture["width"], texture["height"], 0x1908, 0x1406, pixels)
            result += list(pixels)
            self.check("Native image readback")
        return result


def original_kernel(bundle, name, kernel):
    import UnityPy
    matches = [obj.read_typetree() for obj in UnityPy.load(str(bundle)).objects
               if obj.type.name == "ComputeShader" and obj.read_typetree()["m_Name"] == name]
    if len(matches) != 1 or len(matches[0]["variants"]) != 1 or matches[0]["variants"][0]["targetRenderer"] != 11:
        raise RuntimeError("Exact original cooked GLES compute object unavailable: " + name)
    rows = [row for row in matches[0]["variants"][0]["kernels"] if row["name"] == kernel]
    if len(rows) != 1 or len(rows[0]["variantMap"]) != 1 or rows[0]["variantMap"][0][0] != "":
        raise RuntimeError("Exact original native kernel unavailable: " + kernel)
    return bytes(rows[0]["variantMap"][0][1]["code"]).rstrip(b"\0")


def compare(name, observed, expected):
    if observed != expected:
        differences = [(index, actual, wanted) for index, (actual, wanted) in enumerate(zip(observed, expected)) if actual != wanted]
        raise RuntimeError("Original padded edge output mismatch: " + name + " " + repr(differences[:12]))
    payload = struct.pack("<" + str(len(expected)) + "f", *expected)
    return {"name": name, "componentCount": len(expected), "actualAndExpectedFloat32Sha256": hashlib.sha256(payload).hexdigest(),
            "everyComponentMatchesOriginalInstructionFormula": True}


def downsample(bundle):
    code = original_kernel(bundle, "MultiScaleVODownsample1", "MultiScaleVODownsample1")
    # The original Android GLES capability branch excludes MSVO. Its RHalf
    # UAVs require desktop GL formats. Execute the actual cooked instruction
    # body unchanged on GL4.3; only the language/unused-extension prologue is
    # adapted. This is explicitly not an actual GLES/device-kernel proof.
    if not code.startswith(b"#version 310 es\n") or b"samplerBuffer" in code:
        raise RuntimeError("MSVO desktop body proof needs the audited original GLES prologue/no sampled buffer.")
    executed = code.replace(b"#version 310 es\n", b"#version 430 core\n", 1).replace(b"#extension GL_EXT_texture_buffer : require\n", b"", 1)
    device = Device(desktop=True)
    try:
        program = device.program(executed)
        width, height = 17, 19
        # Powers of two make the original reciprocal linear-depth values exact.
        def depth(x, y): return (0.125, 0.25, 0.5, 1.0)[(x + y * 3) % 4]
        source = device.texture(0x0DE1, 0x8814, width, height, values=[component for y in range(height) for x in range(width)
            for component in (depth(x, y), 0.0, 0.0, 1.0)])
        outputs = [("LinearZ", device.texture(0x0DE1, 0x822D, width, height)),
            ("DS2x", device.texture(0x0DE1, 0x822E, (width+1)//2, (height+1)//2)),
            ("DS2xAtlas", device.texture(0x8C1A, 0x822D, (width+7)//8, (height+7)//8, 16)),
            ("DS4x", device.texture(0x0DE1, 0x822E, (width+3)//4, (height+3)//4)),
            ("DS4xAtlas", device.texture(0x8C1A, 0x822D, (width+15)//16, (height+15)//16, 16))]
        for unit, (_, texture) in enumerate(outputs): device.image(unit, texture)
        device.sampler(program, "Depth", 0, source)
        device.constants([1.0, 0.0, 0.0, 0.0])
        device.dispatch((width+15)//16, (height+15)//16, 1)
        def value(x, y): return 1.0 / depth(x, y) if x < width and y < height else 100000.0
        # Original DXBC reads g0[(localY*32)+(localX<<1)] once after
        # the barrier. It selects the top-left sample, not a guessed min/mean.
        def reduced(x, y, factor): return value(x*factor, y*factor)
        comparisons = []
        for name, texture in outputs:
            expected = []
            for layer in range(texture["depth"]):
                for y in range(texture["height"]):
                    for x in range(texture["width"]):
                        if name == "LinearZ": pixel = value(x, y)
                        elif name == "DS2x": pixel = reduced(x, y, 2)
                        elif name == "DS4x": pixel = reduced(x, y, 4)
                        else:
                            factor = 2 if name == "DS2xAtlas" else 4
                            pixel = reduced(x*4 + layer%4, y*4 + layer//4, factor)
                            # Native D3D float32->float16 storage conversion
                            # rounds toward zero and clamps finite overflow to
                            # the largest finite value (not positive infinity).
                            if pixel == 100000.0: pixel = 65504.0
                        expected.extend((pixel, 0.0, 0.0, 1.0))
            comparisons.append(compare(name, device.read(texture), expected))
        return {"kernel": "MultiScaleVODownsample1/MultiScaleVODownsample1", "sourceSize": [width, height],
            "dispatchGroups": [2, 2, 1], "nativeCoveredSourcePixels": 32*32,
            "nativeOutOfBoundsSourceReads": 32*32-width*height, "actualOutputComparisons": comparisons,
            "actualCookedGlslSha256": hashlib.sha256(code+b"\0").hexdigest(), "executedBodyAndPrologueSha256": hashlib.sha256(executed).hexdigest(),
            "onlyLanguagePrologueAdapted": True, "actualGlesDriverExecution": False,
            "originalAndroidGlesCapabilityBranchExcludesKernel": True,
            "hostRenderer": device.renderer, "hostGlVersion": device.version}
    finally: device.close()


def lerp(bundle):
    code = original_kernel(bundle, "Texture3DLerp", "KTexture3DLerp")
    device = Device()
    try:
        program = device.program(code)
        original_size, other_size = (5, 3, 2), (2, 2, 1)
        source = device.texture(0x806F, 0x8814, *original_size, values=[0.25]*math.prod(original_size)*4)
        other = device.texture(0x806F, 0x8814, *other_size, values=[0.75]*math.prod(other_size)*4)
        result = device.texture(0x806F, 0x881A, *original_size)
        device.sampler(program, "_From", 0, source)
        device.sampler(program, "_To", 1, other)
        device.image(0, result)
        device.constants([*original_size, 0.5])
        device.dispatch(2, 2, 2)  # Original max(width,height,depth) / native 4^3 group.
        expected = [value for z in range(original_size[2]) for y in range(original_size[1]) for x in range(original_size[0])
            for value in [0.5 if x < 2 and y < 2 and z < 1 else 0.125]*4]
        comparison = compare("_Output", device.read(result), expected)
        return {"kernel": "Texture3DLerp/KTexture3DLerp", "fromSize": list(original_size), "toSize": list(other_size),
            "dispatchGroups": [2,2,2], "nativeOutOfBoundsToReads": math.prod(original_size)-math.prod(other_size),
            "actualOutputComparisons": [comparison], "actualGlslSha256": hashlib.sha256(code+b"\0").hexdigest(),
            "actualGlesDriverExecution": True, "onlyLanguagePrologueAdapted": False,
            "hostRenderer": device.renderer, "hostGlesVersion": device.version}
    finally: device.close()


def vectorscope(bundle):
    clear = original_kernel(bundle, "Vectorscope", "KVectorscopeClear")
    gather = original_kernel(bundle, "Vectorscope", "KVectorscopeGather")
    device = Device()
    try:
        width, height, size = 5, 3, 5
        source = device.texture(0x0DE1, 0x8814, width, height, values=[component for y in range(height) for x in range(width)
            for component in (1.0 if (x+y)%2 == 0 else 0.0, 0.0, 0.0, 1.0)])
        buffer = device.unsigned()
        device.call("glGenBuffers", None, device.integer, C.POINTER(device.unsigned))(1, C.byref(buffer))
        device.call("glBindBuffer", None, device.unsigned, device.unsigned)(0x90D2, buffer)
        # Nonzero initial data proves the original Clear kernel covers the
        # partial 16x16 group without writing outside the 5x5 native buffer.
        initial = (device.unsigned * (size*size))(*([0x5a5a5a5a]*(size*size)))
        device.call("glBufferData", None, device.unsigned, C.c_ssize_t, device.pointer, device.unsigned)(0x90D2, C.sizeof(initial), initial, 0x88E8)
        device.call("glBindBufferBase", None, device.unsigned, device.unsigned, device.unsigned)(0x90D2, 0, buffer)
        device.program(clear)
        device.constants([width, height, size, 0.0])
        device.dispatch(1,1,1)
        program = device.program(gather)
        device.sampler(program, "_Source", 0, source)
        device.constants([width, height, size, 0.0])
        device.dispatch(1,1,1)
        device.call("glMemoryBarrier", None, device.unsigned)(0x2200)
        device.call("glBindBuffer", None, device.unsigned, device.unsigned)(0x90D2, buffer)
        address = device.call("glMapBufferRange", device.pointer, device.unsigned, C.c_ssize_t, C.c_ssize_t, device.unsigned)(0x90D2, 0, C.sizeof(initial), 1)
        if not address: raise RuntimeError("Native vectorscope readback unavailable.")
        actual = list((device.unsigned * (size*size)).from_buffer_copy(C.string_at(address, C.sizeof(initial))))
        device.call("glUnmapBuffer", device.unsigned, device.unsigned)(0x90D2)
        device.check("Native vectorscope readback")
        expected = [0]*(size*size)
        # Original native coefficients: pure red maps to Cr==1, Cb==0.331.
        # Flattened index 5*5+floor(.331*5)==26 is outside 25 elements. Black
        # maps to floor(.5*5)*5+floor(.5*5)==12. Do not clamp the colour plot.
        expected[12] = sum((x+y)%2 == 1 for y in range(height) for x in range(width))
        if actual != expected: raise RuntimeError("Original vectorscope edge counters differ: " + repr(actual))
        return {"kernel": "Vectorscope/KVectorscopeClear+KVectorscopeGather", "sourceSize": [width,height],
            "nativeBufferCount": size*size, "nativeDiscardedRedAtomicCount": width*height-expected[12],
            "originalRedAtomicIndex": 26, "actualCounters": actual, "expectedOriginalD3dCounters": expected,
            "actualGlesDriverExecution": True, "onlyLanguagePrologueAdapted": False,
            "actualClearGlslSha256": hashlib.sha256(clear+b"\0").hexdigest(),
            "actualGatherGlslSha256": hashlib.sha256(gather+b"\0").hexdigest(),
            "hostRenderer": device.renderer, "hostGlesVersion": device.version}
    finally: device.close()


def run(bundle):
    rows = [downsample(bundle), lerp(bundle), vectorscope(bundle)]
    return {"schema": 1, "actualHostPaddedEdgeDispatchPassed": True, "originalD3dZeroAndDiscardSemanticsVerified": True,
        "fullOriginalPixelParityVerified": False, "hardwareVerified": False, "kernels": rows}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    args = parser.parse_args()
    receipt = run(args.bundle)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2, sort_keys=True)+"\n", encoding="utf-8")
    print(json.dumps({key: receipt[key] for key in ("actualHostPaddedEdgeDispatchPassed", "originalD3dZeroAndDiscardSemanticsVerified", "hardwareVerified")}))
