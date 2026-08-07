import http from "node:http"
import { createRequire } from "node:module"

const require = createRequire(import.meta.url)
const WebSocket = require("ws")

function getJson(url) {
  return new Promise((resolve, reject) => {
    http
      .get(url, (res) => {
        let data = ""
        res.on("data", (chunk) => {
          data += chunk
        })
        res.on("end", () => resolve(JSON.parse(data)))
      })
      .on("error", reject)
  })
}

async function evaluate(expression) {
  const pages = await getJson("http://127.0.0.1:9444/json/list")
  const ws = new WebSocket(pages[0].webSocketDebuggerUrl)
  let nextId = 0
  const pending = new Map()

  const send = (method, params = {}) =>
    new Promise((resolve, reject) => {
      const id = ++nextId
      const timer = setTimeout(() => {
        pending.delete(id)
        reject(new Error(`timeout ${method}`))
      }, 12000)
      pending.set(id, {
        resolve: (message) => {
          clearTimeout(timer)
          resolve(message)
        },
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
  const evaluated = await send("Runtime.evaluate", {
    expression,
    returnByValue: true,
  })
  ws.close()
  return evaluated.result
}

const expression = `(() => {
  const fire = (el) => {
    if (!el) return false
    el.dispatchEvent(new MouseEvent("mousedown", { bubbles: true, cancelable: true, view: window }))
    el.dispatchEvent(new MouseEvent("mouseup", { bubbles: true, cancelable: true, view: window }))
    el.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, view: window }))
    return true
  }

  const sendBtn = Array.from(document.querySelectorAll("button")).find(
    (el) => (el.textContent || "").trim() === "Send",
  )
  if (!sendBtn) return { ok: false, reason: "no-send" }

  let panel = sendBtn.parentElement
  while (panel && String(panel.className || "").indexOf("panel_") < 0) {
    panel = panel.parentElement
  }
  if (!panel) panel = sendBtn.parentElement

  const interrupt = Array.from(panel.querySelectorAll("*")).find(
    (el) => (el.textContent || "").trim() === "Interrupt",
  )
  const inputs = Array.from(panel.querySelectorAll("input"))
  const input = inputs.length ? inputs[inputs.length - 1] : null

  const box = (el) => {
    if (!el) return null
    const r = el.getBoundingClientRect()
    return {
      x: Math.round(r.x + r.width / 2),
      y: Math.round(r.y + r.height / 2),
      left: Math.round(r.left),
      top: Math.round(r.top),
      w: Math.round(r.width),
      h: Math.round(r.height),
      text: String(el.textContent || el.value || "").slice(0, 40),
    }
  }

  fire(interrupt)

  if (input) {
    input.focus()
    try {
      const desc = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value")
      if (desc && desc.set) desc.set.call(input, "debug probe: reply with only OK")
      else input.value = "debug probe: reply with only OK"
    } catch (error) {
      input.value = "debug probe: reply with only OK"
    }
    input.dispatchEvent(new Event("input", { bubbles: true }))
    input.dispatchEvent(new Event("change", { bubbles: true }))
  }

  fire(sendBtn)

  return {
    ok: true,
    viewport: { w: window.innerWidth, h: window.innerHeight },
    send: box(sendBtn),
    interrupt: box(interrupt),
    input: box(input),
    inputValue: input ? input.value : null,
  }
})()`

const result = await evaluate(expression)
console.log(JSON.stringify(result, null, 2))
