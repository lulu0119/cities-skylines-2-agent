# `cities-skylines1-agent-skill` 道路规划能力审计（2026-08-13）

**状态：** 一手源码审计；未修改产品代码。  
**问题：** `Sunwood-ai-labs/cities-skylines1-agent-skill` 如何处理断头路、地图/地形/道路几何、曲线道路与桥梁？哪些思路适合迁移到本项目？  
**审计固定点：** 默认分支 `main` 的 commit
[`70a5116215a4c83820134cca870aae1934e92d87`](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/commit/70a5116215a4c83820134cca870aae1934e92d87)。未合并分支仅在明确标注时作为补充材料，不视为产品现状。  
**来源访问日期：** 2026-08-13。

## 结论

这个项目值得参考，但它解决的是**道路拓扑异常的可观测性和事后 QA**，
不是道路路线规划：

- 它读取 CS1 的真实 `NetNode` / `NetSegment` 图，能区分普通合法死路、
  短断头、端点靠近另一条路但没有真正连接、同高交叉但没有节点、重复/
  重叠路段，以及城市路网是否连接到地图外部道路。
- 它在施工时只会复用端点 2 米内的既有节点；不会吸附并切分道路中段，
  因而并没有从源头消除“看起来接上、图上没接”的问题。
- 所谓 repair 脚本只在指定矩形范围内删除可疑路段并保存，不会自动补接；
  重建仍由 Agent 通过独立命令完成。
- 它没有全局地形高度图、水体掩码、岸线或等高线表示。地形采样只用于
  发现已经修好的道路是否悬空、下沉或跨越异常陡坡。
- 默认分支的建路命令只有起终点，创建单条直路；没有曲线控制点、沿河/
  沿等高线算法，也没有桥头、引桥、跨水净空或桥面纵坡规划。

因此，对本项目最有价值的是“**真实路网图 + 异常分类 + 修复后复测**”的
闭环。沿河岸、沿等高线、普通道路避水和显式桥梁意图仍需一个面向 CS2
原生验证的新路线规划 module，不能从该项目直接移植。

## 1. 它怎样发现断头路和伪连接

### 1.1 使用真实拓扑，而不是截图

`RoadAnomalyCollector` 遍历真实道路段，读取两端节点并计算道路连接数：

- 长度低于阈值且至少一端连接数不超过 1 的路段被标记为
  `shortRoadStub`；
- 对连接度恰好为 1 的节点，查找附近另一条道路。若距离小于
  `nearMissDistance`，返回 `deadEndNearRoad`；否则只有在
  `includeDeadEnds=true` 时才返回普通 `deadEndRoad`。

源码见
[`GameState.cs` 1395–1447](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L1395-L1447)
及其返回的节点、所属路段、附近路段和坐标字段
([`GameState.cs` 1474–1510](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L1474-L1510))。

它还检测：

- 两条道路共享同一对端点的 `duplicateRoadSegments`；
- 路段近似平行、相距很近且重叠足够长的 `overlappingRoadSegments`；
- 两条路在相近高度相交但不共享节点的 `roadCrossingWithoutNode`。

候选道路对先放入 96 米空间网格，再做精确配对，避免全路网的无条件
两两比较
([`GameState.cs` 1637–1730](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L1637-L1730))。
城市与地图外部道路的关系则用 BFS 遍历真实道路连通分量，而不是根据
道路坐标猜测
([`GameState.cs` 2259–2420](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L2259-L2420))。

### 1.2 它没有把所有断头路都当成 bug

项目文档明确说 `deadEndRoad` 在 CS1 中是合法道路，只适合 Agent 做设计
QA；`deadEndNearRoad` 才代表常见的“视觉上碰到、拓扑上未连接”
([API 文档 200–222](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/docs/api.md#L200-L222))。
Skill 的常用检查默认 `includeDeadEnds=false`，并再次提醒不要把所有
cul-de-sac/stub 都视为错误
([`SKILL.md` 44–61](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/SKILL.md#L44-L61)、
[`SKILL.md` 123–128](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/SKILL.md#L123-L128))。

这一区分应该迁移：**节点度为 1 是事实，不自动等于错误**。只有近接未连、
孤立组件、意外短桩或与建设意图冲突时，才应进入修复候选。

### 1.3 “预防”和“修复”的实际边界

建路时，项目只在请求起终点的 2 米范围内寻找兼容节点；找不到就创建
新节点，再调用 `NetManager.CreateSegment`
([`RoadCommands.cs` 42–91](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/RoadCommands.cs#L42-L91)、
[`RoadCommands.cs` 110–151](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/RoadCommands.cs#L110-L151))。
它不会把端点吸附到既有路段的中间，也不会切分该路段来创建真实交叉点。
这恰好解释了为什么仍需要 `deadEndNearRoad` 事后诊断。

检查脚本只是输出“把这个端点接到那条路”的提示
([`inspect-road-anomalies.ps1` 14–27](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/scripts/inspect-road-anomalies.ps1#L14-L27))。
repair 脚本则筛出位于用户指定矩形范围内的短桩、近接未连路段和可选的
普通死路，调用 bulldoze 后保存；没有任何补路步骤
([`repair-road-anomalies.ps1` 21–56](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/scripts/repair-road-anomalies.ps1#L21-L56))。
README 所述流程同样是 inspect → bulldoze → rebuild → settle → re-check →
save，而非隐藏的一键修复
([`README.md` 92–101](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/README.md#L92-L101))。

准确结论不是“它解决了断头路”，而是：

> 它让 Agent 能从真实拓扑中发现断头、近接未连和孤立组件，并拿到修复
> 所需的实体 ID；路线重建仍由 Agent 决定。

## 2. 地图、地形和道路几何表示

### 2.1 道路是端点/中点列表，不是语义地图

`/state/networks` 返回每段的 ID、prefab、service/sub-service、问题、
起终节点 ID、起终点三维坐标和一个中点
([`GameState.cs` 598–703](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L598-L703))。
默认分支没有导出：

- 完整道路曲线或切线；
- 道路宽度、车道或可通行方向；
- 全局地形高度栅格；
- 水深/水体掩码、岸线或河流方向；
- 等高线或坡度场。

异常检测把道路几何近似成起点到终点的弦线：点到路段距离
([`GameState.cs` 1995–2040](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L1995-L2040))、
交叉和重叠判断
([`GameState.cs` 2042–2128](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L2042-L2128))
都没有使用实际 Bezier。这适合粗粒度 QA，但不能作为曲线路线规划的精确
几何基础。

### 2.2 地形采样是事后 QA，不是路线生成

地形异常检查在每段路的 25%、50%、75% 位置取样，并向道路两侧
8、22、44、66、96 米继续采样，用于比较路面、道路两侧和相邻地形的
高度差
([`GameState.cs` 1778–1863](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L1778-L1863)、
[`GameState.cs` 1892–1906](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L1892-L1906))。
它能报告 `roadTerrainCliff` 或 `roadBelowLocalGrade`，但不会从高度场生成
沿等高线候选。

示例城市甚至硬编码“默认地图东侧 `x=640+` 低路会被淹”，所以把城区
放在内陆
([`develop-starter-city.ps1` 36–45](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/scripts/develop-starter-city.ps1#L36-L45))。
这不是通用水体理解。

## 3. 曲线、沿地形道路和桥梁

### 3.1 默认分支只建两点直路

`BuildRoad` 只读取 `start` / `end`；两端高度分别采样地形和水面，再把
切线固定为整条线的正反方向
([`RoadCommands.cs` 9–35](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/RoadCommands.cs#L9-L35)、
[`RoadCommands.cs` 73–85](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/RoadCommands.cs#L73-L85))。
Agent 可以多次调用拼出折线，但项目没有平滑、曲率限制、岸线平行或
等高线平行算法。其 `dryRun` 也只检查 prefab 和最短长度，随后直接返回
“validation passed”，没有碰撞、水体、坡度或拓扑连接预检
([`RoadCommands.cs` 18–36](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/RoadCommands.cs#L18-L36))。

### 3.2 默认分支没有桥梁规划

默认分支没有 elevation 参数、bridge mode、跨水检测、桥头选点、引桥
长度、纵坡或净空规划。代码只是按 prefab/AI 名称含 `Bridge`、`Elevated`、
`Tunnel`、`Slope` 来跳过地形异常检查；这是分类排除，不是建桥能力
([`GameState.cs` 2194–2209](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/src/GameState.cs#L2194-L2209))。

未合并的 `develop` commit
[`d9f7067a64658b536559f12c52ba6b1229ea5aaa`](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/commit/d9f7067a64658b536559f12c52ba6b1229ea5aaa)
增加了统一 `heightOffset`，但仍只生成一条两点直段，也没有水体识别或
桥梁语义
([该分支 `RoadCommands.cs` 9–128](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/d9f7067a64658b536559f12c52ba6b1229ea5aaa/src/RoadCommands.cs#L9-L128))。
它不能作为默认分支的现有能力，也不是可直接复用的桥梁规划器。

## 4. 对本项目的可迁移结论

### 4.1 值得迁移的概念

1. **真实拓扑 QA。** 查询 CS2 的 `Edge`、端点 `Node`、`ConnectedEdge`
   和实际 `Curve`，返回节点度、连通分量与实体引用，而不只给模型两端
   坐标。
2. **异常有类型。** 至少区分合法 dead end、near-miss、意外短桩、交叉
   无节点、孤立路网、重复/重叠段；不能把“度为 1”当成统一错误。
3. **施工后复测。** `build_road` 成功只证明原生 transaction 被接受；
   随后应验证实际拓扑、曲线、水体关系和外部连通性。
4. **空间索引。** 对道路、岸线或路线走廊先用网格/R-tree 缩小局部候选，
   再做曲线精确计算。参考项目的 96 米网格说明无需全路网两两计算。
5. **限制破坏范围。** 修复候选必须带位置/实体证据；合法死路默认不进入
   批量拆除。

### 4.2 不应照搬的实现

- 不要移植 CS1 的 `NetManager.CreateNode/CreateSegment` 直接变更路径。
  本项目已决定使用游戏正常放置验证；CS2 的 native transaction 才是权威。
- 不要把曲线道路压成端点弦线做精确连接判断。CS2 已有真实 Bezier，
  应对曲线采样或做曲线最近点计算。
- 不要把“删除坏路”冒充修复完成，也不要依赖事后删除代替 transaction
  失败回滚。
- 不要硬编码地图坐标或依赖模型自己从 8×8 探针拼出完整岸线。
- 不要把多次低级 `build_road` 调用等同于路线规划；平滑、坡度、跨水和
  连接不变量应由一个更深的 module 持有。

## 5. 沿河岸/等高线道路与桥梁：可行性和建议边界

### 5.1 难度判断

“在大致位置修一条直路”已经有原语；“稳定地沿河岸或等高线修出曲折、
连通且可用的路网”是**中高难度但可实现**的路线规划功能。困难不在让模型
多给几个坐标，而在低层 module 必须同时持有以下约束：

- 岸线/水体边界与所需退距；
- 地形坡度、纵坡和横坡；
- 道路宽度、曲率与交叉口几何；
- 已购地边界、建筑和其他障碍物；
- 起终点真实吸附与路段切分；
- 普通道路、桥梁、隧道三种不同的水体/高程语义；
- 每段原生验证失败后的局部重规划，以及部分施工的回滚/清理。

当前 CS2 `build_road` 已能创建直线或单控制点曲线，并用端点相对地形高度
形成高架段
([本项目 `RequestHandlers.Build.cs` 1216–1300](https://github.com/lulu0119/cities-skylines-2-agent/blob/6fc3c604c3ae5e175dda117c4ab785640d3319bf/Mod/CS2MCP/RequestHandlers.Build.cs#L1216-L1300)、
[`BridgeToolSystem.cs` 1432–1485](https://github.com/lulu0119/cities-skylines-2-agent/blob/6fc3c604c3ae5e175dda117c4ab785640d3319bf/Mod/CS2MCP/BridgeToolSystem.cs#L1432-L1485))。
缺少的是把地图意图转成一组安全曲线的规划层，而不是新的单段写入原语。

### 5.2 建议的深 module

模型面对的仍应是城市意图，例如“从 A 附近到 B 附近，尽量沿河岸修一条
普通道路”或“跨过这条河修桥”。路线 module 在内部完成：

1. **构建局部语义场。** 在请求走廊内按道路尺度自适应采样地形、水深、
   已购地、障碍物与既有路网；当前固定 8×8 `terrain` 适合给模型摘要，
   不足以直接做精确路线规划
   ([本项目 `RequestHandlers.Perception.cs` 125–200](https://github.com/lulu0119/cities-skylines-2-agent/blob/6fc3c604c3ae5e175dda117c4ab785640d3319bf/Mod/CS2MCP/RequestHandlers.Perception.cs#L125-L200))。
2. **提取几何。** 从水体掩码提取岸线 polyline（例如 marching squares），
   从高度场计算坡度/等高方向；沿河路线取岸线的陆侧 offset，沿等高线
   路线优化“前进方向与高度梯度垂直”及低纵坡，不要求机械地贴住一根
   理论等高线。
3. **搜索并平滑。** 在候选走廊中用 A*/Theta*/visibility graph 等算法
   最小化长度、坡度、曲率、障碍物和偏离目标形态的代价；再把 polyline
   简化成满足曲率/路段长度的若干 Bezier 段。
4. **吸附和原生验证。** 起终点吸附到真实节点或由原生工具创建交叉；逐段
   通过游戏正常 validation，失败时只重规划局部走廊。
5. **提交后复查。** 用真实路网图检查近接未连、意外 dead end、孤立组件、
   入水、过陡和重复路段。

模型不需要接收完整高度栅格或亲自计算每个控制点。它需要的是压缩的全局
地图摘要来决定城市向哪里发展；高分辨率地形、水体和路网几何应留在路线
module 内部。这也避免大地图数据持续占用模型上下文。

### 5.3 普通道路自动避水不会妨碍修桥

两者应由一个显式意图区分，而不是让同一参数组合具有歧义：

| 意图 | 水体代价 | 路线规划责任 |
| --- | --- | --- |
| `ground` / 普通道路 | 水面为不可通行障碍 | 绕水、沿岸、控制坡度；不得悄悄变桥 |
| `bridge` / 显式跨水 | 允许在一个选定跨越走廊进入水面上方 | 识别两岸、选择桥头、控制引桥纵坡/净空，生成 approach → span → exit |
| `auto`（若以后需要） | 先尝试普通道路；只有用户/Agent 的上层意图允许跨水时才评估桥 | 返回选择理由和估算，不把一次误入水自动升级为桥梁工程 |

因此，“普通道路自动避水”是 ground 模式的安全不变量；“Agent 如何修桥”
由 bridge 意图解决。桥梁不能只是在误入水后给整段道路统一加 10 米高度：
至少要规划两岸连接点、引桥长度、允许纵坡、跨水段和落地连接。当前接受的
ADR 也已经把单控制点高架原语与可靠 landmark bridge planner 区分开
([ADR-0004](../adr/0004-linear-networks.md))。

### 5.4 建议的实施次序

1. 先补真实路网拓扑观测与施工后 QA，解决 near-miss、孤立组件和短桩；
2. 再做普通道路的局部水体/坡度感知路线，默认水面不可通行；
3. 在此基础上增加沿岸 offset 和近似等高线 cost；
4. 最后加入显式 bridge 模式及引桥/净空约束；地标式多跨桥继续作为更高层
   规划问题。

这样能先消除现有“道路扎进水里”和“视觉接上但拓扑未连”的可靠性问题，
同时不把完整桥梁工程塞进第一版普通道路规划器。

## 6. 许可证、活跃度与证据强度

- 仓库使用 MIT License
  ([`LICENSE` 1–20](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/LICENSE#L1-L20))。
  设计思想可以重新实现；若复制实质性代码，分发物需保留其版权和许可
  声明。
- 默认分支固定点比 `v0.3.0` tag 多一个文档提交；正式版为
  [`v0.3.0`](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/releases/tag/v0.3.0)。
  本次拉取到的 52 个 commit 集中在 2026-05-12 至 2026-05-15，默认分支
  最后提交时间为 2026-05-14 00:51:44 +09:00；截至访问日没有更晚的代码
  提交进入 `main`。
- README 明确将项目标为 experimental，并要求先在可丢弃存档测试
  ([`README.md` 170–176](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/README.md#L170-L176))。
- 内置 smoke 只检查 API、prefab 和建路 `dryRun`，没有断头路修复、曲线、
  沿岸或桥梁场景验收
  ([`smoke-test.ps1` 1–49](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/scripts/smoke-test.ps1#L1-L49))。
  v0.3.0 发布说明列出的验证也是编译、文档链接和文档构建，不是道路规划
  真机场景
  ([发布说明 54–63](https://github.com/Sunwood-ai-labs/cities-skylines1-agent-skill/blob/70a5116215a4c83820134cca870aae1934e92d87/docs/releases/v0.3.0/index.md#L54-L63))。

它适合作为接口和诊断思路的参考，不应被当作已经经过长期城市验证的
道路规划算法库。
