# Cities: Skylines 2 Agent

为《都市：天际线 2》打造「游戏内 AI 市长」的基础仓库：游戏里一个聊天面板，玩家与 LLM 对话，AI 通过 C# 模组执行建路、划区、调税、跑模拟等操作。目标形态是纯游戏内 mod，玩家在 Paradox Mods 安装后只需填 API Key。

当前状态：**POC 已通过（2026-08-05，Mac 侧）**。下一步是在 Windows 真机上做游戏内冒烟验证，见 [docs/windows-onboarding.md](./docs/windows-onboarding.md)。

## 为什么是这个仓库

这是后续所有 CS2 Agent 工作的基地，不只做一次测试：

- 浏览器侧 agent loop（`@apeira/core` + XSAI）已验证可打进 bundle；
- C# 侧 agent loop（OpenAI .NET SDK）已验证可编译/运行；
- 模拟城市工具、mock LLM、无头浏览器测试都已就位；
- 未来 mod 本体、UI、运行时、打包都在这一个仓库里演进。

## 目录

| 目录 | 内容 |
|---|---|
| [`web/`](./web) | React/TS 聊天 UI + `@apeira/core` agent loop（浏览器侧），带模拟城市工具 |
| [`mock/`](./mock) | 零依赖 OpenAI 兼容 mock 服务器（SSE 流式 + 非流式 tool calls） |
| [`cs/ModHost`](./cs/ModHost) | C# agent loop + 模拟 mod 宿主；多目标 `net10.0;net472` |
| [`docs/`](./docs) | [愿景与路线图](./docs/vision.md)、[Windows 上手](./docs/windows-onboarding.md) |

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

1. 在 Windows 真机上跑 [游戏内冒烟测试](./docs/windows-onboarding.md)。
2. 根据结果决定 agent loop 放 Gameface TS 还是 C#。
3. 按 [路线图](./docs/vision.md) 推进到可上 Paradox Mods 的 mod。
