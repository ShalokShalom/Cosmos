/*
 * The conformance test is the executable definition of a correct adapter.
 *
 * It loads a kernel through the C adapter, checks the ABI version and both
 * sides of version equality, initialises a known image, runs an exported unit
 * that dispatches a declared primitive, verifies the result, and frees the
 * handle. Passing this harness is the definition of "the adapter is correct".
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "scale_adapter.h"

/* The known image: one exported unit that pushes two numbers and calls the
 * declared primitive `add`. The host answers with 5. */
static const char image_known[] =
    "{"
    "\"schema\":2,"
    "\"form\":\"image\","
    "\"name\":\"conformance\","
    "\"version\":\"1\","
    "\"sourceHash\":\"conformance-source\","
    "\"imports\":[],"
    "\"exports\":[{\"name\":\"main\",\"unit\":\"main\"}],"
    "\"capabilities\":[],"
    "\"kinds\":[],"
    "\"predicates\":[],"
    "\"primitives\":[{\"name\":\"add\",\"arity\":2,\"results\":1}],"
    "\"units\":[{"
    "\"name\":\"main\","
    "\"sourceHash\":\"main-source\","
    "\"dependencies\":[],"
    "\"arity\":0,"
    "\"captures\":[],"
    "\"instructions\":["
    "{\"op\":\"number\",\"value\":2},"
    "{\"op\":\"number\",\"value\":3},"
    "{\"op\":\"call\",\"name\":\"add\"}"
    "]"
    "}],"
    "\"groups\":[]"
    "}";

/* One replacement unit: it pushes 4 and 5 and calls the same primitive. */
static const char unit_replacement[] =
    "{"
    "\"name\":\"main\","
    "\"sourceHash\":\"main-source-2\","
    "\"dependencies\":[],"
    "\"arity\":0,"
    "\"captures\":[],"
    "\"instructions\":["
    "{\"op\":\"number\",\"value\":4},"
    "{\"op\":\"number\",\"value\":5},"
    "{\"op\":\"call\",\"name\":\"add\"}"
    "]"
    "}";

/* A replacement that changes arity must be refused. */
static const char unit_wrong_arity[] =
    "{"
    "\"name\":\"main\","
    "\"sourceHash\":\"main-source-3\","
    "\"dependencies\":[],"
    "\"arity\":1,"
    "\"captures\":[],"
    "\"instructions\":[{\"op\":\"number\",\"value\":1}]"
    "}";

/* A patch that adds a helper unit and exports it. */
static const char unit_patch[] =
    "{"
    "\"unit\":{"
    "\"name\":\"helper\","
    "\"sourceHash\":\"helper-source\","
    "\"dependencies\":[],"
    "\"arity\":0,"
    "\"captures\":[],"
    "\"instructions\":[{\"op\":\"number\",\"value\":11}]"
    "},"
    "\"exports\":[{\"name\":\"helper\",\"unit\":\"helper\"}]"
    "}";

/* The same image with a schema the kernel must refuse. */
static const char image_wrong_schema[] =
    "{"
    "\"schema\":1,"
    "\"form\":\"image\","
    "\"name\":\"conformance\","
    "\"version\":\"1\","
    "\"sourceHash\":\"conformance-source\","
    "\"imports\":[],"
    "\"exports\":[{\"name\":\"main\",\"unit\":\"main\"}],"
    "\"capabilities\":[],"
    "\"kinds\":[],"
    "\"predicates\":[],"
    "\"primitives\":[{\"name\":\"add\",\"arity\":2,\"results\":1}],"
    "\"units\":[{"
    "\"name\":\"main\","
    "\"sourceHash\":\"main-source\","
    "\"dependencies\":[],"
    "\"arity\":0,"
    "\"captures\":[],"
    "\"instructions\":["
    "{\"op\":\"number\",\"value\":2},"
    "{\"op\":\"number\",\"value\":3},"
    "{\"op\":\"call\",\"name\":\"add\"}"
    "]"
    "}],"
    "\"groups\":[]"
    "}";

static int failures = 0;
static int saw_prelude = 0;
static int saw_add = 0;

static void check(int condition, const char *message) {
    if (condition) {
        printf("  PASS: %s\n", message);
    } else {
        printf("  FAIL: %s\n", message);
        failures++;
    }
}

static void *conformance_alloc(size_t size) {
    return malloc(size);
}

static void conformance_free(void *pointer) {
    free(pointer);
}

/* Sums the primitive's two `data` operands. The harness is the reference host,
 * so a small scan of the known request shape is enough; it refuses anything
 * that is not two numeric vectors. */
static int sum_operands(const uint8_t *args, size_t args_len, double *total) {
    const char *cursor = (const char *)args;
    const char *end = cursor + args_len;
    const char needle[] = "\"data\"";
    int operands = 0;

    while (cursor < end) {
        const char *found = NULL;

        for (const char *p = cursor; p + 6 <= end; p++) {
            if (memcmp(p, needle, 6) == 0) {
                found = p;
                break;
            }
        }

        if (found == NULL) {
            break;
        }

        const char *p = found + 6;

        while (p < end && (*p == ' ' || *p == '\t' || *p == ':')) {
            p++;
        }

        if (p >= end || *p != '[') {
            return 0;
        }

        p++;

        char *number_end = NULL;
        double value = strtod(p, &number_end);

        if (number_end == p || number_end > end) {
            return 0;
        }

        *total += value;
        operands++;
        cursor = number_end;
    }

    return operands;
}

static int32_t conformance_dispatch(
    void *context,
    const uint8_t *package,
    size_t package_len,
    const uint8_t *function,
    size_t function_len,
    const uint8_t *args,
    size_t args_len,
    uint8_t **out_result,
    size_t *out_len) {
    char name[64];

    (void)context;

    if (package == NULL || function == NULL) {
        return SCALE_INVALID;
    }

    if (function_len >= sizeof name) {
        return SCALE_INVALID;
    }

    memcpy(name, function, function_len);
    name[function_len] = '\0';

    /* The prelude notification carries no result buffers: scale_init uses it
     * only to hand the validated image to the host. */
    if (package_len == 5 && memcmp(package, "scale", 5) == 0 && strcmp(name, "prelude") == 0) {
        saw_prelude = 1;

        if (out_result != NULL) {
            *out_result = NULL;
        }

        if (out_len != NULL) {
            *out_len = 0;
        }

        return SCALE_OK;
    }

    if (out_result == NULL || out_len == NULL) {
        return SCALE_INVALID;
    }

    if (strcmp(name, "add") == 0) {
        double total = 0.0;

        if (sum_operands(args, args_len, &total) != 2) {
            return SCALE_ERROR;
        }

        saw_add = 1;
        char response[128];
        int written = snprintf(
            response,
            sizeof response,
            "{\"results\":[{\"shape\":[],\"data\":[%.17g]}]}",
            total);

        if (written < 0 || (size_t)written >= sizeof response) {
            return SCALE_ERROR;
        }

        uint8_t *buffer = (uint8_t *)conformance_alloc((size_t)written + 1);

        if (buffer == NULL) {
            return SCALE_ERROR;
        }

        memcpy(buffer, response, (size_t)written + 1);
        *out_result = buffer;
        *out_len = (size_t)written;
        return SCALE_OK;
    }

    return SCALE_ERROR;
}

static scale_callbacks callbacks = {
    .context = NULL,
    .alloc = conformance_alloc,
    .free = conformance_free,
    .host_dispatch = conformance_dispatch,
    .log = NULL,
    .abort = NULL,
};

int main(int argc, char **argv) {
    if (argc != 2) {
        fprintf(stderr, "usage: %s <kernel-library>\n", argv[0]);
        return 2;
    }

    scale_adapter_status status = SCALE_ADAPTER_OK;
    scale_adapter *adapter = scale_adapter_open(argv[1], &status);

    if (adapter == NULL) {
        fprintf(stderr, "conformance: adapter open failed: %s\n", scale_adapter_describe(status));
        return 1;
    }

    const scale_kernel *kernel = scale_adapter_kernel(adapter);

    if (kernel == NULL) {
        fprintf(stderr, "conformance: the adapter exposed no kernel table\n");
        scale_adapter_close(adapter);
        return 1;
    }

    check(scale_adapter_accepts_version(SCALE_EXPECTED_ABI_VERSION), "the adapter accepts the expected ABI version");
    check(!scale_adapter_accepts_version(SCALE_EXPECTED_ABI_VERSION + 1), "the adapter refuses a different ABI version");
    check(kernel->abi_version() == SCALE_EXPECTED_ABI_VERSION, "the loaded kernel reports the expected ABI version");

    void *handle = NULL;
    check(
        kernel->init(&callbacks, (const uint8_t *)image_wrong_schema, strlen(image_wrong_schema), &handle) ==
                SCALE_INVALID &&
            handle == NULL,
        "scale_init refuses an image with a different schema");

    check(
        kernel->init(&callbacks, (const uint8_t *)image_known, strlen(image_known), &handle) == SCALE_OK &&
            handle != NULL,
        "scale_init accepts the known image");
    check(saw_prelude, "scale_init dispatches the prelude to the host");

    uint8_t *result = NULL;
    size_t result_len = 0;
    check(
        kernel->run(handle, (const uint8_t *)"main", 4, &result, &result_len) == SCALE_OK,
        "scale_run executes the exported unit");
    check(result != NULL && result_len == 1 && result[0] == '5', "scale_run returns the known result");

    if (result != NULL) {
        conformance_free(result);
    }

    check(
        kernel->image(
            handle,
            SCALE_IMAGE_REPLACE_UNIT,
            (const uint8_t *)unit_wrong_arity,
            strlen(unit_wrong_arity),
            NULL,
            NULL) == SCALE_INVALID,
        "scale_image refuses a unit arity change");

    check(
        kernel->image(
            handle,
            SCALE_IMAGE_REPLACE_UNIT,
            (const uint8_t *)unit_replacement,
            strlen(unit_replacement),
            NULL,
            NULL) == SCALE_OK,
        "scale_image replaces one validated unit");

    result = NULL;
    result_len = 0;
    check(
        kernel->run(handle, (const uint8_t *)"main", 4, &result, &result_len) == SCALE_OK,
        "scale_run executes the replaced unit");
    check(result != NULL && result_len == 1 && result[0] == '9', "the replaced unit returns its new result");

    if (result != NULL) {
        conformance_free(result);
    }

    check(
        kernel->image(
            handle,
            SCALE_IMAGE_PATCH_UNIT,
            (const uint8_t *)unit_patch,
            strlen(unit_patch),
            NULL,
            NULL) == SCALE_OK,
        "scale_image applies a unit patch");

    result = NULL;
    result_len = 0;
    check(
        kernel->run(handle, (const uint8_t *)"helper", 6, &result, &result_len) == SCALE_OK,
        "scale_run executes a patched unit");
    check(result != NULL && result_len == 2 && result[0] == '1' && result[1] == '1',
          "the patched unit returns its result");

    if (result != NULL) {
        conformance_free(result);
    }

    check(saw_add, "scale_run dispatches the declared primitive to the host");
    check(kernel->set_callbacks(handle, &callbacks) == SCALE_OK, "scale_set_callbacks accepts a live handle");

    kernel->free(handle);
    result = NULL;
    result_len = 0;
    check(
        kernel->run(handle, (const uint8_t *)"main", 4, &result, &result_len) == SCALE_INVALID,
        "a freed handle is no longer usable");

    if (result != NULL) {
        conformance_free(result);
    }

    scale_adapter_close(adapter);

    if (failures != 0) {
        printf("conformance: FAIL (%d check(s))\n", failures);
        return 1;
    }

    printf("conformance: PASS\n");
    return 0;
}
