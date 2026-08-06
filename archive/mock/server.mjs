// Zero-dependency OpenAI-compatible chat/completions mock (SSE streaming + tool calls).
// Usage: node server.mjs  (listens on 127.0.0.1:8787)

import { createServer } from 'node:http'

const PORT = Number(process.env.PORT ?? 8787)

const CITY_STATE = {
  population: 1200,
  happiness: 62,
  budget: 450_000,
  residentialDemand: 'high',
  roads: 8,
  residentialZones: 6,
  taxRate: 11,
  paused: true,
}

const TOOL_NAMES = ['get_city_overview', 'build_road', 'zone_area', 'set_tax_rate', 'run_simulation']

const overview = () => JSON.stringify(CITY_STATE)

// Decide the next mock action from the conversation.
// Deterministic demo: step 0 -> overview, 1 -> build road, 2 -> run simulation,
// 3+ -> final summary. This exercises multi-round function calling.
const planNext = (messages, tools) => {
  const last = messages.at(-1)
  if (!tools?.length)
    return { text: `城市状态：${overview()}` }

  const toolMessageCount = messages.filter(m => m.role === 'tool').length
  const text = messages.filter(m => m.role === 'user').map(m => String(m.content ?? '')).join('\n').toLowerCase()

  if (last?.role === 'tool' && toolMessageCount >= 3)
    return { text: `三步操作已完成。城市当前快照：${overview()}` }
  if (toolMessageCount >= 3)
    return { text: `已分析城市状态。告诉我你想做什么（建路、划区、调税率、跑模拟），我会调用对应工具。` }

  switch (toolMessageCount) {
    case 0:
      return { tool: 'get_city_overview', args: '{}' }
    case 1:
      return text.includes('税') || text.includes('tax')
        ? { tool: 'set_tax_rate', args: '{"rate":9}' }
        : { tool: 'build_road', args: '{"start":[0,0],"end":[100,0]}' }
    default:
      return text.includes('区') || text.includes('zone')
        ? { tool: 'zone_area', args: '{"type":"residential","x":10,"y":10,"size":2}' }
        : { tool: 'run_simulation', args: '{"hours":4}' }
  }
}

const sse = (res, chunk) => res.write(`data: ${JSON.stringify(chunk)}\n\n`)

const handleChat = async (req, res, body) => {
  const { messages = [], tools, stream = false } = body
  const action = planNext(messages, tools)

  if (!stream) {
    const id = `mock_${Math.random().toString(36).slice(2, 10)}`
    res.writeHead(200, { 'content-type': 'application/json' })
    res.end(JSON.stringify({
      id,
      object: 'chat.completion',
      created: Math.floor(Date.now() / 1000),
      model: body.model ?? 'mock-gpt',
      choices: [{
        index: 0,
        message: action.tool != null
          ? {
              role: 'assistant',
              content: null,
              tool_calls: [{
                id: `call_${id}`,
                type: 'function',
                function: { name: action.tool, arguments: action.args },
              }],
            }
          : { role: 'assistant', content: action.text },
        finish_reason: action.tool != null ? 'tool_calls' : 'stop',
      }],
      usage: { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 },
    }))
    return
  }

  res.writeHead(200, {
    'content-type': 'text/event-stream',
    'cache-control': 'no-cache',
    connection: 'keep-alive',
  })

  const id = `mock_${Math.random().toString(36).slice(2, 10)}`
  const flush = () => res.flush?.()

  if (action.tool != null) {
    sse(res, {
      id,
      object: 'chat.completion.chunk',
      created: Math.floor(Date.now() / 1000),
      model: body.model ?? 'mock-gpt',
      choices: [{
        index: 0,
        delta: {
          role: 'assistant',
          tool_calls: [{
            index: 0,
            id: `call_${id}`,
            type: 'function',
            function: { name: action.tool, arguments: '' },
          }],
        },
        finish_reason: null,
      }],
    })
    flush()
    await new Promise(r => setTimeout(r, 30))
    sse(res, {
      id,
      object: 'chat.completion.chunk',
      created: Math.floor(Date.now() / 1000),
      model: body.model ?? 'mock-gpt',
      choices: [{
        index: 0,
        delta: { tool_calls: [{ index: 0, function: { arguments: action.args } }] },
        finish_reason: null,
      }],
    })
    flush()
    sse(res, {
      id,
      object: 'chat.completion.chunk',
      created: Math.floor(Date.now() / 1000),
      model: body.model ?? 'mock-gpt',
      choices: [{ index: 0, delta: {}, finish_reason: 'tool_calls' }],
    })
  }
  else {
    const text = action.text
    for (let i = 0; i < text.length; i += 4) {
      sse(res, {
        id,
        object: 'chat.completion.chunk',
        created: Math.floor(Date.now() / 1000),
        model: body.model ?? 'mock-gpt',
        choices: [{ index: 0, delta: { content: text.slice(i, i + 4) }, finish_reason: null }],
      })
      flush()
    }
    sse(res, {
      id,
      object: 'chat.completion.chunk',
      created: Math.floor(Date.now() / 1000),
      model: body.model ?? 'mock-gpt',
      choices: [{ index: 0, delta: {}, finish_reason: 'stop' }],
    })
  }

  if (body.stream_options?.include_usage) {
    sse(res, {
      id,
      object: 'chat.completion.chunk',
      created: Math.floor(Date.now() / 1000),
      model: body.model ?? 'mock-gpt',
      choices: [],
      usage: { prompt_tokens: 10, completion_tokens: 10, total_tokens: 20 },
    })
  }

  res.write('data: [DONE]\n\n')
  res.end()
}

const readBody = (req) => new Promise((resolve, reject) => {
  const chunks = []
  req.on('data', c => chunks.push(c))
  req.on('end', () => {
    try {
      resolve(chunks.length ? JSON.parse(Buffer.concat(chunks).toString('utf8')) : {})
    }
    catch (error) {
      reject(error)
    }
  })
  req.on('error', reject)
})

const server = createServer(async (req, res) => {
  res.setHeader('access-control-allow-origin', '*')
  res.setHeader('access-control-allow-headers', '*')
  res.setHeader('access-control-allow-methods', 'GET,POST,OPTIONS')

  if (req.method === 'OPTIONS') {
    res.writeHead(204)
    res.end()
    return
  }

  const url = new URL(req.url, `http://${req.headers.host}`)
  if (req.method === 'GET' && url.pathname === '/health') {
    res.writeHead(200, { 'content-type': 'application/json' })
    res.end(JSON.stringify({ ok: true, tools: TOOL_NAMES }))
    return
  }

  if (req.method === 'POST' && url.pathname === '/v1/chat/completions') {
    try {
      await handleChat(req, res, await readBody(req))
    }
    catch {
      res.writeHead(400, { 'content-type': 'application/json' })
      res.end(JSON.stringify({ error: 'invalid json body' }))
    }
    return
  }

  res.writeHead(404, { 'content-type': 'application/json' })
  res.end(JSON.stringify({ error: 'not found' }))
})

server.listen(PORT, '127.0.0.1', () => {
  console.log(`mock OpenAI-compatible server: http://127.0.0.1:${PORT}/v1/chat/completions`)
})
