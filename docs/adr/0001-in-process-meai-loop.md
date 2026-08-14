# In-process MEAI loop

Status: accepted

The shippable product is a Paradox mod, so the mayor loop has to run inside the game process on Unity's managed surface. We use MEAI `IChatClient` plus a hand-written function-calling loop, and enqueue every tool onto the simulation thread.

## Considered Options

- **Gameface apeira/xsai.** Rejected: Gameface has no `ReadableStream`.
- **Semantic Kernel or Microsoft Agent Framework.** Rejected: extra orchestration the mod does not need; the loop is ours.
- **An external Node or MCP process.** Rejected: players should paste an API key in mod settings, not run a sidecar.

## Consequences

The runtime is no longer provisional. Research that still says "暂定选型" is historical.
