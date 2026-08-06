using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CitiesSkylines2Agent.Agent
{
    public enum PlanStatus
    {
        None,
        Proposed,
        Approved,
        InProgress,
        Done,
        Failed,
        Cancelled,
    }

    public enum PlanStepStatus
    {
        Pending,
        InProgress,
        Done,
        Failed,
        Skipped,
    }

    /// <summary>Persistent structured task plan (one at a time, resumable).</summary>
    public sealed class PlanState
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Goal = "";
        public PlanStatus Status = PlanStatus.None;
        public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt = DateTimeOffset.UtcNow;
        public List<PlanStep> Steps = new List<PlanStep>();
        public int CurrentStep = -1;
        public string ApprovalNote = "";

        public PlanStep GetCurrentStep()
        {
            return CurrentStep >= 0 && CurrentStep < Steps.Count ? Steps[CurrentStep] : null;
        }

        public JsonObject ToJson()
        {
            var steps = new JsonArray();
            foreach (PlanStep step in Steps)
            {
                steps.Add(new JsonObject
                {
                    ["index"] = step.Index,
                    ["title"] = step.Title,
                    ["detail"] = step.Detail,
                    ["status"] = step.Status.ToString(),
                    ["result"] = step.Result,
                });
            }
            return new JsonObject
            {
                ["id"] = Id,
                ["goal"] = Goal,
                ["status"] = Status.ToString(),
                ["createdAt"] = CreatedAt.ToString("o"),
                ["updatedAt"] = UpdatedAt.ToString("o"),
                ["currentStep"] = CurrentStep,
                ["approvalNote"] = ApprovalNote,
                ["steps"] = steps,
            };
        }

        public string ToJsonString()
        {
            return ToJson().ToJsonString();
        }
    }

    /// <summary>One step of the structured plan.</summary>
    public sealed class PlanStep
    {
        public int Index;
        public string Title = "";
        public string Detail = "";
        public PlanStepStatus Status = PlanStepStatus.Pending;
        public string Result = "";
    }

    /// <summary>
    /// Owns the current plan: propose/approve/step-update, persistence to
    /// state/plan.json, and resume notes after a restart.
    /// </summary>
    public static class PlanStore
    {
        private static readonly object s_Lock = new object();

        public static PlanState Current { get; private set; } = Load();

        public static void Propose(string goal, string approvalNote, List<PlanStep> steps)
        {
            lock (s_Lock)
            {
                Current = new PlanState
                {
                    Goal = goal ?? "",
                    ApprovalNote = approvalNote ?? "",
                    Steps = steps ?? new List<PlanStep>(),
                };
                for (int i = 0; i < Current.Steps.Count; i++)
                {
                    Current.Steps[i].Index = i;
                }
                Current.Status = PlanStatus.Proposed;
                Current.CurrentStep = -1;
                Save();
            }
        }

        public static void Approve()
        {
            lock (s_Lock)
            {
                if (Current == null || Current.Status != PlanStatus.Proposed)
                {
                    return;
                }
                Current.Status = PlanStatus.Approved;
                Current.CurrentStep = 0;
                Current.UpdatedAt = DateTimeOffset.UtcNow;
                Save();
            }
        }

        public static void MarkStep(int index, PlanStepStatus status, string result)
        {
            lock (s_Lock)
            {
                if (Current == null || index < 0 || index >= Current.Steps.Count)
                {
                    return;
                }
                Current.Steps[index].Status = status;
                Current.Steps[index].Result = result ?? "";
                if (status == PlanStepStatus.Done)
                {
                    Current.CurrentStep = index + 1;
                }
                else if (status == PlanStepStatus.Failed)
                {
                    Current.Status = PlanStatus.Failed;
                }
                bool allDone = true;
                foreach (PlanStep step in Current.Steps)
                {
                    if (step.Status != PlanStepStatus.Done)
                    {
                        allDone = false;
                        break;
                    }
                }
                if (allDone)
                {
                    Current.Status = PlanStatus.Done;
                    Current.CurrentStep = -1;
                }
                else if (Current.Status == PlanStatus.Approved || Current.Status == PlanStatus.InProgress)
                {
                    Current.Status = PlanStatus.InProgress;
                }
                Current.UpdatedAt = DateTimeOffset.UtcNow;
                Save();
            }
        }

        public static void Reset()
        {
            lock (s_Lock)
            {
                Current = new PlanState { Status = PlanStatus.None };
                Save();
            }
        }

        public static void Save()
        {
            try
            {
                ModPaths.EnsureDirectories();
                File.WriteAllText(ModPaths.PlanFile, Current.ToJsonString());
            }
            catch (Exception e)
            {
                CS2MCP.Mod.Log.Warn($"plan save failed: {e.Message}");
            }
        }

        private static PlanState Load()
        {
            try
            {
                if (File.Exists(ModPaths.PlanFile))
                {
                    string json = File.ReadAllText(ModPaths.PlanFile);
                    using (JsonDocument document = JsonDocument.Parse(json))
                    {
                        JsonElement root = document.RootElement;
                        var plan = new PlanState
                        {
                            Id = GetString(root, "id", Guid.NewGuid().ToString("N")),
                            Goal = GetString(root, "goal", ""),
                            Status = Enum.TryParse(GetString(root, "status", "None"), out PlanStatus status)
                                ? status
                                : PlanStatus.None,
                            CreatedAt = DateTimeOffset.TryParse(GetString(root, "createdAt", ""), out DateTimeOffset created)
                                ? created
                                : DateTimeOffset.UtcNow,
                            UpdatedAt = DateTimeOffset.TryParse(GetString(root, "updatedAt", ""), out DateTimeOffset updated)
                                ? updated
                                : DateTimeOffset.UtcNow,
                            CurrentStep = root.TryGetProperty("currentStep", out JsonElement current)
                                ? current.GetInt32()
                                : -1,
                            ApprovalNote = GetString(root, "approvalNote", ""),
                        };
                        if (root.TryGetProperty("steps", out JsonElement stepsElement) &&
                            stepsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement stepElement in stepsElement.EnumerateArray())
                            {
                                plan.Steps.Add(new PlanStep
                                {
                                    Index = stepElement.TryGetProperty("index", out JsonElement index)
                                        ? index.GetInt32()
                                        : 0,
                                    Title = GetString(stepElement, "title", ""),
                                    Detail = GetString(stepElement, "detail", ""),
                                    Status = Enum.TryParse(GetString(stepElement, "status", "Pending"), out PlanStepStatus stepStatus)
                                        ? stepStatus
                                        : PlanStepStatus.Pending,
                                    Result = GetString(stepElement, "result", ""),
                                });
                            }
                        }
                        return plan;
                    }
                }
            }
            catch (Exception e)
            {
                CS2MCP.Mod.Log.Warn($"plan load failed, starting fresh: {e.Message}");
            }
            return new PlanState { Status = PlanStatus.None };
        }

        private static string GetString(JsonElement element, string name, string fallback)
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return fallback;
        }
    }
}
