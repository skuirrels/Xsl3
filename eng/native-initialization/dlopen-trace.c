// Licensed under the MIT license. See LICENSE.TXT in the repository root.

#define _GNU_SOURCE
#include <dlfcn.h>
#include <execinfo.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>

// Diagnostic interposition only. Never linked into the product or acceptance image.
void *dlopen(const char *name, int flags) {
    static void *(*next)(const char *, int);
    static __thread int tracing;
    if (!next) next = dlsym(RTLD_NEXT, "dlopen");
    if (!tracing && name && (strstr(name, "icu") || strstr(name, "Globalization"))) {
        tracing = 1;
        void *frames[64];
        int count = backtrace(frames, 64);
        dprintf(STDERR_FILENO, "P7_DLOPEN %s\n", name);
        backtrace_symbols_fd(frames, count, STDERR_FILENO);
        tracing = 0;
    }
    return next(name, flags);
}
