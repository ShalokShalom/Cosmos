#ifndef SCALE_ADAPTER_H
#define SCALE_ADAPTER_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "scale_abi.h"

#ifdef __cplusplus
extern "C" {
#endif

/*
 * The adapter targets one ABI version. It must equal SCALE_ABI_VERSION in
 * scale_abi.h; the assertion in scale_adapter.c makes a mismatch a compile
 * error, and scale_adapter_open checks the loaded kernel at run time.
 */
#define SCALE_EXPECTED_ABI_VERSION 4

typedef enum scale_adapter_status {
    SCALE_ADAPTER_OK = 0,
    SCALE_ADAPTER_INVALID_ARGUMENT = 1,
    SCALE_ADAPTER_OPEN_FAILED = 2,
    SCALE_ADAPTER_SYMBOL_MISSING = 3,
    SCALE_ADAPTER_VERSION_MISMATCH = 4
} scale_adapter_status;

typedef int32_t (SCALE_CALL *scale_abi_version_function)(void);

typedef int32_t (SCALE_CALL *scale_init_function)(
    const scale_callbacks *callbacks,
    const uint8_t *prelude,
    size_t prelude_len,
    void **out_handle);

typedef int32_t (SCALE_CALL *scale_set_callbacks_function)(
    void *handle,
    const scale_callbacks *callbacks);

typedef int32_t (SCALE_CALL *scale_run_function)(
    void *handle,
    const uint8_t *program,
    size_t program_len,
    uint8_t **out_result,
    size_t *out_len);

typedef int32_t (SCALE_CALL *scale_verify_function)(
    void *handle,
    const uint8_t *program,
    size_t program_len,
    uint8_t **out_result,
    size_t *out_len);

typedef int32_t (SCALE_CALL *scale_transport_send_function)(
    void *handle,
    const uint8_t *target,
    size_t target_len,
    const uint8_t *payload,
    size_t payload_len);

typedef int32_t (SCALE_CALL *scale_transport_acquire_function)(
    void *handle,
    const uint8_t *target,
    size_t target_len,
    const uint8_t *payload,
    size_t payload_len,
    uint8_t **out_result,
    size_t *out_len);

typedef int32_t (SCALE_CALL *scale_host_call_function)(
    void *handle,
    const uint8_t *package,
    size_t package_len,
    const uint8_t *function,
    size_t function_len,
    const uint8_t *args,
    size_t args_len,
    uint8_t **out_result,
    size_t *out_len);

typedef int32_t (SCALE_CALL *scale_image_function)(
    void *handle,
    int32_t direction,
    const uint8_t *in_bytes,
    size_t in_len,
    uint8_t **out_bytes,
    size_t *out_len);

typedef void (SCALE_CALL *scale_free_function)(void *handle);

/* The resolved export table. A host calls these after scale_adapter_open. */
typedef struct scale_kernel {
    scale_abi_version_function abi_version;
    scale_init_function init;
    scale_set_callbacks_function set_callbacks;
    scale_run_function run;
    scale_verify_function verify;
    scale_transport_send_function transport_send;
    scale_transport_acquire_function transport_acquire;
    scale_host_call_function host_call;
    scale_image_function image;
    scale_free_function free;
} scale_kernel;

typedef struct scale_adapter scale_adapter;

/* Equality is the whole compatibility rule. */
bool scale_adapter_accepts_version(int32_t actual);

/*
 * Loads the kernel shared library, resolves the export table, and calls
 * scale_abi_version before any other call. A different version is refused with
 * SCALE_ADAPTER_VERSION_MISMATCH and no kernel handle is possible. The adapter
 * owns the library, not the kernel handle: the host calls scale_free.
 */
scale_adapter *scale_adapter_open(const char *library_path, scale_adapter_status *status);

const scale_kernel *scale_adapter_kernel(const scale_adapter *adapter);

const char *scale_adapter_describe(scale_adapter_status status);

/* Frees the library. The host must call kernel->free on a live handle first. */
void scale_adapter_close(scale_adapter *adapter);

#ifdef __cplusplus
}
#endif

#endif
