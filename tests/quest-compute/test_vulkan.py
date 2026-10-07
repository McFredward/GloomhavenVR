"""Synthetic SPIR-V reflection fixtures; no fabricated driver/compiler claims."""
import copy
import importlib.util
from pathlib import Path
import struct
import sys
import unittest
ROOT=Path(__file__).resolve().parents[2]
PACKAGE=ROOT/'tools/quest-compute'
if 'quest_compute_test' not in sys.modules:
    spec=importlib.util.spec_from_file_location('quest_compute_test',PACKAGE/'__init__.py',submodule_search_locations=[str(PACKAGE)])
    module=importlib.util.module_from_spec(spec);sys.modules[spec.name]=module;spec.loader.exec_module(module)
from quest_compute_test.spirv import Module,audit,coordinate
from quest_compute_test.native import ComputeRecoveryError
from quest_compute_test.formats import native_platform_contract


def literal(text):
    data=text.encode()+b'\0';data+=b'\0'*((-len(data))%4)
    return list(struct.unpack('<'+'I'*(len(data)//4),data))


def fixture(stride=16,format=None,uniform_offset=0):
    words=[0x07230203,0x10000,0,100,0]
    def emit(op,*args):words.extend([((1+len(args))<<16)|op,*args])
    emit(17,1);emit(15,5,1,*literal('main'));emit(16,1,17,8,4,1)
    emit(22,2,32);emit(23,3,2,4)
    if format is None:
        emit(21,4,32,0);emit(29,5,4);emit(71,5,6,stride);emit(30,6,5);emit(71,6,3);emit(72,6,0,35,0);emit(32,7,2,6);emit(59,7,8,2)
    else:
        emit(25,6,2,1,0,0,0,2,format);emit(32,7,0,6);emit(59,7,8,0)
    emit(5,8,*literal('_Output_origX0X'));emit(71,8,34,0);emit(71,8,33,1)
    emit(30,9,3);emit(71,9,2);emit(6,9,0,*literal('_Params'));emit(72,9,0,35,uniform_offset);emit(32,10,2,9);emit(59,10,11,2);emit(5,11,*literal('_'));emit(71,11,34,1);emit(71,11,33,0)
    code=struct.pack('<'+'I'*len(words),*words)
    storage={'glslImageQualifier':'r16f','renderTextureFormat':'RHalf'} if format is not None else None
    output={'name':'_Output','strideBytes':16,'dimension':2 if storage else -1,'imageStorage':storage}
    program={'code':list(code),'cbs':[{'name':'CGlobals','bindPoint':65536}], 'cbVariantIndices':[0], 'textures':[], 'builtinSamplers':[], 'inBuffers':[],
             'outBuffers':[{'name':'_Output','bindPoint':33554433,'texDimension':output['dimension']}]}
    expected={'threadGroups':[8,4,1],'outputBindings':[output],'interface':{'bindings':[], 'buffers':[{'fields':[{'name':'_Params','byteOffset':0,'type':0,'columns':4,'rows':1,'arraySize':0}]}]}}
    cbs=[{'params':[{'name':'_Params','offset':0}]}]
    return program,expected,cbs


class VulkanTests(unittest.TestCase):
    def test_raw_executable_local_sizes_and_packed_coordinates(self):
        program,expected,cbs=fixture();proof=audit(program,expected,cbs)
        self.assertEqual(proof['threadGroups'],[8,4,1]);self.assertEqual(proof['outputs'][0]['strideBytes'],16)
        self.assertEqual(coordinate({'bindPoint':0x02000001}),(0,1));self.assertEqual(coordinate({'bindPoint':0x00010000}),(1,0))

    def test_real_typed_rhalf_format_is_retained(self):
        program,expected,cbs=fixture(format=9);proof=audit(program,expected,cbs)
        self.assertEqual(proof['outputs'][0]['imageFormat'],'r16f')
        expected['outputBindings'][0]['imageStorage']['glslImageQualifier']='r32f'
        with self.assertRaisesRegex(ComputeRecoveryError,'format'):audit(program,expected,cbs)

    def test_native_stride_and_uniform_offset_drift_fail(self):
        program,expected,cbs=fixture(stride=4)
        with self.assertRaisesRegex(ComputeRecoveryError,'stride'):audit(program,expected,cbs)
        program,expected,cbs=fixture(uniform_offset=16)
        with self.assertRaisesRegex(ComputeRecoveryError,'layout'):audit(program,expected,cbs)

    def test_cooked_dispatch_or_binding_drift_fail(self):
        program,expected,cbs=fixture();expected['threadGroups'][0]=16
        with self.assertRaisesRegex(ComputeRecoveryError,'LocalSize'):audit(program,expected,cbs)
        program,expected,cbs=fixture();program['outBuffers'][0]['bindPoint']=33554435
        with self.assertRaisesRegex(ComputeRecoveryError,'coordinates'):audit(program,expected,cbs)

    def test_truncated_unknown_version_and_unproven_capability_fail(self):
        program,expected,cbs=fixture();code=bytes(program['code'])
        with self.assertRaises(ComputeRecoveryError):Module(code[:-4])
        changed=bytearray(code);struct.pack_into('<I',changed,4,0x10300)
        with self.assertRaisesRegex(ComputeRecoveryError,'1.0'):Module(changed)
        changed=bytearray(code);struct.pack_into('<I',changed,24,10)
        with self.assertRaisesRegex(ComputeRecoveryError,'capability'):Module(changed)

    def test_builder_temporary_path_loader_retains_vulkan_backend(self):
        # The builder restores sys.path after executing its isolated module.
        spec=importlib.util.spec_from_file_location('quest_compute_builder_fixture',PACKAGE/'compiled.py')
        isolated=importlib.util.module_from_spec(spec)
        sys.path.insert(0,str(PACKAGE))
        try:spec.loader.exec_module(isolated)
        finally:sys.path.pop(0)
        program,expected,cbs=fixture()
        contracts=[];objects=[]
        for index in range(13):
            count=2 if index>=10 else 3
            kernels=[dict(copy.deepcopy(expected),name='Kernel'+str(k)) for k in range(count)]
            contracts.append({'name':'Object'+str(index),'assetPath':'Assets/Fixture.compute','guid':'0'*32,'localFileId':7200000,'kernels':kernels})
            objects.append({'m_Name':'Object'+str(index),'variants':[{'targetRenderer':21,'targetLevel':0,'constantBuffers':cbs,
                'kernels':[{'name':k['name'],'variantMap':[['',dict(program,threadGroupSize=expected['threadGroups'])]]} for k in kernels]}]})
        manifest={'schema':1,'graphicsApi':'Vulkan','shaderCount':13,'kernelCount':36,'shaders':contracts}
        receipt=isolated.validate_objects(manifest,objects)
        self.assertEqual(receipt['kernelCount'],36)
        self.assertTrue(receipt['actualVulkanSpirvBytesVerified'])
        self.assertFalse(receipt['allKernelsActualVulkanDriverValidated'])
        self.assertFalse(receipt['hardwareVerified'])

    def test_vulkan_does_not_inherit_original_gles_msvo_exclusion(self):
        evidence=native_platform_contract('MultiScaleVODownsample1')
        self.assertFalse(evidence['androidOpenGlesBranchReachable'])
        self.assertTrue(evidence['androidVulkanBranchReachableSubjectToOriginalCapabilities'])
        self.assertEqual(evidence['requiredLoadStoreFormats'],['R32_SFloat','R16_SFloat','R8_UNorm'])

if __name__=='__main__':unittest.main()
