/* Host-only native graphics pipeline census. Receives unchanged Unity-cooked
 * SPIR-V and exact reflected descriptors/locations. No shader code generation.
 */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <vulkan/vulkan.h>
#define LIMIT 128
#define CHECK(call) do { VkResult result=(call); if(result!=VK_SUCCESS){fprintf(stderr,"%s failed: %d\n",#call,result);return 2;} } while(0)
static uint32_t *code(const char *path, size_t *size) {
    FILE *file=fopen(path,"rb"); if(!file)return NULL;
    fseek(file,0,SEEK_END);long count=ftell(file); rewind(file);
    if(count<20||count%4){fclose(file);return NULL;}
    uint32_t *bytes=malloc(count); if(!bytes||fread(bytes,1,count,file)!=(size_t)count){fclose(file);free(bytes);return NULL;}
    fclose(file);*size=count;return bytes;
}
int main(int argc,char **argv) {
    if(argc!=2)return 2;
    FILE *file=fopen(argv[1],"r");if(!file)return 2;
    char vertexPath[2048],fragmentPath[2048];uint32_t nr,na,nc,stride,viewmask;
    if(fscanf(file,"%2047s %2047s %u %u %u %u %u",vertexPath,fragmentPath,&nr,&na,&nc,&stride,&viewmask)!=7||nr>LIMIT||na>LIMIT||nc>8)return 2;
    VkDescriptorSetLayoutBinding bindings[4][LIMIT]={0};uint32_t counts[4]={0},setcount=0;
    for(uint32_t i=0;i<nr;i++) {
        uint32_t set,binding,kind,count,stages;
        if(fscanf(file,"%u %u %u %u %u",&set,&binding,&kind,&count,&stages)!=5||set>3||kind>7||!count||counts[set]>=LIMIT)return 2;
        bindings[set][counts[set]++]=(VkDescriptorSetLayoutBinding){.binding=binding,.descriptorType=(VkDescriptorType)kind,.descriptorCount=count,.stageFlags=stages};
        if(set+1>setcount)setcount=set+1;
    }
    VkVertexInputAttributeDescription attributes[LIMIT]={0};
    for(uint32_t i=0;i<na;i++)if(fscanf(file,"%u %u %u",&attributes[i].location,(uint32_t*)&attributes[i].format,&attributes[i].offset)!=3)return 2;
    VkAttachmentDescription attachments[9]={0};VkAttachmentReference color[8]={0};
    for(uint32_t i=0;i<nc;i++) {
        uint32_t format;if(fscanf(file,"%u",&format)!=1)return 2;
        attachments[i]=(VkAttachmentDescription){.format=(VkFormat)format,.samples=VK_SAMPLE_COUNT_1_BIT,.loadOp=VK_ATTACHMENT_LOAD_OP_DONT_CARE,.storeOp=VK_ATTACHMENT_STORE_OP_DONT_CARE,.stencilLoadOp=VK_ATTACHMENT_LOAD_OP_DONT_CARE,.stencilStoreOp=VK_ATTACHMENT_STORE_OP_DONT_CARE,.initialLayout=VK_IMAGE_LAYOUT_UNDEFINED,.finalLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL};
        color[i]=(VkAttachmentReference){i,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL};
    }
    fclose(file);
    attachments[nc]=(VkAttachmentDescription){.format=VK_FORMAT_D32_SFLOAT,.samples=VK_SAMPLE_COUNT_1_BIT,.loadOp=VK_ATTACHMENT_LOAD_OP_DONT_CARE,.storeOp=VK_ATTACHMENT_STORE_OP_DONT_CARE,.stencilLoadOp=VK_ATTACHMENT_LOAD_OP_DONT_CARE,.stencilStoreOp=VK_ATTACHMENT_STORE_OP_DONT_CARE,.initialLayout=VK_IMAGE_LAYOUT_UNDEFINED,.finalLayout=VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL};
    VkAttachmentReference depth={nc,VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL};
    VkApplicationInfo app={.sType=VK_STRUCTURE_TYPE_APPLICATION_INFO,.pApplicationName="Quest native graphics pipeline proof",.apiVersion=VK_API_VERSION_1_2};
    VkInstanceCreateInfo instanceInfo={.sType=VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,.pApplicationInfo=&app};VkInstance instance;CHECK(vkCreateInstance(&instanceInfo,NULL,&instance));
    uint32_t deviceCount=0;CHECK(vkEnumeratePhysicalDevices(instance,&deviceCount,NULL));if(!deviceCount)return 2;
    VkPhysicalDevice *physicalDevices=calloc(deviceCount,sizeof(*physicalDevices));CHECK(vkEnumeratePhysicalDevices(instance,&deviceCount,physicalDevices));VkPhysicalDevice physical=physicalDevices[0];free(physicalDevices);
    VkPhysicalDeviceProperties properties;vkGetPhysicalDeviceProperties(physical,&properties);
    VkPhysicalDeviceVulkan12Features v12={.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES};
    VkPhysicalDeviceMultiviewFeatures multiview={.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MULTIVIEW_FEATURES,.pNext=&v12};
    VkPhysicalDeviceFeatures2 features={.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,.pNext=&multiview};vkGetPhysicalDeviceFeatures2(physical,&features);
    if(viewmask&&!multiview.multiview){fprintf(stderr,"Native multiview capability missing\n");return 2;}
    uint32_t queueCount=0;vkGetPhysicalDeviceQueueFamilyProperties(physical,&queueCount,NULL);VkQueueFamilyProperties *queues=calloc(queueCount,sizeof(*queues));vkGetPhysicalDeviceQueueFamilyProperties(physical,&queueCount,queues);uint32_t family=0;while(family<queueCount&&!(queues[family].queueFlags&VK_QUEUE_GRAPHICS_BIT))family++;free(queues);if(family==queueCount)return 2;
    float priority=1;VkDeviceQueueCreateInfo queue={.sType=VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,.queueFamilyIndex=family,.queueCount=1,.pQueuePriorities=&priority};
    VkDeviceCreateInfo deviceInfo={.sType=VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,.pNext=&features,.queueCreateInfoCount=1,.pQueueCreateInfos=&queue};VkDevice device;CHECK(vkCreateDevice(physical,&deviceInfo,NULL,&device));
    VkDescriptorSetLayout layouts[4];
    for(uint32_t i=0;i<setcount;i++){VkDescriptorSetLayoutCreateInfo info={.sType=VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO,.bindingCount=counts[i],.pBindings=bindings[i]};CHECK(vkCreateDescriptorSetLayout(device,&info,NULL,&layouts[i]));}
    VkPipelineLayoutCreateInfo layoutInfo={.sType=VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,.setLayoutCount=setcount,.pSetLayouts=layouts};VkPipelineLayout layout;CHECK(vkCreatePipelineLayout(device,&layoutInfo,NULL,&layout));
    VkSubpassDescription subpass={.pipelineBindPoint=VK_PIPELINE_BIND_POINT_GRAPHICS,.colorAttachmentCount=nc,.pColorAttachments=color,.pDepthStencilAttachment=&depth};
    VkRenderPassMultiviewCreateInfo views={.sType=VK_STRUCTURE_TYPE_RENDER_PASS_MULTIVIEW_CREATE_INFO,.subpassCount=1,.pViewMasks=&viewmask};
    VkRenderPassCreateInfo renderInfo={.sType=VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO,.pNext=viewmask?&views:NULL,.attachmentCount=nc+1,.pAttachments=attachments,.subpassCount=1,.pSubpasses=&subpass};VkRenderPass renderPass;CHECK(vkCreateRenderPass(device,&renderInfo,NULL,&renderPass));
    VkShaderModule modules[2];const char *paths[]={vertexPath,fragmentPath};
    for(uint32_t i=0;i<2;i++){size_t bytes=0;uint32_t *native=code(paths[i],&bytes);if(!native)return 2;VkShaderModuleCreateInfo info={.sType=VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO,.codeSize=bytes,.pCode=native};CHECK(vkCreateShaderModule(device,&info,NULL,&modules[i]));free(native);}
    VkPipelineShaderStageCreateInfo stages[2]={{.sType=VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,.stage=VK_SHADER_STAGE_VERTEX_BIT,.module=modules[0],.pName="main"},{.sType=VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,.stage=VK_SHADER_STAGE_FRAGMENT_BIT,.module=modules[1],.pName="main"}};
    VkVertexInputBindingDescription vertexBinding={0,stride,VK_VERTEX_INPUT_RATE_VERTEX};
    VkPipelineVertexInputStateCreateInfo vertexInput={.sType=VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO,.vertexBindingDescriptionCount=na?1:0,.pVertexBindingDescriptions=&vertexBinding,.vertexAttributeDescriptionCount=na,.pVertexAttributeDescriptions=attributes};
    VkPipelineInputAssemblyStateCreateInfo assembly={.sType=VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO,.topology=VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST};
    VkPipelineViewportStateCreateInfo viewport={.sType=VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO,.viewportCount=1,.scissorCount=1};
    VkPipelineRasterizationStateCreateInfo raster={.sType=VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO,.polygonMode=VK_POLYGON_MODE_FILL,.cullMode=VK_CULL_MODE_NONE,.frontFace=VK_FRONT_FACE_COUNTER_CLOCKWISE,.lineWidth=1};
    VkPipelineMultisampleStateCreateInfo sample={.sType=VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO,.rasterizationSamples=VK_SAMPLE_COUNT_1_BIT};
    VkPipelineDepthStencilStateCreateInfo depthState={.sType=VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO,.depthCompareOp=VK_COMPARE_OP_ALWAYS};
    VkPipelineColorBlendAttachmentState blend[8]={0};for(uint32_t i=0;i<nc;i++)blend[i].colorWriteMask=15;
    VkPipelineColorBlendStateCreateInfo blendState={.sType=VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO,.attachmentCount=nc,.pAttachments=blend};
    VkDynamicState dynamicStates[]={VK_DYNAMIC_STATE_VIEWPORT,VK_DYNAMIC_STATE_SCISSOR};VkPipelineDynamicStateCreateInfo dynamic={.sType=VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO,.dynamicStateCount=2,.pDynamicStates=dynamicStates};
    VkGraphicsPipelineCreateInfo pipelineInfo={.sType=VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO,.stageCount=2,.pStages=stages,.pVertexInputState=&vertexInput,.pInputAssemblyState=&assembly,.pViewportState=&viewport,.pRasterizationState=&raster,.pMultisampleState=&sample,.pDepthStencilState=&depthState,.pColorBlendState=&blendState,.pDynamicState=&dynamic,.layout=layout,.renderPass=renderPass,.subpass=0};VkPipeline pipeline;CHECK(vkCreateGraphicsPipelines(device,VK_NULL_HANDLE,1,&pipelineInfo,NULL,&pipeline));
    printf("{\"actualPipelineCreated\":true,\"deviceName\":\"%s\",\"vendorId\":%u,\"deviceId\":%u,\"apiVersion\":%u,\"shaderOutputLayerEnabled\":%s,\"multiviewEnabled\":%s}",properties.deviceName,properties.vendorID,properties.deviceID,properties.apiVersion,v12.shaderOutputLayer?"true":"false",multiview.multiview?"true":"false");
    vkDestroyPipeline(device,pipeline,NULL);for(uint32_t i=0;i<2;i++)vkDestroyShaderModule(device,modules[i],NULL);vkDestroyRenderPass(device,renderPass,NULL);vkDestroyPipelineLayout(device,layout,NULL);for(uint32_t i=0;i<setcount;i++)vkDestroyDescriptorSetLayout(device,layouts[i],NULL);vkDestroyDevice(device,NULL);vkDestroyInstance(instance,NULL);return 0;
}
