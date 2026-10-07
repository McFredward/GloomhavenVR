/* Windows x64 worker: original Apparance DLL and threads remain the owners. */
#include "protocol.h"
typedef void *HANDLE;
typedef unsigned long DWORD;
typedef int BOOL;
typedef unsigned long long SIZE_T;
#define WINAPI __stdcall
#define IMPORT __declspec(dllimport)
IMPORT HANDLE WINAPI LoadLibraryA(const char *);
IMPORT void *WINAPI GetProcAddress(HANDLE, const char *);
IMPORT DWORD WINAPI GetLastError(void);
IMPORT HANDLE WINAPI GetStdHandle(DWORD);
IMPORT BOOL WINAPI ReadFile(HANDLE, void *, DWORD, DWORD *, void *);
IMPORT BOOL WINAPI WriteFile(HANDLE, const void *, DWORD, DWORD *, void *);
IMPORT void WINAPI ExitProcess(DWORD);
IMPORT HANDLE WINAPI GetProcessHeap(void);
IMPORT void *WINAPI HeapAlloc(HANDLE, DWORD, SIZE_T);
IMPORT BOOL WINAPI HeapFree(HANDLE, DWORD, void *);
IMPORT HANDLE WINAPI CreateMutexA(void *, BOOL, const char *);
IMPORT DWORD WINAPI WaitForSingleObject(HANDLE, DWORD);
IMPORT BOOL WINAPI ReleaseMutex(HANDLE);
IMPORT BOOL WINAPI SetCurrentDirectoryA(const char *);

static HANDLE input, output, send_mutex, heap, library;
static int initialised;
/* MSVC object marker for floating point use; this worker uses no CRT helpers. */
int _fltused;
typedef void (*LogFn)(const char *, int);
static int (*initialise)(const char *, LogFn, int, int, int);
static int (*is_running)(void);
static void (*update)(float, GhprVector3);
static void (*save)(void), (*shutdown_engine)(void);
static int (*create_entity)(int);
static void (*destroy_entity)(int);
static void (*entity_build)(int, uint32_t, int, const void *, BOOL);
static int (*pop_entity_task)(int *, void **), (*pop_engine_task)(int *, void **);
static void (*update_asset)(int, const char *, int, int, const float *, int);
static char *(*asset_request)(int *, int *);

static void *allocate(uint32_t length) { return HeapAlloc(heap, 8, (SIZE_T)length); }
static void release(void *data) { if (data) HeapFree(heap, 0, data); }
static uint32_t text_length(const char *text, uint32_t maximum) {
    uint32_t length = 0;
    if (text) while (length < maximum && text[length]) length++;
    return length;
}
static BOOL read_exact(void *data, uint32_t length) {
    uint32_t cursor = 0;
    while (cursor < length) {
        DWORD received = 0;
        if (!ReadFile(input, (char *)data + cursor, length - cursor, &received, 0) || !received) return 0;
        cursor += received;
    }
    return 1;
}
static BOOL write_exact(const void *data, uint32_t length) {
    uint32_t cursor = 0;
    while (cursor < length) {
        DWORD sent = 0;
        if (!WriteFile(output, (const char *)data + cursor, length - cursor, &sent, 0) || !sent) return 0;
        cursor += sent;
    }
    return 1;
}
static BOOL send(uint16_t operation, uint32_t sequence, int32_t status, const void *data, uint32_t length) {
    GhprHeader header = { GHPR_MAGIC, GHPR_VERSION, operation, sequence, length, status };
    WaitForSingleObject(send_mutex, 0xffffffffu);
    BOOL success = write_exact(&header, sizeof header) && (!length || write_exact(data, length));
    ReleaseMutex(send_mutex);
    return success;
}
static void log_message(const char *message, int level) {
    /* Original callbacks may run on original synthesis threads. Serialize the
       complete frame so logs cannot interleave bytes with task responses. */
    send(GHPR_LOG, 0, level, message, text_length(message, 65536));
}
static int load(const char *path) {
    library = LoadLibraryA(path);
    if (!library) return 0;
#define BIND(field, name) field = (void *)GetProcAddress(library, name); if (!field) return 0
    BIND(initialise, "ApparanceInitialise"); BIND(is_running, "ApparanceIsRunning");
    BIND(update, "ApparanceUpdate"); BIND(save, "ApparanceSave");
    BIND(shutdown_engine, "ApparanceShutdown"); BIND(create_entity, "ApparanceCreateEntity");
    BIND(destroy_entity, "ApparanceDestroyEntity"); BIND(entity_build, "ApparanceEntityBuild");
    BIND(pop_entity_task, "ApparancePopEntityTask"); BIND(pop_engine_task, "ApparancePopEngineTask");
    BIND(update_asset, "ApparanceUpdateAsset"); BIND(asset_request, "ApparanceGetNextAssetRequest");
#undef BIND
    return 1;
}
typedef struct { const uint8_t *data; uint32_t length, cursor; int valid; } Reader;
static const void *take(Reader *reader, uint32_t length) {
    if (length > reader->length - reader->cursor) { reader->valid = 0; return 0; }
    const void *value = reader->data + reader->cursor;
    reader->cursor += length;
    return value;
}
static uint32_t integer(Reader *reader) {
    const uint32_t *value = take(reader, 4);
    return value ? *value : 0;
}
static char *string(Reader *reader) {
    uint32_t length = integer(reader);
    const char *source = take(reader, length);
    if (!source || length > 1048576) { reader->valid = 0; return 0; }
    char *value = allocate(length + 1);
    if (!value) { reader->valid = 0; return 0; }
    for (uint32_t index = 0; index < length; index++) value[index] = source[index];
    value[length] = 0;
    return value;
}
static BOOL exact(Reader *reader) { return reader->valid && reader->cursor == reader->length; }
static void failure(const GhprHeader *request, int status) { send(request->operation, request->sequence, status, 0, 0); }
static void reply_int(const GhprHeader *request, int value) { send(request->operation, request->sequence, 0, &value, 4); }

static int dispatch(const GhprHeader *request, const uint8_t *payload) {
    Reader reader = {payload, request->length, 0, 1};
    if (request->operation != GHPR_INITIALISE && !initialised) { failure(request, -3); return 1; }
    switch (request->operation) {
    case GHPR_INITIALISE: {
        if (initialised) { failure(request, -4); break; }
        char *path = string(&reader), *procedures = string(&reader);
        int synthesizers = (int)integer(&reader), megabytes = (int)integer(&reader), live_editing = (int)integer(&reader);
        if (!exact(&reader)) failure(request, -2);
        else if (!load(path)) failure(request, (int)(GetLastError() ? GetLastError() : 127));
        else {
            /* Keep the original engine's optional log.txt in the private Wine
               prefix, never the read-only game/procedure input or APK path. */
            SetCurrentDirectoryA("C:\\");
            int result = initialise(procedures, log_message, synthesizers, megabytes, live_editing);
            initialised = result != 0; reply_int(request, result);
        }
        release(path); release(procedures); break;
    }
    case GHPR_IS_RUNNING:
        if (!exact(&reader)) failure(request, -2); else reply_int(request, is_running()); break;
    case GHPR_UPDATE: {
        const float *values = take(&reader, 16);
        if (!exact(&reader)) failure(request, -2);
        else { GhprVector3 view = {values[1], values[2], values[3]}; update(values[0], view); send(request->operation, request->sequence, 0, 0, 0); } break;
    }
    case GHPR_SAVE:
        if (!exact(&reader)) failure(request, -2); else { save(); send(request->operation, request->sequence, 0, 0, 0); } break;
    case GHPR_SHUTDOWN:
        if (!exact(&reader)) { failure(request, -2); break; }
        shutdown_engine(); initialised = 0; send(request->operation, request->sequence, 0, 0, 0); return 0;
    case GHPR_CREATE_ENTITY: {
        int old = (int)integer(&reader);
        if (!exact(&reader)) failure(request, -2); else reply_int(request, create_entity(old)); break;
    }
    case GHPR_DESTROY_ENTITY: {
        int handle = (int)integer(&reader);
        if (!exact(&reader)) failure(request, -2); else { destroy_entity(handle); send(request->operation, request->sequence, 0, 0, 0); } break;
    }
    case GHPR_ENTITY_BUILD: {
        int handle = (int)integer(&reader); uint32_t procedure = integer(&reader);
        BOOL dynamic = (BOOL)integer(&reader); uint32_t size = integer(&reader);
        const void *bytes = take(&reader, size);
        if (!exact(&reader)) failure(request, -2);
        else { entity_build(handle, procedure, (int)size, bytes, dynamic); send(request->operation, request->sequence, 0, 0, 0); } break;
    }
    case GHPR_POP_ENTITY_TASK:
    case GHPR_POP_ENGINE_TASK: {
        if (!exact(&reader)) { failure(request, -2); break; }
        int size = 0; void *bytes = 0;
        int result = request->operation == GHPR_POP_ENTITY_TASK ? pop_entity_task(&size, &bytes) : pop_engine_task(&size, &bytes);
        if (size < 0 || (uint32_t)size > GHPR_MAX_PAYLOAD - 8 || (size && !bytes)) { failure(request, -5); break; }
        uint8_t *response = allocate((uint32_t)size + 8);
        if (!response) { failure(request, -6); break; }
        ((int *)response)[0] = result; ((int *)response)[1] = size;
        for (int index = 0; index < size; index++) response[index + 8] = ((uint8_t *)bytes)[index];
        send(request->operation, request->sequence, 0, response, (uint32_t)size + 8); release(response); break;
    }
    case GHPR_UPDATE_ASSET: {
        int context = (int)integer(&reader); char *name = string(&reader);
        int id = (int)integer(&reader), bounds = (int)integer(&reader), variants = (int)integer(&reader);
        uint32_t frame_size = integer(&reader);
        const float *frame = take(&reader, frame_size);
        if (!exact(&reader) || (frame_size != 0 && frame_size != 60)) failure(request, -2);
        else { update_asset(context, name[0] ? name : 0, id, bounds, frame_size ? frame : 0, variants); send(request->operation, request->sequence, 0, 0, 0); }
        release(name); break;
    }
    case GHPR_ASSET_REQUEST: {
        if (!exact(&reader)) { failure(request, -2); break; }
        int context = 0, id = 0; char *name = asset_request(&context, &id);
        uint32_t size = text_length(name, 1048576);
        uint8_t *response = allocate(size + 12);
        if (!response) { failure(request, -6); break; }
        ((int *)response)[0] = context; ((int *)response)[1] = id; ((int *)response)[2] = name ? (int)size : -1;
        for (uint32_t index = 0; index < size; index++) response[index + 12] = (uint8_t)name[index];
        send(request->operation, request->sequence, 0, response, size + 12); release(response);
        /* The original return is CoTaskMemAlloc-owned by its managed caller. */
        if (name) { typedef void (*FreeFn)(void *); static FreeFn free_task;
            if (!free_task) { HANDLE ole = LoadLibraryA("ole32.dll"); free_task = ole ? (FreeFn)GetProcAddress(ole, "CoTaskMemFree") : 0; }
            if (free_task) free_task(name);
        } break;
    }
    default: failure(request, -1); break;
    }
    return 1;
}

void Main(void) {
    heap = GetProcessHeap(); input = GetStdHandle((DWORD)-10); output = GetStdHandle((DWORD)-11);
    send_mutex = CreateMutexA(0, 0, 0);
    if (!heap || !input || !output || !send_mutex) ExitProcess(70);
    for (;;) {
        GhprHeader request;
        if (!read_exact(&request, sizeof request)) break;
        if (request.magic != GHPR_MAGIC || request.version != GHPR_VERSION || request.length > GHPR_MAX_PAYLOAD || !request.sequence || request.status) ExitProcess(71);
        uint8_t *payload = allocate(request.length ? request.length : 1);
        if (!payload || !read_exact(payload, request.length)) { release(payload); break; }
        int more = dispatch(&request, payload);
        release(payload);
        if (!more) break;
    }
    if (initialised) shutdown_engine();
    ExitProcess(0);
}
