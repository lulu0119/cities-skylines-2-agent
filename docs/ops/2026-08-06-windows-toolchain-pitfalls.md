# Windows / CS2 模组工具链踩坑记

**环境实录：** 2026-08-06，Windows 11，GTX 1070，Steam via Scoop（`scoop/apps/steam/current` → `nightly-*` → `persist/steam`），游戏 `1.6.0f1`。  
**日志位置：** `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\`（`Player.log`、`Logs\Modding.log`）。

官方工具链入口：游戏内 **选项 → 模组 → 自动安装**。依赖大致顺序：Unity Editor → Unity License → Unity Mod Project → .NET SDK → C# template → Node.js → UI template（`create-csii-ui-mod`）→ IDE。

相关参考：[windows-onboarding](../guide/2026-08-06-windows-onboarding.md)、[research-in-game-ai-mods](../research/2026-08-06-research-in-game-ai-mods.md)。

---

## 1. 启动黑屏（进程还在，画面全黑）

### 现象
- `Cities2.exe` 占用数 GB 内存，窗口黑屏。
- `Player.log` 大量重复：

```text
Kernel 'KDepthDownsample8DualUav' not found
ArgumentException: Kernel 'KDepthDownsample8DualUav' not found
→ HDRenderPipeline / MipGenerator 创建失败
```

### 原因
HDRP compute shader 初始化失败。本机曾强制 D3D12（`-force-d3d12`），与驱动/资源组合下易炸；也可能是校验不完整的安装。

### 解决
1. 结束卡住的 `Cities2` / `UnityCrashHandler64`。
2. Steam 启动选项先清成空，或只用 `--disableModding` 排查模组；**不要**一上来就加 `-force-d3d12` / `-uiDeveloperMode`。
3. Steam → 已安装文件 → **验证游戏文件完整性**。
4. 仍黑：把 `LocalLow\Colossal Order\Cities Skylines II` **改名备份**（勿删，存档在里面），让游戏重建配置。
5. 本机后来以 **D3D11** 正常进主菜单。

### 开发用启动选项（能进游戏后再加）
```text
-developerMode
```
需要 UI CDP 时再加 `-uiDeveloperMode`。

---

## 2. Unity 中国区 `…f2c1` vs 工具链期望 `…f2`

### 现象
```text
Installing Unity to directory 'C:\Program Files\Unity 2022.3.62f2'
Error while installing dependency "Unity license":
Unity installation path is incorrect or does not exist: Editor\Unity.exe
```
或 **Unity Mod Project** 报同样的 `Editor\Unity.exe`（UI 里有时显示成 `EditorUnity.exe`，少了反斜杠）。

### 原因
- 国内安装器常装成 `C:\Program Files\Unity 2022.3.62f2c1`（`c1` = China）。
- 注册表 `InstallLocation` 可能为空；`Unity Technologies\Installer` 只有 `Unity 2022.3.62f2c1`。
- 工具链按 **`2022.3.62f2`（无 c1）** 查路径；查不到时 `unityPath` 为空，拼出相对路径 `Editor\Unity.exe`。
- 游戏静默安装国际版到 `Unity 2022.3.62f2` **可能失败**（目录根本不出现），但注册表已被写成指向该空路径。

### 解决（本机最终有效做法）
1. **不要长期依赖 junction 指向 c1 再让静默安装往里写**——安装器与 junction 搅在一起容易装丢。
2. 若只有 c1、国际版目录不存在：把  
   `C:\Program Files\Unity 2022.3.62f2c1`  
   **重命名**为  
   `C:\Program Files\Unity 2022.3.62f2`  
   （实体目录，中国区 Editor 可用于初始化 Mod 项目）。
3. 补注册表（管理员），让 Installer / Uninstall 都指向真实目录：

```powershell
$intl = "C:\Program Files\Unity 2022.3.62f2"
New-Item "HKLM:\SOFTWARE\Unity Technologies\Installer\Unity 2022.3.62f2" -Force | Out-Null
New-ItemProperty "HKLM:\SOFTWARE\Unity Technologies\Installer\Unity 2022.3.62f2" -Name "Location x64" -Value $intl -PropertyType String -Force
New-ItemProperty "HKLM:\SOFTWARE\Unity Technologies\Installer\Unity 2022.3.62f2" -Name "Version" -Value "2022.3.62f2" -PropertyType String -Force
# Uninstall 键同样写 DisplayName / DisplayVersion / InstallLocation / UninstallString / DisplayIcon
```

4. 安装 [Unity Hub](https://unity.com/download)，登录 **Personal** 许可。验证：

```powershell
& "C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -logFile "$env:TEMP\unity-lic.log"
# 期望：Exiting batchmode successfully / Successfully updated license
```

5. 弹窗没有「重试」时：点 **继续**，再进 **选项 → 模组 → 自动安装**，或重启游戏后再装。

---

## 3. Unity Mod Project 初始化

### 现象
工具链过了 License 后卡在 / 报错 **Unity模组项目**；或手动 `-projectPath` 秒退。

### 原因
- `unityExe` 无效（见 §2）。
- 项目在  
  `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Modding\UnityModsProject`  
  路径含空格；`Start-Process -ArgumentList` 拆参会导致  
  `Couldn't set project path to: .../Colossal`。
- `IsInstalled` 需要解压后的工程里有 **`Library/`** + `ProjectSettings/ProjectVersion.txt`。

### 解决
用**整串引号**跑 batchmode（或让游戏内工具链来跑）：

```powershell
$unity = "C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe"
$proj  = "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Modding\UnityModsProject"
$log   = "$env:TEMP\cs2-unity-modproject.log"
$arg   = "-batchmode -nographics -projectPath `"$proj`" -logFile `"$log`" -quit"
Start-Process $unity -ArgumentList $arg -Wait
# 期望：Library 目录出现；log 含 Exiting batchmode successfully
```

ZIP 源在游戏内：  
`Cities2_Data\Content\Game\.ModdingToolchain\UnityModsProject.zip`。

---

## 4. UI 模板「版本过低」（Scoop + npm link）

### 现象
模组工具链里 **UI 模板 / UI Mod Project Template** 一直提示版本过低或需要更新；点更新后仍红。

### 原因（已反编译确认）
- 依赖是 `@colossalorder/create-csii-ui-mod`，安装方式为对游戏目录  
  `.ModdingToolchain\npx-create-csii-ui-mod` 做 **`npm link`（junction）**。
- `LongFile.TryGetSymlinkTarget` **并不判断是否为 symlink**：只要路径能打开，就用  
  `GetFinalPathNameByHandle` 取出**解析 junction 后的最终路径**，再和  
  `Path.GetFullPath(kNpxPackagePath)` 做**字符串相等**比较。
- `Path.GetFullPath` **不会**解析 junction；Scoop 下游戏常从  
  `apps\steam\current\...` 启动，而最终路径是 `persist\steam\...` → **永远不相等**。
- 因此「改成真实目录拷贝」也没用：拷贝仍会被 `GetFinalPathName` 打开，最终路径落在 nvm/prefix 下，照样对不上。
- 每次点「安装/更新」游戏会再 `npm link`。

### 解决（本机已用，且有效）
仅改 `libraryfolders` 指向 `persist` **会被 Steam 写回** `apps\steam\current`，进程路径仍带 Scoop junction，比较继续失败。

可靠做法：把 CS2 **物理挪到无 reparse 的库**（不要 junction）：

1. 完全退出 Steam / CS2（并关掉会扫游戏目录的 `findstr` 等）。
2. 游戏装到例如 `C:\SteamLibrary\steamapps\common\Cities Skylines II`（同盘 `Move`/`robocopy /MOVE`）。
3. 在 `...\steam\config\libraryfolders.vdf`（以及 `steamapps\libraryfolders.vdf`）增加库 `"path" "C:\\SteamLibrary"`，把 `949230` 挂到该库。
4. `npm link` 指向新目录下的 `npx-create-csii-ui-mod`；校验  
   `GetFinalPathName(全局 create-csii-ui-mod) == GetFullPath(游戏内 npx 路径)`。
5. 开 Steam → CS2，任务管理器确认 `Cities2.exe` 在 **`C:\SteamLibrary\...`**，再进选项 → 模组。

**不要**再用「拷贝全局包」或「只改 persist 库路径」——前者对不上路径，后者会被 Steam 还原。
---

## 5. C# 模板 / Mod.props 不同步

### 现象
C# 模板显示过期，或 `dotnet new list` 没有 `csiimod`。

### 原因
`ProjectTemplateDependency` 用 CRC 比较游戏内 `Mod.props` / `Mod.targets` 与用户缓存，并检查 `.templateengine` 里的 nupkg。

### 解决

```powershell
$game = "...\Cities2_Data\Content\Game\.ModdingToolchain"
$user = "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Modding"
Copy-Item "$game\Mod.props","$game\Mod.targets" $user -Force
dotnet new uninstall ColossalOrder.ModTemplate
dotnet new install "$game\ColossalOrder.ModTemplate.1.0.0.nupkg" --force
dotnet new list | Select-String csiimod
```

---

## 6. 本机其它环境笔记

| 项 | 实录 |
|---|---|
| .NET | `winget install Microsoft.DotNet.SDK.10` → `10.0.302`；工具链日志会出现 “Welcome to .NET 10.0!” |
| Node | 工具链期望约 **20.11**，最低 **18**；Scoop nvm 的 22.x 一般能过 Node 检查 |
| **.NET 6 runtime** | `ModPostProcessor` / `ModPublisher` 为 **net6.0**；只装 .NET 10 SDK 会报 “You must install or update .NET”。`winget install Microsoft.DotNet.Runtime.6` |
| 游戏 Content 路径 | 开发库：`C:\SteamLibrary\steamapps\common\Cities Skylines II\...`（见 §4） |
| 模组 Playset | 可空；工具链与是否启用玩法模组无关 |
| 弹窗按钮 | 常见只有 **继续** / **退出游戏**，没有「重试」——继续后再进模组页装 |

---

## 7. 推荐排查顺序（下次）

1. `Player.log` 是否还在刷 `KDepthDownsample` → §1  
2. `Test-Path 'C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe'` → §2  
3. Unity Hub 许可 batchmode → §2  
4. `UnityModsProject\Library` 是否存在 → §3  
5. `Cities2.exe` 是否在 **`C:\SteamLibrary\...`**（无 Scoop junction）+ npm link 路径字符串匹配 → §4  
6. `dotnet new list` 是否有 `csiimod` + user tooling 下 Mod.props → §5  

---

## 8. 与本仓库目标的关系

工具链装好后才能：`dotnet new csiimod`、`npx create-csii-ui-mod`，再按 [windows-onboarding](../guide/2026-08-06-windows-onboarding.md) 构建 `Mod/`。历史三项冒烟结果见 [archive M1](../../archive/docs/2026-08-06-m1-smoke.md)。  
Scoop + 中国区 Unity 不是官方文档假设环境；上述修复是 **本机实测 workaround**，不是 CO 官方流程。
