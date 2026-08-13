# Tool surface audit & open-work inventory (2026-08-12)

**Status:** 2026-08-13 implementation reconciliation and bounded placement-planner
upgrade complete; controlled real-game acceptance remains open.
**Context:** the Windows verification machine is available again and `main` is clean
and pushed. The accepted product decisions now live in
`docs/adr/2026-08-13-agent-tool-surface-and-permissions.md`; they supersede the
alternatives captured during the original 2026-08-12 audit. This note retains the
investigation evidence and tracks the remaining live acceptance work.

## 1. 当前选址/放置工具面（三件套）

| 工具 | 语义 | 当前状态 |
| --- | --- | --- |
| `place_building(prefab, x, z, radius?)` | 一步搜索+提交：radius>0 时读取一次局部 ECS 快照，按 prefab flags 生成有界候选，做完整 footprint 的归属/旋转碰撞/普通陆地避水/临街/岸线/自动接管预检并稳定排序，只把一个 finalist 送进游戏原生管线验证并建造；radius 省略时精确放置 | 代码与构建已完成；主路径；待全新存档真机验收 |
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

### 2026-08-13 实现结果

- 三条 backend 路径已共用同一个内部 placement planner；外部
  `place_building(prefab, x, z, radius?, rotation?)` 接口没有增加参数。
- `Shoreline` 与 `RequireRoad` 作为独立能力组合；岸线候选来自真实水面 wet/dry
  transition，临街候选沿真实道路曲线和 lot depth 生成，不再把抽水站当名称特例。
- 普通候选使用自适应网格；内部 seed 上限 1024，位置/旋转去重，所有通过预检的
  pose 按中心距离、临街间隙和 connector 长度稳定排序。
- 预检覆盖完整旋转 footprint 的已购地、建筑 SAT 碰撞、道路折线碰撞和普通
  `OnGround` 水体采样；岸线、漂浮、悬空、水下、水道 prefab 不受普通避水规则误伤。
- 离路设施从 prefab 的开放连接 marker 判断水、污水或低压需求；先检测端点附近已有
  同 prefab 网络，否则才规划 150m 内通往道路的 connector。临街 prefab 不额外接管。
- 搜索失败汇总前三类主要拒绝原因；仍只有唯一 finalist 进入原生 Tool 状态机，避免
  已知的多候选 preview 卡死。
- 纯几何回归已覆盖旋转矩形相交/分离、长条 lot 的旧外接圆假阳性、道路折线碰撞和
  最近点投影；`dotnet build` 通过。抽水站、排水口、风机、水塔、垃圾场和普通临街
  建筑仍需玩家在全新存档完成真实 prefab/原生验证验收。

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
2. **科技树正向购买冷重载**：佩恩顿长期经营中已成功购买 `CrematoriumNode`、
   `HospitalNode`、`IncineratorPlantNode`，三次均真实扣除点数且同会话解锁生效；保存重载后
   仍需用 `get_progression` 复查持久化。（backlog §2）
3. **道路分级**：佩恩顿长期经营日志已证实 Agent 看见严重拥堵却没有升级道路或形成
   治理闭环；`list_roads` 的交通量/拥堵数据仍未与游戏 infoview 做精确对比。
   （backlog §4）
4. **道路替换**：只验过独立无主路段；交叉口/连续路/保存重载未验。该工具只在
   开发/验收模式暴露，且永远只支持 road-to-road。
   （backlog §3）
5. **填埋场扩容冷启动重载**：佩恩顿中已把非空、运行中的填埋场从 3,264 m² 扩至
   12,000 m²，再扩至 30,000 m²，容量与已有垃圾量均随之更新；保存重载仍需复查。
   当前约 110° 扇形并非最终产品目标，目标是尽可能覆盖“圆形最大范围减去其他障碍物”。
   （backlog §7）
6. **风车/污水连接端点埋深 −10m**：未做实体级验证。
   （placement-utilities handoff §4.3）

### B. 只有提案/调研，代码未写

7. **地图图片工具 `map_export`**：CS2MapView/Carto 调研完成，明确"本次未改
   代码"。（research/2026-08-11-cs2-map-image-mod.md）
8. **`place_specialized_industry` 组合接口**（hub+提取区一步）：设计建议后续做，
   无代码。（research/2026-08-11-tool-deepening-next-seams.md §3）
9. **UI 自动滚动 / Gameface CDP 打包为 MCP**：08-07 handoff 的可选项，未做。

**新增观测缺口：generation usage / KV cache。** timeline 当前没有记录
`UsageDetails.CachedInputTokenCount` 或 provider 的 cache hit/miss 计数，因而不能从累计
input tokens 推算实际成本或延迟。框架类型已经提供 cached-input 字段，但仍需验证各
provider/adapter 是否填充，并决定如何把标准字段与 `AdditionalCounts` 稳定写入 timeline。

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
错误跨回合保留，第二个玩家回合才可直接调用 `demolish`。`f4a5ed2` 已让每个新玩家
回合先 reset 工具组，并让 `agent_enable_tool_group` 返回经过视觉/科技树/拆除权限过滤的
真实工具名及“下一 generation 直接调用”的明确指令；工具说明同步强调该行为。
`dotnet build` 通过（0 error，15 条既有 ILRepack warning）。随后在另一张全新河谷三角洲
普通模式城市的 session `f5990708` 完成真机复验：回合 `aa4ca9a4` 先启用 construction，
下一 generation 立即调用 `demolish(2147483647, 2147483647)` 并稳定得到
`kind=not_found`；新玩家回合 `e0dcfacc` 在禁止重新启用工具组后看不到 `demolish`，没有
产生 function 事件。`agent-timeline-f5990708.jsonl` 完整记录两个回合、两次首回合 function
与各自 `turn.finish`；验收过程中一度看到空文件，但后续同一路径已实时出现完整事件，
没有形成可复现的 observability 产品故障。该验收城未发生有效实体写入，对应
`13-August-05-09-28.cok` / `.cid` 已精确移入 Windows 回收站，不再复用。

该轮仍出现一项独立红灯：`Cities2` 私有内存从约 31 GB 持续增至 92.8 GB，系统可用
物理内存降至约 1.4 GB；向主窗口发出正常关闭后，进程超过 75 秒仍无响应，只能精确
终止该 `Cities2` PID。此时 CDP 已使用共享单连接 helper，且本轮只做少量、最小返回值
评估，因此不能直接沿用“旧 CDP helper 泄漏”作为根因。长期自主经营前必须用冷启动
基线区分空闲模拟、Agent generation、timeline 写入与 CDP 评估各自的内存增量，并保证
正常退出不再卡死。

#### 本轮排水口排查新增的代码问题

截至 2026-08-13，第一轮修复已由 `2bc07d7` 提交并推送；本轮又把候选生成、能力读取、
完整 footprint 预检、自动接管计划和排序收敛进一个内部 placement planner。风机低压
自动连接曾在全新存档完成真机验收；本轮算法升级仍需按各自清单复验：

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

### E. 2026-08-13 长期自主经营日志审计（佩恩顿）

本节审计活跃日志
`C:\Users\super\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\CitiesSkylines2Agent\logs\agent-timeline-25e2ef2e.jsonl`。
为避免一边审计、一边继续经营导致统计口径漂移，以下数量统一冻结在本地时间
2026-08-13 15:55:08（`seq=1553`）：1,553 个事件、872 次 function、433 次 generation、
70 次 error；最近一次城市快照人口 17,169，此前峰值 18,728，峰值后曾回落约 9%。
这张城随后仍在继续运行，故本文是该冻结点的审计，不声称是文件终态。

#### 长时间运行与网络中断

- 游戏连续运行数小时后，`Cities2` 工作集约 5.82 GB；该现场证据不支持“游戏或 mod
  存在持续内存泄漏”的结论。约 11.59 GB 的 Private Bytes 也不能直接解释为物理常驻
  内存。上文短时验收曾出现的 31→92.8 GB 私有内存异常仍保留为历史事实，但长期自主
  经营证据已推翻它作为当前产品阻塞项的判断；RDP、断开连接或锁屏是否影响该异常也未
  证实，无需再做 Steam 文件完整性修复。
- 12:14:55 出现一次 `Expecting chunk trailer`。13:07:03–13:29:49 又有 69 次
  TLS/网络失败，形成 68 个“0 generation / 0 function”的空回合，约每 20 秒重试一次，
  没有修改城市。日志只记录模型名 `deepseek-v4-flash`，不能据此确定 API 服务商切换时刻。
- 截至冻结点，433 次 generation 的逻辑 input tokens 累计约 105,769,482，中位数约
  237k，单次最大 435,167。这只能证明上下文持续膨胀以及模型需要在更长历史中维持注意力，
  不能直接推导实际费用或首 token 延迟：两者还取决于服务商的 KV/prompt cache 命中率、
  命中/未命中的计价和实现。当前 timeline 的每条 `usage` 都只有 `input/output/total`，而
  `AgentLoop.EmitGeneration` 也只序列化这三个字段，因此本次日志无法计算 cache hit/miss
  tokens。补齐该可观测性后才能评价真实成本；长期目标、人口下跌和交通治理被近期建设
  噪声淹没，则仍是从行为结果可直接观察到的自主性缺口。

#### 工具调用总数与失败分布

872 次调用中 722 次成功、150 次失败。失败中 29 次只是当前回合没有启用对应工具组；
扣除后有 121 次参数、选址或游戏原生验证失败。该区分很重要：前者暴露工具面使用问题，
但不应计作建造算法本身失败。

| 工具 | 总数 | 成功 | 失败 | 未启用 | 实际失败 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `build_road` | 137 | 102 | 35 | 2 | 33 |
| `place_building` | 118 | 31 | 87 | 5 | 82 |
| `zone_rectangle` | 97 | 92 | 5 | 3 | 2 |
| `notifications` | 50 | 50 | 0 | 0 | 0 |
| `city_overview` | 49 | 49 | 0 | 0 | 0 |
| `wait_simulation` | 48 | 48 | 0 | 0 | 0 |
| `demolish` | 38 | 30 | 8 | 4 | 4 |
| `budget` | 37 | 37 | 0 | 0 | 0 |
| `demand` | 37 | 37 | 0 | 0 | 0 |
| `city_services` | 33 | 33 | 0 | 0 | 0 |
| `find_prefabs` | 25 | 21 | 4 | 4 | 0 |
| `list_buildings` | 23 | 23 | 0 | 0 | 0 |
| `agent_enable_tool_group` | 23 | 23 | 0 | 0 | 0 |
| `terrain` | 23 | 23 | 0 | 0 | 0 |
| `buy_tiles` | 21 | 19 | 2 | 2 | 0 |
| `list_roads` | 20 | 20 | 0 | 0 | 0 |
| `zoning` | 19 | 17 | 2 | 2 | 0 |
| `labor` | 13 | 13 | 0 | 0 | 0 |
| `set_service_budget` | 8 | 7 | 1 | 1 | 0 |
| `set_tax` | 7 | 6 | 1 | 1 | 0 |
| `list_zones` | 6 | 4 | 2 | 2 | 0 |
| `list_tiles` | 5 | 4 | 1 | 1 | 0 |
| `inspect` | 4 | 4 | 0 | 0 | 0 |
| `get_progression` | 3 | 2 | 1 | 1 | 0 |
| `set_fee` | 3 | 3 | 0 | 0 | 0 |
| `expand_operational_area` | 3 | 2 | 1 | 1 | 0 |
| `purchase_development_node` | 3 | 3 | 0 | 0 | 0 |
| `get_operational_area` | 3 | 3 | 0 | 0 | 0 |
| `statistics` | 2 | 2 | 0 | 0 | 0 |
| `zone_area` | 2 | 2 | 0 | 0 | 0 |
| `get_taxes` | 2 | 2 | 0 | 0 | 0 |
| `agent_read_skill` | 2 | 2 | 0 | 0 | 0 |
| `service_budgets` | 2 | 2 | 0 | 0 | 0 |
| 其余 6 个只读工具 | 各 1 | 各 1 | 0 | 0 | 0 |

高频反复失败集中在少数基础设施：`WaterPumpingStation01` 19 次尝试仅 1 次成功；
`WindTurbine01` 26 次中 5 次成功、18 次实际失败、3 次未启用；`Landfill01` 16 次中
2 次成功、13 次实际失败、1 次未启用；`IncinerationPlant01` 10 次中没有成功，9 次实际
失败、1 次未启用。其中一个回合连续 13 次尝试抽水站且全部失败。`place_building` 的实际
失败是 63 次搜索耗尽、16 次碰撞和 3 次越界；`build_road` 是 26 次碰撞、2 次明确
`InWater`、3 次越界和 2 次 `InvalidShape`。这里不只是正常探索成本：失败响应没有转化为
新的空间约束，Agent 会在相邻点重复同一种策略。

#### 水电接入：施工成功不等于最终供给

需要把“建筑成功放置”“自动连接施工成功”“建筑最终获得供给”分开判断：

- 排水口有一次成功放置并明确自动修建污水管。地表抽水站的问题发生在
  `RequireRoad + Shoreline` 的联合选址阶段，不应误判为自动拉水管失败。本轮 planner
  已改为从真实 wet/dry transition 生成岸线候选，并优先保留能到达附近真实道路曲线的
  pose，再统一验证临街距离；能否消除抽水站反复耗尽仍待全新存档验收。
- 水塔属于离路设施，两次成功放置都由 `place_building` 自动接水管。初期发电容量存在，
  但 `fulfilledConsumption` 并未立刻完整满足；Agent 后来手工用两次 `build_road` 拉了低压线。
- 当前能力解析只选择一种 utility（污水→水→低压优先），同一离路设施若声明两种连接需求，
  可能遗漏第二种。`RequireRoad == true` 的建筑则跳过显式自动连接，依赖临街道路自带网络。
- 城市后来恢复到约 93k 水容量对 27.7k 消耗，电力和污水也已满足。这说明初期接入故障
  最终得到补救，但不能据此把放置/连接链路判定为可靠。

#### 遗留水管、电缆缺少可观察和可清理接口

Agent 曾尝试拆除两条孤立低压线，但 `demolish` 只接受建筑和道路，`list_roads` 也不列出
水管或电缆；当前工具面既不能完整查询这些网络，也不能拆除它们。模型随后长期把 5 个
警告称为 “harmless legacy”，直到 17k 人口仍存在。后续设计需要同时决定通用网络查询/
拆除能力，以及自动连接链路部分成功时是否事务回滚，不能只靠 prompt 要求 Agent 记得清理。

#### 交通：看见拥堵，却没有治理闭环或道路分级

- 约 5k 人口时已有拥堵；12:30 的 `list_roads` 读到主干流速约 20%、
  `congestionIndex≈269`。15:16 已有 15 个交通瓶颈，模型也明确把交通称为 “main concern”，
  却继续买地和分区。人口从 18,728 一度降至 17,018，Agent 也没有认真诊断下降原因。
- 20 次 `list_roads` 没有一次按 `congestion` 或 `traffic_volume` 排序；137 次
  `build_road` 中 131 次使用 `Small Road`，没有中型或大型道路，0 次曲线道路，131 次为
  轴对齐直线，6 次斜直线主要是管线；23 段道路超过工具建议的 250m。
- 0 次 `set_road_features`、0 次 `replace_road_type`。因此根因不仅是没有地图全局视野，
  也是策略层没有形成“发现瓶颈 → 选择治理手段 → 施工 → 重新测量”的闭环。

#### 地图缺口与单向扩张

- 19 次成功购地中，12 块形成 `gridX=13→18`、`gridZ=10/11` 的连续同向成对推进；
  Agent 只有 5 次 `list_tiles`，却调用 21 次 `buy_tiles`。道路从初始区域一直向
  `x≈3200` 延伸，符合玩家观察到的城市长期朝一个方向生长。
- 空间观测总计 23 次 `terrain`、1 次 `gridmap`、0 次截图。普通道路多次修入水中，也没有
  沿河岸建立平行道路。给模型直接增加整张栅格或截图未必能解决上下文和路线规划问题；
  待决策方案应区分“战略层的压缩全局地图摘要”和“道路 module 内部的避水、岸线切线与
  路线评分”。

#### 公交是能力缺口，不只是模型遗漏

当前 Agent 只能搜索和放置 `transport` 类建筑，没有创建站点、线路或车辆路线的写工具。
因此它没有用公交缓解拥堵，主要是工具能力缺口；在补齐线路能力前，不能把这项行为完全
归咎于模型策略。

#### 填埋场扩容证据与剩余几何问题

佩恩顿中的填埋场初始面积 3,264 m²、容量 51,000、已有垃圾 28,618；第一次扩至
12,000 m² 后容量为 187,501，第二次扩至 30,000 m² 后容量为 468,751、已有垃圾
129,155。由此可以补签“非空且运行中的 operational area 扩容”，但冷重载仍未验。
当前实现固定 `halfAngle=55°`（总角约 110°），skew 只有 `0, ±15, ±30`，所以天然只能
生成扇形。已接受的产品目标是尽可能接近圆形最大可扩展范围并扣除障碍；实现前仍需查清
原生区域是否支持凹多边形、孔洞，以及 16 节点限制下怎样优雅退化。

#### 这张存档能补签和不能补签的验收

当前自动存档为
`C:\Users\super\AppData\LocalLow\Colossal Order\Cities Skylines II\Saves\76561198152466558\13-August-15-42-04.cok`。

- 可补签：三项科技节点真实购买并在同会话生效；非空运行中填埋场扩容；
  `zone_rectangle` 基本路径有大量真实成功。
- 可继续只读/冷重载复查：科技树持久化、填埋场区域持久化，以及 `list_roads` 与拥堵
  infoview 的精确对比。
- 不能补签：专门工业完整闭环、道路替换、风机/排水口端点 y=−10、权限矩阵、
  `list_objects` 半径过滤；`zone_rectangle` 的边界与误覆盖也未系统验收。
- 该存档可作为本轮问题证据和冷重载复查材料，但正式修复后的最终验收仍必须使用全新
  存档；不能拿它替代用户已经要求的修复后新存档验收。

### F. 已解除的机器/环境阻塞

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
- Mod/Agent/AgentLoop.cs（generation usage / timeline 序列化）
- Mod/Agent/ToolCatalog.json（工具 schema 与描述）
