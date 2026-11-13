"""Match the complete original interface to actual Unity-cooked Vulkan SPIR-V."""
import hashlib
if __package__:
    from .native import ComputeRecoveryError
    from .spirv import audit
else:
    from native import ComputeRecoveryError
    from spirv import audit


def validate_vulkan(manifest,objects):
    contracts={r['name']:r for r in manifest['shaders']};observed={r.get('m_Name'):r for r in objects}
    if len(observed)!=len(objects) or contracts.keys()!=observed.keys():raise ComputeRecoveryError('Vulkan native compute object census differs.')
    rows=[]
    for name,contract in contracts.items():
        variants=observed[name]['variants']
        if len(variants)!=1 or variants[0]['targetRenderer']!=21 or variants[0]['targetLevel']!=0:raise ComputeRecoveryError('Actual compute bank is not Vulkan-only: '+name)
        variant=variants[0];kernels=variant['kernels']
        if [k['name'] for k in kernels]!=[k['name'] for k in contract['kernels']]:raise ComputeRecoveryError('Actual Vulkan kernel order/identity changed.')
        kernel_rows=[]
        for kernel,expected in zip(kernels,contract['kernels']):
            programs=kernel['variantMap']
            if len(programs)!=1 or programs[0][0]!='':raise ComputeRecoveryError('Original Vulkan kernel variant changed or stripped.')
            program=programs[0][1]
            if program['threadGroupSize']!=expected['threadGroups']:raise ComputeRecoveryError('Cooked Vulkan dispatch metadata changed.')
            proof=audit(program,expected,variant['constantBuffers']);code=bytes(program['code'])
            kernel_rows.append({'name':expected['name'],'actualSpirvSha256':hashlib.sha256(code).hexdigest(),'actualCodeBytes':len(code),**proof})
        rows.append({'name':name,'assetPath':contract['assetPath'],'guid':contract['guid'],'originalLocalFileId':contract['localFileId'],
                     'nativeRenderer':21,'nativeTargetLevel':0,'kernels':kernel_rows,'nativePlatformCapabilityEvidence':contract.get('nativePlatformCapabilityEvidence')})
    return {'schema':1,'graphicsApi':'Vulkan','shaderCount':len(rows),'kernelCount':sum(len(r['kernels']) for r in rows),
            'androidCompiled':True,'actualVulkanSpirvBytesVerified':True,'actualExecutableBytesVerified':True,
            'originalKernelIdentitiesRetained':True,'originalThreadGroupsRetained':True,'nativeImageAllocationsMatched':True,
            'originalNativePlatformCapabilityBranchesRetained':True,'allKernelsActualVulkanDriverValidated':False,
            'compilationEvidence':'Actual Unity Android Vulkan SPIR-V and binding/layout metadata; separate host driver proof required',
            'originalPixelParityVerified':False,'hardwareVerified':False,'shaders':rows}
