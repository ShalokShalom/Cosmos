#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
config_home="${XDG_CONFIG_HOME:-$HOME/.config}"
unit_dir="$config_home/systemd/user"
state_dir="$config_home/scale"
opencode_config="$config_home/opencode/opencode.json"
data_dir="${VESTIGE_DATA_DIR:-$HOME/.local/share/opencode-v1/vestige}"

for command in jq openssl systemctl; do
    if ! command -v "$command" >/dev/null 2>&1; then
        echo "Required command is missing: $command" >&2
        exit 1
    fi
done

install -d -m 755 "$unit_dir"
install -d -m 700 "$state_dir"
install -d -m 700 "$data_dir"

install -m 644 "$repo_root/tools/script/scale-sagefs.service" "$unit_dir/scale-sagefs.service"
install -m 644 "$repo_root/tools/script/scale-vestige.service" "$unit_dir/scale-vestige.service"
chmod 755 "$repo_root/tools/script/run-vestige-http.sh"

token_file="$state_dir/vestige-auth-token"
if [[ ! -s "$token_file" ]]; then
    (umask 077; openssl rand -hex 32 > "$token_file")
fi
token="$(<"$token_file")"

env_file="$state_dir/vestige.env"
(umask 077; printf 'VESTIGE_DATA_DIR=%s\nVESTIGE_AUTH_TOKEN=%s\n' "$data_dir" "$token" > "$env_file")
chmod 600 "$token_file" "$env_file"

if [[ ! -f "$opencode_config" ]]; then
    echo "OpenCode config not found: $opencode_config" >&2
    exit 1
fi

temporary_config="$(mktemp)"
jq --arg token "$token" '
    .mcp = (.mcp // {}) |
    .mcp.sagefs = {
        type: "remote",
        url: "http://127.0.0.1:37749/",
        enabled: true,
        timeout: 120000
    } |
    .mcp.vestige = {
        type: "remote",
        url: "http://127.0.0.1:3928/mcp",
        enabled: true,
        timeout: 120000,
        headers: {Authorization: ("Bearer " + $token)}
    }
' "$opencode_config" > "$temporary_config"
chmod 600 "$temporary_config"
mv "$temporary_config" "$opencode_config"

systemctl --user stop scale-sagefs.service scale-vestige.service 2>/dev/null || true
sagefs stop >/dev/null 2>&1 || true
pkill -u "$(id -u)" -f '^/usr/bin/vestige-mcp( |$)' 2>/dev/null || true

systemctl --user daemon-reload
systemctl --user enable --now scale-sagefs.service scale-vestige.service

for _ in $(seq 1 20); do
    if systemctl --user is-active --quiet scale-sagefs.service &&
       systemctl --user is-active --quiet scale-vestige.service; then
        break
    fi
    sleep 1
done

if ! systemctl --user is-active --quiet scale-sagefs.service; then
    echo "scale-sagefs.service failed to start." >&2
    systemctl --user --no-pager status scale-sagefs.service >&2 || true
    exit 1
fi

if ! systemctl --user is-active --quiet scale-vestige.service; then
    echo "scale-vestige.service failed to start." >&2
    systemctl --user --no-pager status scale-vestige.service >&2 || true
    exit 1
fi

if [[ "$(loginctl show-user "$USER" -p Linger 2>/dev/null || true)" != "Linger=yes" ]]; then
    echo "Note: user services stop at logout. Enable persistence with: loginctl enable-linger $USER"
fi

echo "SageFs is managed by scale-sagefs.service on http://127.0.0.1:37749/."
echo "Vestige is managed by scale-vestige.service on http://127.0.0.1:3928/mcp."
echo "Start a new OpenCode session (or restart the TUI) to load the remote MCPs; reloading an open session keeps its old MCP clients."
