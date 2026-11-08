/*
 * A kernel with only the version export and a deliberately different version.
 * The adapter must refuse it before resolving any other symbol, so this is the
 * alpha-runnable proof of the version-equality rule (D136).
 */

#include <stdint.h>

#include "scale_abi.h"

int32_t SCALE_CALL scale_abi_version(void) {
    return SCALE_ABI_VERSION + 1;
}
