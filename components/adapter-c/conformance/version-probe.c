/*
 * Proves the adapter refuses a kernel whose ABI version differs, before it
 * trusts any other export. The fake kernel exports only scale_abi_version, so
 * a version check that ran after resolving the other symbols would fail with a
 * missing symbol instead of a version mismatch.
 */

#include <stdio.h>

#include "scale_adapter.h"

int main(int argc, char **argv) {
    if (argc != 2) {
        fprintf(stderr, "usage: %s <kernel-library>\n", argv[0]);
        return 2;
    }

    scale_adapter_status status = SCALE_ADAPTER_OK;
    scale_adapter *adapter = scale_adapter_open(argv[1], &status);

    if (adapter != NULL) {
        scale_adapter_close(adapter);
        fprintf(stderr, "version probe: the adapter accepted a mismatched kernel\n");
        return 1;
    }

    if (status != SCALE_ADAPTER_VERSION_MISMATCH) {
        fprintf(stderr, "version probe: expected a version mismatch, got %s\n", scale_adapter_describe(status));
        return 1;
    }

    printf("version probe: the adapter refused ABI version %d\n", SCALE_ABI_VERSION + 1);
    return 0;
}
