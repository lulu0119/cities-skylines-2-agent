# Research: In-game LLM / AI agent mods

Primary-source survey of open and public projects that put an LLM (or LLM-driven agent) **inside or controlling a game**. Focus: architecture patterns useful for a Cities: Skylines II in-game AI mayor (C# + React/Gameface).

**Architecture pattern key**

| Code | Meaning |
| --- | --- |
| **(A)** | Pure in-game mod: API key / provider config in mod settings; agent loop runs in the game process |
| **(B)** | In-game bridge + external agent: mod exposes localhost HTTP/RCON/files; LLM loop lives outside |
| **(C)** | External bot/client only: Mineflayer, MCP client, etc.; little or no in-mod agent |

Sources fetched from first-party READMEs / official docs / repo trees (GitHub, project sites). Secondary blogs used only when no open source exists (noted).

---

## 1. Airicraft (required deep dive)

- **URL:** https://github.com/shinohara-rin/airicraft (default branch `dev`)
- **Game + stack:** Minecraft Java 1.21.8, Fabric, Java 21, Yarn mappings; Gradle multi-project (`root` mod + `wrapper/` CLI)
- **Pattern:** **(A)+(B) hybrid** — planner / agent runtime lives **inside** the Fabric client; public control surface is a **standalone CLI** over a **localhost HTTP bridge**
- **Where the agent loop lives:** In-mod (Java). Config under `run/config/airicraft/agent.yml` (from `agent.yml.example`). Reload via CLI or `/airicraft reload` resets active agent/planner/task state while keeping bridge session. Package namespace `ai.moeru.airicraft.*` (AIRI org affinity; author also contributes to [moeru-ai/airi](https://github.com/moeru-ai/airi))
- **UI approach:** In-game chat injection (`agent debug chat`), highlights overlays, `/airicraft reload`; primary operator UX is the external `airicraft` CLI (status, worlds, ticks pause/step, world snapshot, traces). No productized chat-mayor panel like Gameface
- **Tools / eval:** Planner tools include vision `take_a_look` and exact world reads `inspect_world` (`inspect_area` / `find_blocks` / `find_placement_sites`); guarded `place_block` / `use_block` require recent `inspect_world` ledger hits. Scenario eval under `scenarios/*/scenario.yml` with checks (`inventory_contains`, `block_state`, `batch harness `scripts/run-evaluation-scenarios`). Addon `addons/evaluator`, actionsets YAML under `actionsets/`
- **Repo layout notes (CS2-relevant):**
  - Split: **game process bridge** (`ModBridgeServer`, `ClientRuntimeController`) vs **external CLI** (`wrapper/…/AiricraftCliMain`, `HttpBridgeTransport`)
  - Bridge discovery via `~/.airicraft/bridge-state.json` (overridable) — same idea as CS2 localhost bridge port/env
  - Deterministic CLI stdout contract (`status: ok|error`, stable `error_code`) — good model for tool result schemas
  - Eval isolation: one client + bridge file + game dir per scenario; pause/step ticks for reproducible observation
  - Observability: OTLP / Weave spans on planner + vision LLM calls
- **License:** Not declared on the GitHub license field (treat as unknown / ask before reuse)
- **Maturity:** Active “under construction” research/dev Fabric agent with serious eval harness; not a polished consumer mod

Sources: [README (dev)](https://raw.githubusercontent.com/shinohara-rin/airicraft/dev/README.md), [AGENTS.md (dev)](https://raw.githubusercontent.com/shinohara-rin/airicraft/dev/AGENTS.md), [repo tree](https://github.com/shinohara-rin/airicraft/tree/dev)

---

## 2. AIRI ecosystem

### 2a. moeru-ai/airi

- **URL:** https://github.com/moeru-ai/airi
- **Game + stack:** Companion app (Web / Electron desktop); Vue/TS; game play via separate agents
- **Pattern:** **(C)** for games — Minecraft via **Mineflayer** agent service; Factorio via separate PoC; not an in-game Fabric/Forge mod for MC
- **Agent loop:** Outside the game process (AIRI “server runtime” → Minecraft / Factorio agent subgraphs in README architecture diagram)
- **UI:** AIRI’s own chat / VRM / voice UI — not in-game CS2-style panel
- **Layout notes:** Monorepo of companion + services; Minecraft path historically `services/minecraft` (see archived repo below). Factorio linked to `airi-factorio`
- **License:** MIT ([repo license](https://github.com/moeru-ai/airi))
- **Maturity:** Large, popular open companion (~47k★); game-playing is one capability among many, still evolving

Source: [AIRI README](https://github.com/moeru-ai/airi/blob/main/README.md)

### 2b. moeru-ai/airi-minecraft (archived)

- **URL:** https://github.com/moeru-ai/airi-minecraft
- **Game + stack:** Minecraft 1.20+, Node/pnpm, Mineflayer; OpenAI-compatible env keys
- **Pattern:** **(C)** external bot — connects to a Minecraft server; chat `#` commands + NL tasks
- **Agent loop:** Node process (`src/agents`, `skills`, `prompts`, `mineflayer`)
- **UI:** In-game chat commands only (from bot perspective)
- **Layout notes:** Acknowledges [Mindcraft](https://github.com/kolbytn/mindcraft) lineage; merged into `airi` under `services/minecraft` (commit noted in README)
- **License:** MIT (repo metadata)
- **Maturity:** Archived PoC; use live `airi` tree for current code

Source: [airi-minecraft README](https://github.com/moeru-ai/airi-minecraft/blob/main/README.md)

### 2c. moeru-ai/airi-factorio

- **URL:** https://github.com/moeru-ai/airi-factorio
- **Game + stack:** Factorio; pnpm monorepo — `packages/autorio` (Lua/TS Factorio mod), agent, factorio-wrapper; RCON API; optional YOLO CV models
- **Pattern:** **(B)** — Factorio mod (`autorio`) + external agent/wrapper processes over RCON / websockets
- **Agent loop:** Outside Factorio (`packages/agent`, wrapper); mod provides automation surface
- **UI:** Factorio commands / headless-oriented; AIRI companion is the product UI
- **Layout notes:** Symlink `autorio/dist` into Factorio `data/`; `.env` for hosts; CV path under `models/`
- **License:** MIT
- **Maturity:** Explicit PoC / WIP relative to main AIRI; useful Factorio bridge reference, less CS2-UI relevant

Source: [airi-factorio README](https://github.com/moeru-ai/airi-factorio/blob/main/README.md)

**Airicraft vs AIRI Minecraft path:** Airicraft is a **client Fabric mod + in-process planner** under `ai.moeru.airicraft`; AIRI’s shipped Minecraft play path is **Mineflayer external bot**. Same org/ecosystem branding, different control planes.

---

## 3. Cities: Skylines II (and CS1) related

### 3a. LancerComet/cities-skylines-2-mcp (CS2MCP)

- **URL:** https://github.com/LancerComet/cities-skylines-2-mcp
- **Game + stack:** CS2 (Windows/Steam); C# bridge mod (`CS2MCP.Bridge`); TypeScript MCP server (`mcp-server/`); .NET 8+ / Node 18+
- **Pattern:** **(B)** — in-game HTTP bridge + external MCP client (Claude Code / Desktop) owns the agent loop
- **Agent loop:** External (any MCP host). Mod only serves tools on `127.0.0.1:8642`, executes on sim main thread via ECS at `SystemUpdatePhase.UIUpdate` (works while paused)
- **UI:** None in-game for chat; screenshots + camera tools give the external AI “eyes”
- **Layout notes (directly reusable for cities-skylines-2-agent):**
  - `HttpBridgeServer` + request queue → main-thread handlers split by domain (`RequestHandlers.Build|CityData|Economy|…`)
  - Construction through native `ToolBaseSystem` / bulldozer pipelines (no bypass mutations)
  - Local Mods folder deploy (`…\Mods\CS2MCP\`); env `CS2_PATH` / `CS2MCP_PORT` / `CS2_BRIDGE_URL`
  - **44 tools**: observe / build / zone / taxes / policies / `cs2_run_simulation` — tool surface to reuse or wrap without inventing ECS plumbing
- **License:** Apache-2.0
- **Maturity:** Focused, documented gameplay MCP; strong tool coverage for mayor-style ops; not Paradox Mods–packaged product UI

Source: [CS2MCP README](https://github.com/LancerComet/cities-skylines-2-mcp/blob/master/README.md)

### 3b. ai_licia Cities: Skylines II integration

- **URL (first-party product docs):** https://www.getailicia.com/post/bring-your-city-to-life-the-ailicia-cities-skylines-ii-integration  
  Integrations hub: https://www.getailicia.com/integrations
- **Game + stack:** Proprietary desktop app + local mod **`CS2NativeTelemetryMod`** installed into CS2 Mods folder by the app
- **Pattern:** **(B)** — telemetry / control bridge in-game; agent personality and loop in ai_licia app (streaming-oriented)
- **Agent loop:** External (ai_licia). Mod monitors city lifecycle, finances, utilities, happiness; “Actions” can drive local control through the mod bridge
- **UI:** ai_licia product UI / stream co-host — not an open in-game mayor chat panel
- **Open source?** No public first-party game-mod agent repo found; treat as closed commercial reference for “bridge + external companion” UX
- **License:** Proprietary (Novasquare / ai_licia)
- **Maturity:** Shipped product integration for streamers; useful as market/UX existence proof, not as code to fork

### 3c. Other CS2 / CS1 adjacent (not full in-game mayor)

| Project | URL | Notes |
| --- | --- | --- |
| mayor-modder/Cities2-MCP | https://github.com/mayor-modder/Cities2-MCP | MCP + skills for **wiki/modding workflows**, not live city control |
| sunwood-ai-labs/cities-skylines1-agent-skill | https://github.com/sunwood-ai-labs/cities-skylines1-agent-skill | CS1 **(B)** HTTP bridge `127.0.0.1:32123` + Codex skill; MIT — pattern cousin of CS2MCP for CS1 |

No public open-source “City Agent / AI mayor” mod with full in-game chat + tools for CS2 was found beyond CS2MCP (external agent) and ai_licia (closed). This repo’s stated goal remains sparsely occupied.

---

## 4. Cross-game survey (8–15 projects)

### 4a. mindcraft-bots/mindcraft

- **URL:** https://github.com/mindcraft-bots/mindcraft
- **Game + stack:** Minecraft Java; Node.js + Mineflayer; multi-provider LLM keys
- **Pattern:** **(C)**
- **Agent loop:** External Node (`main.js`, profiles like `andy.json`); optional MineCollab task eval
- **UI:** Optional auto-open UI / Discord; bot speaks in MC chat
- **Layout notes:** Profiles + `settings.js`; task JSON under `tasks/`; coding actions sandboxed (caution in README)
- **License:** MIT
- **Maturity:** Mature research/community agent (~5k★); reference for Mineflayer loops, not in-mod packaging

Source: [Mindcraft README](https://github.com/mindcraft-bots/mindcraft/blob/main/README.md)

### 4b. MineDojo/Voyager

- **URL:** https://github.com/MineDojo/Voyager  
  Site: https://voyager.minedojo.org/
- **Game + stack:** Minecraft; Python Voyager + Node Mineflayer env; Fabric mods for research features; GPT-4
- **Pattern:** **(C)** (+ Fabric helper mods for the research stack)
- **Agent loop:** External Python — curriculum, skill library, iterative code prompting
- **UI:** Research CLI / scripts; not a player-facing in-game mayor UI
- **Layout notes:** Skill library checkpoints; `learn()` / `inference()` API; strong eval narrative in paper
- **License:** MIT
- **Maturity:** Landmark research agent (2023); still the citation baseline for embodied Minecraft LLMs

Source: [Voyager README](https://github.com/MineDojo/Voyager/blob/main/README.md)

### 4c. YuvDwi/Steve

- **URL:** https://github.com/YuvDwi/Steve
- **Game + stack:** Minecraft 1.20.1 Forge; Java 17; HTTP clients to Groq/OpenAI/Gemini
- **Pattern:** **(A)** — pure in-mod agent; API key in `config/steve-common.toml`
- **Agent loop:** In-process — TaskPlanner → LLM → ResponseParser → tick-based ActionExecutor (avoids freezing). Direct action sequences (not classic multi-round ReAct) for latency
- **UI:** **In-game overlay** (press **K**); `/steve spawn`; chat responses optional
- **Layout notes:** `com.steve.ai.{llm,action,entity,client,memory,execution}` — clearest open example of **in-mod NL → tools → tick queue** with a chat/command panel
- **License:** MIT (stated in README)
- **Maturity:** Popular consumer-facing demo (~1k★); known gaps (crafting, persistence) called out in README

Source: [Steve README](https://github.com/YuvDwi/Steve/blob/main/README.md)

### 4d. kayroye/LLMCraft

- **URL:** https://github.com/kayroye/LLMCraft
- **Game + stack:** Minecraft 1.21.4 Forge; Java 21; Ollama HTTP
- **Pattern:** **(A)** (assistant, not autonomous builder)
- **Agent loop:** In-mod on item use — context → Ollama → reply in chat
- **UI:** Chat output; Position Reader item; config for model/prompt
- **License:** BSD-3-Clause
- **Maturity:** Early WIP assistant mod — pattern for “API in mod settings + in-game chat”, minimal tool surface

Source: [LLMCraft README](https://github.com/kayroye/LLMCraft/blob/main/README.md)

### 4e. thedemon117/ai-player-v3

- **URL:** https://github.com/thedemon117/ai-player-v3
- **Game + stack:** Factorio 2.0 Lua mod + Python bridge; RCON; LM Studio / Ollama / OpenAI / Anthropic; optional MCP server in bridge
- **Pattern:** **(B)** — mod writes perception to `script-output`; bridge polls, calls LLM, returns skills via RCON
- **Agent loop:** External Python (`bridge/agent.py`); skill layer in Lua keeps LLM decisions high-level
- **UI:** In-game chat / console (`/ai-coop`, `/ai-do`); mod settings for provider
- **Layout notes:** Clean split `mod/` vs `bridge/`; optional `mcp_server.py` — same family as CS2MCP
- **License:** Not set on GitHub license API (check repo files before reuse)
- **Maturity:** Actively documented Factorio 2.0 agent; good “perception file + RCON” bridge template

Source: [ai-player-v3 README](https://github.com/thedemon117/ai-player-v3/blob/master/README.md) (default branch `master`)

### 4f. ylc395/RimTalk

- **URL:** https://github.com/ylc395/RimTalk
- **Game + stack:** RimWorld; C#; multi-provider HTTP (Gemini, OpenAI-compatible, …); Steam Workshop
- **Pattern:** **(A)** — API key in mod settings; dialogue generation in-process
- **Agent loop:** In-mod (prompt templates / Scriban → LLM → speech bubble). Dialogue-first, not full colony tool agent (expansions exist for actions)
- **UI:** Speech bubbles; mod settings tabs; debug overlay button; player dialogue modes
- **Layout notes:** Settings UX + provider table + template system — strong **C# Unity/RW-style** reference for “key in options, chat-ish output”
- **License:** CC BY-NC-SA 4.0 (README) — **non-commercial share-alike**; do not copy code lightly into Apache/MIT products
- **Maturity:** Popular Workshop dialogue mod with expansion ecosystem

Source: [RimTalk README](https://github.com/ylc395/RimTalk/blob/main/README.md)

### 4g. oidahdsah0/Rimworld_AI_Core (+ Framework)

- **URL:** https://github.com/oidahdsah0/Rimworld_AI_Core  
  Framework: https://github.com/oidahdsah0/Rimworld_AI_Framework
- **Game + stack:** RimWorld 1.6; C# /.NET Framework 4.7.2; Harmony; depends on RimAI.Framework
- **Pattern:** **(A)** — API credentials in Framework mod settings; orchestration + tools in-process
- **Agent loop:** In-mod `IOrchestrationService` five-step tool-assisted workflow; `IToolRegistryService`; `IWorldDataService` + **scheduler for main-thread safety**
- **UI:** Debug Panel; Assistant / Dialog windows (architecture diagram)
- **Layout notes:** Closest **C# “mayor/assistant with tools”** architecture to this repo’s goal: Contracts assembly, DI, LLM gateway, tooling, UI layer. Main-thread scheduling maps to CS2 `UIUpdate` queues
- **License:** MIT
- **Maturity:** Ambitious Workshop + GitHub framework (v4 phased); heavy AI-authored codebase per README — evaluate carefully, but architecture is on-target

Source: [RimAI Core README](https://github.com/oidahdsah0/Rimworld_AI_Core/blob/main/README.md)

### 4h. MinLL/SkyrimNet-GamePlugin

- **URL:** https://github.com/MinLL/SkyrimNet-GamePlugin
- **Game + stack:** Skyrim / SKSE native C++ DLL; OpenAI-compatible / OpenRouter; PrismaUI in-game chat; embedded `localhost:8080` web config
- **Pattern:** **(A)** (strongly marketed as single-DLL, no WSL/Python sidecar) — LLM/TTS work on worker threads inside the game process
- **Agent loop:** In-process (conversation, action selection, memory, triggers). Extensible Papyrus/C++ APIs for third-party action packs (e.g. IntelEngine)
- **UI:** **PrismaUI chat overlay** (modern chat client in-game) + browser wizard for keys — highly analogous to **Gameface React chat + mod settings**
- **Layout notes:** Async heavy work off game thread; streaming TTS; YAML triggers; vision via screenshots — product-grade in-game AI UX reference
- **License:** Not declared on GitHub license field (confirm before reuse)
- **Maturity:** Feature-rich shipped AI NPC platform; best-in-class “in-game chat UI” precedent among surveyed projects

Source: [SkyrimNet README](https://github.com/MinLL/SkyrimNet-GamePlugin/blob/main/README.md)

### 4i. alexanderolvera/dfhack-mcp

- **URL:** https://github.com/alexanderolvera/dfhack-mcp
- **Game + stack:** Dwarf Fortress + DFHack Remote RPC (`localhost:5000`); Node MCP server (npm `dfhack-mcp`)
- **Pattern:** **(B)/(C)** — no in-DF agent UI; external MCP client is the agent; DFHack is the bridge
- **Agent loop:** External (Claude / any MCP host). Tools are sensors (+ optional actuators behind `DFHACK_MCP_ACTUATORS`)
- **UI:** None in-game for the agent
- **Layout notes:** Preview → confirm → apply → undo for writes; Lua queries under `src/dfhack-queries/` — safety UX for destructive city tools
- **License:** ISC
- **Maturity:** Polished advisor MCP with live-fort verification culture

Source: [dfhack-mcp README](https://github.com/alexanderolvera/dfhack-mcp/blob/main/README.md)

### 4j. Bonus: ClaudeStoryteller (RimWorld)

- **URL:** https://github.com/S4L7/ClaudeStoryteller
- **Pattern:** **(A)** storyteller uses Claude API from mod settings; MIT; small scoped example of “API key in options + in-process decisions”

---

## Pattern comparison (quick matrix)

| Project | Pattern | Agent loop host | In-game chat/UI | Tool execution |
| --- | --- | --- | --- | --- |
| Airicraft | A+B | Fabric Java | Chat inject + CLI | In-client planner tools |
| AIRI MC path | C | Node Mineflayer | MC chat | Bot API |
| airi-factorio | B | External TS/agent | Limited | RCON + autorio |
| CS2MCP | B | MCP host (TS) | No | C# UIUpdate queue |
| ai_licia CS2 | B | Closed app | App UI | Local mod bridge |
| Mindcraft / Voyager | C | Node/Python | Chat | Mineflayer |
| Steve | A | Forge Java | **K overlay** | Tick actions |
| LLMCraft | A | Forge Java | Chat | Read-only context |
| ai-player-v3 | B | Python bridge | Chat/console | Lua skills + RCON |
| RimTalk | A | RimWorld C# | Bubbles | Dialogue (± expansions) |
| RimAI Core | A | RimWorld C# | Assistant windows | Tool registry + scheduler |
| SkyrimNet | A | SKSE DLL | **PrismaUI chat** | Actions / Papyrus API |
| dfhack-mcp | B | MCP host | No | DFHack RPC |

---

## Implications for cities-skylines-2-agent

Goal (from repo README): **pure in-game mod** on Paradox Mods, **Gameface React chat**, player pastes API key, tools execute on CS2 sim thread; agent loop TBD (Gameface TS vs C#).

1. **Closest product UX precedents for (A) + chat UI:** [Steve](https://github.com/YuvDwi/Steve) (overlay command panel + in-mod loop) and especially [SkyrimNet](https://github.com/MinLL/SkyrimNet-GamePlugin) (modern in-game chat + keys via local web UI, async off game thread). RimWorld’s [RimAI Core](https://github.com/oidahdsah0/Rimworld_AI_Core) is the closest **C# tool-orchestrated assistant** architecture.

2. **Closest CS2 tool plumbing:** [CS2MCP](https://github.com/LancerComet/cities-skylines-2-mcp) (Apache-2.0) already solves localhost bridge, UIUpdate queue, native tool pipelines, and a rich mayor tool set. Your product can keep **(A)** for the agent loop while **reusing or inlining** that tool layer — i.e. do not need MCP at ship time (matches README: “MCP 暂不需要”).

3. **Pattern mismatch to avoid for Paradox Mods:** Pure **(B)/(C)** like Mindcraft, Voyager, AIRI Mineflayer, or “Claude Desktop + CS2MCP only” requires an external process — fine for power users, bad for “install mod, paste key, play.” ai_licia proves the market for CS2 AI companions but as a **sidecar app**, not a self-contained mod.

4. **Agent loop TS vs C#:**
   - **Gameface / browser TS** (your `@apeira/core` POC): aligns with AIRI’s web-first agent culture and keeps the loop portable; depends on Gameface `fetch`/TLS (Windows smoke still open).
   - **C# in ModHost:** aligns with Steve / RimAI / SkyrimNet “loop beside the engine,” simpler packaging (one process), OpenAI .NET already POC’d; UI stays React via Cohtml bindings.
   - Airicraft’s split (in-mod planner + external CLI) is a research/debug pattern — useful for eval harnesses, not required for end users.

5. **Eval / safety takeaways:** Airicraft’s scenario fixtures + tick pause; dfhack-mcp’s preview/confirm for writes; CS2MCP’s `cs2_save_game` / `cs2_run_simulation` — bake “pause → act → simulate N hours → observe” into the mayor loop.

6. **Licensing caution:** Prefer MIT/Apache references (CS2MCP, Steve, RimAI, Mindcraft, Voyager, AIRI). RimTalk’s CC BY-NC-SA blocks commercial Paradox Mods reuse of its code.

**Practical recommendation:** Ship as **(A)** with Gameface chat; implement tools like CS2MCP’s C# bridge (in-process, no stdio MCP); decide loop language after Windows Gameface networking smoke — either path has strong open precedents.

---

## Source index

| Claim area | Primary sources |
| --- | --- |
| Airicraft structure / CLI / eval | https://github.com/shinohara-rin/airicraft/blob/dev/README.md , https://github.com/shinohara-rin/airicraft/blob/dev/AGENTS.md |
| AIRI / Factorio / Minecraft path | https://github.com/moeru-ai/airi , https://github.com/moeru-ai/airi-factorio , https://github.com/moeru-ai/airi-minecraft |
| CS2MCP | https://github.com/LancerComet/cities-skylines-2-mcp |
| ai_licia CS2 | https://www.getailicia.com/post/bring-your-city-to-life-the-ailicia-cities-skylines-ii-integration |
| Mindcraft / Voyager / Steve / LLMCraft | Linked READMEs above |
| Factorio ai-player-v3 | https://github.com/thedemon117/ai-player-v3 |
| RimTalk / RimAI | https://github.com/ylc395/RimTalk , https://github.com/oidahdsah0/Rimworld_AI_Core |
| SkyrimNet | https://github.com/MinLL/SkyrimNet-GamePlugin |
| DFHack MCP | https://github.com/alexanderolvera/dfhack-mcp |

*Survey date: 2026-08-06. Re-check licenses and default branches before depending on any tree.*
