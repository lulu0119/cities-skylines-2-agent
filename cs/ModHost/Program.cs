using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;

namespace ModHost;

// Simulates the part that would live inside a CS2 mod:
//   an agent loop (LLM function calling) + tools that mutate game state.
// In the real mod, tool execution is queued to the simulation main thread
// (UIUpdate/ToolUpdate, works while paused), same pattern as CS2MCP.
internal static class Program
{
    private const string Instructions =
        "你是《城市：天际线 2》里的 AI 市长助手。先读取城市状态，再根据用户要求调用工具执行操作。工具执行会真实改变模拟状态；执行后把结果用中文简要汇报。";

    private static int Main(string[] args)
    {
        var apiKey = Environment.GetEnvironmentVariable("CS2POC_API_KEY") ?? "sk-mock";
        var baseUrl = Environment.GetEnvironmentVariable("CS2POC_BASE_URL") ?? "http://127.0.0.1:8787/v1";
        var model = Environment.GetEnvironmentVariable("CS2POC_MODEL") ?? "mock-gpt";
        var prompt = args.Length > 0 ? string.Join(" ", args) : "建一条路，然后跑 4 小时模拟";

        var client = new ChatClient(model, new ApiKeyCredential(apiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(baseUrl),
        });

        var city = new CitySim();
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(Instructions),
            new UserChatMessage(prompt),
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.2f,
            Tools =
            {
                ChatTool.CreateFunctionTool(
                    "get_city_overview",
                    "读取当前城市状态（人口、幸福感、预算、需求、道路、税率、暂停状态）。",
                    BinaryData.FromString("""{"type":"object","properties":{}}""")),
                ChatTool.CreateFunctionTool(
                    "build_road",
                    "修建一条道路，会消耗预算并改善交通与需求。",
                    BinaryData.FromString("""{"type":"object","properties":{"start":{"type":"array","items":{"type":"number"}},"end":{"type":"array","items":{"type":"number"}}},"required":["start","end"]}""")),
                ChatTool.CreateFunctionTool(
                    "zone_area",
                    "规划一块区域（residential/commercial/industrial），会消耗预算并增加对应区划。",
                    BinaryData.FromString("""{"type":"object","properties":{"type":{"type":"string","enum":["residential","commercial","industrial"]},"x":{"type":"number"},"y":{"type":"number"},"size":{"type":"number"}},"required":["type","x","y","size"]}""")),
                ChatTool.CreateFunctionTool(
                    "set_tax_rate",
                    "设置税率（0-30），提高税率增加收入但降低幸福感。",
                    BinaryData.FromString("""{"type":"object","properties":{"rate":{"type":"number","minimum":0,"maximum":30}},"required":["rate"]}""")),
                ChatTool.CreateFunctionTool(
                    "run_simulation",
                    "把模拟向前推进指定游戏内小时数，观察城市变化。",
                    BinaryData.FromString("""{"type":"object","properties":{"hours":{"type":"number","minimum":1,"maximum":24}},"required":["hours"]}""")),
            },
        };

        for (var step = 0; step < 12; step++)
        {
            var completion = client.CompleteChat(messages, options);
            if (completion.Value.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(new AssistantChatMessage(completion.Value));
                foreach (var toolCall in completion.Value.ToolCalls)
                {
                    var result = ExecuteTool(city, toolCall.FunctionName, toolCall.FunctionArguments.ToString());
                    Console.WriteLine($"[tool] {toolCall.FunctionName} -> {result}");
                    messages.Add(new ToolChatMessage(toolCall.Id, result));
                }
                continue;
            }

            Console.WriteLine($"[ai] {completion.Value.Content[0].Text}");
            return 0;
        }

        Console.Error.WriteLine("agent loop reached max steps");
        return 1;
    }

    private static string ExecuteTool(CitySim city, string name, string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;
        return name switch
        {
            "get_city_overview" => city.Overview(),
            "build_road" => city.BuildRoad(),
            "zone_area" => city.ZoneArea(
                root.GetProperty("type").GetString() ?? "residential",
                root.GetProperty("x").GetDouble(),
                root.GetProperty("y").GetDouble(),
                root.GetProperty("size").GetInt32()),
            "set_tax_rate" => city.SetTaxRate(root.GetProperty("rate").GetDouble()),
            "run_simulation" => city.RunSimulation(root.GetProperty("hours").GetInt32()),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown tool"),
        };
    }
}
