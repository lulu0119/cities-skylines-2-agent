# 愿景与路线图

**日期：** 2026-08-06

## 目标

一个发布到 Paradox Mods 的《都市：天际线 2》模组：

- 游戏内有聊天面板（Gameface，React/TS）；
- 玩家输入 API Key（模组设置）后即可与 LLM 对话；
- AI 通过 C# 工具层在游戏内执行市长操作（建路、划区、建筑、税收、政策、跑模拟）；
- 纯游戏内闭环，不需要外挂 agent 程序；
- 暂停优先：检查/规划在暂停窗口进行，模拟按需快进。

## 目标架构

```text
Gameface UI（React/TS 聊天面板）
      ↕ Cohtml 绑定（C# ↔ UI 事件）
Agent Loop（候选位置：Gameface TS 或 C#）
  - 对话历史 + tools（function calling）→ OpenAI 兼容 API
      ↕
C# 工具层（Tool registry）
  - 队列到模拟主线程（UIUpdate/ToolUpdate，暂停时可用）
  - 观察：ECS 查询 + 截图；执行：原生 Tool 管线
      ↓
Unity ECS / 游戏原生系统
```

## 待决点

| 决策 | 选项 | 现状 |
|---|---|---|
| Agent loop 放哪 | Gameface TS bundle vs C# mod 内 | POC 两条路都通了；等 Windows 游戏内冒烟结果定 |
| API Key 存储 | 游戏模组设置（推荐）vs localStorage | 未定；倾向模组设置 |
| 工具面 | 自研最小工具集 vs fork CS2MCP 的 Bridge 工具 | CS2MCP（Apache-2.0）是主要参考；建路/划区管线可直接复用 |
| MCP | 不需要（游戏内闭环） | 以后若做外部客户端再考虑 |
| 实时性 | 不追求 tick 级；暂停 + 快进 | 天际线天然适合回合制市长 |

## 里程碑

- **M0 POC（完成，2026-08-05）**：两条技术路线验证 + mock + e2e。
- **M1 Windows 游戏内冒烟（下一步）**：三个未知项实测——Gameface fetch/streams、模组内 HTTPS/TLS、暂停时 UIUpdate/ToolUpdate 队列。
- **M2 最小聊天 mod**：官方 `csiimod` + `create-csii-ui-mod` 模板，聊天面板 + 发送消息 + 显示回复（先不接工具）。
- **M3 工具层 + agent loop**：C# 工具注册 + function calling 闭环；观察用 ECS 查询，执行用原生 Tool 管线；暂停/快进控制。
- **M4 产品化**：API Key 设置、错误处理、任务汇报、玩家抢占/确认（HITL 式）、Paradox Mods 打包与商店页。
- **M5 迭代**：工具面扩充（财政/政策/公交/地形）、确定性规划层（action-plan-advisor 思路）等。

## 原则

1. 游戏侧胶水是 C#，UI 是 React/TS，agent runtime 保持可移植（浏览器可跑）。
2. 工具执行永远排队到模拟主线程，不在后台线程直接改 ECS。
3. 暂停是玩法时钟：冻结时思考，快进时观察。
4. 不把 API Key 或任何秘密写进仓库；只通过环境变量/运行时设置注入。
5. 发布前至少一次在真实 Windows 环境验证（转译层环境不算数）。
