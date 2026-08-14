# Cities: Skylines II multi-instance on one Windows PC

**Date:** 2026-08-14
**Status:** frozen
**Scope:** whether two *Cities: Skylines II* processes can run at once on one Windows machine, what officially locks that, and what it would mean for this in-game AI mayor mod. This is not a product change, migration, or a proposal to ship a dual-process workflow.

The official CS2 wiki (`cs2.paradoxwikis.com`) returned a Cloudflare “Client Challenge” to this research fetch. Wiki claims below are taken from indexed wiki text and edit-mode snippets, not from a full page dump. Where a wiki fetch failed, that is marked.

## Recommendation

Do **not** run two CS2 processes as a development or acceptance strategy for this repo.

| Question | Answer |
| --- | --- |
| Is multi-instance officially supported? | **No.** No first-party CS2, Steam, or Paradox document describes launching two `Cities2.exe` processes on one PC. |
| Does the Paradox license allow it? | **No.** The CS2 Steam EULA and the Paradox User Agreement both say you may install a game on different computers but **may only run a Game on one computer/device at a time**. |
| What should this project use instead? | One Steam-launched `Cities2.exe` (app id `949230`), sequential restarts, and the already-documented UI/content reload paths. Do not start `Cities2.exe` directly for the normal acceptance path. |
| If isolation is ever required? | A second **Windows user** isolates LocalLow on the same PC (one device; EULA §8 still allows that machine). A **full VM** is a second device and collides with §8. Both are untested here and not justified by current work. Windows Sandbox is a disposable VM, not a second persistent profile, and Microsoft currently documents that it cannot run multiple sandbox instances at once. |

Flags such as `-multiple` or `-allowmultiple` were **not** found in any CS2 wiki, Paradox, or Colossal Order source. `-allowmultiple` is a Valve Source 2 client flag, not a CS2 flag. Do not invent launch options.

## What officially locks a second instance

Several independent locks exist. They are not the same mechanism.

### Paradox license: one running game per device

The Steam-hosted CS2 user agreement (`store.steampowered.com/eula/949230_eula_1`) and Paradox’s current User Agreement (last update 21 January 2026) both include:

> You may install a Game on different computers but you may only run a Game on one computer/device at a time.

The same agreements forbid multiple Paradox Accounts. Paradox may require the Paradox Launcher; skipping it is a support workaround, not a supported second-instance path. [Paradox User Agreement](https://legal.paradoxplaza.com/eula?locale=en), [CS2 Steam EULA](https://store.steampowered.com/eula/949230_eula_1).

The written clause is one **computer/device** at a time: two machines (or two VMs treated as two devices) at once. Two `Cities2.exe` processes on the same Windows desktop are still one device, so that sentence is not the process mutex. Same-PC dual-run is blocked by Steam, shared LocalLow, and port `9444`. A second VM would collide with this clause. Sandboxie on one host is not a second device; it is still unsupported technically and is not a licensed dual-run path.

### Steam client: app `949230` is a single running session

CS2’s Steam AppID is **949230**. This repo’s acceptance loop launches it with `steam.exe -applaunch 949230` (optionally plus `-uiDeveloperMode`). Steam Support publishes an FAQ titled **“Failed to start game (app already running)”** ([help.steampowered.com/en/faqs/view/7CFE-6339-CEA8-DC13](https://help.steampowered.com/en/faqs/view/7CFE-6339-CEA8-DC13)). The page body did not extract in this fetch (JavaScript-rendered), but the official title is itself evidence: Steam treats a second launch of an already-running app as a failure, not as a second process.

Steamworks documents `SteamAPI_RestartAppIfNecessary` as effectively running `steam://run/<AppId>` and relaunching from the installed library copy. That is a **relaunch-through-Steam** check, not a “spawn a second copy” API. [Steamworks: steam_api.h](https://partner.steamgames.com/doc/api/steam_api).

`SteamAPI_Init` failure reasons documented by Valve are: Steam client not running; AppID unknown (missing `steam_appid.txt` when launched outside Steam); different OS user / elevation than the Steam client; no license on the active account; AppID not set up. Valve does **not** document “another process of this AppID is already running” as an `Init` failure. Whether a second `Cities2.exe` can still call `SteamAPI_Init` while the first holds the session is **unverified**.

Steam Families sharing is a **different-account** feature, not same-PC dual process. Valve’s Steam Families announcement: one owned copy can be played by one family member at a time; two members can play the same title simultaneously only if the family library contains two copies. That is two Steam accounts, typically two machines, not two `Cities2.exe` under one Windows login. [Steam Families announcement (2024-03-18, updated 2024-09-11)](https://store.steampowered.com/news/posts/?enddate=1710892745).

### Paradox Launcher

Paradox may require the latest launcher before play. Official support threads for CS2 launch failures treat hung **Paradox Launcher** / `dowser.exe` processes as blockers and tell players to kill leftover processes, reinstall the launcher, or (as a support workaround) bypass it. Forum reports of “three Paradox Launcher processes” are **stuck launchers**, not a documented multi-instance mode. [Paradox forum: game does not launch](https://forum.paradoxplaza.com/forum/threads/game-does-not-launch-from-steam-or-from-exe.1603546/), [Paradox forum: cannot resolve issue (launcher bypass .bat)](https://forum.paradoxplaza.com/forum/threads/an-error-occurred-cannot-resolve-issue.1754411/).

A Paradox support reply for the Microsoft Store / Game Pass edition states that **the MS edition does not use the launcher**. That is a storefront difference, not permission to run Steam CS2 and Game Pass CS2 together. [Paradox forum: Game Pass / MS edition](https://forum.paradoxplaza.com/forum/threads/mod-problem-game-crash-launcher-also-not-working.1820884/).

### Shared userdata (saves, mods, settings)

Official CS2 wiki (indexed text): the Windows user-data root is

```text
C:\Users\%USERNAME%\AppData\LocalLow\Colossal Order\Cities Skylines II
```

Mods must obtain it via `Colossal.PSI.Environment.EnvPath.kUserDataPath`, not a hardcoded path. Community convention puts settings under `ModsSettings/` and other data under `ModsData/`. Cache is `EnvPath.kCacheDataPath` (`…\Cities Skylines II\.cache`); temp is `EnvPath.kTempDataPath` (`%Local%\Temp\Colossal Order\Cities Skylines II`). [CS2 Wiki: Naming Folder And Files](https://cs2.paradoxwikis.com/Naming_Folder_And_Files).

Paradox support tells players that **saves live in that LocalLow tree** and to move the `saves/` folder aside before wiping the profile. Two processes under the same Windows user therefore share saves, mods, `ModsData`, settings, and logs unless something redirects the profile. [Paradox forum: Game Pass not loading (rename LocalLow folder)](https://forum.paradoxplaza.com/forum/threads/game-not-loading-from-xbox-gamepass.1805975/).

`CSII_USERDATAPATH` is a **modding-toolchain** environment variable (also `CSII_INSTALLATIONPATH`, `CSII_PDXMODSPATH`, `CSII_LOCALMODSPATH`, …). This repo’s `ModPaths` uses it at runtime when set, otherwise LocalLow. Whether the **game’s** `EnvPath.kUserDataPath` itself honors a redirected `CSII_USERDATAPATH` is **unverified** (would require inspecting `Colossal.PSI`). The toolchain default is the same LocalLow folder, so setting the variable in the user environment does not create a second profile by itself.

### Gameface CDP port `127.0.0.1:9444`

Coherent Gameface’s default DevTools port is **9444** (`DefaultDebuggerPort = 9444`). The vendor docs tell integrators to set `EnableDebugger` and `DebuggerPort` to `9444` “or any other port you prefer.” That choice is the **game’s** `cohtml::SystemSettings`, not a documented CS2 player launch flag. [Gameface: Setting up DevTools](https://docs.coherent-labs.com/cpp-gameface/integration/optional_features/devtools/devtools_cpp/), [Gameface Unity: `CohtmlSystemSettings`](https://docs.coherent-labs.com/unity-gameface/api_reference/classes/classcohtml_1_1_cohtml_system_settings/).

This repo empirically binds inspector traffic at `http://127.0.0.1:9444/json/list` when CS2 is launched with `-uiDeveloperMode`. A second process with the same inspector enabled would fail to bind that loopback port unless CS2 exposed a port override, which no first-party CS2 source documents.

### Unity single-instance (possible, not confirmed for CS2)

Unity 2022.3 documents `PlayerSettings.forceSingleInstance`: a standalone player can abort at startup if another instance of the same player is already running. CS2 1.6.0f1 in this repo’s earlier inspection is Unity `2022.3.71f1`. **Whether Colossal Order enabled that setting in the shipped player is unverified.** No public CS2 mutex name was found. Do not assume a named mutex without inspecting the installed `Cities2.exe` / Unity player.

### Steam Subscriber Agreement

The SSA licenses Steam content for personal, non-commercial use, forbids sharing the account except as Valve specifically authorizes, and binds game-specific Subscription Terms (here, the Paradox EULA). It does not contain a CS2-specific “one process” clause; the Paradox EULA is the Subscription Term that does. Circumvention language in the SSA is about IP proxies, cheating, and protocol emulation, not sandboxing. [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/).

## Methods ranked by evidence quality

Exact steps are given only where a primary source states them.

### 1. Official / first-party — do not dual-run (high)

- Launch one copy via Steam Play / `-applaunch 949230`.
- If Steam says the app is already running, Steam Support’s FAQ is to clear the leftover process, not start another copy.
- Paradox: one running game per device; one Paradox account.
- This repo: Steam path so Paradox Launcher, launch arguments, and the mod environment stay intact. Do not start `Cities2.exe` directly for normal acceptance. See [windows-mcp-game-debug-loop](../ops/2026-08-08-windows-mcp-game-debug-loop.md).

### 2. Steam launch options / `-uiDeveloperMode` (high for *flags that exist*; none enable multi-instance)

Documented or empirically used CS2 flags:

| Flag | Source | What it does |
| --- | --- | --- |
| `-developerMode` | CS2 wiki Developer mode (community-made guide; Steam Properties launch options) | In-game developer menu (Tab) / object menu (Home). |
| `-uiDeveloperMode` | This repo’s in-game evidence; wiki-citing secondary write-ups also mention `--uiDeveloperMode` | Enables Gameface inspector on `:9444`. |
| `--disableModding` / `--disableCodeModding` | CS2 wiki Basic troubleshooting (indexed text) | Vanilla / no code mods. |
| `--logsEffectiveness=DEBUG` | CS2 wiki Logging (indexed text) | Global log level. |

Dash vs double-dash is inconsistent across wiki pages (`-developerMode` vs `--disableModding`). This project’s working UI-inspector flag is **`-uiDeveloperMode`** (single dash), confirmed by `Cities2.exe` command line and `http://127.0.0.1:9444/json/list`.

No CS2 source lists `-multiple`, `-allowmultiple`, or “Allow multiple instances.” `-allowmultiple` appears in Valve’s Source 2 command-line list, not in CS2.

Steam’s own launch-options help exists ([Setting Game Launch Options](https://help.steampowered.com/en/faqs/view/7D01-D2DD-D75E-2955)) but the body did not extract here. Steam launch options are a way to pass flags into the *one* session, not a second session.

### 3. Start `Cities2.exe` directly (medium as a launcher bypass; unverified as dual-run)

Community and Paradox **support** sometimes bypass the launcher by pointing Steam launch options at `Cities2.exe` or a `.bat` (`cities2.bat %command%`). That still goes through Steam’s single app session. Running the exe outside Steam can trip `SteamAPI_RestartAppIfNecessary` (relaunches via `steam://run/949230`) unless a `steam_appid.txt` is present — Valve documents that file as a **development** exception, not a shipping dual-instance switch.

This repo already rejects direct `Cities2.exe` as the normal acceptance path because it drops Paradox Launcher, launch-arg forwarding, and the mod environment. Whether two direct `Cities2.exe` processes can stay alive together is **unverified**; no inspectable successful report was found.

### 4. Two Steam libraries (high as an *install-path* trick; does not create two processes)

This repo already uses a second Steam library (`C:\SteamLibrary`) to get `949230` off a Scoop junction. That is one install location for one running app, not two instances. Steam still tracks a single running session for the AppID.

### 5. Steam Families / two Steam accounts (high for *license sharing*; not same-PC dual process)

Valve: two family members can play the same title at once only with two owned copies, typically on two accounts/machines. One Windows login still has one Steam client. Community workarounds such as `-master_ipc_name_override` for a second Steam client are **undocumented Valve flags**; do not treat them as a CS2 method.

### 6. Game Pass + Steam (medium)

CS2 shipped on Steam and Microsoft Store / PC Game Pass (Colossal Order / Paradox, 24 October 2023). Game Pass install path is under `XboxGames\…\Content\Cities2.exe`. Paradox support: MS edition does not use Paradox Launcher. Both editions still use the same LocalLow userdata for the same Windows user. Paradox EULA still says one running game per device. Running both storefronts together is **unverified** and would still share saves/mods/CDP unless isolated.

### 7. Second Windows user (plausible isolation; unverified for CS2)

LocalLow is per Windows user, so a second Windows account would get a different `EnvPath.kUserDataPath`. Steamworks documents that `SteamAPI_Init` fails if the game is not running under the **same OS user context** as the Steam client. Fast User Switching plus one Steam client is therefore likely to fail unless each session has its own Steam. Community reports that Steam is a machine-wide singleton; that is **not** a Valve CS2 document. Unverified on this project.

### 8. Windows Sandbox / Sandboxie-Plus

Microsoft Windows Sandbox is a disposable hypervisor VM; closing it deletes state; **“Windows Sandbox currently doesn't allow multiple instances to run simultaneously.”** [Windows Sandbox overview](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-overview). That is not a CS2 multi-instance tool.

**Sandboxie-Plus** is a kernel filter (file / registry / named objects), not a second OS. Official isolation model ([Sandbox Hierarchy](https://sandboxie-plus.github.io/sandboxie-docs/Content/SandboxHierarchy.html)):

| Resource | Isolated by default? | What that means for CS2 |
| --- | --- | --- |
| Files / LocalLow userdata | **Yes** (copy-on-write into `FileRootPath`, default `C:\Sandbox\%USER%\%SANDBOX%`). Profile writes go under `user\current`. Unmodified host files are readable. | A boxed `Cities2.exe` would get its own `AppData\LocalLow\Colossal Order\Cities Skylines II` copy. Saves, `ModsData`, settings would not race the host profile. Host `Mods\` is still readable until written. |
| Registry | **Yes** (sandboxed `RegHive`) | Game/Steam per-user keys split. |
| Named IPC (mutex / mutant / event / section) | **Yes, stricter than files.** Sandboxed programs are **never** allowed host IPC objects, not even read-only, unless `OpenIpcPath` opens them. Purpose stated by the vendor: “run the same program sandboxed and un-sandboxed side-by-side.” | If CS2 uses a Unity `forceSingleInstance` named mutant, a box would likely get its own and a second process could start. **Unverified** on CS2 (mutex name unknown). |
| TCP listen (`127.0.0.1:9444`) | **No.** Sandboxie does not give a second network stack. Plus WFP `NetworkAccess` is allow/block; `BindAdapter` picks an outbound NIC. Neither virtualizes loopback binds. | Two `-uiDeveloperMode` inspectors still fight for port **9444**. |
| Steam license / app `949230` | **No.** Boxing only `Cities2.exe` still calls `SteamAPI_Init` against the host client. | One Steam Play session. A second Steam *inside* a box is the generic two-account pattern; it needs a **second owned copy**, and Steam-in-Sandboxie is currently fragile (2026 client updates: [issue 5283](https://github.com/sandboxie-plus/Sandboxie/issues/5283), [issue 5203](https://github.com/sandboxie-plus/Sandboxie/issues/5203)). |
| GPU / RAM | Shared with the host | Two CS2 working sets on one card. Official recommended spec is already 16 GB RAM / 10 GB+ VRAM **per instance**. |
| Large file copy-in | `CopyLimitKb` default **49152** (48 MB). Bigger host files are read-only in the box (`SBIE2102`). [CopyLimitKb](https://sandboxie-plus.github.io/sandboxie-docs/Content/CopyLimitKb.html) | CS2 saves and some assets exceed 48 MB. A boxed instance that tries to rewrite a large host save will fail unless the limit is raised or the file is created inside the box. |

No Paradox, Colossal Order, or CS2-wiki procedure for sandboxed dual CS2 exists. Do not `OpenIpcPath` Steam’s `Steam3Master_SharedMem*` into the host if the goal is a second client — that *joins* the host Steam instead of isolating it ([OpenIpcPath](https://sandboxie-plus.github.io/sandboxie-docs/Content/OpenIpcPath.html) is “access resources … outside the sandbox”).

For this mod: a boxed second `Cities2.exe` would hide writes under `FileRootPath`, so `dotnet build` / `npm run dev` into host `CSII_USERDATAPATH\Mods\` would not update the boxed profile unless that path is opened (`OpenFilePath`) — which then re-shares the mod tree. Gameface CDP still cannot bind twice. Keep one Steam-launched instance.

### 9. Full VM (generic isolation; unverified; ToS)

A second Windows VM would isolate userdata, GPU (if passed through), and Steam. No first-party CS2 guide. Paradox’s one-device clause still applies. RAM/VRAM would be two full CS2 working sets.

## Isolation requirements

| Resource | Shared by default? | Isolated how? | Evidence |
| --- | --- | --- | --- |
| `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II` (saves, Mods, ModsData, settings, Player.log) | Yes, per Windows user | Different Windows user or VM. `CSII_USERDATAPATH` override of the **game** is unverified. | CS2 wiki Naming Folder And Files; Paradox support; this repo `ModPaths` |
| Gameface CDP `127.0.0.1:9444` | Yes, loopback port | Only if CS2 exposed a debugger port setting (not documented for players) | Gameface `DefaultDebuggerPort = 9444`; this repo CDP helpers |
| Steam app `949230` session | Yes, one Steam client | Second Steam account on another machine, or two owned copies via Families — not two processes on one login | Steam Families announcement; Steam “app already running” FAQ title |
| Paradox Launcher / `dowser.exe` | Yes | MS Store edition skips it; Steam edition uses it unless bypassed | Paradox support |
| Paradox account / PDX Mods | Yes; one account allowed | Officially forbidden to create a second Paradox account | Paradox User Agreement §2; PDX Mods FAQ (platform account linked to Paradox account) |
| Same save file | Same `saves/` directory | Two writers on one `.cok`/save is undefined. Official support never describes concurrent load of one save. | Paradox support: saves in LocalLow `saves/` |
| Different saves | Same profile lists all saves | Two processes would still share the save catalog and last-played metadata unless userdata is split | Inference from shared profile; not independently tested |
| Mod DLL under `Mods\CitiesSkylines2Agent` | Yes | Split userdata, or sequential use. CS2 loads via `Assembly.Load(byte[])` so the **file lock** is not the unique blocker; two live hosts would still share one on-disk build. | [cs2-mod-hot-reload](./2026-08-10-cs2-mod-hot-reload.md); `AGENTS.md` |

**Same save / different saves:** there is no official “open this city twice.” Loading the same save in two processes would mean two simulations writing one profile. Loading different saves in two processes still shares `ModsData`, settings, PDX login, logs, and CDP. Isolated userdata directories are a prerequisite for any honest dual-run experiment; they are not a supported CS2 feature.

## Implications for this mod

This product is one in-game mod inside one `Cities2.exe`. Dual-instance does not help the shipped design.

- **Acceptance loop:** Steam `949230` → Paradox Launcher Play → Gameface CDP `:9444` → Continue Game. A second process would steal or fail the CDP port and confuse Windows-MCP window targeting (`Cities2.exe` / title `Cities: Skylines II`).
- **DLL deploy:** `AGENTS.md` still says close the game before `dotnet build`. Hot-reload research found CS2 does not keep a Win32 lock on the deployed DLL (`Assembly.Load(byte[])`), but the loaded assembly, `IMod`, and ECS registrations are preserved until restart. Two processes would not give a live C# swap; they would only risk two hosts reading one `Mods\` tree and one `ModsData\CitiesSkylines2Agent` runtime directory (logs, state, hot-reload payloads).
- **UI rebuild:** `npm run build` / `npm run dev` write to `CSII_USERDATAPATH\Mods\<mod id>`. Two games watching the same Mods folder would both remount Gameface on the same writes.
- **Session model:** ADR 0007 — the Agent is mayor of the **current save**; switching cities clears the session. Concurrent sessions are an explicit long-term goal, not something dual-process fakes.
- **RAM/VRAM:** Official Steam requirements for **one** copy: minimum **8 GB RAM** and GTX 970 **4 GB** VRAM; recommended **16 GB RAM** and RTX 3080 **10 GB** / RX 6800 XT **16 GB** VRAM; **60 GB** disk. [Steam store app 949230](https://store.steampowered.com/app/949230/Cities_Skylines_II/). This repo’s 2026-08-06 ops note observed a hung `Cities2.exe` using **several GB** of RAM. There is **no** official dual-instance memory figure; do not invent one. Two full cities would be at least two recommended working sets plus OS/Steam, which is outside the published single-instance spec.

## Open unknowns

- Whether the shipped CS2 player has Unity `forceSingleInstance` enabled, and if so the mutex name.
- Whether a second `Cities2.exe` started while the first is running is killed by Steam, by Unity, by a CS2-specific check, or can limp along without Steamworks.
- Whether `EnvPath.kUserDataPath` reads `CSII_USERDATAPATH` or only the LocalLow default.
- Whether CS2’s Gameface inspector port can be changed without a private engine setting.
- Whether two processes can open different saves in one shared `saves/` folder without corrupting last-played / cloud / PDX metadata.
- Whether Steam + Game Pass copies can run together on one user (ToS still forbids it even if the processes start).
- Full body of Steam FAQs “Failed to start game (app already running)” and “Setting Game Launch Options” (pages exist; bodies were JS-gated in this fetch).
- Full CS2 wiki Launch Parameters page (URL exists; Cloudflare blocked the fetch).
- Any CS2-specific Sandboxie/Windows Sandbox success report with process list, ports, and userdata paths.

## Sources

Primary:

- [Paradox Interactive User Agreement](https://legal.paradoxplaza.com/eula?locale=en) (21 January 2026) — one Paradox account; launcher may be required; one running game per computer/device.
- [Cities: Skylines II Steam EULA](https://store.steampowered.com/eula/949230_eula_1) — same Games and Launcher clause.
- [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/).
- [Steam store: Cities: Skylines II (app 949230)](https://store.steampowered.com/app/949230/Cities_Skylines_II/) — system requirements.
- [Steamworks `steam_api.h`](https://partner.steamgames.com/doc/api/steam_api) — `SteamAPI_Init`, `SteamAPI_RestartAppIfNecessary`.
- [Steam Support: Failed to start game (app already running)](https://help.steampowered.com/en/faqs/view/7CFE-6339-CEA8-DC13) — title only in this fetch.
- [Steam Families announcement](https://store.steampowered.com/news/posts/?enddate=1710892745) — one copy = one concurrent family player.
- [Unity 2022.3 `PlayerSettings.forceSingleInstance`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PlayerSettings-forceSingleInstance.html).
- [Gameface DevTools setup](https://docs.coherent-labs.com/cpp-gameface/integration/optional_features/devtools/devtools_cpp/) and [`DefaultDebuggerPort = 9444`](https://docs.coherent-labs.com/unity-gameface/api_reference/classes/classcohtml_1_1_cohtml_system_settings/).
- [Windows Sandbox overview](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-overview).
- Sandboxie-Plus: [Sandbox Hierarchy](https://sandboxie-plus.github.io/sandboxie-docs/Content/SandboxHierarchy.html) (file/registry/IPC namespaces), [FileRootPath](https://sandboxie-plus.github.io/sandboxie-docs/Content/FileRootPath.html), [OpenIpcPath](https://sandboxie-plus.github.io/sandboxie-docs/Content/OpenIpcPath.html), [CopyLimitKb](https://sandboxie-plus.github.io/sandboxie-docs/Content/CopyLimitKb.html), [WFP NetworkAccess](https://sandboxie-plus.github.io/sandboxie-docs/PlusContent/WFPSupport/), [BindAdapter](https://sandboxie-plus.github.io/sandboxie-docs/Content/BindAdapter.html).
- Sandboxie + Steam (community, not CS2): [issue 5283](https://github.com/sandboxie-plus/Sandboxie/issues/5283), [issue 5203](https://github.com/sandboxie-plus/Sandboxie/issues/5203).
- CS2 wiki (indexed/edit snippets; full fetch blocked): [Developer mode](https://cs2.paradoxwikis.com/Developer_mode), [Naming Folder And Files](https://cs2.paradoxwikis.com/Naming_Folder_And_Files), [Debugging](https://cs2.paradoxwikis.com/Debugging), [Basic troubleshooting](https://cs2.paradoxwikis.com/Basic_troubleshooting), [Launch Parameters](https://cs2.paradoxwikis.com/Launch_Parameters) (page exists, body not retrieved).
- [Paradox: CS2 Modding](https://www.paradoxinteractive.com/games/cities-skylines-ii/modding).
- Paradox forum support: [Game Pass LocalLow/saves](https://forum.paradoxplaza.com/forum/threads/game-not-loading-from-xbox-gamepass.1805975/), [MS edition does not use launcher](https://forum.paradoxplaza.com/forum/threads/mod-problem-game-crash-launcher-also-not-working.1820884/), [launcher reinstall / hung processes](https://forum.paradoxplaza.com/forum/threads/game-does-not-launch-from-steam-or-from-exe.1603546/).
- [Colossal Order: CS2 release (Steam and Microsoft Store)](https://colossalorder.fi/news/cities-skylines-ii-release/).

This repo (product constraints, not first-party CS2 policy):

- [`Mod/Agent/ModPaths.cs`](../../Mod/Agent/ModPaths.cs)
- [2026-08-08 Windows MCP game debug loop](../ops/2026-08-08-windows-mcp-game-debug-loop.md)
- [2026-08-06 Windows onboarding](../guide/2026-08-06-windows-onboarding.md)
- [2026-08-10 CS2 mod hot reload](./2026-08-10-cs2-mod-hot-reload.md)
- [ADR 0007 session lifecycle](../adr/0007-session-lifecycle.md)

Community / secondary (leads only):

- Steam Community guide “How to skip Paradox Launcher” — launcher bypass, not dual-run.
- Paradox forum thread “Game won't stop on Steam” (2024) — hung Steam session, not multi-instance.
- Steam Community guide “Run 2 Accounts on One Computer” — second Steam folder in Sandboxie, assumes a second owned copy; not CS2.
- No inspectable CS2 Sandboxie dual-run report (process list + ports + userdata paths) was found.
