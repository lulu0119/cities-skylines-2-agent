# Windows 上手：游戏内冒烟验证

**日期：** 2026-08-06
**目的：** 给在 Windows 机器上接手的新 agent 一份可直接执行的清单。目标是验证三个 Mac 上无法验证的游戏内未知项，并为后续 mod 开发建立 Windows 工作流。

## 1. 环境准备

- Windows 10/11（CS2 目标环境）。
- Steam 安装并拥有《都市：天际线 2》（建议开启 modding toolchain / developer mode）。
- .NET SDK（10 或 8+）：`winget install Microsoft.DotNet.SDK.10`。
- Node.js 20+ 与 pnpm（跑 web/mock/e2e）。
- Git；把 `cities-skylines-2-agent` 仓库弄到机器上（本地复制或先推到远端再 clone）。
- 可选：Visual Studio / Rider（C# 调试）、Chrome/Edge（Gameface 是 Chromium 内核，可参考）。

## 2. 先跑不依赖游戏的验证（确保环境 OK）

```bash
cd mock && node server.mjs          # 终端 1
cd web && pnpm install && pnpm build && node e2e.mjs   # 终端 2：无头浏览器 e2e
cd cs/ModHost && dotnet build -f net472 && dotnet run -f net10.0 --project . -- "建一条路"
```

预期：e2e 输出 `PASS — 3 tool calls rendered`；C# 输出三轮 `[tool]` + 最终 `[ai]`。

## 3. 游戏内冒烟（三个未知项）

用官方模板先建一个最小 mod（`dotnet new csiimod` + `create-csii-ui-mod`），然后逐项测：

### 3.1 Gameface 里 fetch / ReadableStream 是否可用

- 在 UI bundle 里加一个测试按钮：`fetch('http://127.0.0.1:8787/v1/chat/completions', ...)`（本地 mock），并把响应渲染到面板。
- 再测流式：用 `fetch` + `response.body.getReader()` 读 SSE。
- 验收：能拿到 mock 的流式文本；若 `ReadableStream` 不可用，记录缺失的 API 并评估降级方案（例如只做非流式）。

### 3.2 模组进程内 HTTPS / TLS

- 在 C# mod 里用 `HttpClient`（或最简 `WebRequest`）请求 `https://api.openai.com/v1/models` 或任意 HTTPS 端点，用假/空 key 看返回（401 也算 TLS 通）。
- 验收：能完成 TLS 握手；若 Unity Mono 下 TLS 失败，记录错误并尝试 `ServicePointManager.SecurityProtocol = Tls12`；最终兜底是仿照 CS2MCP 手写最小 HTTP 客户端。

### 3.3 暂停时 UIUpdate / ToolUpdate 队列

- 移植 CS2MCP 的最小模式：`BridgeSystem` 挂在 `UIUpdate`，队列在工作线程入队、模拟主线程排空。
- 暂停状态下调用一个无害工具（读城市状态），再调用一个建设工具（建一段路），确认：
  - 暂停时读取可用；
  - 建设在 `ToolUpdate` 的 tool 管线能完成（参考 `BridgeToolSystem` 的三帧 Apply 状态机）；
  - 主线程 10s 超时语义下长操作如何处理。
- 验收：暂停时读/建都成功，日志无异常。

## 4. 需要回报的结果

用表格回报三项结果：✅ 通过 / ❌ 失败 + 错误日志 / ⚠️ 部分可用 + 缺什么。附：游戏版本、mod 加载日志、截图（可选）。

## 5. 结论给后续决策

- 三项全过 → Agent loop 放 Gameface TS 可行（Apeira 路线）；否则放 C#。
- HTTPS 不通但 HTTP 通 → 本地代理/中转方案，或手写 TLS。
- 暂停队列有问题 → 先解决执行模型，再谈 agent。

## 6. 坑位提醒

- 游戏更新会改 DLL：mod 需要重新编译/重试；CrossOver 补丁场景下还要重跑 patcher。
- net472 API 缺口：`Math.Clamp`、`string.Join(char)` 等新 API 不可用，写代码时避免。
- OpenAI .NET SDK 是 netstandard2.0 兼容，但 Unity Mono 运行时行为要实测（正是 3.2 要测的）。
- API Key 只放环境变量或运行时设置，别提交到仓库。
