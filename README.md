# Cities: Skylines 2 Agent

为《都市：天际线 2》打造「游戏内 AI 市长」的基础仓库：游戏里一个聊天面板，玩家与 LLM 对话，AI 通过 C# 模组执行建路、划区、调税、跑模拟等操作。目标形态是纯游戏内 mod，玩家在 Paradox Mods 安装后只需填 API Key。

当前状态：**M1 冒烟已归档**（[archive/docs/2026-08-06-m1-smoke.md](./archive/docs/2026-08-06-m1-smoke.md)）；**`Mod/` 已去掉冒烟面板**，换成本地 echo 聊天壳 + `ToolQueueSystem`。Agent loop 暂定 C#。Windows 交接见 [docs/guide/2026-08-06-windows-onboarding.md](./docs/guide/2026-08-06-windows-onboarding.md)；踩坑见 [docs/ops/2026-08-06-windows-toolchain-pitfalls.md](./docs/ops/2026-08-06-windows-toolchain-pitfalls.md)。

## 愿景（摘要）

```text
Gameface UI（React/TS 聊天面板）
      ↕ Cohtml 绑定
Agent Loop（暂定 C#：MEAI IChatClient + 手撸 ReAct/tool 循环）
  - 对话历史 + tools（function calling）→ OpenAI 兼容 API
      ↕
C# 工具层：队列到模拟主线程（UIUpdate/ToolUpdate，暂停时可用）
      ↓
Unity ECS / 游戏原生 Tool 管线
```

待决点：API Key 存模组设置、工具面自研或复用 CS2MCP（Apache-2.0）、MCP 暂不需要、实时性走「暂停 + 快进」。Agent loop 暂定见 [docs/research/2026-08-06-csharp-agent-runtimes.md](./docs/research/2026-08-06-csharp-agent-runtimes.md)「暂定选型」。

里程碑：**M0 POC ✅** → **M1 Windows 冒烟 ✅（已归档）** → **M2 聊天壳（进行中）** → M3 工具层 + agent loop → M4 产品化（设置/打包/Paradox Mods 上架）→ M5 迭代。

原则：游戏侧胶水是 C#、UI 是 React/TS；**agent loop 暂定 C#（不绑 apeira/SK/MAF）**；工具执行永远排队到模拟主线程；暂停优先；API Key 绝不进仓库；真实 Windows 是权威验证环境。

## 目录

| 目录 | 内容 |
|---|---|
| [`Mod/`](./Mod) | 游戏内 mod（C# + Gameface UI）：`ToolQueueSystem` + 本地 echo 聊天壳 |
| [`archive/`](./archive) | 离线 POC：`web/`、`mock/`、`cs/ModHost` + M1 冒烟文档 |
| [`docs/`](./docs) | [索引](./docs/README.md)：guide / research / ops |

## 快速开始（游戏内 mod）

环境要求——**不需要官方工具链全绿**（参考实际配置的 Windows 环境）：

**需要**
- 游戏本体（Cities: Skylines II）
- .NET SDK 8+：`dotnet build`（新终端需能直接找到 `dotnet`）
- Node.js：仅改 UI 时需要
- 从游戏目录 `Cities2_Data/Content/Game/.ModdingToolchain` 拷 `Mod.props` / `Mod.targets` 到 `%LocalLow%\Colossal Order\Cities Skylines II\.cache\Modding\`，并按需设置 `CSII_*` 用户环境变量（`CSII_TOOLPATH`、`CSII_USERDATAPATH`、`CSII_MANAGEDPATH` 等，详见 [Windows onboarding](docs/guide/2026-08-06-windows-onboarding.md)）

**不需要**
- Unity Editor / Unity Hub / License / Unity Mod Project：本 mod 是 C# + Gameface UI，无 Burst 作业，官方 ModPostProcessor 已在 csproj 中跳过
- `dotnet new csiimod` / `create-csii-ui-mod` 脚手架
- 游戏内「自动安装」的一整排工具链不必全绿

```bash
cd Mod
dotnet build
```

构建输出会部署到 `%LocalLow%\Colossal Order\Cities Skylines II\Mods\CitiesSkylines2Agent\`（DLL + 合并依赖 + UI）。

仅改 UI：`cd Mod/UI && npm run build`（需设置 `CSII_USERDATAPATH`）。

进游戏启用 **CitiesSkylines2Agent**，进存档；右下聊天壳（`GameBottomRight` + `Portal`）。调试可加启动参数 `-developerMode`。

离线 POC（可选）：见 [`archive/README.md`](./archive/README.md)。

## 已验证

- ✅ Mac 侧浏览器 + C# ModHost POC（2026-08-05）；源码在 `archive/`。
- ✅ Windows 工具链；M1：**C# HTTPS ✅**、**暂停下 UIUpdate 队列 ✅**、**Gameface `ReadableStream` ❌**（详见 archive M1 文档）。

## 下一步

1. 在 `Mod/` 接上 C# `IChatClient` + tool 循环（聊天壳已就位）。
2. 工具面排队进 `ToolQueueSystem`；设置里存 API Key。
3. 产品化打包 / Paradox Mods。
