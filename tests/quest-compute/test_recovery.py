"""Focused non-proprietary fixtures for native compute interface/safety contracts."""
import copy
import importlib.util
from pathlib import Path
import struct
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / "tools/quest-compute"
spec = importlib.util.spec_from_file_location("quest_compute_test", PACKAGE / "__init__.py", submodule_search_locations=[str(PACKAGE)])
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)
from quest_compute_test.addressing import scalarize, restore_integer_addresses
from quest_compute_test.adapter import declarations, restore
from quest_compute_test.compiled import validate_objects
from quest_compute_test.formats import image_contract
from quest_compute_test.native import ComputeRecoveryError, parse


class GraphicsFixture:
    @staticmethod
    def dxbc_container(program):
        return program, {b"SHEX": program}

    @staticmethod
    def native_buffer_layouts(program):
        return []

    @staticmethod
    def restore_uniforms(hlsl, interface, resource_layouts):
        return hlsl, []


def program(kind=0x9e, stride=16, groups=(1, 1, 1)):
    words = [0x50050, 0, (4 << 24) | 0x9b, *groups, (4 << 24) | kind, 0x11e000, 0, stride]
    words[1] = len(words)
    return struct.pack("<" + str(len(words)) + "I", *words)


def kernel(name="KWaveformGather", shader="Waveform", output="_WaveformBuffer", dimension=-1, kind=0x9e, stride=16):
    return {"name": name, "shaderName": shader, "code": program(kind, stride), "threadGroups": [1, 1, 1],
        "interface": {"buffers": [], "bindings": []},
        "outputs": [{"name": output, "slot": 0, "dimension": dimension, "samplerSlot": -1}]}


YAML = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!72 &7200000
ComputeShader:
  m_Name: Fixture
  variants:
  - serializedVersion: 2
    targetRenderer: 2
    targetLevel: 0
    kernels:
    - serializedVersion: 2
      name: FixtureKernel
      variantMap:
        :
          serializedVersion: 2
          cbVariantIndices:
          cbs: []
          textures: []
          builtinSamplers: []
          inBuffers: []
          outBuffers: []
          code: 44584243
          threadGroupSize: 010000000100000001000000
          requirements: 0
          keywords: []
          isCompiled: 0
      globalKeywords: []
      localKeywords: []
    constantBuffers: []
    resourcesResolved: 0
    compilerPlatform: 0
    needsReflectionData: 0
  m_CompilationContext:
"""


class NativeTests(unittest.TestCase):
    def test_exact_metadata_shape(self):
        parsed = parse(YAML)
        self.assertEqual(parsed["name"], "Fixture")
        self.assertEqual(parsed["kernels"][0]["threadGroups"], [1, 1, 1])
        self.assertEqual(parsed["kernels"][0]["code"], b"DXBC")

    def test_unknown_renderer_keywords_fields_and_dispatch_fail(self):
        changes = [("targetRenderer: 2", "targetRenderer: 11"), ("keywords: []", "keywords: [EXTRA]"),
            ("requirements: 0", "requirements: 0\n          unknownField: 1"),
            ("010000000100000001000000", "000000000100000001000000"),
            ("!u!72 &7200000", "!u!72 &7200001"), ("targetLevel: 0", "targetLevel: 2")]
        for before, after in changes:
            with self.subTest(after=after), self.assertRaises(ComputeRecoveryError):
                parse(YAML.replace(before, after))

    def test_native_group_and_stride_declarations_are_checked(self):
        self.assertEqual(declarations(program(), GraphicsFixture)["outputs"][0]["strideBytes"], 16)
        for bad in (program(stride=8), program()[:-4]):
            with self.assertRaises(ComputeRecoveryError): declarations(bad, GraphicsFixture)

    def test_waveform_keeps_native_four_word_stride_and_atomic_address(self):
        text = "RWBuffer<uint> u0 : register(u0);\nvoid comp_main()\n{\n    InterlockedAdd(u0[(7u * 4u) + 2u], 1u);\n}\n[numthreads(1, 1, 1)]\nvoid main()\n{\n    comp_main();\n}\n"
        result, proof = restore(text, kernel(), GraphicsFixture)
        self.assertIn("RWStructuredBuffer<QuestOriginalComputeWords4> _WaveformBuffer", result)
        self.assertIn("_WaveformBuffer[((7u * 4u) + 2u) / 4u].words[((7u * 4u) + 2u) % 4u]", result)
        self.assertIn("InterlockedAdd", result)
        self.assertEqual(proof["outputBindings"][0]["strideBytes"], 16)

    def test_native_texture_allocations_restore_stored_channels(self):
        text = "RWTexture2D<float4> u0 : register(u0);\nvoid comp_main()\n{\n    u0[int2(0, 0)] = r0.xxxx;\n}\n[numthreads(1, 1, 1)]\nvoid main()\n{\n    comp_main();\n}\n"
        result, proof = restore(text, kernel("KAutoExposureAvgLuminance_fixed", "AutoExposure", "_Destination", 2, 0x9c, 0x5555), GraphicsFixture)
        self.assertIn("RWTexture2D<float> _Destination", result)
        self.assertIn("(r0.xxxx).x;", result)
        self.assertEqual(proof["outputBindings"][0]["imageStorage"]["glslImageQualifier"], "r32f")

    def test_unknown_output_and_kernel_dispatch_do_not_silently_change(self):
        value = kernel(); value["threadGroups"] = [2, 1, 1]
        with self.assertRaises(ComputeRecoveryError): restore("", value, GraphicsFixture)
        with self.assertRaises(ComputeRecoveryError): image_contract("Unknown", "Unknown", "Unknown")
        self.assertEqual(image_contract("MultiScaleVODownsample1", "MultiScaleVODownsample1_MSAA", "LinearZ")["glslImageQualifier"], "rg16f")
        self.assertEqual(image_contract("MultiScaleVORender", "MultiScaleVORender_interleaved", "Occlusion")["glslImageQualifier"], "r8")


class IntegerIdentityTests(unittest.TestCase):
    def test_signedness_and_hex_literals_are_preserved(self):
        for expression, kind in (("int(gl_LocalInvocationID.x) >> 2u", "int"),
            ("uint(gl_LocalInvocationID.x) >> 2", "uint"), ("uint(0x12f)", "uint")):
            result = scalarize(expression, {})
            self.assertEqual(result[0].kind, kind)
        self.assertIn("303", scalarize("uint(0x12f)", {})[0].text)
        self.assertEqual(scalarize("0.5f", {})[0].text, "uint(0x3f000000)")
        self.assertEqual(scalarize("0.0f", {})[0].text, "0u")
        self.assertEqual(scalarize("-0.0f", {})[0].text, "uint(0x80000000)")
        self.assertEqual(scalarize("0.5f * textureValue", {}), None)

    def test_shared_and_final_writer_addresses_expose_only_thread_integer_identity(self):
        text = "groupshared uint g0[128];\nRWTexture2D<float4> Output;\nvoid comp_main()\n{\n    float4 r0;\n    r0.x = asfloat(int(gl_LocalInvocationID.x) + int(gl_LocalInvocationID.y) * 16);\n    g0[uint(asint(r0.x))] = 7u;\n    if (asuint(r0.x) == 0u)\n    {\n        Output[int2(0, 0)] = textureValue;\n    }\n}\n"
        restored, proof = restore_integer_addresses(text)
        self.assertIn("g0[uint(", restored)
        self.assertNotIn("if (asuint(r0.x)", restored)
        self.assertIn("= textureValue;", restored)
        self.assertTrue(all(item["integerBitcastIdentityOnly"] for item in proof))

    def test_unknown_float_and_compound_mutation_block_propagation(self):
        text = "RWTexture2D<float4> Output;\nvoid comp_main()\n{\n    float4 r0;\n    r0.x = asfloat(int(gl_LocalInvocationID.x));\n    r0.x += sourceFloat;\n    Output[asint(r0.x)] = value;\n}\n"
        result, proof = restore_integer_addresses(text)
        self.assertIn("Output[asint(r0.x)]", result)
        self.assertEqual(proof, [])

    def test_texture_dependent_integer_bits_keep_original_expression_and_float_consumer(self):
        text = "RWStructuredBuffer<uint> Output;\nvoid comp_main()\n{\n    float4 r0;\n    r0.w = asfloat(uint(textureDependentWeight));\n    originalFloatConsumer(r0.w);\n    InterlockedAdd(Output[0], asuint(r0.w));\n}\n"
        result, proof = restore_integer_addresses(text)
        self.assertIn("uint QuestOriginalIntegerBits_0 = uint(uint(textureDependentWeight));", result)
        self.assertIn("r0.w = asfloat(QuestOriginalIntegerBits_0);", result)
        self.assertIn("originalFloatConsumer(r0.w);", result)
        self.assertIn("InterlockedAdd(Output[0], (QuestOriginalIntegerBits_0));", result)
        self.assertEqual(sum("textureDependentWeight" in line for line in result.splitlines()), 1)
        self.assertTrue(any(item.get("exactIntegerExpressionCapturedOnce") for item in proof))

    def test_branch_mutation_invalidates_only_written_components(self):
        text = "RWTexture2D<float4> Output;\nvoid comp_main()\n{\n    float4 r0;\n    r0.x = asfloat(int(gl_LocalInvocationID.x));\n    r0.y = asfloat(int(gl_LocalInvocationID.y));\n    if (unknown)\n    {\n        r0.y = asfloat(7);\n    }\n    Output[asint(r0.x)] = value;\n    Output[asint(r0.y)] = value;\n}\n"
        result, proof = restore_integer_addresses(text)
        self.assertNotIn("Output[asint(r0.x)]", result)
        self.assertIn("Output[asint(r0.y)]", result)
        self.assertGreater(len(proof), 0)

    def test_future_loops_and_unknown_integer_helpers_require_reaudit(self):
        for text in ("while (condition)\n{\n}\n", "uint spvBitfieldInsert(uint a)\n{ return 0; }\n"):
            with self.assertRaises(ValueError): restore_integer_addresses(text)

    def test_subnormal_native_structured_byte_offsets_are_not_flushed(self):
        text = "RWStructuredBuffer<uint> Output;\nvoid comp_main()\n{\n    float4 r0;\n    r0.x = 5.6051938572992682836949183331597e-45f;\n    r0.y = 1.1210387714598536567389836666319e-44f;\n    InterlockedAdd(Output[uint(asint(r0.x)) >> 2u], 1u);\n    InterlockedAdd(Output[uint(asint(r0.y)) >> 2u], 1u);\n}\n"
        result, proof = restore_integer_addresses(text)
        self.assertNotIn("asint(r0.x)", result)
        self.assertNotIn("asint(r0.y)", result)
        self.assertIn("uint(4)", result)
        self.assertIn("uint(8)", result)
        self.assertIn("5.6051938572992682836949183331597e-45f;", result)


def cooked_fixture():
    counts = [2, 2, 2, 2, 2, 2, 2, 2, 10, 4, 4, 1, 1]
    manifest, objects = {"schema": 1, "shaderCount": 13, "kernelCount": 36, "shaders": []}, []
    for index, count in enumerate(counts):
        name = "SyntheticFixture" + str(index)
        contracts, actuals = [], []
        for kernel_index in range(count):
            kernel_name = "Kernel" + str(kernel_index)
            storage = {"renderTextureFormat": "RFloat", "glslImageQualifier": "r32f"}
            contracts.append({"name": kernel_name, "threadGroups": [1, 2, 1], "interface": {"bindings": []},
                "outputBindings": [{"name": "Result", "dimension": 2, "imageStorage": storage}]})
            code = b"#version 310 es\nlayout(local_size_x = 1, local_size_y = 2, local_size_z = 1) in;\nwriteonly layout(binding=0, r32f) highp uniform image2D Result;\nvoid main() {}\n\0"
            actuals.append({"name": kernel_name, "variantMap": [("", {"threadGroupSize": [1, 2, 1], "code": list(code),
                "outBuffers": [{"name": "Result", "texDimension": 2}], "textures": [], "inBuffers": []})]})
        manifest["shaders"].append({"name": name, "assetPath": "Assets/" + name + ".compute", "guid": str(index).zfill(32), "localFileId": 7200000, "kernels": contracts})
        objects.append({"m_Name": name, "variants": [{"targetRenderer": 11, "targetLevel": 3, "kernels": actuals}]})
    return manifest, objects


class CookedByteTests(unittest.TestCase):
    def test_actual_gles_code_dispatch_and_image_format_are_required(self):
        manifest, objects = cooked_fixture()
        receipt = validate_objects(manifest, objects)
        self.assertEqual((receipt["shaderCount"], receipt["kernelCount"]), (13, 36))
        self.assertFalse(receipt["hardwareVerified"])
        self.assertFalse(receipt["originalPixelParityVerified"])
        mutations = [lambda v: v[0]["variants"][0].update(targetRenderer=2),
            lambda v: v[0]["variants"][0]["kernels"].pop(),
            lambda v: v[0]["variants"][0]["kernels"][0]["variantMap"][0][1].update(threadGroupSize=[2, 2, 1]),
            lambda v: v[0]["variants"][0]["kernels"][0]["variantMap"][0][1].update(code=list(b"DXBC")),
            lambda v: v[0]["variants"][0]["kernels"][0]["variantMap"][0][1].update(code=list(bytes(v[0]["variants"][0]["kernels"][0]["variantMap"][0][1]["code"]).replace(b"r32f", b"rgba32f")))]
        for mutate in mutations:
            values = copy.deepcopy(objects); mutate(values)
            with self.subTest(mutation=mutate), self.assertRaises(ComputeRecoveryError): validate_objects(manifest, values)


if __name__ == "__main__": unittest.main()
