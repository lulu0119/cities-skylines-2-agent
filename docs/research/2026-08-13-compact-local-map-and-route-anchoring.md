# 紧凑局部地图文本化与道路范围收敛

- **日期：** 2026-08-13
- **状态：** 设计已接受为 [ADR-0004](../adr/0004-linear-networks.md) 与 [ADR-0006](../adr/0006-budgeted-local-map.md)。`LOCAL_MAP` 是否被 Agent 使用仍见 [open-work.md](../open-work.md)。
- **范围：** 高分辨率局部地形、水体、归属和道路如何压缩成模型可读文本；记录本轮已经收敛的道路边界

## 结论

**实现更新（2026-08-13）：** `terrain` 现在默认返回本节设计的纯文本
`LOCAL_MAP v1`，内部最多使用 96×96 自适应局部采样，包含摘要、四向
sector、water/steep/buildable/owned 连通区域、真实道路 node/edge 和明确的
`omitted`。输出由 module 固定限制为 16,000 字符；旧 8×8 JSON 仅保留为
未暴露给 Agent 的 `format=samples` 兼容入口。首个实现尚未生成第二分辨率的
fine patch，输出会明确写 `detail_patches=0`，后续是否增加由模型消费评估决定。

不要把现有 `terrain` 的 8×8 改成让 Agent 选择 16×16、32×32 或更大的原始矩阵。这样只会按面积增加数字，仍要求模型从稠密数组中自行恢复形状、连通关系和道路与水体的相对位置。

建议把 `terrain` 深化成一个内部持有高分辨率栅格、模型面只返回**预算受控的语义矢量局部地图**的 module：

1. 在请求范围内读取高分辨率地形和水体，并叠加土地归属、可建设性及真实道路曲线；
2. 内部计算高程统计、坡度分带及水域/归属/候选可建设地的连通区域；
3. 把区域边界和道路曲线保拓扑简化，转成相对请求原点的量化整数坐标；
4. 按“整体摘要 → 主要区域与道路拓扑 → 关注位置的细节补丁”逐层装入输出预算；
5. 明确报告被省略的图层、要素和细节，不能让模型把“未发送”理解成“不存在”。

这里的“纯文本地图”不是一段模糊的自然语言，也不是完整 GeoJSON/WKT，更不是压缩后的二进制命令流；它是一个短字段、逐行、有坐标系和省略语义的确定性文本格式。模型负责区域选择和城市意图，精确碰撞、坡度、水体采样与游戏原生 validation 仍留在写工具内部。

本轮也已经放弃自动生成沿河岸或等高线道路。因此 `build_road` 不增加 `alignment`、`local_fit` 或“只给中心点便自动选择端点”的模式；也不实现自动端点锚定、沿岸/等高线路线搜索。道路施工只区分：

- `ground`：普通地面道路，以约 4 米或更密的离散采样执行避水和局部 10% 坡度上限（若 prefab 的非零原生上限更严格，则采用更严格值）；
- `grade-separated`：显式的立体交叉意图，统一涵盖桥梁/高架与地下形态，再由实现和原生 validation 处理具体高度语义。

普通地面道路遇到 `InWater` 或 `SteepSlope` 应返回诊断，而不是暗中改成桥、地下道路或自行改写端点。

## 1. 当前实现与可用原始数据

当前 [`RequestHandlers.Perception.cs`](../../Mod/CS2MCP/RequestHandlers.Perception.cs#L24-L26) 把 `kAreaSampleGrid` 固定为 8；`terrain` 在给定 bounds 中取 64 个均匀中心点，逐点返回世界 `x/z`、高程、是否有水及水深，并明确声明“不返回完整 heightmap” ([同文件 125–200 行](../../Mod/CS2MCP/RequestHandlers.Perception.cs#L125-L200))。这个接口适合粗略探针，但不能稳定表达以下关系：

- 一条窄河是否穿过区域；
- 水域是一个连通湖面还是多个水洼；
- 平缓土地分布在哪一侧、是否连通；
- 道路是否被水域/陡坡隔开；
- 归属边界和道路前沿的相对形状。

8×8 并不是游戏数据源的分辨率限制。本机第一方程序集
`C:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed\Game.dll`
（检查时 SHA-256 `721E7E17BF74299AA2B988C1BD07E90874BB8BC72D263229500C4BF639E7E4EE`）中：

- `Game.Simulation.TerrainSystem` 声明默认 heightmap 宽高为 4096，`GetHeightData()` 返回 CPU 高程数组、分辨率、scale 和 offset；
- `TerrainHeightData` 暴露 `heights`、`downscaledHeights`、`resolution`、`scale` 和 `offset`；
- `WaterSurfaceData<T>` 同样暴露 depths、resolution、scale 和 offset；
- `TerrainUtils.SampleHeight`、`WaterUtils.SampleDepth` 已提供世界坐标采样语义。

这些是 2026-08-06 安装版本的反编译证据，不应把私有实现细节当作稳定公共承诺；但足以证明 module 可以在内部按任务所需分辨率采样，而不是让模型接收整张原始数组。

## 2. 从栅格到语义地图

### 2.1 先裁剪，再派生语义层

所有计算先裁剪到请求 bounds，并额外保留一个小的上下文 buffer，避免恰在边缘的水域、道路或归属关系被截断。内部至少派生：

- `water`：按游戏水深语义形成布尔水域；若对决策有价值，可另分浅水/深水；
- `slope`：从高程场计算坡度，分成少数有含义的区间；
- `owned`：已购土地的分类掩码；
- `buildable_candidate`：只表示通过静态地形/水体/归属筛选的候选地，不能声称通过了原生放置 validation；
- `roads`：真实 road node/edge 与简化后的曲线，不从占用栅格重新猜路。

GDAL 官方 `gdaldem` 能从 DEM 生成 slope、aspect、TRI、TPI 和 roughness，并提供 Horn 与 Zevenbergen–Thorne 梯度算法；这证明“内部从高程派生坡度层、模型只看摘要/分带”是成熟做法，而不是必须导出完整 DEM
([GDAL DEM 工具文档](https://gdal.org/en/stable/programs/gdaldem.html))。首版只需要 slope；aspect、TRI/TPI 和 roughness 没有明确消费场景时不进入模型面。

### 2.2 连通区域，而不是逐像元列表

对 `water`、`owned`、`slope band` 和其他分类栅格，先做连通区域标记，再把同值区域 polygonize。GDAL 官方 `GDALPolygonize` 的语义正是“为栅格中值相同的每个连通区域创建矢量多边形”，并允许 4 邻接或 8 邻接
([GDAL algorithm API](https://gdal.org/en/stable/api/gdal_alg.html#_CPPv414GDALPolygonize15GDALRasterBandH15GDALRasterBandH9OGRLayerHiPPc16GDALProgressFuncPv))。

邻接规则必须按图层固定并记录：例如水体是否把仅在角点相触的像元视为同一水域，会改变“能否绕开”的拓扑。不能根据输出预算临时更改邻接规则。

采样噪声形成的单像元斑点可以按图层使用最小面积规则合并，但安全相关要素不得仅因面积小而删除。例如，横穿关注区域的窄水道面积可能很小，却比远处的大湖更重要。GDAL 的 sieve 算法会把小于阈值的栅格区域替换成最大的相邻区域，并明确区分 4/8 邻接；它可作为实现参考，但阈值必须按图层和与关注范围的相交关系决定
([`GDALSieveFilter`](https://gdal.org/en/stable/api/gdal_alg.html#_CPPv415GDALSieveFilter15GDALRasterBandH15GDALRasterBandH15GDALRasterBandHiiPPc16GDALProgressFuncPv))。

### 2.3 简化必须保住拓扑

polygonize 会产生沿像元边界的稠密几何，必须简化后才能进入上下文。不能分别对相邻区域使用普通 Douglas–Peucker 后直接拼接，因为两侧可能形成缝隙、重叠或改变相邻关系。

JTS 的 `TopologyPreservingSimplifier` 保证输出与输入维度、组件数和组件间拓扑关系相同；有效多边形仍有效，开放 LineString 的端点保持不变，并以最大距离 tolerance 控制简化
([JTS 1.20 文档](https://locationtech.github.io/jts/javadoc/org/locationtech/jts/simplify/TopologyPreservingSimplifier.html))。若多个共享边界需要一致简化，应先把共享边界建成一次存储的 arc，再让各区域引用同一 arc。TopoJSON 的核心正是让几何共享 arcs，并支持 scale/translate 量化和 arc delta 编码
([TopoJSON specification](https://github.com/topojson/topojson-specification/blob/master/README.md))。

首版不必把 arc 引用暴露给模型：内部共享/简化一次，序列化时为少量主要区域展开点列即可。只有共享边界重复已经成为实际 token 主因且模型消费测试证明 arc 引用不降低正确率时，才考虑将 arcs 放到文本接口中。

CGAL 的折线简化实现提供另一个有用约束：简化不会引入原折线集合中不存在的新交点，并能通过 cost function 与 stop predicate 按误差或目标数量停止
([CGAL Polyline Simplification 2](https://doc.cgal.org/latest/Polyline_simplification_2/index.html))。道路曲线简化至少要保持端点、交叉节点和真实连接关系，不能把接近的两条道路简化成视觉相交但拓扑未连接。

## 3. 模型侧文本格式

### 3.1 坐标帧

每次响应先声明一个局部坐标帧：

- `origin_world=(x,z)`：请求中心或稳定的局部原点；
- `axes=(+x,+z)`：沿游戏世界轴，不擅自称为东/北；
- `unit=m`；
- `quantum`：坐标量化步长，例如一个整数单位代表若干米；
- `bounds_local`：响应覆盖范围；
- `revision` / `observed_at`：说明几何来自哪次模拟快照。

正文只传相对原点的量化整数坐标。Mapbox Vector Tile 规范同样在已知 tile bounds 内使用局部整数 extent，TopoJSON 则用 scale/translate 把整数恢复成绝对坐标；两者都证明“先声明变换，再传局部整数”能够减少重复浮点数
([Mapbox Vector Tile 2.1](https://github.com/mapbox/vector-tile-spec/blob/master/2.1/README.md)，[TopoJSON transforms](https://github.com/topojson/topojson-specification/blob/master/README.md#211-transforms))。

但不要把 MVT 的 command stream、ZigZag 或 TopoJSON delta-encoded arc 原样交给模型。那是机器传输格式；节省的字符可能转化为更高的接口认知成本。首版使用局部绝对整数点，容易检查和复述。

### 3.2 三个语义层级

同一响应按以下顺序组织，而不是按固定 N×N 分辨率组织：

1. **摘要层（必有）**：bounds、高程 min/median/max、坡度分带占比、水域/归属/候选可建设占比、主要连通区域数量；可增加以中心为基准的 `+x/-x/+z/-z` 扇区摘要，帮助比较扩展方向。
2. **主要要素层**：与关注范围相交或影响通行/建设的水域、陡坡、归属区域，以及道路 nodes/edges；每个要素有短 id、面积/长度、包围盒和简化几何。
3. **细节补丁层**：只对请求中心、关键道路邻域、水/坡度临界边界给更小 quantum 或更低 simplify tolerance 的 geometry。补丁必须声明自己覆盖哪个粗要素及 bounds。

空间索引使这种“先找相关要素，再分配细节”无需扫描全部几何。JTS `STRtree` 是打包 R-tree，支持 envelope query、nearest-neighbour 和 within-distance 查询
([JTS `STRtree`](https://locationtech.github.io/jts/javadoc/org/locationtech/jts/index/strtree/STRtree.html))。

### 3.3 建议的 v1

```text
LOCAL_MAP v1 revision=18420
frame origin_world=(1250,-640) axes=(+x,+z) unit=m quantum=4 bounds_local=[-64,-64,64,64]
summary elevation_m={min:12,p50:18,max:41} water=18% owned=73% candidate_buildable=56%
slope bands={0..5%:52%,5..12%:31%,>12%:17%}
sectors +x={water:4%,buildable:71%} -x={water:48%,buildable:22%} +z={water:8%,buildable:63%} -z={water:12%,buildable:55%}
regions
  W1 kind=water area_m2=18400 bbox=[-64,-52,-18,61] ring=[(-64,-52),(-31,-49),(-23,4),(-18,61),(-64,58)]
  S1 kind=steep band=>12% area_m2=6100 bbox=[14,9,63,54] ring=[(14,9),(61,15),(63,54),(21,49)]
  O1 kind=owned area_m2=73200 bbox=[-64,-64,51,64] ring=[...]
networks
  node N12 at=(8,-6) degree=3
  node N13 at=(49,17) degree=1
  road R7 class=small from=N12 to=N13 line=[(8,-6),(27,1),(49,17)]
relations W1 touches_bounds=[-x,+z]; R7 separates_from=W1 min_gap_m=16; S1 blocks=[+x,+z]
omitted vertices=348 features={minor_water:3,minor_steep:7,roads_outside_focus:12} reason=output_budget
```

这个例子同时给出：

- 精确但量化的形状；
- 显式关系（相邻、间距、阻挡方向），避免要求模型只凭点列重新计算；
- 路网拓扑的 node/edge，而不是仅画线；
- `omitted`，避免缺失数据被误读。

字段名和数值仍需通过实际模型 eval 校准；当前没有证据证明某种文本语法对所有模型都“可靠”。因此接口测试应包含可判分问题，例如“哪一侧候选可建设地最大”“道路 R7 是否与 N12 拓扑连接”“从中心向 -x 是否跨水”，而不只比较序列化长度。

## 4. 输出预算算法

`output_budget` 应由上下文预算策略在内部计算，不让 Agent 每次猜。不存在适用于所有模型、语言和地图复杂度的固定 token 常数；首版用多档预算做 eval 后再定 Auto 预设。

确定性的装载顺序如下：

1. 预留 header、summary 和 `omitted` 的固定空间；
2. 空间裁剪到请求 bounds，并标记边界相交要素；
3. 必须保留与 focus 相交的水域、陡坡、归属边界和道路拓扑；
4. 按任务相关性、距 focus 距离、阻挡作用、面积/长度排序其他要素；
5. 超预算时先聚合远处小斑块为 count/coverage，再删除低优先要素；
6. 对保留几何提高 simplification tolerance；可二分 tolerance，直到**实际序列化后的 token 数**入预算；
7. 每图层、每要素另设顶点上限，防止一个复杂岸界占满响应；
8. 记录每层原始/返回 feature 和 vertex 数、使用的 quantum/tolerance 以及省略原因。

如果当前 provider 没有可用 tokenizer，应以实际序列化字符数/UTF-8 bytes 做保守硬上限并记录测量单位，不能伪装成精确 token。预算收缩不能改变连通性、删除穿越 focus 的窄障碍或删除道路节点；这些情况应降低非关键层细节，或返回“本预算不足以安全表达”的显式状态。

量化、简化和输出预算是三个不同误差来源，必须分别记录：

- `source_resolution_m`：内部采样能看见多小的要素；
- `quantum_m`：输出坐标精度；
- `simplify_tolerance_m`：几何最大简化尺度。

这样真机验收发现模型误判时，才能区分“数据源没采到”“序列化抹掉了”还是“模型看到了但没使用”。

## 5. 首版实现顺序与验收

### 5.1 最小可实施切片

1. 新的内部 snapshot 在局部 bounds 高分辨率采样 height/water，并读 owned tiles 与真实 road curves；
2. 派生 slope bands，连通标记后 polygonize water/owned/steep；
3. 对区域覆盖和道路几何保拓扑简化；
4. 实现局部量化坐标和上面的 `LOCAL_MAP v1` 序列化；
5. 实现真实序列化预算、per-feature cap 和 `omitted`；
6. 给 `terrain` 返回 compact 模式，保留旧 8×8 仅作短期兼容/对照，确认消费方迁移后再移除；
7. 在新存档用固定局部场景做模型判题与真机对照。

首版不需要引入完整 GIS runtime。连通区域、栅格边界追踪、共享边界简化和空间索引可以针对局部数据实现；引用 GDAL/JTS/CGAL 是为了明确算法语义和不变量，不代表必须把这些库装进游戏 Mod。

### 5.2 必须验证的性质

- 窄水道与小型陡坡只要穿过 focus 就不会因面积阈值消失；
- 相邻区域简化后不产生缝隙、重叠或连通性变化；
- road node/edge 与 ECS 实体拓扑一致，视觉相交但没有节点时不能写成 connected；
- 同一 snapshot 和参数产生字节级稳定的输出顺序；
- 所有世界坐标都能按 frame/quantum 恢复到声明误差内；
- 每档预算都遵守上限，并准确报告省略内容；
- 使用 1k/2k/4k 等实验预算档（不是产品预设）对同一组空间问题测正确率、工具调用次数和总上下文成本，再选择 Auto 预设。

## 6. 明确不做的算法与接口

由于产品不再自动生成沿岸/等高线路线，以下内容不是当前 TODO：

- Marching Squares 等高线输出、岸线平滑或岸线 offset；
- 沿岸/近似等高的 A*、Theta*、Hybrid A* 代价搜索；
- 为路线自动选择道路端点、吸附/切分既有 edge；
- 根据一个中心点推断整条道路的起终点；
- 自动 Bezier/spline 路线拟合和分段施工；
- `alignment`、`local_fit` 或隐式 route-recovery 参数。

若未来只为地图显示需要等高线，Marching Squares 是可用的独立算法；例如 scikit-image 的官方 `find_contours` 明确使用 marching squares
([文档](https://scikit-image.org/docs/stable/api/skimage.measure.html#skimage.measure.find_contours))。它不应以“已经有库可用”为由重新进入当前道路范围。

`build_road` 继续要求 Agent 给出明确端点。`ground` 的水体/坡度约束与 `grade-separated` 的显式立体交叉意图属于写工具内部的不同不变量；紧凑局部地图帮助 Agent 先做空间判断，但不能替代原生施工 validation，也不能把一次普通道路失败自动升级成桥梁或地下工程。

## 7. 来源清单

- 本仓库当前 `terrain`：[`RequestHandlers.Perception.cs`](../../Mod/CS2MCP/RequestHandlers.Perception.cs#L125-L200)
- 本机 CS2 第一方程序集：`Game.Simulation.TerrainSystem`、`TerrainHeightData`、`WaterSurfaceData<T>`、`TerrainUtils`、`WaterUtils`，位于上述 `Game.dll`（SHA-256 已记录）
- [GDAL `gdaldem`](https://gdal.org/en/stable/programs/gdaldem.html)
- [GDAL raster polygonize API](https://gdal.org/en/stable/api/gdal_alg.html#_CPPv414GDALPolygonize15GDALRasterBandH15GDALRasterBandH9OGRLayerHiPPc16GDALProgressFuncPv)
- [GDAL sieve API](https://gdal.org/en/stable/api/gdal_alg.html#_CPPv415GDALSieveFilter15GDALRasterBandH15GDALRasterBandH15GDALRasterBandHiiPPc16GDALProgressFuncPv)
- [JTS topology-preserving simplifier](https://locationtech.github.io/jts/javadoc/org/locationtech/jts/simplify/TopologyPreservingSimplifier.html)
- [JTS packed R-tree](https://locationtech.github.io/jts/javadoc/org/locationtech/jts/index/strtree/STRtree.html)
- [CGAL Polyline Simplification 2](https://doc.cgal.org/latest/Polyline_simplification_2/index.html)
- [TopoJSON specification](https://github.com/topojson/topojson-specification/blob/master/README.md)
- [Mapbox Vector Tile 2.1 specification](https://github.com/mapbox/vector-tile-spec/blob/master/2.1/README.md)
- [GeoJSON RFC 7946](https://www.rfc-editor.org/rfc/rfc7946)
