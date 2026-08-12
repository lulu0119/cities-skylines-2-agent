import http from "node:http"
import { createRequire } from "node:module"

const require = createRequire(import.meta.url)
const WebSocket = require("ws")

const DEFAULT_ENDPOINT = "http://127.0.0.1:9444"
const REQUEST_TIMEOUT_MS = 10000
const CLEANUP_TIMEOUT_MS = 1000
const CLOSE_TIMEOUT_MS = 1500

function getJson(url) {
  return new Promise((resolve, reject) => {
    const request = http.get(url, (response) => {
      let data = ""
      response.on("data", (chunk) => {
        data += chunk
      })
      response.on("end", () => {
        if (response.statusCode < 200 || response.statusCode >= 300) {
          reject(new Error(`CDP discovery failed with HTTP ${response.statusCode}`))
          return
        }
        try {
          resolve(JSON.parse(data))
        } catch (error) {
          reject(error)
        }
      })
    })
    request.setTimeout(REQUEST_TIMEOUT_MS, () => {
      request.destroy(new Error("CDP discovery timed out"))
    })
    request.on("error", reject)
  })
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}

async function openSession(endpoint) {
  const pages = await getJson(`${endpoint}/json/list`)
  const wsUrl = pages[0]?.webSocketDebuggerUrl
  if (!wsUrl) {
    throw new Error("no Gameface page on :9444 — start CS2 with -uiDeveloperMode")
  }

  const ws = new WebSocket(wsUrl)
  let nextId = 0
  let closed = false
  const pending = new Map()

  const rejectPending = (error) => {
    for (const request of pending.values()) {
      clearTimeout(request.timer)
      request.reject(error)
    }
    pending.clear()
  }

  ws.on("message", (raw) => {
    const message = JSON.parse(String(raw))
    const request = pending.get(message.id)
    if (!request) return
    clearTimeout(request.timer)
    pending.delete(message.id)
    request.resolve(message)
  })
  ws.on("error", (error) => rejectPending(error))
  ws.on("close", () => {
    closed = true
    rejectPending(new Error("CDP connection closed"))
  })

  await new Promise((resolve, reject) => {
    ws.once("open", resolve)
    ws.once("error", reject)
  })

  const send = (method, params = {}, timeoutMs = REQUEST_TIMEOUT_MS) =>
    new Promise((resolve, reject) => {
      if (closed || ws.readyState !== WebSocket.OPEN) {
        reject(new Error("CDP connection is not open"))
        return
      }
      const id = ++nextId
      const timer = setTimeout(() => {
        pending.delete(id)
        reject(new Error(`timeout ${method}`))
      }, timeoutMs)
      pending.set(id, { resolve, reject, timer })
      ws.send(JSON.stringify({ id, method, params }))
    })

  const close = async () => {
    if (closed) return
    await new Promise((resolve) => {
      let settled = false
      let timer
      const finish = () => {
        if (settled) return
        settled = true
        clearTimeout(timer)
        resolve()
      }
      timer = setTimeout(() => {
        ws.terminate()
        finish()
      }, CLOSE_TIMEOUT_MS)
      ws.once("close", finish)
      ws.close(1000)
    })
  }

  return { wsUrl, send, close }
}

export async function evaluateExpressions(
  expressions,
  { endpoint = DEFAULT_ENDPOINT, delayMs = 0 } = {},
) {
  if (!Array.isArray(expressions) || expressions.length === 0) {
    throw new Error("at least one CDP expression is required")
  }

  const session = await openSession(endpoint)
  const results = []
  try {
    for (let index = 0; index < expressions.length; index += 1) {
      const objectGroup = `codex-cdp-${process.pid}-${Date.now()}-${index}`
      try {
        results.push(
          await session.send("Runtime.evaluate", {
            expression: expressions[index],
            objectGroup,
            returnByValue: true,
            awaitPromise: true,
            silent: true,
          }),
        )
      } finally {
        try {
          await session.send(
            "Runtime.releaseObjectGroup",
            { objectGroup },
            CLEANUP_TIMEOUT_MS,
          )
        } catch {
          // Some Gameface builds do not implement every Runtime cleanup command.
        }
      }
      if (delayMs > 0 && index + 1 < expressions.length) {
        await delay(delayMs)
      }
    }
  } finally {
    await session.close()
  }

  return { wsUrl: session.wsUrl, results }
}

export async function evaluateExpression(expression, options) {
  const evaluated = await evaluateExpressions([expression], options)
  return { wsUrl: evaluated.wsUrl, result: evaluated.results[0] }
}
