# Skylines 2 Agent POC

验证「游戏内 AI 市长」两条技术路线的可行性，不需要安装《城市：天际线 2》：

| 目录 | 内容 | 验证什么 |
|---|---|---|
| [`web/`](./web) | Vite + React/TS，浏览器里跑 `@apeira/core`（底层 XSAI），带模拟城市工具 | Apeira 能否打成浏览器 bundle、流式对话 + 多轮 function calling 是否跑得通 |
| [`mock/`](./mock) | 零依赖 OpenAI 兼容 mock 服务器（SSE + tool calls） | 没有 API Key 也能跑通全流程 |
| [`cs/`](./cs) | C# console「ModHost」：用 OpenAI .NET SDK 跑同一套模拟城市工具 | 游戏内 C# agent loop 的可行性（含 net472 编译兼容检查） |

## 快速开始（浏览器侧）

```bash
# 终端 1：mock LLM
cd mock && node server.mjs

# 终端 2：web
cd web && pnpm install && pnpm dev
```

打开 http://127.0.0.1:5173 ，直接对 AI 说话。默认走本地 mock（无需 API Key）；
想用真实模型，把 baseURL 改成 `https://api.openai.com/v1/`、填 API Key 和模型名，点「应用配置」。

无头浏览器验证：

```bash
cd web && pnpm build && node e2e.mjs
```

Node 冒烟（同一套 agent 循环）：

```bash
cd web && node smoke.mjs
```

## 快速开始（C# 侧）

```bash
cd cs/ModHost
dotnet run -f net10.0 --project . -- "建一条路，然后跑 4 小时模拟"
```

默认打本地 mock；用 `CS2POC_BASE_URL` / `CS2POC_MODEL` / `CS2POC_API_KEY` 切换真实端点。

## 验证结果（2026-08-05）

- ✅ **浏览器 bundle**：`@apeira/core` 打进 Vite 产物，gzip 约 71 KB。
- ✅ **Node 冒烟**（`web/smoke.mjs`）：读状态 → 建路 → 跑模拟 → 总结，多轮工具调用闭环。
- ✅ **无头 Chromium e2e**（`web/e2e.mjs`）：页面里发「建一条路，然后跑 4 小时模拟」，渲染 3 次工具调用并流式输出最终回答。
- ✅ **C# agent loop**（`cs/ModHost`，net10.0 实跑）：OpenAI .NET SDK 非流式 + function calling，同样的三步工具循环跑通。
- ✅ **net472 编译兼容检查**：`dotnet build -f net472` 通过；过程中抓到并修复了两个真实兼容问题（`Math.Clamp` 和 `string.Join(char)` 在 net472 不存在）。
- ❓ **仍未验证**：Gameface 是 Chromium 受限子集，`fetch`/`ReadableStream` 是否可用必须进游戏实测；CS2 模组进程内 HTTPS 到模型 API 的 TLS 兼容性也需实测（兜底：手写最小 HTTP 客户端，参考 CS2MCP）。

## 目前结论（POC 范围）

- ✅ `@apeira/core` + XSAI 可以打进浏览器 bundle（gzip 约 71 KB），流式输出、多轮工具调用可用。
- ✅ 多轮 function calling 在 mock 上闭环：读状态 → 建路 → 跑模拟 → 总结。
- ✅ C# 侧可以用 OpenAI .NET SDK 写同样的 agent loop；`net472` 目标可编译（真实 Unity Mono 运行仍需游戏内验证）。
- ❓ 未验证：Gameface 是 Chromium 受限子集，`fetch`/`ReadableStream` 是否可用必须进游戏实测；
  CS2 模组进程内 HTTPS 到模型 API 的 TLS 兼容性也需实测（兜底：手写最小 HTTP 客户端，参考 CS2MCP）。
