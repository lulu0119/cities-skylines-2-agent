# Cities: Skylines II mod hot reload: feasible layers and architecture

**Date:** 2026-08-10
**Scope:** development-time iteration without restarting *Cities: Skylines II*. This is not a proposal to ship a required external agent process.

## Recommendation

Treat “hot reload” as three different capabilities:

| Layer | Feasible without restarting the game? | Recommendation |
| --- | --- | --- |
| Skills, prompts, tool catalog, other data | **Yes** | Implement first: embedded shipping defaults plus an optional development override directory, parsed into one immutable, last-known-good content snapshot. |
| React / Gameface UI | **Yes, already the intended workflow** | Keep `-uiDeveloperMode` and `npm run dev`; preserve chat state across a Gameface remount and keep runtime logs/state outside the watched mod directory. |
| C# code that owns Unity/ECS systems or Cohtml bindings | **Not as a general in-process DLL swap** | Keep this in a stable in-game host. CS2 loads DLL bytes without locking the deployed file, but deliberately preserves the loaded assembly, `IMod` instances and registered systems; its supported path is restart. |
| Pure agent policy/orchestration C# behind a stable seam | **Technically plausible; requires an in-game spike** | Prefer a secondary `AppDomain` with a JSON/string interface, shadow-copy/unique payload paths and atomic adapter swap. Keep a restartable development worker as the robust fallback if Unity's hosted Mono cannot unload reliably. |

This gives fast iteration for the code that changes most—skills, prompts, tool descriptions, planning policy and UI—while the stable host continues to own the simulation-thread queue, Unity objects, ECS system registration, Cohtml bindings, settings and cancellation. If the fallback worker is needed, it remains a development adapter and is never shipped to players.

## Evidence and constraints

### CS2's managed target is .NET Framework-shaped, not modern .NET

The installed official CS2 modding toolchain currently sets `TargetFramework` to `net48`; this project imports those toolchain props rather than choosing a framework itself. The stock C# template also imports `Mod.props`, and a public mirror identifies itself as an unmodified snapshot of the official toolchain templates. See the [project file](../../Mod/CitiesSkylines2Agent.csproj), [stock template project](https://github.com/CitiesSkylinesModding/StockModTemplatesDiffer/blob/main/dotnet/StockMod.csproj), and the [mirror's provenance statement](https://github.com/CitiesSkylinesModding/StockModTemplatesDiffer#readme). The installed toolchain remains the authority; the mirror is useful only as a public, inspectable snapshot.

`AssemblyLoadContext` is therefore not the available unload mechanism. Microsoft documents that .NET Framework cannot unload an individual assembly: every `AppDomain` containing it must be unloaded. `AssemblyLoadContext` is the corresponding mechanism for .NET Core / modern .NET. [Microsoft: load and unload assemblies](https://learn.microsoft.com/en-us/dotnet/standard/assembly/load-unload), [Microsoft: collectible `AssemblyLoadContext`](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability).

The CLR normally locks an assembly file after path-based loading. Shadow copying loads a copy so the original file can be updated, but it does **not** replace the executing code; applying the new version still requires a new/unloaded application domain. [Microsoft: shadow-copying assemblies](https://learn.microsoft.com/en-us/dotnet/framework/app-domains/shadow-copy-assemblies). CS2 is a noteworthy exception to the file-lock part because its loader uses `Assembly.Load(byte[])`; that still does not make the loaded assembly unloadable.

Unity 2022.3's Mono backend is a Unity fork of Mono and JIT-compiles managed code. Unity's documented whole-domain script reload sequence is an **Editor Play Mode** operation: Unity stops the C# domain, disconnects managed wrappers from native objects, destroys the Unity child domain, creates a new one, reloads assemblies and restores serialized state. That documentation does not establish a callable domain-reload facility in a built game such as CS2. [Unity: Mono overview](https://docs.unity3d.com/2022.3/Documentation/Manual/Mono.html), [Unity: details of entering Play Mode](https://docs.unity3d.com/Manual/configurable-enter-play-mode-details.html).

No first-party CS2 documentation defines a supported transaction for unloading a live code mod, replacing its assembly, and loading it again. The official architecture asks a mod to create systems and register them in the game's update loop; the official guides explain creation and integration, not live managed-assembly replacement. [Paradox/Colossal Order: Code Modding development diary](https://www.paradoxinteractive.com/games/cities-skylines-ii/modding/dev-diary-3-code-modding), [CS2 Wiki: Creating UI and Code Mods](https://cs2.paradoxwikis.com/Creating_UI_And_Code_Mods).

### The actual CS2 loader preserves old code

Local inspection of the installed first-party binaries for CS2 `1.6.0f1` (Unity `2022.3.71f1`, Mono `6.13.0`, CLR `4.0.30319.42000`) makes the constraint more precise:

- `Colossal.IO.AssetDatabase.ExecutableAsset.LoadAssemblyImpl` reads the mod stream and calls `Assembly.Load(byte[])`. Rebuilding can therefore overwrite the deployed DLL; the deployed-file lock is **not** the blocker.
- `ExecutableAsset.TransferState` copies the already loaded `Assembly` into the refreshed asset.
- `Game.Modding.ModManager.ModInfo.TransferState` copies the prior state and existing `IMod` instances. `Load` only loads an item in `Unknown` state; `Dispose` calls `IMod.OnDispose` but does not unload an assembly; `ModManager.RequireRestart()` is the explicit supported transition.
- `Game.UpdateSystem` stores direct `ComponentSystemBase` references and exposes registration/order methods but no corresponding unregister method.

These observations come from decompiling the installed runtime with ILSpy, not from a public source mirror. To make them reproducible, the inspected SHA-256 values were: `Game.dll` `721E7E17BF74299AA2B988C1BD07E90874BB8BC72D263229500C4BF639E7E4EE`, `Colossal.IO.AssetDatabase.dll` `BB62C74D70639479C00D5DDDEE85E31EDBD74CFBCA750518689AF24427DBA0D6`, and `Colossal.UI.dll` `00D53980E98433EAA0E2373FA8385DC61D6CA6CD831F5A20447D8FC732F8AC7B`. This runtime behavior matches the officially described system-registration architecture. [Paradox/Colossal Order: Code Modding](https://www.paradoxinteractive.com/games/cities-skylines-ii/modding/dev-diary-3-code-modding).

The conclusion is not “the DLL cannot be copied.” It is that refreshing assets carries forward the old managed identity and live engine registrations. Full code-mod reload is therefore unsupported by the loader and must not be faked by merely rebuilding over the deployed DLL.

Visual Studio Hot Reload is not a substitute for this design. Microsoft lists unsupported Attach-to-Process scenarios, while CS2 is an already running Unity/Mono player rather than an F5-launched .NET project. Treat debugger patching as an optional experiment, never the repeatable development workflow. [Microsoft: Hot Reload](https://learn.microsoft.com/en-us/visualstudio/debugger/hot-reload?view=visualstudio).

### ECS and bindings make “load the new DLL too” unsafe

The current mod registers four managed systems into CS2 update phases from `IMod.OnLoad`; those system types, their callbacks and their update-list membership are owned by the loaded mod assembly. See [`Mod.OnLoad`](../../Mod/Mod.cs).

Unity ECS gives systems an explicit lifecycle (`OnCreate`, updates, stop, `OnDestroy`). Destroying a system creates a synchronization point, is invalid while systems are executing, and must happen through its owning `World`. [Unity Entities: system lifecycle](https://docs.unity.cn/Packages/com.unity.entities%401.2/manual/systems-systembase.html), [Unity Entities: `World.DestroySystem`](https://docs.unity.cn/Packages/com.unity.entities%401.0/api/Unity.Entities.World.DestroySystem.html). System groups also maintain explicit update lists; adding a replacement type does not remove the old instance. [Unity Entities: system groups and manual creation](https://docs.unity.cn/Packages/com.unity.entities%401.0/manual/systems-update-order.html). In CS2 specifically, `Game.UpdateSystem` keeps its own direct references and has no unregister operation, so even destroying a world system would leave an unsafe update-loop reference.

Loading successive DLLs into the same domain—whether from a new path, new version, or byte array—does not provide a lifecycle boundary. Microsoft warns that multiple load contexts or identities can create type-identity, dependency-resolution and casting problems; assemblies loaded without context require custom dependency handling. [Microsoft: best practices for assembly loading](https://learn.microsoft.com/en-us/dotnet/framework/deployment/best-practices-for-assembly-loading). It would also leave old ECS systems, delegates, static state, bindings, model tasks and threads alive unless every owner were perfectly dismantled. Reject this as the default reload design.

The clean design is to define this error out of existence: reloadable code must not declare or retain Unity/CS2/ECS types. The stable host owns every engine-facing object for the lifetime of the game process.

## Layer 1: content hot reload

Shipping defaults for `ToolCatalog.json` and every `Agent/Skills/**/SKILL.md` are embedded resources. The current uncommitted working tree has already started the right development overlay: it reads catalog/skill overrides from `CitiesSkylines2Agent/hot-reload`, outside the watched `Mods` directory, while retaining embedded/built-in fallback. See the [`EmbeddedResource` entries](../../Mod/CitiesSkylines2Agent.csproj), [`ModPaths`](../../Mod/Agent/ModPaths.cs), [`ToolCatalog`](../../Mod/Agent/ToolCatalog.cs), and [`SkillStore`](../../Mod/Agent/SkillStore.cs).

Add one deep content module with a small interface: expose the current validated `ContentSnapshot` and an explicit `Reload` operation. Its implementation should:

1. Load embedded resources as production defaults.
2. In an explicit development mode, overlay files from a directory under the mod's existing runtime-data root, **outside** `...\Mods\...`.
3. Read and validate all related files together, then publish one immutable snapshot with a revision identifier.
4. If any file is partial or invalid, keep the previous snapshot and report one aggregated diagnostic; do not make every caller handle individual parse/file exceptions.
5. Pin a snapshot for the duration of one model turn. A reload affects the next turn, so tool names/descriptions and dispatch metadata cannot disagree halfway through a turn.

Use an explicit in-game “reload content” trigger first; automatic watching can be added later as a convenience. `FileSystemWatcher` can emit multiple events for one operation and can lose events if its buffer overflows, so an automatic implementation still needs debouncing and a full rescan/validation pass. [Microsoft: `FileSystemWatcher` remarks](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=netframework-4.8).

Do not put state, logs, screenshots or frequently written runtime files into the deployed mod asset directory. This project already observed that the game's media watcher reacts to writes there and repeatedly remounts Gameface; moving runtime writes outside `Mods` stopped the reload storm. See the [in-game debug evidence](../ops/2026-08-07-chat-ui-debug-computer-use-handoff.md#black-screen--hitch) and [repeatable game-debug loop](../ops/2026-08-08-windows-mcp-game-debug-loop.md).

## Layer 2: Gameface UI hot reload

The official CS2 UI template's development script is Webpack watch, and its output directory is `CSII_USERDATAPATH\Mods\<mod id>`. The current project preserves that arrangement with `npm run dev`. [Stock template `package.json`](https://github.com/CitiesSkylinesModding/StockModTemplatesDiffer/blob/main/ui/package.json), [stock template `webpack.config.js`](https://github.com/CitiesSkylinesModding/StockModTemplatesDiffer/blob/main/ui/webpack.config.js), [CS2 Wiki: UI Modding](https://cs2.paradoxwikis.com/UI_Modding).

The same installed-runtime inspection confirms the other half of the loop: `Colossal.UI.UILiveReload` watches local UI host paths, schedules a page reload for `.js`, `.css` and `.html`, then calls Gameface `View.Reload()`; other media changes clear unused image cache. Gameface documents `View::Reload` as a supported page reload operation. [Gameface: `cohtml::View`](https://docs.coherent-labs.com/cpp-gameface/api_reference/classes/classcohtml_1_1_view/). Thus UI hot reload is a real CS2 player feature when UI developer mode enables live reload, not an inference from Unity Editor behavior.

The practical development loop is:

```powershell
# Launch CS2 with -uiDeveloperMode, then:
Set-Location Mod/UI
npm run dev
```

Changing TSX/SCSS rebuilds the deployed media; CS2's media watcher reloads it. A media reload can remount React, so durable chat/session state must live behind the React tree (the project already uses `window.__cs2AgentChat`) and bindings must tolerate resubscription. Use Gameface CDP on `127.0.0.1:9444` to inspect the live DOM. These are empirical CS2 findings from this repository, not promises made by current generic Gameface vendor docs. See the [Gameface/CDP handoff](../ops/2026-08-07-chat-ui-debug-computer-use-handoff.md) and [CDP helper documentation](../ops/scripts/2026-08-07-gameface-cdp/README.md).

UI reload cannot change C# binding names, argument shapes or behavior. Treat the binding contract as part of the stable host interface; coordinate a game restart when that contract changes.

## Layer 3: C# logic reload behind a stable seam

### Preferred architecture: child `AppDomain` behind a stable host

Create one real seam with two adapters:

```text
Gameface / Cohtml
       |
stable in-game host  -- versioned JSON messages --  agent-policy adapter
       |                                         /                  \
Unity ECS + ToolQueue                 production: direct   development: child AppDomain
```

The stable host owns settings/API keys, conversation persistence, Cohtml bindings, `ToolQueueSystem`, all Unity/ECS queries and mutations, tool-result validation, cancellation and observability. The reloadable policy receives serializable snapshots and returns messages/tool intents; it never receives `World`, `Entity`, system instances, delegates, Unity objects, file handles or host-owned tasks.

A pure managed payload can be loaded into a secondary `AppDomain`, reached through a narrow `MarshalByRefObject` interface carrying primitive/string JSON messages, then unloaded and recreated. .NET Framework supports unloading a secondary domain, and shadow copying keeps source files replaceable. Mono documents application-domain isolation as a runtime capability, but Unity's documented domain management remains Editor-owned, so the last step must be proven in the actual CS2 player. [Microsoft: unload an application domain](https://learn.microsoft.com/en-us/dotnet/framework/app-domains/how-to-unload-an-application-domain), [Microsoft: using application domains](https://learn.microsoft.com/en-us/dotnet/framework/app-domains/use), [Mono runtime](https://www.mono-project.com/docs/advanced/runtime/).

The reload transaction should be transactional from the host's perspective:

1. Build the payload to a unique staging path; never ask CS2's `ModManager` to discover it as another mod DLL.
2. Create a new child domain, instantiate the proxy and complete a protocol/version plus health handshake before changing the active adapter.
3. Stop admitting new turns, cancel or finish the old active turn, then atomically switch the adapter reference.
4. Dispose the old payload, detach callbacks and unload the old domain. If unload fails, keep the new adapter active, report the leak clearly and require a game restart before another reload.

Avoid a custom contracts DLL if possible because it becomes another assembly identity and another asset for CS2 to scan. A BCL-only `MarshalByRefObject` proxy with string/JSON methods keeps the interface small and prevents Unity/MEAI/tool types from crossing the seam. Microsoft notes that domain-neutral assemblies remain until process shutdown and that domain unload can fail, which is why the 50-cycle in-game acceptance test is mandatory. [Microsoft: unload an application domain](https://learn.microsoft.com/en-us/dotnet/framework/app-domains/how-to-unload-an-application-domain).

This architecture hot-reloads pure orchestration and tool-selection logic, not arbitrary engine integration. A change to ECS queries, system registration, Cohtml binding contracts, settings types, or the stable host still requires a game restart. Keep that host deliberately small and slow-changing to maximize useful hot-reload coverage.

### Fallbacks

If Unity's hosted Mono cannot unload the child reliably, use a development-only restartable local worker over a versioned named-pipe protocol. Process exit is then the hard unload boundary. Named pipes are supported by .NET Framework and provide local duplex interprocess communication. The production adapter remains in-process, so this fallback does not alter the shipped one-mod product. [Microsoft: pipe operations in .NET](https://learn.microsoft.com/en-us/dotnet/standard/io/pipe-operations), [Microsoft: `System.IO.Pipes`](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes?view=netframework-4.8).

The simplest fallback is repeated `Assembly.Load(byte[])` with a unique/versioned payload and atomic adapter swap. It avoids the deployed-file lock, but it is **not true unloading**: old assemblies and any rooted state live until CS2 exits. Cap the reload count, never put engine-facing types in the payload, and treat this only as a bounded development convenience. Microsoft documents the dependency and type-identity hazards of byte-array/no-context loads. [Microsoft: best practices for assembly loading](https://learn.microsoft.com/en-us/dotnet/framework/deployment/best-practices-for-assembly-loading).

The current uncommitted `HotReloadRequestHandlerSlot` is this bounded fallback: it loads handler bytes, validates `IRequestHandlerAdapter`, and swaps only between requests while retaining the last known-good adapter. Its payload project directly references Game/Unity/ECS, so old payload assemblies cannot unload; the slot therefore caps a game session at 32 successful swaps and logs that a restart is required. This is useful for short in-game iterations, but it is not true assembly hot reload. Use the stable-host/child-domain seam for long-running reloadability. See [`HotReloadRequestHandlerSlot`](../../Mod/CS2MCP/HotReloadRequestHandlerSlot.cs) and the [payload project](../../Mod/HotReload/CitiesSkylines2Agent.HotReload.csproj).

## Safe development workflow

Use the cheapest reload boundary that covers the change:

1. **TSX/SCSS/images:** `npm run dev`; no game restart.
2. **Skills/prompts/tool descriptions:** edit the development override and trigger content reload; no rebuild or restart.
3. **Pure agent orchestration/policy:** build a new payload and trigger the child-domain reload; no game restart after the spike passes.
4. **ECS request handlers, system lifecycle, Cohtml binding contract, mod settings, stable host:** close and restart the game after `dotnet build`.
5. **Before merging/releasing:** always run the normal in-process production adapter from a clean game start. The development seam must not hide startup, disposal or packaging failures.

## Implementation order and acceptance gates

1. Implement content snapshots and explicit reload; demonstrate a skill and tool-description change on the next agent turn without restarting.
2. Keep and document the existing Gameface watch loop; demonstrate UI edit → one media reload → preserved chat/session state.
3. Extract the pure policy seam without changing behavior; test it in-process first.
4. Time-box the child-`AppDomain` spike. Demonstrate 50 reloads while a city stays loaded; check memory, threads, duplicate callbacks/bindings, one active policy revision, and correct tool execution.
5. If the spike fails, implement the named-pipe development adapter; use capped `Assembly.Load(byte[])` only if a worker is operationally unacceptable.

The key design decision is ownership: engine state stays in one stable, deep host module; reloadable adapters exchange only versioned data. That concentrates lifecycle complexity at one seam and prevents assembly unloading details from leaking across every tool and caller.
