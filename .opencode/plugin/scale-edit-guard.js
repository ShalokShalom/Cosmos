const BASH_NOOP = /^\s*(true|:|exit\s+0)\s*$/

export default async () => ({
  "tool.execute.before": async (input, output) => {
    if (input.tool !== "bash") return

    const command = output.args?.command
    if (typeof command === "string" && BASH_NOOP.test(command)) {
      throw new Error(
        "scale-edit-guard: no-op Bash command blocked. Bash is for observable effects; use the owning edit, read, or MCP tool instead.",
      )
    }
  },
})
