# CS2 地图图片生成调研：Cimtographer / CSL Map View / Carto

日期：2026-08-11

## 结论摘要

用户描述的“从游戏内采集信息、生成类似真实地图的垂直俯视图”的 mod，在《天际线1》里有两条线索：

- **Cimtographer**：导出 OSM 风格数据（`.osm` XML），再用外部工具 Maperitive 渲染成地图图片。
- **CSL Map View**（作者 gansaku）：mod 导出数据 + 外部查看器直接渲染成矢量风格二维地图。

《天际线2》没有 Cimtographer 的官方移植；**CSL Map View 的续作是 CS2MapView**（同一作者 gansaku），最贴合用户描述。另有 **Carto**（开源、可编程导出空间数据）+ **cs2-carto-citymap**（Python 渲染器）这条更利于 agent 调用的路线。

## CS2MapView

- 仓库：https://github.com/gansaku/CS2MapView
- 许可证：MIT（仓库根目录 `LICENSE`）
- 最新发布：v.0.1.1（2025-11-23），含 `CS2MapView.Exporter_0.1.1.zip`（游戏内 mod）和 `cs2mapview_0.1.1.zip`（Windows 查看器）
- 平台：Windows 64 位 + .NET 8 Desktop Runtime；官方 README 说明目前**未上架 Paradox Mods**（见 issues/3 “Mod banned on Paradox Mods”），需手动解压到 `AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\CS2MapView.Exporter`

### 工作方式（mod + 外部查看器）

1. 游戏内 mod `CS2MapView.Exporter`：在 mod 选项界面点 “Export” 按钮，把城市数据导出为 `.cs2map`（zip 格式，包含 `main.xml`、`buildings.xml`、`districts.xml`、`roads.xml`、`rails.xml`、`transports.xml`、`terrain.dat` 等条目）。
2. 外部查看器 `cs2mapview.exe`：打开 `.cs2map`，渲染并保存图片。

### 生成图片的内容与格式

- 图层：地形（等高线/山体阴影）、道路、铁路、建筑、公交/地铁/轨道线路与站点、区域、网格、街道名/建筑名/地标图标（含 Maki 图标集）。
- 输出格式：PNG / BMP / JPEG / SVG；图片宽度 32–16384 像素（默认 2048），可框选打印区域。
- 渲染是程序化二维绘图（SkiaSharp），因此是垂直俯视、无透视畸变的地图，不是游戏截图。

来源：https://github.com/gansaku/CS2MapView/blob/main/README.md 、`CS2MapView.Form/SaveImageForm.cs` 、`CS2MapView.Serialization/CS2MapDataZipEntryKeys.cs` 、`LICENSE`

### Agent 能否调用

- **导出环节可以**：`CS2MapViewSystem.RequestExport(dir, heightMapRestriction, addTimestamp)` 是公开方法，且有 `ExportFinished` 事件（见 `CS2MapView.Exporter/Systems/CS2MapViewSystem.cs`）。游戏内另一个 mod（例如本仓库的 agent mod）可以用反射调用它，就像 Carto 文档推荐的“运行时反射”模式。
- **渲染环节不能直接调用**：查看器是 Windows Forms GUI，`Program.cs` 只支持 `-locale=` 参数，没有命令行打开文件/保存图片的入口；README 也明确“mod 没有启动查看器的功能”。
- 因此“装好就能被 agent 调用”不成立，需要自己做一层胶水：要么把 `CS2MapView.Core`（MIT）封装成一个小型 headless 渲染 CLI，要么用 UI 自动化操作查看器（脆弱，不推荐），要么走下面的 Carto 路线。

## Carto（推荐给 agent 集成的开源路线）

- 仓库：https://github.com/taipei-native/Carto
- 许可证：MIT
- 分发：Paradox Mods（https://mods.paradoxplaza.com/mods/87428/Windows ），也可用 Skyve 安装
- 功能：导出建筑、道路、铁路、公交线路、POI、区域、地块边界、高程、水深等数据
- 输出格式：矢量 GeoJSON / Shapefile；栅格 GeoTIFF（高程、水深）
- 输出目录：`AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\Carto`
- **公开 API**：`Carto.IO.IO.Export(Carto.IO.Options)`，返回 `ExportResult { Success, FilesWritten, ErrorMessage }`；官方 Wiki 明确建议下游 mod 用**运行时反射**调用（因为 Carto 随游戏补丁重建，硬引用会坏），并给出静默导出示例（`CompletionSound=false`、`CompletionDialog=false`）。

来源：https://github.com/taipei-native/Carto/blob/main/README.md 、https://raw.githubusercontent.com/wiki/taipei-native/Carto/Api.md

### cs2-carto-citymap（把 Carto 数据渲染成地图）

- 仓库：https://github.com/HamsterPark/cs2-carto-citymap
- 许可证：代码 MIT；示例图片/数据 CC BY 4.0
- 功能：纯 Python（matplotlib + numpy + rasterio）把一次 Carto 导出渲染成 **13 张出版级地图**：基础城市图 + 高速/干道、公交、地铁、铁路、有轨电车、公交线路、航运、水道、街道图、地形山体阴影等专题图；支持最高 600 DPI。
- 调用方式：`python render_city.py`，全脚本化，适合被 agent 的 tool 直接调用。

来源：https://github.com/HamsterPark/cs2-carto-citymap/blob/main/README.md

## 与现有截图链路的对比

本仓库当前截图链路（`Mod/`）：

1. `ScreenshotRunner` 在 `WaitForEndOfFrame` 截屏并 `EncodeToPNG`，内存中得到 PNG `byte[]`（`BridgeResponse.Png`）。
2. `AgentToolBridge.SaveScreenshot` 把字节写盘，返回 `ImagePath`。
3. `AgentToolExecutor.AppendToolImage` 再**从磁盘读回**字节，以 `DataContent(image/png)` 附加到聊天历史。

结论：**“先落盘再读盘”不是必需步骤**，PNG 字节本来就在内存里。可以改为让 `ToolInvocationResult` 直接携带 `byte[]`，在 `AppendToolImage` 里直接附加；落盘可以保留为调试/审计副本。需要注意的点：

- 当前有 8 MB 附件上限与 `SupportsVision`/`StaticEnableVisionTools` 开关。
- 地图图建议按模型输入尺寸缩放（如 1024–2048 宽）再发送，避免 16384 像素原图超限。
- OpenAI 视觉接口本身支持“HTTP URL 或 base64 data URI”两种图片传入方式，所以把字节编码成 `data:image/png;base64,...` 直接放进请求是官方支持的做法（来源见下方来源列表）。

相关代码：`Mod/CS2MCP/ScreenshotRunner.cs`、`Mod/Agent/AgentToolBridge.cs`、`Mod/Agent/AgentToolExecutor.cs`

## 推荐的 agent tool 设计（供后续实现参考，本次未改代码）

### 感知分工：Agent 自主选择地图还是截图

做这个功能的定位不是“让 agent 能导出地图”，而是给视觉模型一种**更好的全局视角**：截图是三维世界的单点投影（有透视、遮挡、远近和 UI 干扰），地图是等比例、无畸变的二维全貌。对路网规划、区域布局这类高层空间推理，地图远清晰于截图；对“哪里冒了感叹号”“某个路口卡住了”这类局部、动态问题，截图仍然更直接。

因此工具设计的关键不是二选一，而是让 Agent 自己判断当前任务该用哪个：

- 排查/验证类（发现问题、看警告、确认某处细节）→ 先看截图。
- 规划/设计类（铺路网、分区、整体布局、评估地形与建成区关系）→ 主动要一张地图。

补充一点：Carto 导出的是 GeoJSON 矢量数据，不只是位图，Agent 还可以拿到道路的精确坐标与拓扑，等于“结构化世界模型 + 视觉地图”双通道，比纯看图更利于规划推理。

方案 A（推荐）：`map_export` tool

1. 游戏内 agent mod 反射调用 `Carto.IO.IO.Export`，导出 GeoJSON/GeoTIFF 到已知目录。
2. 外部/同进程调用 `cs2-carto-citymap` 的 `render_city.py`，生成 PNG。
3. 把 PNG 字节直接附加到消息（或先落盘再附加），路径可选。

方案 B：`map_export` + CS2MapView

1. 反射调用 `CS2MapViewSystem.RequestExport` 得到 `.cs2map`。
2. 基于 MIT 的 `CS2MapView.Core` 写一个 headless 渲染 CLI（或调用查看器自动化），输出 PNG/SVG。

方案 C：不新增渲染器，仅用当前 `/screenshot` 工具把相机设成垂直俯视（`set_camera`）再截图。成本最低，但仍是游戏渲染截图，分辨率受屏幕限制，且包含游戏 UI/透视残差，不是真正的“地图”。

## 来源列表

- https://github.com/gansaku/CS2MapView
- https://github.com/gansaku/CS2MapView/blob/main/README.md
- https://github.com/gansaku/CS2MapView/blob/main/CS2MapView.Exporter/Systems/CS2MapViewSystem.cs
- https://github.com/gansaku/CS2MapView/blob/main/CS2MapView.Form/Program.cs
- https://github.com/gansaku/CS2MapView/blob/main/CS2MapView.Form/SaveImageForm.cs
- https://github.com/gansaku/CS2MapView/issues/3
- https://github.com/taipei-native/Carto
- https://raw.githubusercontent.com/wiki/taipei-native/Carto/Api.md
- https://mods.paradoxplaza.com/mods/87428/Windows
- https://github.com/HamsterPark/cs2-carto-citymap
- https://github.com/PropaneDragon/Cimtographer
- https://github.com/mike77777/CitiesSkylines-Maperitive-Rules
- https://platform.openai.com/docs/guides/vision （图片可经 base64 data URI 传入）
- https://platform.openai.com/docs/guides/vision-fine-tuning （“Images can be provided either as HTTP URLs or data URLs containing Base64-encoded images.”）
