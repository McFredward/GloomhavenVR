/* Package this ARM64 executable in nativeLibraryDir, never writable app data. */
#include <stdlib.h>
#include <stdio.h>
#include <unistd.h>
int main(int argc, char **argv) {
    const char *box = getenv("GHPR_BOX64"), *guest = getenv("GHPR_WINESERVER");
    if (!box || !guest) { fputs("Quest procedural Wine server launcher was not configured.\n", stderr); return 70; }
    char **arguments = calloc((size_t)argc + 2, sizeof *arguments);
    if (!arguments) return 71;
    arguments[0] = (char *)box; arguments[1] = (char *)guest;
    for (int index = 1; index < argc; index++) arguments[index + 1] = argv[index];
    execv(box, arguments);
    perror("Quest procedural packaged Box64 launcher");
    free(arguments); return 72;
}
