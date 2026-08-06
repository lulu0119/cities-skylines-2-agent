# Cities: Skylines 2 Agent

为《都市：天际线 2》打造「游戏内 AI 市长」的基础仓库：游戏里一个聊天面板，玩家与 LLM 对话，AI 通过 C# 模组执行建路、划区、调税、跑模拟等操作。目标形态是纯游戏内 mod，玩家在 Paradox Mods 安装后只需填 API Key。

当前状态：**POC 已通过（2026-08-05，Mac 侧）**。下一步是在 Windows 真机上做游戏内冒烟验证，见 [docs/windows-onboarding.md](./docs/windows-onboarding.md)。

## 愿景（摘要）

```text
Gameface UI（React/TS 聊天面板）
      ↕ Cohtml 绑定
Agent Loop（待定：Gameface TS 或 C#）
  - 对话历史 + tools（function calling）→ OpenAI 兼容 API
      ↕
C# 工具层：队列到模拟主线程（UIUpdate/ToolUpdate，暂停时可用）
      ↓
Unity ECS / 游戏原生 Tool 管线
```

待决点：agent loop 放哪（等 Windows 冒烟结果）、API Key 存模组设置、工具面自研或复用 CS2MCP（Apache-2.0）、MCP 暂不需要、实时性走「暂停 + 快进」。

里程碑：**M0 POC ✅** → **M1 Windows 游戏内冒烟（下一步）** → M2 最小聊天 mod → M3 工具层 + agent loop → M4 产品化（设置/打包/Paradox Mods 上架）→ M5 迭代。

原则：游戏侧胶水是 C#、UI 是 React/TS、agent runtime 保持浏览器可移植；工具执行永远排队到模拟主线程；暂停优先；API Key 绝不进仓库；真实 Windows 是权威验证环境。

## 目录

| 目录 | 内容 |
|---|---|
| [`web/`](./web) | React/TS 聊天 UI + `@apeira/core` agent loop（浏览器侧），带模拟城市工具 |
| [`mock/`](./mock) | 零依赖 OpenAI 兼容 mock 服务器（SSE 流式 + 非流式 tool calls） |
| [`cs/ModHost`](./cs/ModHost) | C# agent loop + 模拟 mod 宿主；多目标 `net10.0;net472` |
| [`docs/`](./docs) | Windows 交接与游戏内验证清单（唯一文档） |

## 快速开始

```bash
# 终端 1：mock LLM（不需要 API Key）
cd mock && node server.mjs

# 终端 2：web（浏览器侧 POC）
cd web && pnpm install && pnpm dev
```

无头浏览器验证（Mac/Windows 通用）：

```bash
cd web && pnpm build && node e2e.mjs
```

C# 侧（需要 .NET SDK）：

```bash
cd cs/ModHost
dotnet run -f net10.0 --project . -- "建一条路，然后跑 4 小时模拟"
```

真实端点：用 `CS2POC_BASE_URL` / `CS2POC_MODEL` / `CS2POC_API_KEY` 环境变量切换（不要把 key 写进仓库）。

## 已验证（2026-08-05）

- ✅ `@apeira/core` 浏览器 bundle 构建成功（gzip 约 71 KB）。
- ✅ Node 冒烟 + 无头 Chromium e2e：读状态 → 建路 → 跑模拟 → 总结，多轮工具调用闭环。
- ✅ C#（OpenAI .NET SDK 2.10）同样的工具循环跑通；`net472` 编译检查通过（修掉 `Math.Clamp`、`string.Join(char)` 两个兼容坑）。
- ❓ 未验证（需要 Windows 真机）：Gameface 的 fetch/streams、模组内 HTTPS/TLS、暂停时 UIUpdate 队列。

## 下一步

1. 在 Windows 真机上跑 [docs/windows-onboarding.md](./docs/windows-onboarding.md)。
2. 根据结果决定 agent loop 放 Gameface TS 还是 C#。
3. 按里程碑推进到可上 Paradox Mods 的 mod。
