# C# agent runtimes — depth survey (for CS2 in-mod loop)

**Date:** 2026-08-06  
**Question:** Does the C# ecosystem have mature *and/or* lightweight agent runtimes suitable for an in-process Cities: Skylines II mod?  
**Constraint lens:** Paradox Mods packaging, Unity Mono / `net472`-class surface, UIUpdate tool queue, OpenAI-compatible HTTP (already proven in-mod for TLS).

---

## Short answer

| Need | Best fit |
| --- | --- |
| **Lightest workable agent** | **`OpenAI` NuGet + 50–100 lines tool loop** (this repo’s [`archive/cs/ModHost`](../../archive/cs/ModHost)) — *you* own ReAct-equivalent |
| **Batteries-included orchestration** | **Semantic Kernel** (`FunctionChoiceBehavior.Auto`) — mature plugins/DI, not “tiny” |
| **Current Microsoft “agent product” SDK** | **Microsoft Agent Framework** (`Microsoft.Agents.AI*`) — successor to SK Agents + AutoGen; heavier, enterprise-shaped |
| **Classic multi-agent research kit** | **AutoGen for .NET** — migrate toward Agent Framework ([MS learn migration](https://learn.microsoft.com/en-us/agent-framework/migration-guide/from-autogen/)) |

There is **no C# clone of apeira** (stream-first, browser-sized). Closest *lightweight* thing is **raw OpenAI.NET tool loop**, not a named “AgentRuntime” package.

None of these SDKs are branded **ReAct**. ReAct is a *pattern*; modern .NET stacks implement it as **native function calling** (model emits `tool_calls` → host runs code → append tool results → repeat).

---

## Tier map

```text
lighter                                                                 heavier
─────────────────────────────────────────────────────────────────────────────►
 HttpClient JSON     OpenAI NuGet      MEAI IChatClient     Semantic Kernel
 (hand schema)       + hand loop       (+ optional tools)   + Agents.Core
                                                              │
                                                              ▼
                                                    Microsoft Agent Framework
                                                    (agents + workflows + harness)
```

---

## 1. OpenAI .NET SDK (`OpenAI` on NuGet)

- **What it is:** Official-lineage **API client** for Chat Completions / tools / streaming — not an agent framework.  
- **Version seen in-repo:** `2.10.0` in ModHost; NuGet latest sampled **2.12.0**.  
- **TFMs (2.12.0):** `netstandard2.0`, `net8.0`, `net10.0`.  
- **Agent / ReAct:** **No.** You write the loop (ModHost already does).  
- **Pros for CS2:** Smallest dependency among “real” clients; already targeted in POC; streams handled in C# (no Gameface Streams).  
- **Cons:** Retries, history truncation, tracing, multi-agent = DIY.  
- **Sources:** [NuGet OpenAI](https://www.nuget.org/packages/OpenAI), [`archive/cs/ModHost/Program.cs`](../../archive/cs/ModHost/Program.cs).

## 2. Microsoft.Extensions.AI (MEAI)

- **What it is:** Shared abstractions (`IChatClient`, tools) used under SK / Agent Framework.  
- **TFMs (10.8.3 sample):** includes `netstandard2.0`, `net462`, `net8+`.  
- **Agent / ReAct:** Thin — chat + tool abstractions; **auto multi-step loop is not the product**.  
- **Fit:** Good if you want DI-friendly chat without full SK; still not an apeira-class runtime.  
- **Sources:** [NuGet Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI).

## 3. Semantic Kernel (+ Agents.Core)

- **What it is:** Microsoft orchestration SDK — Kernel, plugins (`[KernelFunction]`), planners historically, now **function calling** via `FunctionChoiceBehavior.Auto()` (can auto-invoke tools).  
- **Agents:** `Microsoft.SemanticKernel.Agents.Core` (`ChatCompletionAgent`, etc.).  
- **TFMs (1.78.0 sample):** `netstandard2.0`, `net8.0`, `net10.0`.  
- **Agent / ReAct:** **Yes, as auto tool loop** — closest “完善” single-agent experience in mainstream .NET until Agent Framework. Not classic text-ReAct.  
- **Pros:** Mature docs, plugin model maps cleanly to “mayor tools”, DI-friendly.  
- **Cons for CS2 mod:** Dependency graph + abstractions heavier than ModHost; Unity Mono / IL2CPP / linker pain risk; overkill for “one mayor agent + N tools”.  
- **Status note:** SK GitHub positions **Microsoft Agent Framework** as enterprise successor for agent work ([semantic-kernel README](https://github.com/microsoft/semantic-kernel)).  
- **Sources:** [SK quick start](https://learn.microsoft.com/en-us/semantic-kernel/get-started/quick-start-guide), [SK Agent Framework docs](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/).

## 4. Microsoft Agent Framework (`Microsoft.Agents.AI*`)

- **What it is:** **Current** Microsoft agent SDK — agents, tools/MCP, sessions, middleware, **workflows**, optional **Harness** (planning/todos/memory/approvals). Successor combining SK enterprise ideas + AutoGen agent patterns.  
- **NuGet (sampled):** `Microsoft.Agents.AI` / `Microsoft.Agents.AI.OpenAI` **1.17.0**.  
- **TFMs:** includes `netstandard2.0`, `net472`, `net8+` (broader than many libs).  
- **Agent / ReAct:** First-class **agents + tool use**; workflows for multi-step graphs — still not “ReAct” naming.  
- **Pros:** Most “完善” Microsoft story in 2026; OpenAI + other providers.  
- **Cons for CS2:** Heaviest; Designed for apps/services more than Unity game mods; packaging & Mono risk highest.  
- **Sources:** [Agent Framework overview](https://learn.microsoft.com/en-us/agent-framework/overview/), [OpenAI provider](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai/), [NuGet Microsoft.Agents.AI.OpenAI](https://www.nuget.org/packages/Microsoft.Agents.AI.OpenAI/).

## 5. AutoGen for .NET

- **What it is:** Multi-agent / conversational agent kit (Core, OpenAI connectors, group chat, …).  
- **Trajectory:** Microsoft documents **migration to Agent Framework**; treat AutoGen.NET as **legacy/maintenance** for new greenfield.  
- **Fit for CS2:** Poor default — multi-agent research weight without product upside for a single mayor.  
- **Sources:** [AutoGen .NET docs](https://microsoft.github.io/autogen/dotnet/dev/), [Migrate from AutoGen](https://learn.microsoft.com/en-us/agent-framework/migration-guide/from-autogen/).

## 6. Other community options (brief)

| Library | Notes |
| --- | --- |
| **LangChain / LangChain-ish .NET ports** | Community; uneven parity with Python; not the Microsoft-supported path. |
| **LLamaSharp / local-only** | Local inference, not a hosted-API agent runtime. |
| **Hand `HttpClient` + JSON** | Absolute lightest; CS2MCP-style fallback if OpenAI package fails on Mono. |

Game-adjacent **in-mod C#** precedents (from [research-in-game-ai-mods](./2026-08-06-research-in-game-ai-mods.md)): RimAI Core, Steve — typically **custom orchestration + HTTP**, not SK/MAF inside the game process.

---

## “完善” vs “轻量” for *this* mod

| Criterion | OpenAI + hand loop | MEAI | Semantic Kernel | Agent Framework |
| --- | --- | --- | --- | --- |
| Completeness (memory, workflows, multi-agent) | Low | Low–mid | Mid–high | **Highest** |
| Bundle / dependency risk in Unity | **Lowest** | Low–mid | Mid | **Highest** |
| Matches M0 POC | **Already** | Easy migrate | Rewrite | Rewrite |
| Auto tool loop | DIY | Partial | **Yes** | **Yes** |
| ReAct-as-name | No | No | No | No |
| Recommended for Paradox Mods v1 | **Yes** | Optional shim | Later if DIY loop hurts | Unlikely first |

**Recommendation (CS2 AI mayor):**

1. **Ship loop:** keep **OpenAI.NET + explicit tool loop** (ModHost → in-mod), queue tool bodies to `UIUpdate`.  
2. **If** loop boilerplate becomes painful (many tools, filters, telemetry): evaluate **SK `FunctionChoiceBehavior.Auto`** behind a facade — still verify Mono load.  
3. **Defer Agent Framework** until you need workflows/multi-agent/harness; not required for “chat + mayor tools”.  
4. Do **not** pick a framework hoping for “ReAct package” — implement or inherit **function-calling loop**.

---

## 暂定选型（2026-08-06）

**手撸：`IChatClient`（Microsoft.Extensions.AI）+ ReAct / function-calling 循环。**

| 项 | 决定 |
| --- | --- |
| 放哪 | **C# 进程内**（Gameface 只做聊天 UI + bindings） |
| 客户端抽象 | **`IChatClient`**，背后可用 OpenAI.NET / 兼容 endpoint 适配器 |
| 循环 | **自己写**（Thought→tool_calls→执行→Observation→再聊），不引入 SK / Agent Framework / apeira |
| 工具执行 | 仍排队 **`UIUpdate`**（与冒烟 3.3、CS2MCP 同模式） |
| 与 ModHost | ModHost 已是同构 POC（直接 `ChatClient`）；下一步可收成 `IChatClient` + 共用 loop，便于换 provider |
| 不做（暂定） | Gameface 内 apeira/xsai；外挂 Node；Semantic Kernel / MAF |

推翻条件（再议）：Mono 加载 MEAI/OpenAI 失败 → 退回手写 `HttpClient`；或 DIY loop 维护成本明显过高 → 再评 SK Auto。
---

## Relation to Gameface TS / apeira

| Runtime | Streams issue | Packaging |
| --- | --- | --- |
| apeira / xsai in Gameface | Hard (see [agent-runtime-gameface-requirements](./2026-08-06-agent-runtime-gameface-requirements.md)) | In-UI bundle |
| C# OpenAI / SK / MAF | Handled in-process .NET | DLL in mod; no Node |

C# side wins packaging and streaming; TS side wins if you later polyfill or use XHR-only custom client.

---

## Source index

| Claim | Source |
| --- | --- |
| Agent Framework overview / successor narrative | https://learn.microsoft.com/en-us/agent-framework/overview/ |
| SK → agents / function calling | https://learn.microsoft.com/en-us/semantic-kernel/get-started/quick-start-guide |
| AutoGen → Agent Framework migration | https://learn.microsoft.com/en-us/agent-framework/migration-guide/from-autogen/ |
| NuGet TFMs (sampled 2026-08-06) | nuget.org flat container for OpenAI, SK, Agents.AI, MEAI |
| In-repo POC loop | `archive/cs/ModHost` |
| Game-mod C# patterns | `docs/research/2026-08-06-research-in-game-ai-mods.md` |
