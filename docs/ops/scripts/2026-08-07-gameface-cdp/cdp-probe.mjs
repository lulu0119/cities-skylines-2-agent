/**
 * Gameface CDP DOM probe for Cities: Skylines II (-uiDeveloperMode → :9444).
 * Requires `ws` on NODE_PATH (for example, npm i ws in a temp directory).
 */
import { evaluateExpressions } from "./cdp-client.mjs"

// Gameface QuerySelector rejects :not(); keep selectors simple.
const defaultExpression = `(() => {
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
    bodyLen: document.body?.innerHTML?.length || 0,
    textHits: Array.from(new Set(textHits)).slice(0, 40),
    panels,
    controls,
  }
})()`

let expressions
if (process.env.CDP_EXPRESSIONS) {
  expressions = JSON.parse(process.env.CDP_EXPRESSIONS)
  if (!Array.isArray(expressions) || expressions.some((value) => typeof value !== "string")) {
    throw new Error("CDP_EXPRESSIONS must be a JSON array of strings")
  }
} else {
  const expression = process.env.CDP_EXPRESSION || defaultExpression
  const parsedRepeat = Number.parseInt(process.env.CDP_REPEAT || "1", 10)
  const repeat = Number.isFinite(parsedRepeat) ? Math.max(1, Math.min(100, parsedRepeat)) : 1
  expressions = Array.from({ length: repeat }, () => expression)
}

const parsedDelay = Number.parseInt(process.env.CDP_DELAY_MS || "0", 10)
const delayMs = Number.isFinite(parsedDelay) ? Math.max(0, parsedDelay) : 0
const evaluated = await evaluateExpressions(expressions, { delayMs })
const output = evaluated.results.length === 1
  ? { wsUrl: evaluated.wsUrl, result: evaluated.results[0] }
  : evaluated
console.log(JSON.stringify(output, null, 2))
