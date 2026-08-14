# Ops: Windows Game.dll evidence handoff (2026-08-15)

**Audience:** anyone on Windows with an installed Cities: Skylines II who can decompile `Game.dll`.  
**Status:** open — Mac cannot verify these type/method details from the repo alone.  
**Purpose:** gather paste-back evidence for the tool-surface plan (`list_networks` low-voltage electricity fields; optional `SimulationSystem` 8-step cap). Do **not** change product code unless asked.

Vocabulary: [CONTEXT.md](../../CONTEXT.md) — typed-network edge, `list_networks`, `wait_simulation`.

Related: [ADR-0009 typed-network graph](../adr/0009-typed-network-graph.md), [Windows onboarding](../guide/2026-08-06-windows-onboarding.md).

---

## Mac / repo status (read first)

Mac shipped `list_networks(kind=low_voltage)` as **geometry-only** with a `TODO(windows)` in `RequestHandlers.Networks.cs`. No `ElectricityFlowEdge` references remain in product code until signatures are confirmed.

**Windows task:** decompile and paste signatures (below), then implement `electricity{flow,capacity,bottleneck}` + `sort=load` on low_voltage rows. Public docs suggest `Game.Simulation.ElectricityFlowEdge` (`m_Flow`, `m_Capacity`, `isBottleneck`) via net edge or `ElectricityNodeConnection` → `ConnectedFlowEdge`.

The 8-step simulation hard cap is **not** verified from any in-repo `Game.dll` dump — optional confirm below.

---

## Setup (PowerShell)

Adjust the Steam path if yours differs.

```powershell
$GameDll = "C:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed\Game.dll"
Get-FileHash $GameDll -Algorithm SHA256
dotnet tool install -g ilspycmd   # skip if already installed
ilspycmd --version
```

Paste back: SHA-256 of `Game.dll` and `ilspycmd` version.

---

## 1. ElectricityFlowEdge for `list_networks(kind=low_voltage)`

Need: exact component/buffer types and how a placed low-voltage `Game.Net` edge entity maps to flow / capacity / bottleneck.

```powershell
ilspycmd -t Game.Simulation.ElectricityFlowEdge $GameDll
ilspycmd -t Game.Simulation.ElectricityNodeConnection $GameDll
ilspycmd -t Game.Simulation.ConnectedFlowEdge $GameDll
```

Optional (who writes / links the graph):

```powershell
ilspycmd -t Game.Simulation.ElectricityFlowSystem $GameDll
# or search in ILSpy GUI: ElectricityFlowEdge, ConnectedFlowEdge
```

**Paste back:**

1. Full type signatures (fields/properties) for the three types above — especially `m_Flow`, `m_Capacity`, `isBottleneck` (or renamed equivalents), and link fields (`m_ElectricityNode`, `m_Edge`, …).
2. A short note or decompiled snippet of **edge → flow edge** (net edge has `ElectricityFlowEdge` directly, *or* net edge → `ElectricityNodeConnection` → `ConnectedFlowEdge` buffer → flow-edge entity). Cite method/line names from your dump if obvious.

---

## 2. `SimulationSystem` 8-step hard cap (optional)

Confirm `OnUpdate` clamps simulation steps to 8 for this build (product keeps `wait_simulation` at `selectedSpeed = 8`).

```powershell
ilspycmd -t Game.Simulation.SimulationSystem $GameDll
```

In the dump, find `OnUpdate` (or the method that consumes the time bucket / advances steps). Look for a literal **8** clamp on step count.

**Paste back:** method name + nearby lines showing the clamp (or “no 8-step clamp found; here’s what limits steps instead”).

---

## 3. What NOT to do on Windows

- Do **not** edit `Mod/` or open a PR unless asked — evidence only.
- Do **not** Harmony-patch `SimulationSystem` or raise the step cap.
- Do **not** commit decompiled `Game.dll` sources into this repo.

Paste answers into a reply, or append a “Paste-back” section at the bottom of this file.
