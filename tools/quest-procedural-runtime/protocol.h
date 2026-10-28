/* Local copied-buffer protocol. No guest pointer or callback crosses processes. */
#ifndef GHPR_PROTOCOL_H
#define GHPR_PROTOCOL_H
#include <stdint.h>
#define GHPR_MAGIC 0x52504847u
#define GHPR_VERSION 1u
#define GHPR_MAX_PAYLOAD (128u * 1024u * 1024u)
enum GhprOperation {
    GHPR_INITIALISE = 1, GHPR_IS_RUNNING = 2, GHPR_UPDATE = 3,
    GHPR_SAVE = 4, GHPR_SHUTDOWN = 5, GHPR_CREATE_ENTITY = 6,
    GHPR_DESTROY_ENTITY = 7, GHPR_ENTITY_BUILD = 8,
    GHPR_POP_ENTITY_TASK = 9, GHPR_POP_ENGINE_TASK = 10,
    GHPR_UPDATE_ASSET = 11, GHPR_ASSET_REQUEST = 12,
    GHPR_LOG = 0x100
};
/* Little endian; header size is twenty bytes on both hosts. */
typedef struct {
    uint32_t magic;
    uint16_t version, operation;
    uint32_t sequence, length;
    int32_t status;
} GhprHeader;
typedef struct { float x, y, z; } GhprVector3;
#endif
