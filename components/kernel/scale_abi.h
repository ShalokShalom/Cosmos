#ifndef SCALE_ABI_H
#define SCALE_ABI_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define SCALE_ABI_VERSION 4

#if defined(_WIN32)
#define SCALE_CALL __cdecl
#else
#define SCALE_CALL
#endif

typedef enum scale_status {
    SCALE_OK = 0,
    SCALE_ERROR = 1,
    SCALE_PANIC = 2,
    SCALE_INVALID = 3,
    SCALE_UNIMPLEMENTED = 4
} scale_status;

typedef enum scale_image_direction {
    SCALE_IMAGE_EXPORT = 0,
    SCALE_IMAGE_IMPORT = 1,
    SCALE_IMAGE_REPLACE_UNIT = 2,
    SCALE_IMAGE_PATCH_UNIT = 3
} scale_image_direction;

typedef enum scale_log_level {
    SCALE_LOG_DEBUG = 0,
    SCALE_LOG_INFO = 1,
    SCALE_LOG_WARNING = 2,
    SCALE_LOG_ERROR = 3
} scale_log_level;

/*
 * The host owns every callback and the opaque context. The callback table and
 * its context must outlive the kernel handle. Memory returned by the kernel in
 * an out parameter is allocated with alloc and is freed by the host with free.
 * A buffer passed into the kernel is borrowed for the duration of the call.
 */
typedef struct scale_callbacks {
    void *context;
    void * (SCALE_CALL *alloc)(size_t size);
    void (SCALE_CALL *free)(void *pointer);
    int32_t (SCALE_CALL *host_dispatch)(
        void *context,
        const uint8_t *package, size_t package_len,
        const uint8_t *function, size_t function_len,
        const uint8_t *args, size_t args_len,
        uint8_t **out_result, size_t *out_len);
    void (SCALE_CALL *log)(
        void *context,
        int32_t level,
        const uint8_t *message, size_t message_len);
    void (SCALE_CALL *abort)(void *context);
} scale_callbacks;

/* Returns SCALE_ABI_VERSION. A host must check this before any other call. */
int32_t SCALE_CALL scale_abi_version(void);

/*
 * Installs the callbacks and validates the prelude image in the kernel. A
 * valid image is retained by the handle; during the transition to
 * kernel-owned execution it is also sent to the host adapter through
 * callbacks->host_dispatch with package "scale" and function "prelude".
 * A null prelude with length zero is allowed.
 * On success the handle is written to out_handle and SCALE_OK is returned.
 * An invalid image returns SCALE_INVALID and out_handle is set to NULL.
 *
 * Lifetime and concurrency: each successful scale_init creates an independent
 * handle with its own callback table and image. Any number of handles may be
 * live in one process. A handle is invalid after scale_free; calls through it
 * return SCALE_INVALID and freeing it again is a no-op. The registry is
 * thread-safe; calls that share a handle are serialized by the kernel handle
 * gate. A callback must not reenter the same handle. A host still serializes
 * callbacks if its callback table is not thread-safe.
 */
int32_t SCALE_CALL scale_init(
    const scale_callbacks *callbacks,
    const uint8_t *prelude, size_t prelude_len,
    void **out_handle);

/*
 * Replaces the callback table for a live handle. The handle keeps its original
 * opaque value; the new table and its context must outlive the handle.
 */
int32_t SCALE_CALL scale_set_callbacks(
    void *handle,
    const scale_callbacks *callbacks);

/*
 * Runs one exported image unit by name. The kernel resolves the export, runs
 * the unit with the kernel abstraction, and writes the textual top-of-stack result to
 * out_result. Image-resident words execute recursively. A word declared in the
 * image's "primitives" registry is dispatched to callbacks->host_dispatch as
 * package "scale" and function <word>; args is
 * {"word":...,"operands":[{"shape":[...],"data":[...]},...]} and the host
 * replies {"results":[{"shape":[...],"data":[...]},...]} with out_result/out_len
 * allocated through callbacks->alloc. The kernel frees that reply with
 * callbacks->free. A non-zero status, missing result, or wrong result count
 * fails the run. out_result for scale_run is also allocated with
 * callbacks->alloc; the host frees it with callbacks->free.
 */
int32_t SCALE_CALL scale_run(
    void *handle,
    const uint8_t *program, size_t program_len,
    uint8_t **out_result, size_t *out_len);

/*
 * Verifies one term-program document and writes its receipt. The document is
 * the `term-program` JSON form accepted by the portable reader; the verifier
 * hashes its canonical bytes.
 * On success the canonical `term-receipt` JSON is allocated with
 * callbacks->alloc and returned through out_result/out_len; the host frees it
 * with callbacks->free. A malformed or oversized document, a null output pair,
 * or an invalid handle returns SCALE_INVALID; a malformed document also writes
 * a diagnostic text. The receipt is not an execution authorization until the
 * P3 runtime representation makes execution consume it.
 */
int32_t SCALE_CALL scale_verify(
    void *handle,
    const uint8_t *program, size_t program_len,
    uint8_t **out_result, size_t *out_len);

/* Delivers one transport payload to a named peer. */
int32_t SCALE_CALL scale_transport_send(
    void *handle,
    const uint8_t *target, size_t target_len,
    const uint8_t *payload, size_t payload_len);

/*
 * Acquires a transport resource and writes the host's opaque token result.
 * The token result is allocated through callbacks->alloc and is freed by the
 * host with callbacks->free. The host table is the only mapping from token to
 * live handle; the kernel never dereferences the token.
 */
int32_t SCALE_CALL scale_transport_acquire(
    void *handle,
    const uint8_t *target, size_t target_len,
    const uint8_t *payload, size_t payload_len,
    uint8_t **out_result, size_t *out_len);

/* Calls a host function through the callback table. */
int32_t SCALE_CALL scale_host_call(
    void *handle,
    const uint8_t *package, size_t package_len,
    const uint8_t *function, size_t function_len,
    const uint8_t *args, size_t args_len,
    uint8_t **out_result, size_t *out_len);

/*
 * Imports or exports the current image, replaces one named unit, or applies a
 * unit patch. SCALE_IMAGE_IMPORT validates a whole image. SCALE_IMAGE_REPLACE_UNIT
 * reads one unit document from in_bytes and revalidates the whole graph before the
 * swap. SCALE_IMAGE_PATCH_UNIT reads
 * {"unit":...,"exports":[...],"groups":[...],"dependencyPins":{...}} and can add
 * or replace a unit, update exports and groups, and check dependency source
 * hashes. Replacement and patch preserve the unit's arity and capture slots; a
 * signature change requires whole-image import. The
 * image source hash is recomputed from the canonical artifact after a unit swap.
 * Import, replacement, and patch can return the previous image through out_bytes
 * for host rollback. out_bytes for export is allocated with callbacks->alloc.
 * Unit replacement preserves the handle's execution state; state is not
 * serialized or migrated between handles.
 */
int32_t SCALE_CALL scale_image(
    void *handle,
    int32_t direction,
    const uint8_t *in_bytes, size_t in_len,
    uint8_t **out_bytes, size_t *out_len);

/* Releases the kernel handle. The callback table is not freed. */
void SCALE_CALL scale_free(void *handle);

#ifdef __cplusplus
}
#endif

#endif
