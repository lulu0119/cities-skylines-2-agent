# Agent runtime 参考：长行程压缩、可观测与插话

**Status:** Survey evidence only. Current loop decision is [ADR-0001](../adr/0001-in-process-meai-loop.md); context budget is [ADR-0008](../adr/0008-context-budget-auto-custom.md). "暂停优先" in the constraint lens is not current — the player owns the clock via wait simulation.

**Date:** 2026-08-06

**Question:** 在 C# `IChatClient` + 手写 function-calling loop（进程内 mod）里，如何实现 (1) 长行程任务的上下文压缩，(2) 最大化可观测的模型/tool call 日志，(3) Codex 式的“工具执行期间用户插话”？必须参考成熟的 agent runtime 实现。

**Constraint lens:** 平时不动消息流（保 prefix cache 命中率）；只在接近窗口上限时压缩；多 API 供应商共用 `IChatClient`；工具走 `ToolQueueSystem`（UIUpdate 主线程）；暂停优先。

---

## 结论摘要

| 需求 | 成熟参考 | 对本项目的落地 |
| --- | --- | --- |
| **压缩** | LangChain `summarizationMiddleware`（trigger + keep）、SK `ChatHistorySummarizationReducer`、Claude Code `/compact`、OpenAI server-side compaction、Airicraft `planner-compaction.md` | C# 自研两级：仅接近窗口上限（默认 85–90%）触发；keep 最近 N 条消息原样 + 旧消息压缩为结构化摘要；平时零改动 |
| **可观测** | OpenAI Agents SDK span schema、Airicraft `LlmFlightRecorder`、MS Agent Framework OTel | JSONL 事件时间线：`task / turn / generation / function / custom` 五类 span，含 usage、延迟、参数、结果、错误、压缩事件 |
| **插话** | Apeira `AgentQueue`（`pendingInput` + `turn.input_drained`） | C# `Channel<AgentInput>`：每轮 model+tool 结束后 drain 新输入，作为下一轮继续同一 turn；UI 实时显示中间回复 |

---

## 1. 压缩：为什么平时不动，只在接近上限时触发

成熟 runtime 无一例外都采用“接近窗口上限才压缩”的触发模型，而不是每轮都重写历史：

| 实现 | 触发 | 保留策略 |
| --- | --- | --- |
| Claude Code `/compact` | 接近 context window 上限时自动触发（也可手动） | 模型生成摘要，保留关键决策、代码变更、未完成任务；最近消息原样 |
| LangChain `summarizationMiddleware` | `trigger: {tokens \| fraction \| messages}`（可 AND/OR） | `keep: {messages \| tokens \| fraction}` 保留最近消息（multimodal 原样），旧消息 LLM 摘要 |
| SK `ChatHistorySummarizationReducer` | `threshold_count` | 压到 `target_count`；`ChatHistoryTruncationReducer` 纯截断 |
| OpenAI Responses API | `context_management.compact_threshold` | 服务端在流中自动压缩 |

结论：用户已锁定的“85–90% 触发、平时保持原消息流”与主流实现一致。平时不压缩的最大收益是 **prefix cache 命中率**：同一个前缀重复请求时 provider 可以复用 KV cache；任何历史改写都会破坏前缀。压缩只应在真正接近窗口时发生，且压缩后产生的新前缀是稳定的，直到下一次压缩。

### 1.1 最有价值的参考：Airicraft 的压缩检查点格式

Airicraft 的 `planner-compaction.md` 是本 repo 已克隆的本地源码（`codex-refs/airicraft`），直接给出一个可照抄的摘要任务格式：

```text
COMPACTION TASK:
Return strict JSON with:
{
  "time_anchor": string,
  "session_state": string,
  "active_goal": string,
  "active_commitments": string[],
  "durable_facts": string[],
  "relevant_people": string[],
  "open_loops": string[],
  "recent_timeline": string[],
  "forgettable_noise": string[]
}
Preserve user constraints, operator instructions, active goals, open loops,
important names, and current world/session state.
Prefer compressing assistant chatter, tool chatter, and stale notices.
Do not keep stale relative-time phrases such as "4 seconds ago";
convert them into stable facts or timeline notes.
```

这套字段的意图很清晰：**摘要不是“复述对话”，而是把可继续工作的状态显式化**。对本 mod 需要扩展的字段：

- `current_plan`：结构化任务计划的当前状态（已完成/进行中/待办步骤）；
- `context_blocks`：地图钉子、MoveIt 式选路网产生的命名上下文块；
- `paused_state`：暂停优先模式下当前暂停原因与恢复点；
- `last_world_snapshot`：最近一次城市快照（资金、人口、需求等关键数字，转稳定事实）。

### 1.2 本项目建议的 C# 实现

```text
AgentHistoryCompactor
  ├─ ShouldCompact(historyTokens, windowTokens, threshold=0.85~0.9)
  ├─ Compact(history):
  │    ├─ keptTail = 最近 N 条消息原样保留（含最近 tool 结果）
  │    └─ summary = 旧消息 → 结构化 JSON 检查点（Airicraft 字段 + 本 mod 扩展）
  └─ Emit("compact") 事件到可观测日志
```

- token 估算：优先用 provider 返回的 `usage.input_tokens` 回填，无则用本地近似计数兜底；窗口大小按模型配置。
- 摘要消息插入为一条 **system 级 summary message**（类似 SK 的 `SummaryMetadataKey` 标记），后续消息保持原样。
- 压缩本身是一次 LLM 调用，必须走同一 `IChatClient`（多 provider 兼容），并计入可观测日志，便于调优摘要 prompt。
- 不依赖 OpenAI server-side compaction：它只在单一供应商可用，且不满足“多 provider + 本地可观测”约束。

---

## 2. 可观测：最大化的 model/tool call 日志

### 2.1 参考 schema

**OpenAI Agents SDK**（本地源码 `codex-refs/agents-python/src/agents/tracing/`）定义 span 类型：

| Span | 字段 |
| --- | --- |
| `task` | 一次顶层运行，累计 usage |
| `turn` | 一次 agent loop turn，含 usage |
| `agent` | agent 名称、tools、handoffs |
| `generation` | input/output（完整消息序列）、model、model_config、usage |
| `function` | name、input、output、mcp_data |
| `response` | response_id、usage |
| `handoff / guardrail / custom / mcp_tools / speech...` | 各自语义 |

`FunctionSpanData` 的 `name/input/output` 正是“改进 tool/prompt/skill”所需的最小字段；`GenerationSpanData` 的 `input/output/model/usage` 让每次工具调用都能回溯到触发它的模型请求与成本。

**Airicraft `LlmFlightRecorder`**（本地源码）：环形缓冲（默认容量 2048），每条记录含 `sequenceId`、`requestedAtMs/completedAtMs`、`status`（REQUESTED / RAW_RESPONSE / COMPLETED / FAILED / UNMATCHED）、provider/model/endpoint/timeout、`messageCount`、`requestBody`、`statusCode`、`rawResponseBody`、usage、parsedResponse、failureType/Message；CLI 支持 `--since entryId` 增量查询 + `truncated` 标记。这给出两个好模式：**增量游标** 和 **状态机式记录（请求→原始响应→解析完成/失败）**。

**MS Agent Framework**：OpenTelemetry GenAI 语义约定，`invoke_agent` / `chat` / `execute_tool` spans，`gen_ai.client.token.usage` 指标，敏感数据开关。

### 2.2 本项目建议：JSONL 事件时间线

文件位置：`CSII_USERDATAPATH/Mods/CitiesSkylines2Agent/logs/agent-timeline-<sessionId>.jsonl`，大小轮转（如 50 MB / 保留最近 N 个文件）。

事件类型（一行一个 JSON 对象，统一字段 `seq, ts, type, session, turnId`）：

| type | 内容 |
| --- | --- |
| `task.start / task.finish` | 会话开始/结束、模型、窗口、累计 usage |
| `turn.start / turn.finish` | 一次用户输入→agent 停下的完整周期；插话 drain 也记录 |
| `generation` | 发给 provider 的消息序列（引用或截断）、返回 choices/tool_calls、usage、延迟、model、重试次数 |
| `function` | tool 名、参数、结果、状态（ok/error/not_allowed）、延迟、UIUpdate 排队等待时长 |
| `context_block` | 钉子/选路网上下文块注入：名称、来源、内容 |
| `plan` | 计划创建/批准/步骤完成/失败/恢复 |
| `compact` | 触发阈值、被压缩条数、摘要内容、新窗口占用 |
| `interleaved_input` | 插话消息何时排队、何时 drain 注入 |
| `error` | 异常/重试/降级 |

关键追溯链：`function` 事件必须能通过 `turnId + seq` 找到触发它的 `generation`（看当时模型看到了什么、是否采纳结果、之后是否重试/纠错）——这正是改进 tool 描述、system prompt、技能的唯一可靠证据。

**红线：API key 永不写入日志。** key 只存在于 settings/env；日志只记 provider 名与 endpoint，不记请求 header。

---

## 3. 插话：用户消息在“当前 tool call 结果之后”注入

### 3.1 Apeira `AgentQueue`（本地源码 `codex-refs/apeira/packages/core/src/agent/queue.ts`）

这是“Codex 式插话”的成熟语义，核心机制：

```text
send(item):
  if activeTurn exists → pendingInput.push(item)   // 只排队，不打断
  else               → 新开 turn 并 pump()

runTurn(turn):
  while !aborted:
    result = runner(input)                          // 一轮 model + tools 完成
    if pendingInput.length > 0:
      drained = pendingInput.splice(0)
      emit("turn.input_drained", count)
      input = drained                               // 新消息作为下一轮输入
      continue                                       // 同一 turn 继续
    else: break
```

配套事件：`turn.input_queued`（用户消息排队）、`turn.input_drained`（注入到下一轮）；`turnInput / turnOutput` 累计，`onTurnFinish` 拿到整轮累计 usage。另有 `interrupt()`（中止当前 turn）与 `clear()`——**打断与插话是两个独立能力**。

### 3.2 本项目 C# 映射

```text
AgentLoop（后台任务）
  Channel<AgentInput> pendingInput

每轮循环：
  1. response = await chatClient.GetResponseAsync(messages)     // 流式，UI 实时显示
  2. 若 response 含 tool_calls → 排 ToolQueueSystem（UIUpdate 主线程）执行
  3. 追加 tool results 到 messages
  4. 若 pendingInput 有用户消息 → 追加为 user message，继续下一轮（同一 turn）
  5. 否则 → turn.finish，等待下一用户消息
```

要点：

- **注入边界 = 一轮 model+tool 完成之后**，即 Apeira 的 runner 边界；不是打断正在执行的工具（那是 `interrupt()`，另做“中止”按钮）。
- UI 输入框在 agent 工作期间不锁；消息先显示“排队中”，drain 后显示“已插入”，与 agent 的中间回复交错出现——即“边回复边继续工作”。
- 插话消息与普通消息走同一 `IChatClient` 消息格式，天然多 provider 兼容。
- 累计 usage 语义与 Apeira 相同：同一 turn 内多次 generation 的 usage 累加，供可观测日志与成本统计。

---

## 4. 落地顺序建议

1. `AgentLoop` 骨架 + JSONL 事件（最小可观测先跑起来）；
2. 工具注册 + `ToolQueueSystem` 接线（CS2MCP 44 工具内联）；
3. 插话 channel + UI 排队/已插入状态；
4. 压缩器 + 85–90% 触发；
5. 计划状态机 + 持久化恢复。

---

## 来源索引

| Claim | Source |
| --- | --- |
| Apeira 插话队列 | `codex-refs/apeira/packages/core/src/agent/queue.ts` |
| Airicraft 压缩检查点 | `codex-refs/airicraft/src/client/resources/prompts/planner-compaction.md` |
| Airicraft 飞行记录器 | `codex-refs/airicraft/src/client/java/ai/moeru/airicraft/agent/debug/LlmFlightRecorder.java` |
| OpenAI Agents SDK span | `codex-refs/agents-python/src/agents/tracing/span_data.py` |
| LangChain summarizationMiddleware | https://docs.langchain.com/oss/javascript/langchain/middleware/built-in |
| SK HistoryReducer | https://github.com/microsoft/semantic-kernel/blob/main/dotnet/samples/Concepts/Agents/ChatCompletion_HistoryReducer.cs |
| Claude Code 自动压缩 | https://code.claude.com/docs/en/context-window |
| OpenAI server-side compaction | https://developers.openai.com/api/docs/guides/compaction |
