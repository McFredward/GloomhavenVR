/* ARM64 Android ABI shim. The original x64 engine executes in a separate worker. */
#define _GNU_SOURCE
#include "protocol.h"
#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <pthread.h>
#include <signal.h>
#include <spawn.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>
#define API __attribute__((visibility("default")))
extern char **environ;
typedef void (*LogFn)(const char *, int);
typedef struct Log { struct Log *next; int level; char text[]; } Log;
typedef struct { uint8_t *data; uint32_t length; } Buffer;
static pthread_mutex_t api_mutex = PTHREAD_MUTEX_INITIALIZER;
static char executable_dir[4096], payload_dir[4096], writable_dir[4096], last_error[256];
static int channel = -1, configured;
static pid_t worker;
static uint32_t sequence;
static LogFn log_callback;
static Log *logs, *logs_tail;
static void *entity_task, *engine_task;
static void append_log(int level, const uint8_t *data, uint32_t length);
static void error(const char *message) {
    /* Preserve useful failure context in the original engine log stream, with
       stable-message deduplication so a lost worker cannot log every frame. */
    if (strcmp(last_error, message)) {
        snprintf(last_error, sizeof last_error, "%s", message);
        append_log(2, (const uint8_t *)last_error, (uint32_t)strlen(last_error));
    }
}
static void close_worker(void) {
    if (channel >= 0) close(channel);
    channel = -1;
    if (worker > 0) {
        kill(worker, SIGTERM);
        /* A hung guest must not hold Unity's shutdown indefinitely. */
        for (int attempt = 0; attempt < 100; attempt++) {
            pid_t ended = waitpid(worker, 0, WNOHANG);
            if (ended == worker || (ended < 0 && errno == ECHILD)) break;
            if (attempt == 99) { kill(worker, SIGKILL); waitpid(worker, 0, WNOHANG); break; }
            struct timespec delay = {0, 10000000}; nanosleep(&delay, 0);
        }
    }
    worker = 0;
}
static int transfer(void *data, uint32_t length, int writing) {
    uint32_t cursor = 0;
    while (cursor < length) {
        struct pollfd poller = {channel, writing ? POLLOUT : POLLIN, 0};
        int ready;
        do { ready = poll(&poller, 1, 30000); } while (ready < 0 && errno == EINTR);
        if (ready <= 0) { error("Original procedural worker I/O timed out or failed."); return 0; }
        ssize_t count = writing ? send(channel, (uint8_t *)data + cursor, length - cursor, MSG_NOSIGNAL)
                                : read(channel, (uint8_t *)data + cursor, length - cursor);
        if (count < 0 && errno == EINTR) continue;
        if (count <= 0) { error("Original procedural worker disconnected."); return 0; }
        cursor += (uint32_t)count;
    }
    return 1;
}
static void append_log(int level, const uint8_t *data, uint32_t length) {
    if (length > 65536) { error("Original procedural worker returned an invalid log frame."); return; }
    Log *item = malloc(sizeof *item + length + 1);
    if (!item) return;
    item->next = 0; item->level = level;
    memcpy(item->text, data, length); item->text[length] = 0;
    if (logs_tail) logs_tail->next = item; else logs = item;
    logs_tail = item;
}
static void finish(void) {
    /* Deliver original log callbacks after the current reply has been consumed.
       A callback may then safely call another native operation, outside this lock. */
    Log *pending = logs; logs = logs_tail = 0;
    LogFn callback = log_callback;
    pthread_mutex_unlock(&api_mutex);
    while (pending) { Log *next = pending->next; if (callback) callback(pending->text, pending->level); free(pending); pending = next; }
}
static Buffer request(uint16_t operation, const void *data, uint32_t length) {
    Buffer result = {0, 0};
    if (channel < 0 || length > GHPR_MAX_PAYLOAD) { error("Original procedural worker is not running or request is oversized."); return result; }
    GhprHeader header = {GHPR_MAGIC, GHPR_VERSION, operation, ++sequence, length, 0};
    if (!transfer(&header, sizeof header, 1) || (length && !transfer((void *)data, length, 1))) { close_worker(); return result; }
    for (;;) {
        GhprHeader reply;
        if (!transfer(&reply, sizeof reply, 0) || reply.magic != GHPR_MAGIC || reply.version != GHPR_VERSION || reply.length > GHPR_MAX_PAYLOAD) {
            error("Original procedural worker returned an invalid protocol header."); close_worker(); return result;
        }
        uint8_t *content = malloc(reply.length ? reply.length : 1);
        if (!content || (reply.length && !transfer(content, reply.length, 0))) { free(content); close_worker(); return result; }
        if (reply.operation == GHPR_LOG) { append_log(reply.status, content, reply.length); free(content); continue; }
        if (reply.operation != operation || reply.sequence != header.sequence || reply.status) {
            char message[256];
            snprintf(message, sizeof message, "Original procedural worker operation %u failed (status %d).", operation, reply.status);
            error(message);
            free(content); close_worker(); return result;
        }
        result.data = content; result.length = reply.length; return result;
    }
}
static void put(uint8_t **cursor, const void *data, uint32_t length) { memcpy(*cursor, data, length); *cursor += length; }
static void put_i(uint8_t **cursor, uint32_t value) { put(cursor, &value, 4); }
static void put_s(uint8_t **cursor, const char *value) { uint32_t length = value ? (uint32_t)strlen(value) : 0; put_i(cursor, length); if (length) put(cursor, value, length); }
static int response_i(Buffer data) { int value = 0; if (data.data && data.length == 4) memcpy(&value, data.data, 4); else if (data.data) error("Original procedural worker integer response differs."); free(data.data); return value; }
static void response_void(Buffer data) { if (data.data && data.length) error("Original procedural worker void response differs."); free(data.data); }
static int environment_key(const char *entry, const char *key) { size_t size = strlen(key); return !strncmp(entry, key, size) && entry[size] == '='; }
static char *environment(const char *key, const char *value) { size_t size = strlen(key) + strlen(value) + 2; char *result = malloc(size); if (result) snprintf(result, size, "%s=%s", key, value); return result; }
static int launch(void) {
    if (!configured) { error("Quest original procedural runtime was not configured."); return 0; }
    int sockets[2];
    if (socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, sockets)) { error("Cannot create original procedural worker channel."); return 0; }
    char box[8192], worker_path[8192], wine[8192], server[8192], prefix[8192], library_path[16384], diagnostics[8192], server_guest[8192];
    snprintf(box, sizeof box, "%s/libquest_box64.so", executable_dir);
    snprintf(worker_path, sizeof worker_path, "%s/ApparanceWorker.exe", payload_dir);
    snprintf(wine, sizeof wine, "%s/wine/bin/wine64", payload_dir);
    snprintf(server, sizeof server, "%s/libquest_wineserver.so", executable_dir);
    snprintf(server_guest, sizeof server_guest, "%s/wine/bin/wineserver", payload_dir);
    snprintf(prefix, sizeof prefix, "%s/wine-prefix", writable_dir);
    snprintf(library_path, sizeof library_path, "%s/wine/lib/wine/x86_64-unix:%s/wine/lib", payload_dir, payload_dir);
    snprintf(diagnostics, sizeof diagnostics, "%s/procedural-worker.log", writable_dir);
    char fonts[8192], cache[8192];
    snprintf(fonts, sizeof fonts, "%s/fontconfig", payload_dir);
    snprintf(cache, sizeof cache, "%s/cache", writable_dir);
    const char *keys[] = {"WINEPREFIX", "WINEDEBUG", "WINESERVER", "WINELOADER", "BOX64_LD_LIBRARY_PATH", "BOX64_DYNACACHE", "GHPR_BOX64", "GHPR_WINESERVER", "WINEDLLOVERRIDES", "BOX64_NOBANNER", "BOX64_LOG", "BOX64_EMULATED_LIBS", "FONTCONFIG_PATH", "FONTCONFIG_FILE", "XDG_CACHE_HOME"};
    const char *values[] = {prefix, "-all", server, wine, library_path, "0", box, server_guest,
        "mscoree,mshtml,winegstreamer,winebus,winevulkan,winemenubuilder=", "1", "0",
        "libfontconfig.so.1:libfreetype.so.6:libexpat.so.1:libpng16.so.16:libz.so.1:libbrotlidec.so.1:libbrotlicommon.so.1:libbz2.so.1.0",
        fonts, "fonts.conf", cache};
    const size_t key_count = sizeof keys / sizeof keys[0];
    size_t existing = 0; while (environ[existing]) existing++;
    char **env = calloc(existing + key_count + 1, sizeof *env);
    if (!env) { close(sockets[0]); close(sockets[1]); error("Cannot allocate procedural worker environment."); return 0; }
    size_t count = 0;
    for (size_t index = 0; index < existing; index++) {
        int replaced = 0;
        for (size_t key = 0; key < key_count; key++) replaced |= environment_key(environ[index], keys[key]);
        if (!replaced) env[count++] = environ[index];
    }
    size_t allocated = count;
    for (size_t key = 0; key < key_count; key++) {
        env[count] = environment(keys[key], values[key]);
        if (!env[count]) {
            for (size_t index = allocated; index < count; index++) free(env[index]);
            free(env); close(sockets[0]); close(sockets[1]);
            error("Cannot allocate original worker environment entry."); return 0;
        }
        count++;
    }
    posix_spawn_file_actions_t actions;
    posix_spawn_file_actions_init(&actions);
    posix_spawn_file_actions_adddup2(&actions, sockets[1], STDIN_FILENO);
    posix_spawn_file_actions_adddup2(&actions, sockets[1], STDOUT_FILENO);
    posix_spawn_file_actions_addclose(&actions, sockets[0]);
    posix_spawn_file_actions_addclose(&actions, sockets[1]);
    posix_spawn_file_actions_addopen(&actions, STDERR_FILENO, diagnostics, O_WRONLY | O_CREAT | O_APPEND, 0600);
#ifdef GHPR_HOST_PROOF
    char host_wine[8192]; snprintf(host_wine, sizeof host_wine, "%s/wine64", executable_dir);
    char *arguments[] = {host_wine, worker_path, 0};
    /* The Linux proof uses installed x64 Wine; Android always uses the APK executable. */
    for (size_t index = allocated; index < count; index++) if (environment_key(env[index], "WINESERVER")) { free(env[index]); env[index] = environment("WINESERVER", "/usr/lib/wine/wineserver64"); }
    int status = posix_spawn(&worker, host_wine, &actions, 0, arguments, env);
#else
    char *arguments[] = {box, wine, worker_path, 0};
    int status = posix_spawn(&worker, box, &actions, 0, arguments, env);
#endif
    posix_spawn_file_actions_destroy(&actions);
    for (size_t index = allocated; index < count; index++) free(env[index]);
    free(env); close(sockets[1]);
    if (status) { close(sockets[0]); worker = 0; error("Cannot launch APK-packaged original procedural worker."); return 0; }
    channel = sockets[0]; sequence = 0; return 1;
}
static char *windows_path(const char *path) {
    size_t size = strlen(path); char *result = malloc(size + 3);
    if (!result) return 0;
    result[0] = 'Z'; result[1] = ':';
    for (size_t index = 0; index < size; index++) result[index + 2] = path[index] == '/' ? '\\' : path[index];
    result[size + 2] = 0; return result;
}

API int quest_apparance_configure(const char *executables, const char *payload, const char *writable) {
    pthread_mutex_lock(&api_mutex);
    int valid = executables && payload && writable && strlen(executables) < sizeof executable_dir && strlen(payload) < sizeof payload_dir && strlen(writable) < sizeof writable_dir && channel < 0;
    if (valid) { snprintf(executable_dir, sizeof executable_dir, "%s", executables); snprintf(payload_dir, sizeof payload_dir, "%s", payload); snprintf(writable_dir, sizeof writable_dir, "%s", writable); configured = 1; }
    else error("Quest procedural runtime configuration is invalid or already active.");
    finish(); return valid ? 0 : -1;
}
API const char *quest_apparance_last_error(void) { return last_error; }
API int ApparanceInitialise(const char *procedures, LogFn callback, int synthesizers, int megabytes, int live_editing) {
    pthread_mutex_lock(&api_mutex); log_callback = callback; last_error[0] = 0;
    if (!procedures || !launch()) { finish(); return 0; }
    char raw_library[8192]; snprintf(raw_library, sizeof raw_library, "%s/ApparanceEngine.dll", payload_dir);
    char *library = windows_path(raw_library), *directory = windows_path(procedures);
    if (!library || !directory) { free(library); free(directory); close_worker(); finish(); return 0; }
    uint32_t size = (uint32_t)(strlen(library) + strlen(directory)) + 20;
    uint8_t *payload = malloc(size), *cursor = payload;
    if (!payload) { free(library); free(directory); close_worker(); finish(); return 0; }
    put_s(&cursor, library); put_s(&cursor, directory); put_i(&cursor, (uint32_t)synthesizers); put_i(&cursor, (uint32_t)megabytes); put_i(&cursor, (uint32_t)live_editing);
    int result = response_i(request(GHPR_INITIALISE, payload, size));
    free(payload); free(library); free(directory); finish(); return result;
}
API int ApparanceIsRunning(void) { pthread_mutex_lock(&api_mutex); int value = response_i(request(GHPR_IS_RUNNING, 0, 0)); finish(); return value; }
API void ApparanceUpdate(float dt, GhprVector3 view) { pthread_mutex_lock(&api_mutex); float values[] = {dt, view.x, view.y, view.z}; response_void(request(GHPR_UPDATE, values, sizeof values)); finish(); }
API void ApparanceSave(void) { pthread_mutex_lock(&api_mutex); response_void(request(GHPR_SAVE, 0, 0)); finish(); }
API void ApparanceShutdown(void) { pthread_mutex_lock(&api_mutex); if (channel >= 0) response_void(request(GHPR_SHUTDOWN, 0, 0)); close_worker(); free(entity_task); free(engine_task); entity_task = engine_task = 0; finish(); }
API int ApparanceCreateEntity(int old) { pthread_mutex_lock(&api_mutex); int value = response_i(request(GHPR_CREATE_ENTITY, &old, 4)); finish(); return value; }
API void ApparanceDestroyEntity(int handle) { pthread_mutex_lock(&api_mutex); response_void(request(GHPR_DESTROY_ENTITY, &handle, 4)); finish(); }
API void ApparanceEntityBuild(int handle, uint32_t procedure, int size, const void *bytes, int dynamic) {
    pthread_mutex_lock(&api_mutex);
    if (size < 0 || (uint32_t)size > GHPR_MAX_PAYLOAD - 16 || (size && !bytes)) { error("Original procedural build parameters are invalid."); finish(); return; }
    uint8_t *payload = malloc((uint32_t)size + 16), *cursor = payload;
    if (!payload) { error("Cannot allocate procedural build request."); finish(); return; }
    put_i(&cursor, (uint32_t)handle); put_i(&cursor, procedure); put_i(&cursor, (uint32_t)dynamic); put_i(&cursor, (uint32_t)size); put(&cursor, bytes, (uint32_t)size);
    response_void(request(GHPR_ENTITY_BUILD, payload, (uint32_t)size + 16)); free(payload); finish();
}
static int pop(uint16_t operation, int *size, void **bytes, void **lease) {
    pthread_mutex_lock(&api_mutex); if (size) *size = 0; if (bytes) *bytes = 0;
    Buffer response = request(operation, 0, 0); int result = 0, length = 0;
    if (response.data && response.length >= 8) {
        memcpy(&result, response.data, 4); memcpy(&length, response.data + 4, 4);
        if (length < 0 || response.length != (uint32_t)length + 8) { error("Original procedural task payload differs."); result = 0; }
        else { free(*lease); *lease = response.data; response.data = 0; if (size) *size = length; if (bytes && length) *bytes = (uint8_t *)*lease + 8; }
    } else if (response.data) error("Original procedural task response differs.");
    free(response.data); finish(); return result;
}
API int ApparancePopEntityTask(int *size, void **bytes) { return pop(GHPR_POP_ENTITY_TASK, size, bytes, &entity_task); }
API int ApparancePopEngineTask(int *size, void **bytes) { return pop(GHPR_POP_ENGINE_TASK, size, bytes, &engine_task); }
API void ApparanceUpdateAsset(int context, const char *name, int id, int bounds, const float *frame, int variants) {
    pthread_mutex_lock(&api_mutex); uint32_t length = name ? (uint32_t)strlen(name) : 0, frame_size = frame ? 60 : 0;
    if (length > 1048576) { error("Original asset descriptor exceeds the native boundary."); finish(); return; }
    uint32_t size = length + frame_size + 24; uint8_t *payload = malloc(size), *cursor = payload;
    if (!payload) { error("Cannot allocate original asset response."); finish(); return; }
    put_i(&cursor, (uint32_t)context); put_s(&cursor, name); put_i(&cursor, (uint32_t)id); put_i(&cursor, (uint32_t)bounds); put_i(&cursor, (uint32_t)variants); put_i(&cursor, frame_size); if (frame) put(&cursor, frame, frame_size);
    response_void(request(GHPR_UPDATE_ASSET, payload, size)); free(payload); finish();
}
API char *ApparanceGetNextAssetRequest(int *context, int *id) {
    pthread_mutex_lock(&api_mutex); if (context) *context = 0; if (id) *id = 0;
    Buffer response = request(GHPR_ASSET_REQUEST, 0, 0); char *result = 0;
    if (response.data && response.length >= 12) {
        int length; memcpy(&length, response.data + 8, 4);
        if (length >= 0 && response.length == (uint32_t)length + 12) {
            result = malloc((uint32_t)length + 1);
            if (result) { memcpy(result, response.data + 12, (uint32_t)length); result[length] = 0; }
            if (context) memcpy(context, response.data, 4);
            if (id) memcpy(id, response.data + 4, 4);
        } else if (length != -1 || response.length != 12) error("Original procedural asset request payload differs.");
    } else if (response.data) error("Original procedural asset request response differs.");
    free(response.data); finish(); return result;
}
