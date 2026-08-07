/**
 * Gameface CDP DOM probe for Cities: Skylines II (-uiDeveloperMode → :9444).
 * Session 548a1a handoff helper. Requires `ws` on NODE_PATH (e.g. npm i ws in a temp dir).
 *
 *   $env:NODE_PATH = "$env:TEMP\cdp-ws\node_modules"
 *   node docs/ops/scripts/2026-08-07-gameface-cdp/cdp-probe.mjs
 */
import http from "node:http"
import { createRequire } from "node:module"

const require = createRequire(import.meta.url)

function getJson(url) {
  return new Promise((resolve, reject) => {
    http
      .get(url, (res) => {
        let data = ""
        res.on("data", (chunk) => {
          data += chunk
        })
        res.on("end", () => {
          try {
            resolve(JSON.parse(data))
          } catch (error) {
            reject(error)
          }
        })
      })
      .on("error", reject)
  })
}

function loadWebSocket() {
  return require("ws")
}

async function evaluate(wsUrl, expression) {
  const WebSocket = loadWebSocket()
  const ws = new WebSocket(wsUrl)
  let nextId = 0
  const pending = new Map()

  const send = (method, params = {}) =>
    new Promise((resolve, reject) => {
      const id = ++nextId
      const timer = setTimeout(() => {
        pending.delete(id)
        reject(new Error(`timeout ${method}`))
      }, 10000)
      pending.set(id, {
        resolve: (message) => {
          clearTimeout(timer)
          resolve(message)
        },
        reject,
      })
      ws.send(JSON.stringify({ id, method, params }))
    })

  ws.on("message", (raw) => {
    const message = JSON.parse(String(raw))
    if (message.id && pending.has(message.id)) {
      pending.get(message.id).resolve(message)
      pending.delete(message.id)
    }
  })

  await new Promise((resolve, reject) => {
    ws.once("open", resolve)
    ws.once("error", reject)
  })

  await send("Runtime.enable")
  const result = await send("Runtime.evaluate", {
    expression,
    returnByValue: true,
  })
  ws.close()
  return result
}

const pages = await getJson("http://127.0.0.1:9444/json/list")
const wsUrl = pages[0]?.webSocketDebuggerUrl
if (!wsUrl) {
  throw new Error("no Gameface page on :9444 — start CS2 with -uiDeveloperMode")
}

// Gameface QuerySelector rejects :not(); keep selectors simple.
const expression = `(() => {
  const textHits = []
  const walk = (node, depth) => {
    if (!node || depth > 30) return
    const text = (node.textContent || "").trim()
    if (text && text.length < 160 && /Agent|SEND|Interrupt|Thinking|working|CITIES/i.test(text)) {
      textHits.push(text.slice(0, 120))
    }
    const children = node.children || []
    for (let i = 0; i < children.length; i += 1) walk(children[i], depth + 1)
  }
  walk(document.body, 0)

  const panels = Array.from(document.querySelectorAll("*"))
    .filter((el) => /CITIES SKYLINES 2 AGENT|Cities Skylines 2 Agent/i.test(el.textContent || ""))
    .slice(0, 8)
    .map((el) => ({
      tag: el.tagName,
      className: String(el.className || "").slice(0, 100),
      childCount: el.children ? el.children.length : 0,
      text: (el.textContent || "").slice(0, 240),
    }))

  const controls = Array.from(
    document.querySelectorAll("input, textarea, button, [contenteditable], [role='button']"),
  )
    .slice(0, 60)
    .map((el) => ({
      tag: el.tagName,
      type: el.type || "",
      className: String(el.className || "").slice(0, 80),
      text: String(el.textContent || el.value || "").slice(0, 80),
    }))

  return {
    title: document.title,
    url: location.href,
    bodyLen: (document.body && document.body.innerHTML ? document.body.innerHTML.length : 0),
    textHits: Array.from(new Set(textHits)).slice(0, 40),
    panels,
    controls,
  }
})()`

const result = await evaluate(wsUrl, expression)
console.log(JSON.stringify({ wsUrl, result }, null, 2))
