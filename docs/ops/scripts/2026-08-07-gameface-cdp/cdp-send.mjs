import { evaluateExpression } from "./cdp-client.mjs"

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
    } catch {
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

const evaluated = await evaluateExpression(expression)
console.log(JSON.stringify(evaluated.result.result, null, 2))
