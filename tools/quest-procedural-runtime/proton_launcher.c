/* Source-owned narrow Android Wine loader/layout adapter. No GameNative shim.
 * The immutable nativeLibraryDir contains every executable ELF. Wine's virtual
 * installed paths are interpreted-data paths only. All unrelated libc calls
 * retain their original implementation; x64 code remains in the GHPR worker.
 */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <pthread.h>
#include <spawn.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>
#include "proton_native_map.h"

#define EXPORTED __attribute__((visibility("default")))
#define LIMIT 4096
extern char **environ;
/* Wine's native loader checks for this exported symbol before re-executing.
 * Its null value means no preloader reservations, matching the ARM64 loader.
 */
EXPORTED const void *wine_main_preload_info = NULL;
EXPORTED void *wine_r_debug = NULL;
static char native_dir[LIMIT], wine_root[LIMIT], state_root[LIMIT], logical_ntdll[LIMIT];
static char native_loader[LIMIT], native_server[LIMIT];
static int initialized;
static void *(*real_dlopen)(const char *, int);
static int (*real_dladdr)(const void *, Dl_info *);
static int (*real_execv)(const char *, char *const []);
static int (*real_spawn)(pid_t *, const char *, const posix_spawn_file_actions_t *,
                         const posix_spawnattr_t *, char *const [], char *const []);
static pthread_once_t libc_once = PTHREAD_ONCE_INIT;

static void resolve_libc(void) {
    /* POSIX guarantees dlsym supplies the matching function address. memcpy
       avoids nonportable pointer-punning through a function-pointer lvalue. */
    void *symbol = dlsym(RTLD_NEXT, "dlopen"); memcpy(&real_dlopen, &symbol, sizeof symbol);
    symbol = dlsym(RTLD_NEXT, "dladdr"); memcpy(&real_dladdr, &symbol, sizeof symbol);
    symbol = dlsym(RTLD_NEXT, "execv"); memcpy(&real_execv, &symbol, sizeof symbol);
    symbol = dlsym(RTLD_NEXT, "posix_spawn"); memcpy(&real_spawn, &symbol, sizeof symbol);
    if (!real_dlopen || !real_dladdr || !real_execv || !real_spawn) {
        fprintf(stderr, "[GHPR Proton] Native libc adapter resolution failed.\n");
        _exit(126);
    }
}

static int join(char *out, size_t capacity, const char *root, const char *suffix) {
    int count = snprintf(out, capacity, "%s/%s", root, suffix);
    return count >= 0 && (size_t)count < capacity;
}

static int owned_directory(const char *path) {
    struct stat info;
    if (mkdir(path, 0700) && errno != EEXIST) return 0;
    return !lstat(path, &info) && S_ISDIR(info.st_mode) && info.st_uid == getuid();
}

static int owned_link(const char *path, const char *target) {
    struct stat info;
    if (lstat(path, &info)) return errno == ENOENT && !symlink(target, path);
    if (!S_ISLNK(info.st_mode)) return 0;
    char previous[LIMIT]; ssize_t size = readlink(path, previous, sizeof previous - 1);
    if (size < 0) return 0;
    previous[size] = 0;
    if (!strcmp(previous, target)) return 1;
    /* Only our per-backend prefix links may be repaired. Existing regular files
       and caller-chosen locations are never overwritten or followed. */
    return !unlink(path) && !symlink(target, path);
}

static int initialize_registry(const char *prefix) {
    char path[LIMIT];
    if (!join(path, sizeof path, prefix, "system.reg")) return 0;
    int fd = open(path, O_WRONLY | O_CREAT | O_EXCL | O_CLOEXEC | O_NOFOLLOW, 0600);
    if (fd < 0) {
        struct stat info;
        return errno == EEXIST && !lstat(path, &info) && S_ISREG(info.st_mode) && info.st_uid == getuid();
    }
    static const char registry[] =
        "WINE REGISTRY Version 2\n#arch=win64\n\n"
        "[Software\\\\Microsoft\\\\Wow64\\\\amd64]\n"
        "@=\"libarm64ecfex.dll\"\n\n"
        "[Software\\\\Wine\\\\Drivers]\n\"Graphics\"=\"null\"\n\"Audio\"=\"\"\n\n";
    size_t cursor = 0;
    while (cursor < sizeof registry - 1) {
        ssize_t count = write(fd, registry + cursor, sizeof registry - 1 - cursor);
        if (count < 0 && errno == EINTR) continue;
        if (count <= 0) { close(fd); unlink(path); return 0; }
        cursor += (size_t)count;
    }
    int result = !close(fd);
    return result;
}

static int initialize_system32(const char *prefix, const char *wine) {
    char system32[LIMIT], pe[LIMIT];
    if (!join(system32, sizeof system32, prefix, "drive_c/windows/system32") || !owned_directory(system32) ||
        !join(pe, sizeof pe, wine, "lib/wine/aarch64-windows")) return 0;
    DIR *directory = opendir(pe);
    if (!directory) return 0;
    int valid = 1; struct dirent *entry;
    while ((entry = readdir(directory))) {
        if (!strcmp(entry->d_name, ".") || !strcmp(entry->d_name, "..")) continue;
        char source[LIMIT], destination[LIMIT]; struct stat info;
        if (!join(source, sizeof source, pe, entry->d_name) || lstat(source, &info)) { valid = 0; break; }
        if (S_ISDIR(info.st_mode)) continue;
        if (!S_ISREG(info.st_mode) || !join(destination, sizeof destination, system32, entry->d_name) ||
            !owned_link(destination, source)) { valid = 0; break; }
    }
    closedir(directory);
    /* Keep the directory writable and private. Wine's pinned setupapi
       fake_dll_matches recognizes each existing FILE_ATTRIBUTE_REPARSE_POINT
       as the already installed builtin and registers it without copying.
       New files/subdirectories go into this private directory, never through a
       directory symlink into the immutable installed runtime/content bank.
       Source: pinned dlls/setupapi/fakedll.c fake_dll_matches/install_fake_dll.
     */
    return valid;
}

static int initialize_paths(void) {
    const char *native = getenv("GHPR_NATIVE_DIR"), *wine = getenv("GHPR_WINE_ROOT");
    const char *state = getenv("GHPR_STATE_DIR"), *prefix = getenv("WINEPREFIX");
    if (!native || !wine || !state || !prefix || native[0] != '/' || wine[0] != '/' ||
        state[0] != '/' || prefix[0] != '/' || strlen(native) >= sizeof native_dir ||
        strlen(wine) >= sizeof wine_root || strlen(state) >= sizeof state_root) return 0;
    snprintf(native_dir, sizeof native_dir, "%s", native);
    snprintf(wine_root, sizeof wine_root, "%s", wine);
    snprintf(state_root, sizeof state_root, "%s", state);
    if (!join(logical_ntdll, sizeof logical_ntdll, wine, "lib/wine/aarch64-unix/ntdll.so") ||
        !join(native_loader, sizeof native_loader, native, "libquest_proton.so") ||
        !join(native_server, sizeof native_server, native, "libquest_proton_server.so")) return 0;
    char expected[LIMIT];
    if (!join(expected, sizeof expected, state, "proton-prefix") || strcmp(prefix, expected)) return 0;
    if (!owned_directory(state) || !owned_directory(prefix)) return 0;
    const char *directories[] = {"dosdevices", "drive_c", "drive_c/windows", "drive_c/users",
                                "drive_c/users/quest", "drive_c/users/quest/AppData",
                                "drive_c/users/quest/AppData/Local", ".wineserver"};
    for (size_t index = 0; index < sizeof directories / sizeof directories[0]; index++) {
        if (!join(expected, sizeof expected, prefix, directories[index]) || !owned_directory(expected)) return 0;
    }
    char path[LIMIT];
    if (!initialize_system32(prefix, wine) ||
        !join(path, sizeof path, prefix, "dosdevices/c:") || !owned_link(path, "../drive_c") ||
        !join(path, sizeof path, prefix, "dosdevices/z:") || !owned_link(path, "/")) return 0;
    const char *private_dirs[] = {"tmp", "home", "cache"};
    for (size_t index = 0; index < sizeof private_dirs / sizeof private_dirs[0]; index++) {
        if (!join(expected, sizeof expected, state, private_dirs[index]) || !owned_directory(expected)) return 0;
    }
    if (!initialize_registry(prefix)) return 0;
    initialized = 1;
    return 1;
}

static const char *mapped_library(const char *name, char *result, size_t capacity) {
    if (!initialized || !name) return name;
    const char *short_name = name;
    if (name[0] == '/') {
        char prefix[LIMIT];
        if (!join(prefix, sizeof prefix, wine_root, "lib/wine/aarch64-unix/")) return name;
        size_t length = strlen(prefix);
        if (strncmp(name, prefix, length)) return name;
        short_name = name + length;
    }
    for (size_t index = 0; index < sizeof ghpr_wine_native_map / sizeof ghpr_wine_native_map[0]; index++) {
        if (!strcmp(short_name, ghpr_wine_native_map[index][0])) {
            if (join(result, capacity, native_dir, ghpr_wine_native_map[index][1])) return result;
            errno = ENAMETOOLONG;
            return NULL;
        }
    }
    return name;
}

EXPORTED void *dlopen(const char *name, int flags) {
    pthread_once(&libc_once, resolve_libc);
    char path[LIMIT]; const char *mapped = mapped_library(name, path, sizeof path);
    if (name && !mapped) return NULL;
    return real_dlopen(mapped, flags);
}

EXPORTED int dladdr(const void *address, Dl_info *info) {
    pthread_once(&libc_once, resolve_libc);
    int result = real_dladdr(address, info);
    if (result && initialized && info->dli_fname) {
        char ntdll[LIMIT];
        if (join(ntdll, sizeof ntdll, native_dir, "libqn.so") && !strcmp(info->dli_fname, ntdll))
            info->dli_fname = logical_ntdll;
    }
    return result;
}

static const char *mapped_executable(const char *path, char *normalized, size_t capacity) {
    if (!initialized || !path) return path;
    /* Wine uses lexical '..' segments to derive bin_dir; no writable file is
       executed. Normalize only paths inside the fixed Wine installed tree. */
    size_t root = strlen(wine_root);
    if (strncmp(path, wine_root, root) || path[root] != '/') return path;
    if (strlen(path) >= capacity) { errno = ENAMETOOLONG; return NULL; }
    char copy[LIMIT]; snprintf(copy, sizeof copy, "%s", path);
    char *parts[512], *save = NULL; size_t count = 0;
    for (char *part = strtok_r(copy, "/", &save); part; part = strtok_r(NULL, "/", &save)) {
        if (!strcmp(part, ".")) continue;
        if (!strcmp(part, "..")) { if (!count) return path; count--; continue; }
        if (count == sizeof parts / sizeof parts[0]) { errno = ENAMETOOLONG; return NULL; }
        parts[count++] = part;
    }
    normalized[0] = 0;
    for (size_t index = 0; index < count; index++) {
        size_t length = strlen(normalized), extra = strlen(parts[index]);
        if (length + extra + 2 > capacity) { errno = ENAMETOOLONG; return NULL; }
        normalized[length] = '/'; memcpy(normalized + length + 1, parts[index], extra + 1);
    }
    char expected[LIMIT];
    if (join(expected, sizeof expected, wine_root, "lib/wine/aarch64-unix/wine") && !strcmp(normalized, expected))
        return native_loader;
    if (join(expected, sizeof expected, wine_root, "bin/wineserver") && !strcmp(normalized, expected))
        return native_server;
    return path;
}

EXPORTED int execv(const char *path, char *const argv[]) {
    pthread_once(&libc_once, resolve_libc);
    char normalized[LIMIT]; const char *mapped = mapped_executable(path, normalized, sizeof normalized);
    if (!mapped) return -1;
    return real_execv(mapped, argv);
}

EXPORTED int posix_spawn(pid_t *pid, const char *path, const posix_spawn_file_actions_t *actions,
                        const posix_spawnattr_t *attributes, char *const argv[], char *const env[]) {
    pthread_once(&libc_once, resolve_libc);
    char normalized[LIMIT]; const char *mapped = mapped_executable(path, normalized, sizeof normalized);
    if (!mapped) return ENAMETOOLONG;
    return real_spawn(pid, mapped, actions, attributes, argv, env);
}

int main(int argc, char *argv[]) {
    pthread_once(&libc_once, resolve_libc);
    if (!initialize_paths()) {
        fprintf(stderr, "[GHPR Proton] Source-owned private Wine layout/prefix initialization failed (errno=%d).\n", errno);
        return 126;
    }
    char ntdll[LIMIT];
    if (!join(ntdll, sizeof ntdll, native_dir, "libqn.so")) return 126;
    void *library = real_dlopen(ntdll, RTLD_NOW | RTLD_GLOBAL);
    if (!library) {
        fprintf(stderr, "[GHPR Proton] APK-packaged native ntdll load failed: %s\n", dlerror());
        return 126;
    }
    void *symbol = dlsym(library, "__wine_main");
    void (*wine_main)(int, char **); memcpy(&wine_main, &symbol, sizeof symbol);
    if (!wine_main) {
        fprintf(stderr, "[GHPR Proton] Native ntdll lacks __wine_main.\n");
        return 126;
    }
    /* Rename argv[0] only in this child; Wine's native command-line dispatcher
       otherwise interprets libquest_proton.so as a requested builtin program. */
    argv[0] = (char *)"wine";
    fprintf(stderr, "[GHPR Proton] backend=proton-arm64ec-fex x64Emulator=libarm64ecfex.dll unixHelper=disabled synchronization=server\n");
    wine_main(argc, argv);
    fprintf(stderr, "[GHPR Proton] Native Wine entry returned unexpectedly.\n");
    return 126;
}
