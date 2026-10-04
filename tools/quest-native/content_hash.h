#pragma once
#include <cstdint>

// Independent incremental SHA-256 contexts. No Unity/OpenXR state is consulted.
#define GHVR_CONTENT_EXPORT extern "C" __attribute__((visibility("default")))
struct GhvrContentFileIdentity {
    uint64_t device, inode;
    int64_t size, modified_seconds, modified_nanoseconds, changed_seconds, changed_nanoseconds;
};
GHVR_CONTENT_EXPORT int ghvr_content_hash_abi();
GHVR_CONTENT_EXPORT void* ghvr_content_hash_create();
GHVR_CONTENT_EXPORT int ghvr_content_hash_update(void* context, const uint8_t* bytes, int count);
GHVR_CONTENT_EXPORT int ghvr_content_hash_final(void* context, uint8_t* digest);
GHVR_CONTENT_EXPORT void ghvr_content_hash_destroy(void* context);
GHVR_CONTENT_EXPORT int ghvr_content_file_identity(const char* path, GhvrContentFileIdentity* identity);
