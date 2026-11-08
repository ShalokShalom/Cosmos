#if !defined(_WIN32)
#define _POSIX_C_SOURCE 200809L
#endif

#include "scale_adapter.h"

#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#include <windows.h>
typedef HMODULE scale_library;
#else
#include <dlfcn.h>
typedef void *scale_library;
#endif

_Static_assert(
    SCALE_EXPECTED_ABI_VERSION == SCALE_ABI_VERSION,
    "SCALE_EXPECTED_ABI_VERSION must equal SCALE_ABI_VERSION in scale_abi.h");

struct scale_adapter {
    scale_library library;
    scale_kernel kernel;
};

static scale_library library_open(const char *path) {
#if defined(_WIN32)
    return LoadLibraryA(path);
#else
    return dlopen(path, RTLD_NOW | RTLD_LOCAL);
#endif
}

static void library_close(scale_library library) {
#if defined(_WIN32)
    FreeLibrary(library);
#else
    dlclose(library);
#endif
}

static void *library_symbol(scale_library library, const char *name) {
#if defined(_WIN32)
    FARPROC symbol = GetProcAddress(library, name);
    union {
        FARPROC function;
        void *object;
    } cast;
    cast.function = symbol;
    return cast.object;
#else
    return dlsym(library, name);
#endif
}

#define LOAD_MEMBER(member, symbol_name, function_type) \
    do { \
        union { \
            void *object; \
            function_type function; \
        } symbol; \
        symbol.object = library_symbol(adapter->library, symbol_name); \
        if (symbol.object == NULL) { \
            status = SCALE_ADAPTER_SYMBOL_MISSING; \
            goto fail; \
        } \
        adapter->kernel.member = symbol.function; \
    } while (0)

bool scale_adapter_accepts_version(int32_t actual) {
    return actual == SCALE_EXPECTED_ABI_VERSION;
}

scale_adapter *scale_adapter_open(const char *library_path, scale_adapter_status *out_status) {
    scale_adapter_status status = SCALE_ADAPTER_OK;

    if (out_status != NULL) {
        *out_status = status;
    }

    if (library_path == NULL || library_path[0] == '\0') {
        if (out_status != NULL) {
            *out_status = SCALE_ADAPTER_INVALID_ARGUMENT;
        }

        return NULL;
    }

    scale_adapter *adapter = (scale_adapter *)calloc(1, sizeof *adapter);

    if (adapter == NULL) {
        if (out_status != NULL) {
            *out_status = SCALE_ADAPTER_OPEN_FAILED;
        }

        return NULL;
    }

    adapter->library = library_open(library_path);

    if (adapter->library == NULL) {
        free(adapter);

        if (out_status != NULL) {
            *out_status = SCALE_ADAPTER_OPEN_FAILED;
        }

        return NULL;
    }

    /* The version is checked before any other symbol is trusted. */
    LOAD_MEMBER(abi_version, "scale_abi_version", scale_abi_version_function);

    if (!scale_adapter_accepts_version(adapter->kernel.abi_version())) {
        status = SCALE_ADAPTER_VERSION_MISMATCH;
        goto fail;
    }

    LOAD_MEMBER(init, "scale_init", scale_init_function);
    LOAD_MEMBER(set_callbacks, "scale_set_callbacks", scale_set_callbacks_function);
    LOAD_MEMBER(run, "scale_run", scale_run_function);
    LOAD_MEMBER(verify, "scale_verify", scale_verify_function);
    LOAD_MEMBER(transport_send, "scale_transport_send", scale_transport_send_function);
    LOAD_MEMBER(transport_acquire, "scale_transport_acquire", scale_transport_acquire_function);
    LOAD_MEMBER(host_call, "scale_host_call", scale_host_call_function);
    LOAD_MEMBER(image, "scale_image", scale_image_function);
    LOAD_MEMBER(free, "scale_free", scale_free_function);

    if (out_status != NULL) {
        *out_status = SCALE_ADAPTER_OK;
    }

    return adapter;

fail:
    library_close(adapter->library);
    free(adapter);

    if (out_status != NULL) {
        *out_status = status;
    }

    return NULL;
}

const scale_kernel *scale_adapter_kernel(const scale_adapter *adapter) {
    return adapter == NULL ? NULL : &adapter->kernel;
}

const char *scale_adapter_describe(scale_adapter_status status) {
    switch (status) {
    case SCALE_ADAPTER_OK:
        return "ok";
    case SCALE_ADAPTER_INVALID_ARGUMENT:
        return "invalid argument";
    case SCALE_ADAPTER_OPEN_FAILED:
        return "the kernel library could not be loaded";
    case SCALE_ADAPTER_SYMBOL_MISSING:
        return "a required kernel export is missing";
    case SCALE_ADAPTER_VERSION_MISMATCH:
        return "the kernel ABI version differs from the adapter's expected version";
    }

    return "unknown adapter status";
}

void scale_adapter_close(scale_adapter *adapter) {
    if (adapter == NULL) {
        return;
    }

    library_close(adapter->library);
    free(adapter);
}

/*
 * Potential extensions (not commitments)
 * - Windows and macOS loading: platform-specific platform_load implementations.
 * - Chunked image transfer: stream large images without one contiguous buffer.
 * - Callback threading and async completion: host work off the ABI thread.
 * - Versioned feature negotiation: use newer kernel exports only when present.
 * - Generated bindings: derive C, Rust, Go, and C++ wrappers from scale_abi.h.
 */
