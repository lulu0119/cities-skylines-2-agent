# Tool surface audit & open-work inventory (2026-08-12)

**Status:** read-only audit; no code changed.
**Context:** the Windows verification machine is down (no display signal; last
documented crash `0x133 DPC_WATCHDOG_VIOLATION` after switching Cities II to the
RTX 3060 while the GameViewer virtual display remained active). Before resuming
any real-game acceptance, this note fixes one authoritative inventory of
unfinished work and stale tool-surface items. Old docs keep their historical
status; this note is the current open list.

## 1. 当前选址/放置工具面（三件套）

| 工具 | 语义 | 当前状态 |
| --- | --- | --- |
| `place_building(prefab, x, z, radius?)` | 一步搜索+提交：radius>0 时在 mod 侧做启发式网格搜索（归属/重叠/临街/海岸线），选一个候选直接进游戏原生管线验证并建造；radius 省略时精确放置 | 主路径；prompt 与 skill 均推荐 |
| `find_infrastructure_candidate(role, x?, z?, radius?)` | 只读规划器：仅 11 种基础设施/服务角色，沿现有道路两侧生成候选→预检→按造价/距离/名字排序→把**唯一 finalist** 送原生预览；返回 prefab/position/rotation 供 `place_building` 提交 | 已实现、构建通过；真机验收 pending；命名与 "candidate" 语义不一致 |
| `find_placement(prefab, x, z, attempts=1)` | 旧预览工具：`attempts` 被硬性 clamp 为 1，只对单点做原生 probe，不提交 | 模型面已无引导使用；catalog 描述自称 prefer `place_building`；仍暴露在 construction 组、HTTP `/build/find-place`；AGENTS.md 仍有过时引用 |

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

### 可选设计（待决策，未实现）

| 方案 | 做法 | 代价/收益 |
| --- | --- | --- |
| A. 改名/明示单推荐 | 改为 `recommend_infrastructure_site`，文档说明"返回唯一推荐点，用 place_building 提交" | 最小改动；保留只读预检与原生预览边界 |
| B. 返回 top-N 启发式候选 | 只读返回若干候选+证据，不逐个做原生预览；`place_building` 提交所选 | 恢复 "candidate" 字面含义；不触碰多 probe 卡死；需要定义 N 与排序证据 |
| C. 合并进 place_building | `place_building(role=...)` 直接选点+一步提交，删除两步 | 调用面最小；丢失"先看后建"的只读预检与证据，且与现有验收习惯冲突 |

用户修正后的立场：合并只适用于 `find_placement`（单点、无选择、无独立
信息价值）。`find_infrastructure_candidate` 不应并入 `place_building`：
后续要接视觉模型，希望模型能指定一个小范围、拿到若干候选并用截图比较，
再决定提交哪个。因此方案 B（小范围 top-N 只读候选 + `place_building`
提交所选）是当前方向，方案 C 不再推荐。

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

### F. 命名与重构决定（用户确认，代码改动待后续）

- `find_prefabs` → 建议改名为 `search_prefabs` 或 `list_prefabs`（二选一待定），
  旧名保留为兼容别名。
- `find_placement` → 模型面下线；未来是否以"多候选版"回归，之后再定。
- `find_infrastructure_candidate` → 多结果（top-N）实现后保留；单结果现状下
  不作为独立工具存在。角色扩展（如 `specialized-industry`）可后续考虑。

## 4. 未完成工作清单

### A. 代码已提交、构建通过，但真机验收未完成

1. **专门工业/资源提取闭环**：`expand_operational_area` 已支持提取器+资源打分
   （耕地/矿石/石油/渔业）+ 森林实体扫描，但"建 hub → 画提取区 → 真出车出产"
   整条链路从未在游戏里跑通。（backlog §6）
2. **`find_infrastructure_candidate` 真机验收**：构建通过、单点原生预览机制已
   确认，live acceptance pending。（backlog §3）
3. **科技树正向购买**：只验过"没点数时拒绝"；未验"攒够点数真买一个节点"。
   （backlog §2）
4. **道路分级**：`list_roads` 交通量/拥堵排序未与游戏 infoview 对比。
   （backlog §4）
5. **道路替换**：只验过独立无主路段；交叉口/连续路/保存重载未验。
   （backlog §3）
6. **填埋场扇形扩容冷启动重载**：未验收；非空填埋场+卡车运转未验。
   （backlog §7）
7. **风车/污水连接端点埋深 −10m**：未做实体级验证。
   （placement-utilities handoff §4.3）

### B. 只有提案/调研，代码未写

8. **地图图片工具 `map_export`**：CS2MapView/Carto 调研完成，明确"本次未改
   代码"。（research/2026-08-11-cs2-map-image-mod.md）
9. **`place_specialized_industry` 组合接口**（hub+提取区一步）：设计建议后续做，
   无代码。（research/2026-08-11-tool-deepening-next-seams.md §3）
10. **UI 自动滚动 / Gameface CDP 打包为 MCP**：08-07 handoff 的可选项，未做。

### C. 遗留未清理

11. `find_placement`：模型面应下线（移除出 construction 组）；HTTP 路由与
    handler 是否保留待定；AGENTS.md "blocked calls retry via find_placement"
    为过时描述。
12. `save_game`：当前是核心工具（模型每回合可见）；没有任何验收要求 Agent
    保存，是否保留待定。
13. `list_objects` 半径过滤：08-06/08-07 均标 "suspect / open"，后续无确认。
14. `Debug548a1a.cs` 硬编码 `C:\Users\super\...` 路径未泛化；调试代码按约定
    保留到 sign-off。
15. UI.log 无效 CSS `display` 警告未清理。
16. 10k 任务书的"条件保险丝"（`find_placement` 成功但未 `place_building` 时
    改为代码强制）：目前仍是 prompt 规则；若 `find_placement` 下线则自然作废。
17. "先建路再放建筑"是否已日志级根除：skill 已加规则，但无确认证据。

### D. 产品决策未定

18. 提取区最低资源覆盖率阈值。
19. 是否允许缩小已占用填埋场（文档推荐不允许）。
20. `set_road_features` / `decorate_road` 正式命名（代码已用前者）。
21. 地图图片工具走 Carto / CS2MapView / 方案 C（纯截图）。

### E. 机器/环境阻塞

22. 显示链路不稳定（0x133 DPC_WATCHDOG_VIOLATION；虚拟显示 + RTX 3060 切换；
    当前开机无信号）。backlog 明确要求：display-driver path 稳定前不得声称
    新的真机验收。
23. Windows 机器可能存在的未提交 diff：GitHub 主分支完整（add1706），但机器
    上的 `git status` / `git diff` 尚未查看。

## 5. 建议恢复顺序

1. 恢复显示链路（虚拟显示器/显卡驱动路径）→ 开机。
2. 开机后第一件事：`git status` / `git diff`，确认/提交未保存工作。
3. A 组按 1→2→3→…→7 顺序做真机验收（专门工业闭环优先）。
4. C 组清理可离线进行（工具面暴露、AGENTS.md、路径泛化、文档标记）。
5. 决策 D 组后更新 backlog。

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
