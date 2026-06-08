// Z-Format probe DLL — captures Z buffers from the interpreter for analysis.
// Build: cl /LD /O2 z_probe.c /Fe:z_probe.dll
//
// Usage from APL:
//   'probe_in'  ⎕NA 'z_probe|probe_z_in =Z'
//   'probe_out' ⎕NA 'z_probe|probe_z_out >Z'
//   'probe_echo' ⎕NA 'z_probe|probe_z_echo =Z'

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <windows.h>

// Global log file
static FILE* g_log = NULL;
static HANDLE g_heap = NULL;

static void open_log(void) {
    if (!g_log) {
        char path[MAX_PATH];
        GetTempPathA(MAX_PATH, path);
        strcat(path, "z_probe_log.txt");
        g_log = fopen(path, "a");
        if (g_log) fprintf(g_log, "\n=== Z Probe Session ===\n");
    }
}

static void dump_hex(const uint8_t* buf, size_t len, const char* label) {
    if (!g_log) return;
    fprintf(g_log, "\n--- %s (%zu bytes) ---\n", label, len);
    for (size_t i = 0; i < len; i += 16) {
        fprintf(g_log, "%04zx: ", i);
        for (size_t j = 0; j < 16 && (i+j) < len; j++)
            fprintf(g_log, "%02x ", buf[i+j]);
        fprintf(g_log, "\n");
    }
    // Also decode structured fields
    if (len >= 8) {
        uint32_t size_be = (buf[0]<<24)|(buf[1]<<16)|(buf[2]<<8)|buf[3];
        uint32_t flags_be = (buf[4]<<24)|(buf[5]<<16)|(buf[6]<<8)|buf[7];
        fprintf(g_log, "  Z header: size=%u flags=0x%08X\n", size_be, flags_be);
    }
    if (len >= 24) {
        int64_t wc = *(int64_t*)(buf + 8);
        int64_t zones = *(int64_t*)(buf + 16);
        int type = zones & 0xF;
        int rank = (zones >> 4) & 0xF;
        int eltype = (zones >> 8) & 0xF;
        int squoze = (zones >> 13) & 1;
        fprintf(g_log, "  wc=%lld zones=0x%04llX (type=%d rank=%d eltype=%d squoze=%d)\n",
                wc, zones, type, rank, eltype, squoze);
    }
    if (len >= 32) {
        int64_t zones = *(int64_t*)(buf + 16);
        int rank = (zones >> 4) & 0xF;
        if (rank > 0) {
            fprintf(g_log, "  shape: ");
            for (int i = 0; i < rank && (24 + i*8 + 8) <= (int)len; i++)
                fprintf(g_log, "%lld ", *(int64_t*)(buf + 24 + i*8));
            fprintf(g_log, "\n");
        }
    }
    fflush(g_log);
}

// =Z: receives input from interpreter, dumps it, passes it back unchanged
__declspec(dllexport) int __cdecl probe_z_echo(intptr_t* z_param) {
    open_log();
    
    // =Z input: z_param points to [self-pointer][Z payload]
    // The self-pointer at offset 0 points to offset 8 (the Z payload)
    uint8_t* base = (uint8_t*)z_param;
    intptr_t payload_ptr = *(intptr_t*)base;
    uint8_t* z_buf = (uint8_t*)payload_ptr;
    
    // Read size from Z header
    uint32_t total_size = (z_buf[0]<<24)|(z_buf[1]<<16)|(z_buf[2]<<8)|z_buf[3];
    
    if (g_log) fprintf(g_log, "\nprobe_z_echo: input at %p, payload at %p\n", base, z_buf);
    dump_hex(z_buf, total_size, "=Z INPUT");
    
    // For =Z output: allocate a copy and redirect pointer
    if (!g_heap) g_heap = GetProcessHeap();
    uint8_t* out_buf = (uint8_t*)HeapAlloc(g_heap, HEAP_ZERO_MEMORY, total_size);
    memcpy(out_buf, z_buf, total_size);
    
    // Redirect the pointer (=Z output convention)
    *(intptr_t*)z_param = (intptr_t)out_buf;
    
    if (g_log) fprintf(g_log, "probe_z_echo: output at %p (copy)\n", out_buf);
    fflush(g_log);
    return 0;
}

// =Z: receives input, dumps it, produces a simple int scalar as output
__declspec(dllexport) int __cdecl probe_z_in(intptr_t* z_param) {
    open_log();
    
    uint8_t* base = (uint8_t*)z_param;
    intptr_t payload_ptr = *(intptr_t*)base;
    uint8_t* z_buf = (uint8_t*)payload_ptr;
    
    uint32_t total_size = (z_buf[0]<<24)|(z_buf[1]<<16)|(z_buf[2]<<8)|z_buf[3];
    
    if (g_log) fprintf(g_log, "\nprobe_z_in: input at %p\n", z_buf);
    dump_hex(z_buf, total_size, "=Z INPUT (probe_z_in)");
    
    // Output: int scalar 0 (success indicator)
    if (!g_heap) g_heap = GetProcessHeap();
    int out_size = 32; // 8 header + 8 wc + 8 zones + 8 data
    uint8_t* out_buf = (uint8_t*)HeapAlloc(g_heap, HEAP_ZERO_MEMORY, out_size);
    // Z header (big-endian)
    out_buf[0] = 0; out_buf[1] = 0; out_buf[2] = 0; out_buf[3] = 32;
    out_buf[4] = 0; out_buf[5] = 0; out_buf[6] = 0; out_buf[7] = 0xA4;
    // Pocket: wc=4, zones=0x220F (APLSINT scalar), data=0
    *(int64_t*)(out_buf + 8) = 4;
    *(int64_t*)(out_buf + 16) = 0x220F; // TYPESIMPLE|rank0|APLSINT|squoze
    *(int64_t*)(out_buf + 24) = 0;
    
    *(intptr_t*)z_param = (intptr_t)out_buf;
    return 0;
}

// >Z: produces a test output (string "OK")
__declspec(dllexport) int __cdecl probe_z_out(intptr_t* z_param) {
    open_log();
    
    if (!g_heap) g_heap = GetProcessHeap();
    // Output: char vector "OK" as APLWCHAR8 (single-byte chars)
    int out_size = 40; // 8 header + 8 wc + 8 zones + 8 shape + 8 data(2 bytes + 6 pad)
    uint8_t* out_buf = (uint8_t*)HeapAlloc(g_heap, HEAP_ZERO_MEMORY, out_size);
    // Z header
    out_buf[0] = 0; out_buf[1] = 0; out_buf[2] = 0; out_buf[3] = 40;
    out_buf[4] = 0; out_buf[5] = 0; out_buf[6] = 0; out_buf[7] = 0xA4;
    // Pocket: APLWCHAR8 rank-1
    *(int64_t*)(out_buf + 8) = 5;       // wc = (16+8+8+8)/8
    *(int64_t*)(out_buf + 16) = 0x271F; // TYPESIMPLE|rank1|APLWCHAR8|squoze
    *(int64_t*)(out_buf + 24) = 2;      // shape = 2 chars
    out_buf[32] = 'O';
    out_buf[33] = 'K';
    
    *(intptr_t*)z_param = (intptr_t)out_buf;
    
    if (g_log) fprintf(g_log, "\nprobe_z_out: produced 'OK' as APLWCHAR8 at %p\n", out_buf);
    fflush(g_log);
    return 0;
}

// FreeUsedDyalogResult — interpreter calls this to free >Z and =Z output buffers
__declspec(dllexport) int __cdecl FreeUsedDyalogResult(intptr_t ptr) {
    if (!g_heap) g_heap = GetProcessHeap();
    if (ptr) HeapFree(g_heap, 0, (void*)ptr);
    return 1;
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpReserved) {
    if (fdwReason == DLL_PROCESS_DETACH && g_log) {
        fprintf(g_log, "=== Session End ===\n");
        fclose(g_log);
        g_log = NULL;
    }
    return TRUE;
}
