// Node smoke test: same @apeira/core agent loop the browser page uses,
// talking to the local mock OpenAI-compatible server.
// Run: node smoke.mjs  (after: cd mock && node server.mjs)

import { createAgent, rawTool, run, user } from '@apeira/core'
import { chat } from '@apeira/core/chat'

const city = {
  population: 1200,
  happiness: 62,
  budget: 450_000,
  roads: 8,
  taxRate: 11,
}

const tools = [
  rawTool({
    name: 'get_city_overview',
    description: '读取当前城市状态。',
    parameters: { type: 'object', additionalProperties: false, properties: {}, required: [] },
    execute: () => JSON.stringify(city),
  }),
  rawTool({
    name: 'build_road',
    description: '修建一条道路。',
    parameters: {
      type: 'object',
      properties: {
        start: { type: 'array', items: { type: 'number' }, minItems: 2, maxItems: 2 },
        end: { type: 'array', items: { type: 'number' }, minItems: 2, maxItems: 2 },
      },
      required: ['start', 'end'],
      additionalProperties: false,
    },
    execute: () => {
      city.roads += 1
      city.budget -= 35_000
      return JSON.stringify(city)
    },
  }),
  rawTool({
    name: 'run_simulation',
    description: '推进模拟。',
    parameters: {
      type: 'object',
      properties: { hours: { type: 'number', minimum: 1, maximum: 24 } },
      required: ['hours'],
      additionalProperties: false,
    },
    execute: () => {
      city.population += 180
      return JSON.stringify(city)
    },
  }),
]

const agent = createAgent({
  instructions: '你是城市天际线 2 的 AI 市长助手。先读城市状态，再按用户要求调用工具。',
  runner: chat({
    baseURL: process.env.BASE_URL ?? 'http://127.0.0.1:8787/v1',
    model: process.env.MODEL ?? 'mock-gpt',
    stream: true,
    temperature: 0.2,
  }),
  tools,
})

const prompt = process.argv[2] ?? '建一条路，然后跑 4 小时模拟'
console.log(`\n[user] ${prompt}\n`)

for await (const event of run(agent, user(prompt))) {
  if (event.type === 'text.delta')
    process.stdout.write(event.delta)
  else if (event.type === 'tool-call.start')
    console.log(`\n[tool-call] ${event.toolName}`)
  else if (event.type === 'tool-result.done')
    console.log(`[tool-result] ${typeof event.result === 'string' ? event.result : JSON.stringify(event.result)}\n`)
  else if (event.type === 'turn.failed')
    console.error(`[turn.failed] ${event.error}`)
}

console.log('\n[done]')
