import { useEffect, useRef, useState } from 'react'

import type { AgentConfig } from './agent'
import { createCityAgent, run } from './agent'
import { CitySim } from './citySim'

interface ChatLine {
  role: 'user' | 'assistant' | 'tool' | 'system'
  text: string
}

const DEFAULT_CONFIG: AgentConfig = {
  apiKey: '',
  baseURL: 'http://127.0.0.1:5173/v1', // vite dev proxy -> mock server
  model: 'mock-gpt',
}

export default function App() {
  const [lines, setLines] = useState<ChatLine[]>([])
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [config, setConfig] = useState(DEFAULT_CONFIG)
  const cityRef = useRef(new CitySim())
  const agentRef = useRef<ReturnType<typeof createCityAgent>>()

  const push = (line: ChatLine) => setLines(prev => [...prev, line])

  const send = async () => {
    const text = input.trim()
    if (text === '' || busy)
      return

    setInput('')
    setBusy(true)
    push({ role: 'user', text })

    try {
      agentRef.current ??= createCityAgent(cityRef.current, config)
      let acc = ''
      for await (const event of run(agentRef.current, { role: 'user', content: text, type: 'message' })) {
        if (event.type === 'text.delta') {
          acc += event.delta
        }
        else if (event.type === 'tool-call.start') {
          push({ role: 'tool', text: `工具调用: ${event.toolName}` })
        }
        else if (event.type === 'tool-result.done') {
          const result = typeof event.result === 'string' ? event.result : JSON.stringify(event.result)
          push({ role: 'tool', text: `工具结果: ${result}` })
        }
        else if (event.type === 'turn.failed') {
          push({ role: 'system', text: `失败: ${event.error}` })
        }
      }
      if (acc !== '')
        push({ role: 'assistant', text: acc })
    }
    catch (error) {
      push({ role: 'system', text: `错误: ${error}` })
    }
    finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    push({ role: 'system', text: 'POC 就绪。试试：「建一条路」或「把税率调到 9% 然后跑 4 小时模拟」。' })
  }, [])

  return (
    <main style={{ maxWidth: 760, margin: '0 auto', padding: 24, fontFamily: 'system-ui, sans-serif' }}>
      <h1 style={{ fontSize: 20 }}>Skylines 2 · 游戏内 AI 市长 POC（浏览器侧 @apeira/core）</h1>

      <section style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 12 }}>
        <input
          placeholder="API Key（留空则用本地 mock）"
          value={config.apiKey}
          onChange={e => setConfig({ ...config, apiKey: e.target.value })}
          style={{ flex: 2, minWidth: 180, padding: 6 }}
        />
        <input
          placeholder="baseURL"
          value={config.baseURL}
          onChange={e => setConfig({ ...config, baseURL: e.target.value })}
          style={{ flex: 1, minWidth: 160, padding: 6 }}
        />
        <input
          placeholder="model"
          value={config.model}
          onChange={e => setConfig({ ...config, model: e.target.value })}
          style={{ width: 140, padding: 6 }}
        />
        <button onClick={() => { agentRef.current = undefined; push({ role: 'system', text: 'Agent 已按新配置重建。' }) }}>
          应用配置
        </button>
      </section>

      <section style={{ border: '1px solid #ccc', borderRadius: 8, padding: 12, minHeight: 320, maxHeight: 480, overflowY: 'auto', background: '#fafafa' }}>
        {lines.map((line, i) => (
          <p key={i} style={{ margin: '6px 0', fontSize: 14, whiteSpace: 'pre-wrap', color: line.role === 'user' ? '#0b57d0' : line.role === 'tool' ? '#555' : line.role === 'system' ? '#999' : '#111' }}>
            <strong>{line.role === 'user' ? '你' : line.role === 'tool' ? '工具' : line.role === 'system' ? '系统' : 'AI'}:</strong> {line.text}
          </p>
        ))}
        {busy && <p style={{ color: '#888' }}>…</p>}
      </section>

      <section style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <input
          placeholder="对 AI 市长说话…"
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && !e.nativeEvent.isComposing && send()}
          style={{ flex: 1, padding: 8 }}
          disabled={busy}
        />
        <button onClick={send} disabled={busy}>发送</button>
      </section>
    </main>
  )
}
