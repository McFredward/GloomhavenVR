// The Quest target shares Unity's OpenXR instance, session and xrEndFrame call.
// It only adds a passthrough underlay; no Horizon account or second XR session.
#define XR_NO_PROTOTYPES
#include <openxr/openxr.h>
#include <algorithm>
#include <cstring>
#include <mutex>
#include <vector>

#define GHVR_EXPORT extern "C" __attribute__((visibility("default")))

namespace {
std::mutex gate;
PFN_xrGetInstanceProcAddr original_get = nullptr;
PFN_xrEndFrame original_end = nullptr;
PFN_xrCreatePassthroughFB create_pass = nullptr;
PFN_xrDestroyPassthroughFB destroy_pass = nullptr;
PFN_xrPassthroughStartFB start_pass = nullptr;
PFN_xrPassthroughPauseFB pause_pass = nullptr;
PFN_xrCreatePassthroughLayerFB create_layer = nullptr;
PFN_xrDestroyPassthroughLayerFB destroy_layer = nullptr;
PFN_xrPassthroughLayerResumeFB resume_layer = nullptr;
PFN_xrPassthroughLayerPauseFB pause_layer = nullptr;
XrInstance instance = XR_NULL_HANDLE;
XrSession session = XR_NULL_HANDLE;
XrPassthroughFB passthrough = XR_NULL_HANDLE;
XrPassthroughLayerFB layer = XR_NULL_HANDLE;
bool running = false;
bool wanted = false;
bool active = false;
bool functions_ready = false;
XrResult last_result = XR_SUCCESS;
std::vector<XrCompositionLayerProjection> projection_scratch;
std::vector<const XrCompositionLayerBaseHeader*> layer_scratch;

template<typename T> bool resolve(const char* name, T& function) {
    PFN_xrVoidFunction result = nullptr;
    last_result = original_get(instance, name, &result);
    function = reinterpret_cast<T>(result);
    return XR_SUCCEEDED(last_result) && function;
}

void deactivate() {
    // Always clear admission first, including an interrupted resume attempt.
    active = false;
    if (layer && pause_layer) pause_layer(layer);
    if (passthrough && pause_pass) pause_pass(passthrough);
}

void destroy() {
    deactivate();
    if (layer && destroy_layer) destroy_layer(layer);
    if (passthrough && destroy_pass) destroy_pass(passthrough);
    layer = XR_NULL_HANDLE;
    passthrough = XR_NULL_HANDLE;
    session = XR_NULL_HANDLE;
    running = false;
}

bool activate() {
    if (!wanted || !running || !passthrough || !layer) return false;
    last_result = start_pass(passthrough);
    if (XR_FAILED(last_result)) return false;
    last_result = resume_layer(layer);
    if (XR_FAILED(last_result)) { deactivate(); return false; }
    active = true;
    return true;
}

XrResult XRAPI_CALL end_frame(XrSession xr_session, const XrFrameEndInfo* info) {
    std::lock_guard<std::mutex> lock(gate);
    if (!original_end) return XR_ERROR_FUNCTION_UNSUPPORTED;
    // Empty/loading frames retain Unity's original submission exactly.
    if (!active || xr_session != session || !info || !info->layerCount || !info->layers)
        return original_end(xr_session, info);

    bool has_projection = false;
    for (uint32_t i = 0; i < info->layerCount; ++i)
        has_projection |= info->layers[i] && info->layers[i]->type == XR_TYPE_COMPOSITION_LAYER_PROJECTION;
    if (!has_projection) return original_end(xr_session, info);

    XrCompositionLayerPassthroughFB underlay{XR_TYPE_COMPOSITION_LAYER_PASSTHROUGH_FB};
    underlay.space = XR_NULL_HANDLE; // Reconstruction layers do not use a space.
    underlay.layerHandle = layer;
    auto& projections = projection_scratch;
    auto& layers = layer_scratch;
    projections.clear();
    layers.clear();
    projections.reserve(info->layerCount);
    layers.reserve(info->layerCount + 1);
    layers.push_back(reinterpret_cast<const XrCompositionLayerBaseHeader*>(&underlay));
    for (uint32_t i = 0; i < info->layerCount; ++i) {
        const auto* current = info->layers[i];
        if (current && current->type == XR_TYPE_COMPOSITION_LAYER_PROJECTION) {
            // Do not mutate Unity-owned structures, views or next/depth chains.
            projections.push_back(*reinterpret_cast<const XrCompositionLayerProjection*>(current));
            projections.back().layerFlags |= XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
            layers.push_back(reinterpret_cast<const XrCompositionLayerBaseHeader*>(&projections.back()));
        } else layers.push_back(current);
    }
    XrFrameEndInfo submission = *info;
    submission.layerCount = static_cast<uint32_t>(layers.size());
    submission.layers = layers.data();
    return original_end(xr_session, &submission);
}

XrResult XRAPI_CALL get_proc(XrInstance xr_instance, const char* name, PFN_xrVoidFunction* function) {
    if (!original_get) return XR_ERROR_FUNCTION_UNSUPPORTED;
    XrResult result = original_get(xr_instance, name, function);
    if (XR_SUCCEEDED(result) && name && function && std::strcmp(name, "xrEndFrame") == 0) {
        std::lock_guard<std::mutex> lock(gate);
        original_end = reinterpret_cast<PFN_xrEndFrame>(*function);
        *function = reinterpret_cast<PFN_xrVoidFunction>(end_frame);
    }
    return result;
}
}

GHVR_EXPORT void* ghvr_quest_hook(void* get_function) {
    std::lock_guard<std::mutex> lock(gate);
    if (get_function != reinterpret_cast<void*>(get_proc))
        original_get = reinterpret_cast<PFN_xrGetInstanceProcAddr>(get_function);
    return reinterpret_cast<void*>(get_proc);
}

GHVR_EXPORT int ghvr_quest_instance(uint64_t xr_instance) {
    std::lock_guard<std::mutex> lock(gate);
    destroy();
    functions_ready = false;
    instance = reinterpret_cast<XrInstance>(xr_instance);
    if (!original_get || !instance) return 0;
    functions_ready = resolve("xrCreatePassthroughFB", create_pass)
        && resolve("xrDestroyPassthroughFB", destroy_pass)
        && resolve("xrPassthroughStartFB", start_pass)
        && resolve("xrPassthroughPauseFB", pause_pass)
        && resolve("xrCreatePassthroughLayerFB", create_layer)
        && resolve("xrDestroyPassthroughLayerFB", destroy_layer)
        && resolve("xrPassthroughLayerResumeFB", resume_layer)
        && resolve("xrPassthroughLayerPauseFB", pause_layer);
    return functions_ready ? 1 : 0;
}

GHVR_EXPORT int ghvr_quest_session(uint64_t xr_session) {
    std::lock_guard<std::mutex> lock(gate);
    destroy();
    session = reinterpret_cast<XrSession>(xr_session);
    if (!session || !functions_ready) return 0;
    XrPassthroughCreateInfoFB create_info{XR_TYPE_PASSTHROUGH_CREATE_INFO_FB};
    last_result = create_pass(session, &create_info, &passthrough);
    if (XR_FAILED(last_result)) { destroy(); return 0; }
    XrPassthroughLayerCreateInfoFB layer_info{XR_TYPE_PASSTHROUGH_LAYER_CREATE_INFO_FB};
    layer_info.passthrough = passthrough;
    layer_info.purpose = XR_PASSTHROUGH_LAYER_PURPOSE_RECONSTRUCTION_FB;
    last_result = create_layer(session, &layer_info, &layer);
    if (XR_FAILED(last_result)) { destroy(); return 0; }
    return 1;
}

GHVR_EXPORT void ghvr_quest_running(int is_running) {
    std::lock_guard<std::mutex> lock(gate);
    running = is_running != 0;
    if (running) activate(); else deactivate();
}

GHVR_EXPORT int ghvr_quest_enable(int enabled) {
    std::lock_guard<std::mutex> lock(gate);
    wanted = enabled != 0;
    if (!wanted) deactivate(); else activate();
    return active ? 1 : 0;
}

GHVR_EXPORT int ghvr_quest_status() {
    std::lock_guard<std::mutex> lock(gate);
    return active ? 2 : (layer ? 1 : 0);
}

GHVR_EXPORT int ghvr_quest_error() {
    std::lock_guard<std::mutex> lock(gate);
    return static_cast<int>(last_result);
}

GHVR_EXPORT void ghvr_quest_destroy_session() {
    std::lock_guard<std::mutex> lock(gate);
    destroy();
}

GHVR_EXPORT void ghvr_quest_destroy_instance() {
    std::lock_guard<std::mutex> lock(gate);
    destroy();
    instance = XR_NULL_HANDLE;
    original_end = nullptr;
    create_pass = nullptr;
    create_layer = nullptr;
    wanted = false;
    functions_ready = false;
}
