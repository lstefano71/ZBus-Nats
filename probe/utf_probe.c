// UTF probe: demonstrates the difference between T1 and UTF8 ⎕NA types.
// Build: cl /LD /O2 utf_probe.c /Fe:utf_probe.dll

#include <stdio.h>
#include <string.h>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT
#endif

static FILE* logfile = NULL;

static void open_log(void) {
    if (!logfile) {
        char path[512];
        const char* temp = getenv("TEMP");
        if (!temp) temp = ".";
        snprintf(path, sizeof(path), "%s\\utf_probe_log.txt", temp);
        logfile = fopen(path, "a");
    }
}

// Receives raw bytes via T1 — no encoding applied by interpreter
EXPORT void probe_t1(const char* str, int len) {
    open_log();
    fprintf(logfile, "probe_t1: len=%d bytes:", len);
    for (int i = 0; i < len; i++)
        fprintf(logfile, " %02X", (unsigned char)str[i]);
    fprintf(logfile, " text=\"%s\"\n", str);
    fflush(logfile);
}

// Receives UTF-8 encoded bytes via UTF8 — interpreter encodes before calling
EXPORT void probe_utf8(const char* str, int len) {
    open_log();
    fprintf(logfile, "probe_utf8: len=%d bytes:", len);
    for (int i = 0; i < len; i++)
        fprintf(logfile, " %02X", (unsigned char)str[i]);
    fprintf(logfile, " text=\"%s\"\n", str);
    fflush(logfile);
}

// Receives wide chars via T2 — raw 16-bit codepoints (Windows wchar_t)
EXPORT void probe_t2(const unsigned short* str, int len) {
    open_log();
    fprintf(logfile, "probe_t2: len=%d words:", len);
    for (int i = 0; i < len; i++)
        fprintf(logfile, " %04X", str[i]);
    fprintf(logfile, "\n");
    fflush(logfile);
}
