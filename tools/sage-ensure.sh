#!/usr/bin/env bash
# Ensure this checkout has a live SageFs session. It reuses the first active
# session for the checkout and only creates one when none is active. It never
# stops or restarts a shared daemon.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="${1:-projects/scale-tests/scale-tests.fsproj}"
port="${SAGEFS_MCP_PORT:-37749}"
headers="$(mktemp)"
trap 'rm -f "$headers"' EXIT

if ! sagefs status >/dev/null 2>&1; then
    echo "sage-ensure: starting a daemon"
    setsid sagefs --ttl 2h </dev/null >/dev/null 2>&1 &

    for _ in $(seq 1 30); do
        if sagefs status >/dev/null 2>&1; then
            break
        fi

        sleep 1
    done
fi

if ! sagefs status >/dev/null 2>&1; then
    echo "sage-ensure: the daemon did not start" >&2
    exit 1
fi

extract_json () {
    while IFS= read -r line; do
        case "$line" in
            data:*) printf '%s\n' "${line#data: }" ;;
        esac
    done
}

rpc () {
    curl -sS \
        -H 'Content-Type: application/json' \
        -H 'Accept: application/json, text/event-stream' \
        -H "Mcp-Session-Id: ${session_id}" \
        -X POST "http://localhost:${port}/" \
        --data "$1"
}

session_id="$(
    curl -sS -D "$headers" \
        -H 'Content-Type: application/json' \
        -H 'Accept: application/json, text/event-stream' \
        -X POST "http://localhost:${port}/" \
        --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"scale-sage-ensure","version":"1"}}}' \
        >/dev/null

    while IFS= read -r line; do
        case "$line" in
            Mcp-Session-Id:*) printf '%s\n' "${line#Mcp-Session-Id: }" | tr -d '\r' ;;
        esac
    done < "$headers"
)"

if [[ -z "$session_id" ]]; then
    echo "sage-ensure: the daemon did not return an MCP session" >&2
    exit 1
fi

list_response="$(rpc '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_sessions","arguments":{}}}')"
list_text="$(printf '%s\n' "$list_response" | extract_json | jq -r '.result.content[0].text // empty')"

active_id="$(
    printf '%s\n' "$list_text" |
        while IFS= read -r line; do
            if [[ "$line" =~ ^([0-9a-fA-F]{8})[[:space:]] ]]; then
                printf '%s\n' "${BASH_REMATCH[1]}"
                break
            fi
        done
)"

if [[ -n "$active_id" ]]; then
    status_response="$(
        rpc "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"get_session_status\",\"arguments\":{\"session_id\":\"${active_id}\",\"wait_seconds\":1}}}"
    )"
    status_text="$(printf '%s\n' "$status_response" | extract_json | jq -r '.result.content[0].text // empty')"

    if [[ -n "${SAGE_ENSURE_DEBUG:-}" ]]; then
        printf '%s\n' "$status_text" >&2
    fi

    state="$(printf '%s\n' "$status_text" | jq -r '.sourceState.state // .state // "unknown"')"
    echo "sage-ensure: reusing ${active_id} (${state})"
    exit 0
fi

if [[ "$project" == *.sln || "$project" == *.slnx ]]; then
    create_tool="create_solution_session"
    create_arg="solution"
else
    create_tool="create_project_session"
    create_arg="project"
fi

create_response="$(
    rpc "$(jq -nc \
        --arg name "$create_tool" \
        --arg arg "$create_arg" \
        --arg project "$project" \
        --arg working "$root" \
        '{jsonrpc:"2.0",id:4,method:"tools/call",params:{name:$name,arguments:{($arg):$project,working_directory:$working,workflow:"interactive"}}}')"
)"

create_text="$(printf '%s\n' "$create_response" | extract_json | jq -r '.result.content[0].text // empty')"
create_error="$(printf '%s\n' "$create_response" | extract_json | jq -r '.error.message // empty')"

if [[ -n "$create_error" ]]; then
    echo "sage-ensure: create failed: ${create_error}" >&2
    exit 1
fi

new_id="$(
    printf '%s\n' "$create_text" |
        while IFS= read -r line; do
            if [[ "$line" =~ ([0-9a-fA-F]{8}) ]]; then
                printf '%s\n' "${BASH_REMATCH[1]}"
                break
            fi
        done
)"

if [[ -z "$new_id" ]]; then
    echo "sage-ensure: the session was created but its id was not reported" >&2
    printf '%s\n' "$create_text" >&2
    exit 1
fi

status_response="$(
    rpc "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"get_session_status\",\"arguments\":{\"session_id\":\"${new_id}\",\"wait_seconds\":30}}}"
)"
status_text="$(printf '%s\n' "$status_response" | extract_json | jq -r '.result.content[0].text // empty')"
state="$(printf '%s\n' "$status_text" | jq -r '.sourceState.state // .state // "unknown"')"
echo "sage-ensure: created ${new_id} (${state})"
