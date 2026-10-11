#define XR_NO_PROTOTYPES
#include <openxr/openxr.h>
#include <cassert>
#include <cstring>
#include <iostream>

extern "C" {
void* ghvr_quest_hook(void*);
int ghvr_quest_instance(uint64_t);
int ghvr_quest_session(uint64_t);
void ghvr_quest_running(int);
int ghvr_quest_enable(int);
int ghvr_quest_status();
int ghvr_quest_error();
void ghvr_quest_destroy_session();
void ghvr_quest_destroy_instance();
}

namespace {
int checks = 0, submissions = 0, destroyed_passes = 0, destroyed_layers = 0;
bool fail_layer = false, omit_function = false, expect_underlay = false;
const char* omitted_name = "xrCreatePassthroughFB";
const XrFrameEndInfo* source_info = nullptr;
const XrCompositionLayerProjection* source_projection = nullptr;
void check(bool result) { ++checks; assert(result); }
XrResult XRAPI_CALL make_pass(XrSession, const XrPassthroughCreateInfoFB* info, XrPassthroughFB* output) {
    check(info->type == XR_TYPE_PASSTHROUGH_CREATE_INFO_FB);
    *output = reinterpret_cast<XrPassthroughFB>(uintptr_t(42));
    return XR_SUCCESS;
}
XrResult XRAPI_CALL delete_pass(XrPassthroughFB) { ++destroyed_passes; return XR_SUCCESS; }
XrResult XRAPI_CALL start(XrPassthroughFB) { return XR_SUCCESS; }
XrResult XRAPI_CALL pause(XrPassthroughFB) { return XR_SUCCESS; }
XrResult XRAPI_CALL make_layer(XrSession, const XrPassthroughLayerCreateInfoFB* info, XrPassthroughLayerFB* output) {
    check(info->purpose == XR_PASSTHROUGH_LAYER_PURPOSE_RECONSTRUCTION_FB);
    if (fail_layer) return XR_ERROR_RUNTIME_FAILURE;
    *output = reinterpret_cast<XrPassthroughLayerFB>(uintptr_t(43));
    return XR_SUCCESS;
}
XrResult XRAPI_CALL delete_layer(XrPassthroughLayerFB) { ++destroyed_layers; return XR_SUCCESS; }
XrResult XRAPI_CALL resume(XrPassthroughLayerFB) { return XR_SUCCESS; }
XrResult XRAPI_CALL pause_l(XrPassthroughLayerFB) { return XR_SUCCESS; }
XrResult XRAPI_CALL end(XrSession, const XrFrameEndInfo* info) {
    ++submissions;
    if (!expect_underlay) check(info == source_info);
    else {
        check(info != source_info);
        check(info->layerCount == source_info->layerCount + 1);
        check(info->environmentBlendMode == source_info->environmentBlendMode);
        check(info->displayTime == source_info->displayTime);
        check(info->layers[0]->type == XR_TYPE_COMPOSITION_LAYER_PASSTHROUGH_FB);
        auto pass = reinterpret_cast<const XrCompositionLayerPassthroughFB*>(info->layers[0]);
        check(pass->layerHandle != XR_NULL_HANDLE && pass->space == XR_NULL_HANDLE);
        auto projection = reinterpret_cast<const XrCompositionLayerProjection*>(info->layers[1]);
        check(projection != source_projection);
        check(projection->layerFlags & XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT);
        check(!(source_projection->layerFlags & XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT));
        check(projection->views == source_projection->views);
        check(projection->next == source_projection->next);
        check(info->layers[2] == source_info->layers[1]);
    }
    return XR_FRAME_DISCARDED;
}
XrResult XRAPI_CALL get(XrInstance, const char* name, PFN_xrVoidFunction* output) {
    if (omit_function && std::strcmp(name, omitted_name) == 0) return XR_ERROR_FUNCTION_UNSUPPORTED;
#define PROVIDE(n, f) if (std::strcmp(name, n) == 0) { *output = reinterpret_cast<PFN_xrVoidFunction>(f); return XR_SUCCESS; }
    PROVIDE("xrCreatePassthroughFB", make_pass)
    PROVIDE("xrDestroyPassthroughFB", delete_pass)
    PROVIDE("xrPassthroughStartFB", start)
    PROVIDE("xrPassthroughPauseFB", pause)
    PROVIDE("xrCreatePassthroughLayerFB", make_layer)
    PROVIDE("xrDestroyPassthroughLayerFB", delete_layer)
    PROVIDE("xrPassthroughLayerResumeFB", resume)
    PROVIDE("xrPassthroughLayerPauseFB", pause_l)
    PROVIDE("xrEndFrame", end)
#undef PROVIDE
    return XR_ERROR_FUNCTION_UNSUPPORTED;
}
}

int main() {
    auto hook = reinterpret_cast<PFN_xrGetInstanceProcAddr>(ghvr_quest_hook(reinterpret_cast<void*>(get)));
    check(ghvr_quest_instance(1) == 1);
    check(ghvr_quest_session(2) == 1);
    PFN_xrVoidFunction fn = nullptr;
    check(hook(reinterpret_cast<XrInstance>(uintptr_t(1)), "xrEndFrame", &fn) == XR_SUCCESS);
    auto wrapped = reinterpret_cast<PFN_xrEndFrame>(fn);
    check(ghvr_quest_status() == 1);
    check(ghvr_quest_enable(1) == 0); // Cannot present before the session begins.
    ghvr_quest_running(1);
    check(ghvr_quest_status() == 2);
    XrCompositionLayerProjection projection{XR_TYPE_COMPOSITION_LAYER_PROJECTION};
    XrCompositionLayerQuad quad{XR_TYPE_COMPOSITION_LAYER_QUAD};
    const XrCompositionLayerBaseHeader* layers[]{reinterpret_cast<const XrCompositionLayerBaseHeader*>(&projection),
                                               reinterpret_cast<const XrCompositionLayerBaseHeader*>(&quad)};
    XrFrameEndInfo frame{XR_TYPE_FRAME_END_INFO};
    frame.displayTime = 123;
    frame.environmentBlendMode = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
    frame.layerCount = 2;
    frame.layers = layers;
    source_info = &frame;
    source_projection = &projection;
    expect_underlay = true;
    for (int i = 0; i < 5; ++i) check(wrapped(reinterpret_cast<XrSession>(uintptr_t(2)), &frame) == XR_FRAME_DISCARDED);
    check(submissions == 5); // One downstream xrEndFrame, never an independent submission.
    expect_underlay = false;
    check(ghvr_quest_enable(0) == 0);
    check(wrapped(reinterpret_cast<XrSession>(uintptr_t(2)), &frame) == XR_FRAME_DISCARDED);
    ghvr_quest_enable(1);
    ghvr_quest_running(0);
    check(ghvr_quest_status() == 1);
    check(wrapped(reinterpret_cast<XrSession>(uintptr_t(2)), &frame) == XR_FRAME_DISCARDED);
    ghvr_quest_running(1);
    check(ghvr_quest_status() == 2);
    check(wrapped(reinterpret_cast<XrSession>(uintptr_t(99)), &frame) == XR_FRAME_DISCARDED);
    frame.layerCount = 0;
    check(wrapped(reinterpret_cast<XrSession>(uintptr_t(2)), &frame) == XR_FRAME_DISCARDED);
    ghvr_quest_destroy_session();
    check(ghvr_quest_status() == 0 && destroyed_passes == 1 && destroyed_layers == 1);
    fail_layer = true;
    check(ghvr_quest_session(3) == 0);
    check(ghvr_quest_error() == XR_ERROR_RUNTIME_FAILURE);
    check(ghvr_quest_status() == 0 && destroyed_passes == 2 && destroyed_layers == 1);
    fail_layer = false;
    check(ghvr_quest_session(4) == 1);
    ghvr_quest_running(1);
    check(ghvr_quest_status() == 2);
    ghvr_quest_destroy_instance();
    check(ghvr_quest_status() == 0 && destroyed_passes == 3 && destroyed_layers == 2);
    omit_function = true;
    check(ghvr_quest_instance(5) == 0);
    check(ghvr_quest_error() == XR_ERROR_FUNCTION_UNSUPPORTED);
    check(ghvr_quest_session(5) == 0);
    omit_function = false;
    check(ghvr_quest_instance(6) == 1);
    check(ghvr_quest_session(6) == 1);
    omitted_name = "xrPassthroughLayerPauseFB";
    omit_function = true;
    check(ghvr_quest_instance(7) == 0); // A late failure must clear previously valid bindings.
    check(ghvr_quest_session(7) == 0);
    check(ghvr_quest_status() == 0);
    std::cout << "Quest passthrough: " << checks << " assertions; disabled/suspended/foreign/empty/missing-function/partial-create controls.\n";
}
