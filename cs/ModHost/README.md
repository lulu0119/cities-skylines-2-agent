# ModHost — C# agent-loop POC

模拟「游戏内 C# agent loop」：直接调用 OpenAI 兼容 API（默认打到本地 mock），
用 function calling 驱动 5 个模拟城市工具，工具执行会改变进程内的城市状态。

## 运行

```bash
cd cs/ModHost
dotnet run -f net10.0 --project . -- "建一条路，然后跑 4 小时模拟"
```

环境变量（可选）：

- `CS2POC_BASE_URL` 默认 `http://127.0.0.1:8787/v1`
- `CS2POC_MODEL` 默认 `mock-gpt`
- `CS2POC_API_KEY` 默认 `sk-mock`（mock 不校验）

## 与真实模组的差别

- 真模组里工具执行必须排队到模拟主线程（`UIUpdate`/`ToolUpdate`，暂停时可用），
  参考 CS2MCP 的 Bridge 模式；这里直接同步执行。
- 项目多目标 `net10.0;net472`：net10.0 用于本地实跑，net472 只在编译期验证兼容性（dotnet SDK + reference assemblies）；
  真实运行需要 Unity Mono 环境，必须在游戏里实测。
- 用 OpenAI .NET SDK 是「理想情况」验证；若游戏内 Mono 加载失败，
  兜底是仿照 CS2MCP 手写最小 HTTP 客户端。
