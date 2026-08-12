import { evaluateExpression } from "./cdp-client.mjs"

const evaluated = await evaluateExpression(`(() => {
  const body = document.body ? (document.body.textContent || "") : ""
  return {
    hasProbe: body.indexOf("debug probe") >= 0,
    hasOk: /\\bOK\\b/.test(body),
    statusLine: (body.match(/Thinking[^\\n]*|Idle[^\\n]*|error[^\\n]*/i) || [null])[0],
    youLines: (body.match(/You: [^\\n]+/g) || []).slice(-5),
    agentTail: (body.match(/Agent: [^\\n]+/g) || []).slice(-5),
  }
})()`)

console.log(JSON.stringify(evaluated.result.result, null, 2))
