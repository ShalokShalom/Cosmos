import { spawn } from "node:child_process"
import { basename } from "node:path"

const VESTIGE_BIN = process.env.VESTIGE_BIN || "/usr/bin/vestige-mcp"
const TIMEOUT_MS = Number(process.env.VESTIGE_BRIEF_TIMEOUT_MS || 15000)
const MAX_LINE = Number(process.env.VESTIGE_BRIEF_MAX_LINE || 240)
const MAX_BRIEF = Number(process.env.VESTIGE_BRIEF_MAX_CHARS || 3600)
const MAX_FEED = Number(process.env.VESTIGE_FEED_MAX_CHARS || 6000)
const MAX_FEED_MESSAGE = Number(process.env.VESTIGE_FEED_MAX_MESSAGE_CHARS || 1200)

const pending = new Map()
const capturedThrough = new Map()

const clamp = (text, limit) =>
  text.length <= limit ? text : `${text.slice(0, limit)}...[truncated]`

const oneLine = (text, limit = MAX_LINE) =>
  clamp(String(text || "").replace(/\s+/g, " ").trim(), limit)

export const callVestigeTool = (name, args, timeoutMs = TIMEOUT_MS) =>
  new Promise((resolve, reject) => {
    const proc = spawn(VESTIGE_BIN, [], { stdio: ["pipe", "pipe", "pipe"] })
    let stdout = ""
    let stderr = ""
    let settled = false
    let timer

    const finish = (error, value) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      try {
        proc.kill()
      } catch {}
      if (error) reject(error)
      else resolve(value)
    }

    timer = setTimeout(() => finish(new Error("vestige-mcp timed out")), timeoutMs)
    proc.on("error", (error) => finish(error))
    proc.stderr.on("data", (data) => {
      stderr += data.toString()
    })
    proc.stdout.on("data", (data) => {
      stdout += data.toString()
      for (const line of stdout.split("\n")) {
        if (!line.trim()) continue
        try {
          const message = JSON.parse(line)
          if (message.id !== 2) continue
          if (message.error) finish(new Error(JSON.stringify(message.error)))
          else if (message.result?.isError) finish(new Error(JSON.stringify(message.result)))
          else finish(null, message.result || {})
        } catch {}
      }
    })
    proc.on("close", (code) => {
      if (!settled) finish(new Error(`vestige-mcp exited ${code}: ${stderr.slice(-400)}`))
    })

    const send = (value) => proc.stdin.write(`${JSON.stringify(value)}\n`)
    send({
      jsonrpc: "2.0",
      id: 1,
      method: "initialize",
      params: {
        protocolVersion: "2024-11-05",
        capabilities: {},
        clientInfo: { name: "opencode-vestige-brief", version: "1" },
      },
    })
    send({ jsonrpc: "2.0", method: "notifications/initialized" })
    send({ jsonrpc: "2.0", id: 2, method: "tools/call", params: { name, arguments: args } })
  })

const textOf = (result) => {
  const content = result?.content
  if (!Array.isArray(content)) return result
  const text = content.find((part) => part?.type === "text" && typeof part.text === "string")
  if (!text) return result
  try {
    return JSON.parse(text.text)
  } catch {
    return { text: text.text }
  }
}

const tagList = (stats) => {
  const byTag = stats?.counts?.byTag || {}
  const preferred = [
    "workflow",
    "policy",
    "gotcha",
    "agents",
    "area:style",
    "area:process",
    "preference",
    "user-preference",
    "self-improvement",
  ]
  const present = preferred.filter((tag) => byTag[tag] > 0)
  if (present.length > 0) return present.slice(0, 5)
  return Object.keys(byTag)
    .filter((tag) => /workflow|policy|gotcha|agent|style|process|preference|self/i.test(tag))
    .slice(0, 5)
}

const recallLines = (tag, result) => {
  const nodes = Array.isArray(result?.nodes) ? result.nodes : []
  return nodes
    .filter((node) => node?.id && node?.content)
    .map((node) => `- [${node.id}] ${oneLine(node.content)}`)
}

const redact = (text) =>
  String(text || "")
    .replace(/-----BEGIN [A-Z ]+PRIVATE KEY-----[\s\S]*?-----END [A-Z ]+PRIVATE KEY-----/g, "[redacted private key]")
    .replace(/\b(password|passwd|secret|token|api[_-]?key)\s*[:=]\s*[^\s]+/gi, "$1=[redacted]")
    .replace(/\bBearer\s+[A-Za-z0-9._~+/-]{8,}=*/gi, "Bearer [redacted]")
    .replace(/\b(sk|pk|ghp|gho|ghu|ghs|ghr|xox[baprs])[-_][A-Za-z0-9_-]{10,}\b/g, "[redacted]")

const messageText = (message) =>
  (message?.parts || [])
    .filter((part) => part?.type === "text" && typeof part.text === "string" && !part.synthetic && !part.ignored)
    .map((part) => part.text)
    .join("\n")
    .trim()

const messageRole = (message) =>
  message?.info?.role === "user" ? "User" : "Assistant"

const sanitizeIntent = (value) =>
  value.replace(/[^A-Za-z0-9._:-]/g, "-").slice(0, 128)

export const digestNewMessages = (messages, previousID) => {
  const list = Array.isArray(messages) ? messages : []
  const previousIndex = previousID
    ? list.findIndex((message) => message?.info?.id === previousID)
    : -1
  const start = previousIndex >= 0 ? previousIndex + 1 : Math.max(0, list.length - 20)
  const fresh = list.slice(start).slice(-20)
  const lastID = list[list.length - 1]?.info?.id || ""
  const turns = fresh
    .map((message) => {
      const text = clamp(redact(messageText(message)), MAX_FEED_MESSAGE)
      return text ? `${messageRole(message)}: ${text}` : ""
    })
    .filter(Boolean)
  return { lastID, text: clamp(turns.join("\n\n"), MAX_FEED), fresh }
}

export const extractUserPreferences = (messages) => {
  const list = Array.isArray(messages) ? messages : []
  const seen = new Set()
  const preferences = []
  const marker = /\b(always|never|prefer|from now on|remember that|please remember|do not|don't|avoid)\b/i
  for (const message of list) {
    if (message?.info?.role !== "user") continue
    const text = redact(messageText(message))
    for (const sentence of text.split(/(?<=[.!?])\s+/)) {
      if (!marker.test(sentence)) continue
      const line = oneLine(sentence, 320)
      if (line.length < 12 || seen.has(line)) continue
      seen.add(line)
      preferences.push(line)
      if (preferences.length >= 3) return preferences
    }
  }
  return preferences
}

export const capturePostCompaction = async (client, sessionID, directory, callTool = callVestigeTool) => {
  const [sessionResult, messagesResult] = await Promise.all([
    client.session.get({ path: { id: sessionID } }),
    client.session.messages({ path: { id: sessionID } }),
  ])
  const session = sessionResult?.data
  const messages = Array.isArray(messagesResult?.data) ? messagesResult.data : []
  if (!session || messages.length < 2 || session.parentID) return null

  const previousID = capturedThrough.get(sessionID) || ""
  const digest = digestNewMessages(messages, previousID)
  if (!digest.lastID || digest.lastID === previousID || digest.text.length < 40) return null

  const projectName = basename(directory || session.directory || "unknown")
  const tags = [
    "scale",
    "session-capture",
    "post-compaction-capture",
    "auto",
    "post-compaction",
    `project:${projectName}`,
    `session:${sessionID}`,
  ]
  const items = [
    {
      content: digest.text,
      scope: "project",
      node_type: "event",
      source: "opencode-post-compaction",
      intent_id: sanitizeIntent(`post-compaction:${sessionID}:${digest.lastID}`),
      tags,
    },
  ]
  const preferences = extractUserPreferences(digest.fresh)
  for (const [index, preference] of preferences.entries()) {
    items.push({
      content: `User preference (verbatim): ${preference}`,
      scope: "project",
      node_type: "fact",
      source: "opencode-user-preference",
      intent_id: sanitizeIntent(`user-preference:${sessionID}:${digest.lastID}:${index}`),
      tags: [
        "scale",
        "user-preference",
        "auto",
        "post-compaction",
        `project:${projectName}`,
        `session:${sessionID}`,
      ],
    })
  }

  const result = await callTool("smart_ingest", {
    items,
    batchMergePolicy: "smart",
  })
  capturedThrough.set(sessionID, digest.lastID)
  return result
}

export const buildBriefing = async (directory) => {
  const repoPath = directory || process.cwd()
  const codebase = basename(repoPath)
  const [sessionResult, statsResult, codebaseResult] = await Promise.all([
    callVestigeTool("session_start", {
      context: { repoPath, codebase, topics: ["workflow", "policy", "gotcha", "codebase"] },
      include_intentions: true,
      include_status: true,
    }),
    callVestigeTool("memory_status", {
      view: "stats",
      scope: "project",
      detail_level: "summary",
    }),
    callVestigeTool("codebase", {
      action: "get_context",
      codebase,
      scope: "project",
      repoPath,
      limit: 20,
    }),
  ])

  const session = textOf(sessionResult)
  const stats = textOf(statsResult)
  const codebaseContext = textOf(codebaseResult)
  const tags = tagList(stats)
  const recalls = await Promise.all(
    tags.map(async (tag) => {
      try {
        return [tag, textOf(await callVestigeTool("recall", {
          handle: tag,
          scope: "project",
          detail_level: "summary",
          limit: 3,
        }))]
      } catch (error) {
        console.error("[vestige-brief] recall", tag, error)
        return [tag, null]
      }
    }),
  )

  const seen = new Set()
  const workflow = []
  const perTag = []
  const rest = []
  for (const [tag, result] of recalls) {
    const lines = recallLines(tag, result)
    if (lines.length > 0) perTag.push(`${lines[0]} (${tag})`)
    rest.push(...lines.slice(1).map((line) => `${line} (${tag})`))
  }
  for (const line of [...perTag, ...rest]) {
    const id = line.match(/^- \[([^\]]+)\]/)?.[1] || line
    if (seen.has(id)) continue
    seen.add(id)
    workflow.push(line)
  }

  const codeItems = Array.isArray(session?.codeContext?.items) ? session.codeContext.items : []
  const codeLines = codeItems
    .filter((item) => item?.summary)
    .slice(0, 5)
    .map((item) => `- [${item.id}] ${oneLine(item.summary)}`)

  const codeDecisions = Array.isArray(codebaseContext?.decisions?.items)
    ? codebaseContext.decisions.items
    : []
  const decisionLines = codeDecisions
    .filter((item) => item?.id && item?.content)
    .slice(0, 3)
    .map((item) => `- [${item.id}] ${oneLine(item.content)}`)

  const codePatterns = Array.isArray(codebaseContext?.patterns?.items)
    ? codebaseContext.patterns.items
    : []
  const patternLines = codePatterns
    .filter((item) => item?.id && item?.content)
    .slice(0, 3)
    .map((item) => `- [${item.id}] ${oneLine(item.content)}`)

  const staleLines = codeItems
    .filter((item) => item?.staleReason)
    .slice(0, 4)
    .map((item) => `- ${oneLine(item.staleReason, 320)}`)

  const total = stats?.population?.total ?? stats?.counts?.total ?? "unknown"
  const verification = session?.codeContext?.verification
  const staleCount = verification?.stale ?? staleLines.length
  const freshCount = verification?.fresh ?? 0

  const sections = [
    "## Vestige briefing (post-compaction)",
    `Vestige: ${total} project memories; code evidence fresh=${freshCount}, stale=${staleCount}.`,
  ]

  if (workflow.length > 0) {
    sections.push("", "### Workflow and self-improvement")
    sections.push(...workflow.slice(0, 6))
  } else {
    sections.push("", "### Workflow and self-improvement", "- No workflow or policy memories found.")
  }

  if (codeLines.length > 0) {
    sections.push("", "### Codebase at hand")
    sections.push(...codeLines)
  }

  if (decisionLines.length > 0) {
    sections.push("", "### Codebase decisions")
    sections.push(...decisionLines)
  }

  if (patternLines.length > 0) {
    sections.push("", "### Codebase patterns")
    sections.push(...patternLines)
  }

  if (staleLines.length > 0) {
    sections.push("", "### Stale code memories")
    sections.push(...staleLines)
  }

  const hasUserPreference = (stats?.counts?.byTag?.["user-preference"] || 0) > 0
  sections.push(
    "",
    "### Coverage",
    hasUserPreference
      ? "- user-preference memories exist."
      : "- No `user-preference` memory is recorded; add one when the user states a durable preference.",
  )

  const brief = sections.join("\n")
  return clamp(brief, MAX_BRIEF)
}

export default async ({ client, directory }) => {
  const briefDisabled = /^(1|true|yes|on)$/i.test(process.env.VESTIGE_BRIEF_DISABLED || "")
  const feedDisabled = /^(1|true|yes|on)$/i.test(process.env.VESTIGE_FEED_DISABLED || "")
  if (briefDisabled && feedDisabled) return {}

  return {
    "experimental.compaction.autocontinue": async ({ sessionID }) => {
      if (!sessionID) return
      if (!feedDisabled) {
        try {
          await capturePostCompaction(client, sessionID, directory)
        } catch (error) {
          console.error("[vestige-brief] feed", error)
        }
      }
      if (!briefDisabled) {
        try {
          pending.set(sessionID, await buildBriefing(directory))
        } catch (error) {
          console.error("[vestige-brief] build", error)
        }
      }
    },
    "experimental.chat.system.transform": async (input, output) => {
      const brief = pending.get(input?.sessionID)
      if (!brief) return
      pending.delete(input.sessionID)
      output.system.push(brief)
    },
    dispose: async () => {
      pending.clear()
      capturedThrough.clear()
    },
  }
}
