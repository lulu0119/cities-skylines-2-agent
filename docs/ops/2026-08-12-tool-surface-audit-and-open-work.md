# Tool surface audit & open-work inventory (2026-08-12)

**Status:** 2026-08-13 implementation reconciliation complete; controlled real-game
acceptance remains open.
**Context:** the Windows verification machine is available again and `main` is clean
and pushed. The accepted product decisions now live in
`docs/adr/2026-08-13-agent-tool-surface-and-permissions.md`; they supersede the
alternatives captured during the original 2026-08-12 audit. This note retains the
investigation evidence and tracks the remaining live acceptance work.

## 1. 当前选址/放置工具面（三件套）

| 工具 | 语义 | 当前状态 |
| --- | --- | --- |
| `place_building(prefab, x, z, radius?)` | 一步搜索+提交：radius>0 时在 mod 侧做启发式网格搜索（归属/重叠/临街/海岸线），选一个候选直接进游戏原生管线验证并建造；radius 省略时精确放置 | 主路径；prompt 与 skill 均推荐 |
| `find_infrastructure_candidate(role, x?, z?, radius?)` | 旧只读规划器：沿现有道路两侧生成候选，最终只把一个位置送原生预览 | 已退出模型面；backend 暂留作兼容/诊断实现，不再是真机产品验收项 |
| `find_placement(prefab, x, z, attempts=1)` | 旧预览工具：`attempts` 被硬性 clamp 为 1，只对单点做原生 probe，不提交 | 已退出模型面；backend 暂留作兼容/诊断实现；AGENTS.md 已改为通过 `place_building` 的 radius/rotation 恢复 |

`find_infrastructure_candidate` 支持的角色（代码与 catalog 一致）：

`power`, `water`, `sewage`, `garbage`, `healthcare`, `fire`, `police`,
`education`, `transport`, `post`, `telecom`

它**不支持** `specialized-industry`（特色农场/提取业）、地标/独特建筑、
普通分区建筑（`zone_area`）、道路、树木。找农场仍需
`find_prefabs(role=specialized-industry)` + 手动 `place_building`。

## 2. "candidate 却只回一个" 的问题

### 为什么现在只有一个 finalist

硬 bug（见 placement-utilities handoff §5）：被拒绝的原生预览会让游戏把活动
Tool 切回默认并禁用 `BridgeToolSystem`，多候选 probe 会卡死状态机。因此实现
刻意收敛为：mod 侧启发式生成/预检多个位置，但**只把排序后的第一名**送原生
预览（`TryQueueInfrastructureCandidate → TryQueueProbe` 单点）。

### 语义矛盾

名字叫 "candidate"（候选），行为却永远只返回一个推荐点：这实际是
"recommendation / recommended site"，不是让调用者做选择。既然只有一个，
下一步很自然会问"为什么不直接放"。

### 2026-08-13 已接受设计

不做模型可见的多候选池，也不让写工具接受 `role`。Agent 先通过 prefab 查询选择
具体 prefab，再调用 `place_building(prefab, x, z, radius?, rotation?)`。常规自主
放置提供期望中心和合理 radius、默认省略 rotation；module 内部负责候选搜索、
prefab 能力解析、排序，并且只把唯一 finalist 送进原生验证/提交状态机。
`find_placement` 与 `find_infrastructure_candidate` 均退出模型面。

## 3. 全量工具组合/分离审计（2026-08-12）

### 判断原则（修订）

- `find_*` = **单结果**：如果结果没有独立价值、只能喂给下一步 → 合并或
  下线；如果调用者需要比较（尤其接视觉）→ 应返回 top-N，而不是 1。
- `list_*` = **多结果**：调用者的任务是"看列表 → 选择 → 执行"，读工具
  保留，写工具单独存在；写工具参数放宽（自然名/坐标），但**不合并**。
- 读工具有独立信息价值（观测/证据）→ 保留。
- 命名：`find_` 前缀只留给单结果（或唯一推荐）；多结果查询用
  `list_` / `search_`，避免 `find_prefabs` 这种"多结果却叫 find"的歧义。

### A. 单结果 find 工具（只有 2 个，处理方式不同）

| 工具 | 问题 | 建议 |
| --- | --- | --- |
| `find_placement` | 单点原生预览，无选择、无独立信息价值；catalog 自己已写 prefer place_building | 模型面先下线；HTTP `/build/find-place` 保留为诊断。若未来要做"任意 prefab 的多候选选择"（非 role 限定），可重构为多结果 `find_placement(prefab, x, z, radius, limit)` 后再考虑保留 |
| `find_infrastructure_candidate` | 目前只回 1 个；视觉工作流需要"小范围比较" | 重构为小范围 top-N（建议默认 3）：位置/朝向/证据/预检结果，**不做逐候选原生预览**；`place_building` 提交所选。多结果实现后保留；维持单结果则不单独成工具 |

### B. 选择型读-写（list/多结果 → 保留分离，只放宽参数）

| 读 → 写 | 为什么保留分离 | 参数放宽建议 |
| --- | --- | --- |
| `list_buildings` → `inspect` / `demolish` / `get_operational_area` / `expand_operational_area` | 多建筑要选，且读后动作不同（看/拆/查区域/扩建） | 写端接受自然标识（role/名称/位置/最近者）内部解析；index+version 继续可用 |
| `list_roads` → `set_road_features` / `replace_road_type` / `demolish` | 多路段要选，读后动作不同（装饰/换型/拆） | 写端按位置/查询条件解析 |
| `list_tiles` → `buy_tiles` | 多块地要选（owned/available/价格/位置），选择是真实决策 | `buy_tiles` 收 gridX/gridZ 即可，不依赖 index |
| `list_districts` → `district_policies` / `set_district_policy` | 多区域要选 | `set_district_policy` 收 district 名称/位置；`district_policies` 保留读 |
| `list_zones` → `zone_area` / `zone_rectangle` | 多类型要选 | 保留现状 |
| `find_prefabs`（拟改名 `search_prefabs` / `list_prefabs`）→ `place_building` / `build_road` | 名字是 find 但实际多结果（最多 200），要选 prefab | 改名避免与单结果 find 混淆；保留旧名作兼容别名（先例：`upgrade_road`） |
| `get_progression` → `purchase_development_node` | 多节点要选 | `purchase` 接受子串/服务过滤 |
| `policies` → `set_policy` | 多策略要选 | `set_policy` 接受显示名/子串 |
| `service_budgets` → `set_service_budget` | 多服务要选 | `set_service_budget` 接受显示名/子串 |
| `get_fees` → `set_fee` | 多费率要选 | `set_fee` 接受显示名/子串 |
| `get_taxes` → `set_tax` | 固定 4 个 area，rate 内部钳制 | 保留现状 |
| `get_loan` → `set_loan` | amount 是策略选择，clamp 兜底 | 保留现状 |

### C. 软组合：不是强链，但可提供便捷入口

| 链 | 说明 |
| --- | --- |
| `set_camera` → `screenshot` | screenshot 不依赖 set_camera 的输出；如想"直接看某处"，可给 screenshot 加 camera 参数一次完成 |

### D. 独立工具（无组合/分离问题）

`ping`, `game_state`, `city_overview`, `demand`, `wait_simulation`,
`budget`, `city_services`, `labor`, `statistics`, `notifications`,
`terrain`, `gridmap`, `zoning`, `zone_area`, `zone_rectangle`,
`build_road`, `place_building`, `get_operational_area`, `list_objects`,
`save_game`, `create_district`, `debug_zone_blocks`, `screenshot`,
`get_camera`, `set_camera`，以及 5 个 meta 工具
（`agent_enable_tool_group` / `agent_list_context_blocks` /
`agent_read_skill` / `agent_add_context_block` /
`agent_remove_context_block`）。

### E. 非模型面工具

- `upgrade_road`：HTTP 兼容别名，未暴露给模型；保留即可。
- `find_placement`：模型面下线后，HTTP `/build/find-place` 可保留为诊断。

### F. 命名与重构决定（2026-08-13 对账）

- `find_prefabs` 以及道路 construction/features/replacement 的命名和介绍以后做一次
  一致性整理；当前不为改名扩大验收范围。
- `find_placement` 与 `find_infrastructure_candidate` 已从模型面下线；不做多候选版。
- 兼容 backend 暂留是 implementation detail，不构成 Agent 接口承诺。

## 4. 未完成工作清单

### A. 代码已提交、构建通过，但真机验收未完成

1. **专门工业/资源提取闭环**：`expand_operational_area` 已支持提取器+资源打分
   （耕地/矿石/石油/渔业）+ 森林实体扫描，但"建 hub → 画提取区 → 真出车出产"
   整条链路从未在游戏里跑通。（backlog §6）
2. **科技树正向购买**：只验过"没点数时拒绝"；未验"攒够点数真买一个节点"。
   （backlog §2）
3. **道路分级**：`list_roads` 交通量/拥堵排序未与游戏 infoview 对比。
   （backlog §4）
4. **道路替换**：只验过独立无主路段；交叉口/连续路/保存重载未验。该工具只在
   开发/验收模式暴露，且永远只支持 road-to-road。
   （backlog §3）
5. **填埋场扇形扩容冷启动重载**：未验收；非空填埋场+卡车运转未验。
   （backlog §7）
6. **风车/污水连接端点埋深 −10m**：未做实体级验证。
   （placement-utilities handoff §4.3）

### B. 只有提案/调研，代码未写

7. **地图图片工具 `map_export`**：CS2MapView/Carto 调研完成，明确"本次未改
   代码"。（research/2026-08-11-cs2-map-image-mod.md）
8. **`place_specialized_industry` 组合接口**（hub+提取区一步）：设计建议后续做，
   无代码。（research/2026-08-11-tool-deepening-next-seams.md §3）
9. **UI 自动滚动 / Gameface CDP 打包为 MCP**：08-07 handoff 的可选项，未做。

### C. 遗留未清理

10. `list_objects` 半径过滤：08-06/08-07 均标 "suspect / open"，后续无确认。
11. UI.log 无效 CSS `display` 警告未清理。
12. "先建路再放建筑"是否已日志级根除：skill 已加规则，但无确认证据。

以下遗留已经清理：`find_placement`/`find_infrastructure_candidate` 已退出模型面；
`save_game`、`replace_road_type`、`debug_zone_blocks` 只在默认关闭的开发/验收模式
暴露；`Debug548a1a` 已删除；运行数据已迁到 `ModsData`；进程内 bridge 已用
`Success + BridgeErrorKind` 取代 HTTP 状态码。

2026-08-13 冷启动还发现：游戏会把用户数据树中任意目录下的 hot-reload `.dll`
递归识别为 code mod，仅把目录改名为 disabled 并不能禁用。handler payload 因此改用
`RequestHandlers.payload`，继续由 host 通过 `Assembly.Load(byte[])` 显式加载；构建会
移除新 hot-reload 目录里的旧 `.dll`。历史开发目录由验收机手动移出用户数据树，
不在产品中增加尚未发布数据的迁移逻辑。

同一机器随后完成生产内置 adapter 冷启动复验：`Modding.log` 只加载
`CitiesSkylines2Agent-merged-b5311cae…`，`CitiesSkylines2Agent.HotReload` 加载次数
从 2 降为 0；用户数据树中的 hot-reload `.dll` 数量为 0，正式 override 目录由 host
重建为空目录。该轮尚停留在 MainMenu，没有载入旧城市。

首次受控新城设置验收发现 Visual tools 的值虽然是 `Auto`，Gameface 却显示完整的
未解析 locale key。`ea820bc` 去掉属性名 `.VisionTools.` 后，第一次冷启动复验仍失败：
手工拼接遗漏了 `ModSetting.id` 中游戏生成的完整设置类型标识。当前 `Game.dll` 的
权威实现会通过 `GetEnumValueLocaleID(value)` 生成
`Options.{id}.{EnumType.ToUpper()}[{value}]`；三项枚举现改为直接调用该 helper，避免
复制游戏内部 key 规则。2026-08-13 随后从 Steam → Paradox Launcher 正式冷启动，
设置页下拉菜单同时正确显示 `Auto` / `On` / `Off`；依次选择 `On`、`Off` 后控件文本
均正确，最终恢复为 `Auto`。该本地化 bug 真机验收通过。

同日继续验收时确认旧 Gameface CDP helper 存在验收脚本自身的资源泄漏：每条表达式
都会新建 inspector WebSocket、启用完整 `Runtime` 域，并在未等待关闭握手时退出。
反复调用后 `Cities2` 私有内存增长到约 105–181 GB，物理可用内存降到约 1.7 GB，
CDP 与游戏窗口失去响应；Agent session 日志在挂起前没有未解释的写工具。两张仅做
只读/设置门控的全新受控城市因此作废，最后一组自动存档及 `.cid` 均精确移入 Windows
回收站，后续不复用。

CDP 脚本现共用一个连接 lifecycle module：不启用完整 Runtime 事件域，批量表达式
复用同一连接，每次评估使用并释放独立 object group，退出前等待 WebSocket 关闭握手。
主菜单冷启动回归基线为 9.442 GB 私有内存；单连接连续 50 次小查询后为 9.880 GB，
第二批 50 次仅再增长 103.5 MB；随后 10 个独立连接全部成功，仅增长 113.7 MB，
线程数保持 158、句柄数回到 2816、窗口持续响应。后续真机验收必须优先用
`CDP_EXPRESSIONS` / `CDP_REPEAT` 在一个连接内完成多步操作，并禁止返回整棵 DOM 的
重复文本。

同一轮受控权限验收随后发现所有接收外部 `index/version` 的实体工具存在共同输入边界
缺口：`demolish(2147483647, 2147483647)` 和只读 `inspect` 都把不存在目标返回成
`NullReferenceException` / `kind=internal`，而不是稳定的 `not_found`。根因是 Unity
Entities 1.3 的 `EntityManager.Exists` 只接受已处于实体存储理论索引范围内的
`Entity.Index`；它不是面向不可信请求整数的安全解析接口。修复集中新增一个私有实体
resolver，在调用 `Exists` 前按原生分块存储上限拒绝非法索引，并由 `inspect`、`demolish`、
道路特征/替换、行政区解析和 operational-area 读写共 7 个入口复用。`dotnet build`
通过（0 error，15 条既有 ILRepack warning）。`d262512` 推送后已作废原受控城及其
`13-August-03-55-49.cok` / `.cid` 自动存档，并经正式 Launcher 路径创建另一张
`purpose NewGame` 的河谷三角洲普通模式城市（解锁全部、无限资金、解锁地图区块均关闭）。
新 session `ba2496ad` 中，同一极端 ID 的 `demolish` 与只读 `inspect` 均稳定返回
`kind=not_found`，没有实体写入，公共 resolver 真机验收通过。

该复验同时发现工具组的回合生命周期不符合接口承诺：第一轮调用
`agent_enable_tool_group(construction)` 成功后，下一次 generation 虽已携带扩展后的工具
定义（输入 token 明显增加），模型仍沿用首次 generation 的“没有 demolish”判断并结束回合；
而 `AgentToolSurface.m_EnabledGroups` 又没有在下一玩家回合开始时清空，使 construction
错误跨回合保留，第二个玩家回合才可直接调用 `demolish`。待修复不变量是：启用结果必须
明确要求模型在下一轮使用新增工具，同时每个新玩家回合先 reset 工具组，确保
“Enable a specialized tool group for the current turn” 与真实生命周期一致。

#### 本轮排水口排查新增的代码问题

截至 2026-08-12，这些问题的修复已由 `2bc07d7` 提交并推送；`Mod` 构建通过。
其中风机低压自动连接已在全新存档完成真机验收，其余放置改动仍按各自清单验收：

- **建造 handler 泄漏了过多实现知识。** `RequestHandlers.Build.cs` 同时承担参数
  解析、prefab 查找与分类、候选生成、临路/岸线规则、原生验证和错误文案映射，
  使 `PlaceBuilding`、`FindPlacement`、`FindInfrastructureCandidate` 出现大量重复
  `if/else`。问题不是条件分支本身，而是同一组放置知识散落在多个调用点。应把
  seam 收敛到一个内部的“prefab 能力解析 + 放置解析”module，handler 只负责
  调用并序列化统一结果，避免再拆出一批浅 module。
- **HTTP 状态码泄漏进了进程内接口。** `BridgeResponse` 的 `400` / `404` / `409` /
  `500` 来自旧 HTTP bridge；当前 Agent 主路径通过 `BridgeSystem.InvokeAsync` 进程内
  调用，实际上只判断 `Status == 200` 或失败。内部接口因此携带了无用的传输层知识。
  后续应先盘点兼容 HTTP 路由的真实调用方，再让内部 module 返回类型化结果，只在
  HTTP adapter 的 seam 上映射状态码。
- **建筑放置能力判断重复且有错误。** 当前实现会把 `ServiceUpgradeData` 升级件当作
  可独立建筑、把所有 `BuildingData` 当作必须临路、以“建筑中心干燥 +
  `HasWaterBehindBuilding`”近似原生岸线吸附，并把所有 `InWater` 都解释为需要桥梁。
  这会让污水候选选中升级件，并错误地强迫排水口贴路或把道路修进水里。能力解析应
  集中读取 `ServiceUpgradeData`、`BuildingFlags.RequireRoad`，岸线建筑统一复用原生
  `SnapShoreline` / `CheckSurface` 语义；桥梁提示只能用于 `OperationKind.Net`。
- **自动管线连接从建筑中心起线。** 放置后的连接队列虽然保存了 connector start，
  实际施工却重新使用建筑中心，导致风机的低压电缆被原生验证判为
  `OverlapExisting`。本地修改已将自动连接限定为“不要求临路且声明水、污水或低压
  节点”的 prefab，并从 prefab 原生开放 `SubNet` marker node 起线；高压不在当前
  自动连接范围内。
- **风机能力汇总 flag 不完整。** `WindTurbine03` 的 `BuildingFlags` 没有
  `HasLowVoltageNode`，但 prefab 的开放 `SubNet` 中存在可吸附的低压 marker。只读
  汇总 flag 会把它误判为没有自动连接能力。能力解析现先读 `BuildingFlags`，仅对
  `RequireRoad == false` 且汇总 flag 未声明连接的 prefab，再以开放 `SubNet` marker
  作通用 fallback；水、污水和 `Voltage.Low` 都走同一规范，没有风机名称特例，
  `Voltage.High` 明确不接受。`RequireRoad == true` 的临路建筑由道路自带网络服务，
  不生成额外自动连接。

#### 风机低压自动连接真机验收（2026-08-12）

- 环境：关闭旧城市且未保存，从主菜单明确选择“新游戏 → 简单 → 山间航路”；
  `SceneFlow.log` 记录 `Loading mode Game with purpose NewGame`，未使用继续或载入。
- 约束：先中断自动恢复的“持续经营城市”任务；验收回合只允许只读选点以及一次
  `place_building`，禁止 `build_road`、高压、其他建筑、分区、拆除和保存。
- 操作：`WindTurbine03` 放在 `(-660, 30)`，距既有 Medium Road 约 21m。
- 结果：`placed: true`、`connected: true`，`connection.prefab` 为
  `Low-voltage Ground Cable`，终点 `(-666.6, 8.8)`；无 `connectionError`。
- 写入审计：验收回合唯一写入是一次 `place_building`；17:46:56 的执行日志记录
  该调用成功，本轮没有 `build_road`，没有补救施工，Agent 也没有调用
  `save_game`。游戏曾按自身计时器生成 `12-August-17-49-28` 自动存档；退出后已将
  对应 `.cok` / `.cid` 精确移入 Windows 回收站，因此未保留本次验收存档。
- 结论：marker fallback 已在真实 prefab、真实原生放置与自动施工链路上通过；
  风机的低压地缆由 `place_building` 自动排队施工，不是 Agent 手动接线。

### D. 已决策但仍需后续实现或数据校准

13. 提取区第一版拒绝零资源候选并按剩余资源证据排序；真实阈值以后用真机数据校准，
    不向玩家暴露设置。
14. operational area 不支持缩小，且不作为活跃 TODO。
15. 道路工具统一命名与介绍是后续整理项；当前保留 `set_road_features`。
16. 地图图片能力与可选 Python/Carto adapter 均为长期工作，不阻塞本轮验收。

### E. 已解除的机器/环境阻塞

显示链路已经恢复；2026-08-13 已确认 Windows 工作区干净并与 `origin/main` 同步。
若显示驱动再次出现 `0x133 DPC_WATCHDOG_VIOLATION`，该轮不能计为完成真机验收。

## 5. 建议恢复顺序

1. 完成模型工具面、catalog、skill、ADR/ops 的最终对账并构建、提交、推送。
2. 消除 stale hot-reload payload 覆盖风险。
3. 第一张全新存档做受控功能验收；发现的 bug 每项独立修复、构建、提交、推送、复验。
4. 功能清单全部通过后，第二张全新存档做长期自主经营；人口过万后重点记录 Agent
   如何读取和处理交通拥堵，不提供人工指定的交通方案。

## 6. 依据

- docs/ops/2026-08-10-gameplay-capability-backlog.md
- docs/ops/2026-08-10-placement-utilities-handoff.md
- docs/research/2026-08-11-tool-deepening-next-seams.md
- docs/research/2026-08-11-cs2-map-image-mod.md
- Mod/CS2MCP/RequestHandlers.Build.cs（`PlaceBuilding` / `FindPlacement` /
  `FindInfrastructureCandidate` / `kInfrastructureCandidateRoles`）
- Mod/CS2MCP/BridgeToolSystem.cs（`TryQueueProbe` /
  `TryQueueInfrastructureCandidate`）
- Mod/Agent/AgentToolSurface.cs（construction 组 / core tools）
- Mod/Agent/ToolCatalog.json（工具 schema 与描述）
