#!/usr/bin/env bash
set -euo pipefail

if [[ ! -x /usr/bin/vestige-mcp ]]; then
    echo "vestige-mcp is not installed at /usr/bin/vestige-mcp." >&2
    exit 1
fi

# vestige-mcp always opens a stdio MCP session and exits on stdin EOF, even
# when HTTP is enabled. Keep stdin open so the HTTP listener stays alive.
exec 0< <(sleep infinity)
exec /usr/bin/vestige-mcp --http --http-port "${VESTIGE_HTTP_PORT:-3928}"
