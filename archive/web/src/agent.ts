import { createAgent, rawTool, run } from '@apeira/core'
import { chat } from '@apeira/core/chat'
import type { AgentInput } from '@apeira/core'

import type { CitySim } from './citySim'
import { createCityTools } from './tools'

export interface AgentConfig {
  apiKey: string
  baseURL: string
  model: string
}

export const createCityAgent = (city: CitySim, config: AgentConfig) => createAgent({
  instructions: `你是《城市：天际线 2》里的 AI 市长助手。
先读取城市状态，再根据用户要求调用工具执行操作。
工具执行会真实改变模拟状态；执行后把结果用中文简要汇报。`,
  runner: chat({
    apiKey: config.apiKey || undefined,
    baseURL: config.baseURL,
    model: config.model,
    stream: true,
    temperature: 0.2,
  }),
  tools: createCityTools(city),
})

export type { AgentInput }
export { run, rawTool }
