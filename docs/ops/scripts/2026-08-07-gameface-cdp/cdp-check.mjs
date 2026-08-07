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

const pages = await getJson("http://127.0.0.1:9444/json/list")
const ws = new WebSocket(pages[0].webSocketDebuggerUrl)
let nextId = 0
const pending = new Map()
const send = (method, params = {}) =>
  new Promise((resolve, reject) => {
    const id = ++nextId
    const timer = setTimeout(() => reject(new Error(method)), 10000)
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
  expression: `(() => {
    const body = document.body ? document.body.innerText : ""
    return {
      hasProbe: body.indexOf("debug probe") >= 0,
      hasOk: /\\bOK\\b/.test(body),
      statusLine: (body.match(/Thinking[^\\n]*|Idle[^\\n]*|error[^\\n]*/i) || [null])[0],
      youLines: (body.match(/You: [^\\n]+/g) || []).slice(-5),
      agentTail: (body.match(/Agent: [^\\n]+/g) || []).slice(-5),
    }
  })()`,
  returnByValue: true,
})
ws.close()
console.log(JSON.stringify(evaluated.result, null, 2))
