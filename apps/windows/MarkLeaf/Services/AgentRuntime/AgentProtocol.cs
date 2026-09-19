using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MarkLeaf.Services.AgentRuntime;

internal enum AgentRunStatus { Queued, Running, WaitingForApproval, Completed, Cancelled, Failed }
internal enum AgentMessagePartType { Text, Plan, Progress, Tool, Code, Artifact, Error }
internal enum AgentToolRisk { ReadOnly, WorkspaceWrite, ExternalNetwork, CodeExecution, Destructive }
internal enum AgentPermissionDecision { Allow, RequireApproval, Deny }
internal enum AgentPendingActionKind { ReplaceSection, CreateFile }

internal sealed record AgentMessagePart
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public AgentMessagePartType Type { get; init; }
    public string Role { get; init; } = "assistant";
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? ToolCallId { get; init; }
    public string? ArtifactId { get; init; }
    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; init; } = [];
}

internal sealed record AgentToolDescriptor(
    string Name,
    string Description,
    AgentToolRisk Risk,
    JsonObject ParameterSchema);

internal sealed record AgentToolCall
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public JsonObject Arguments { get; init; } = [];
    public DateTime RequestedAtUtc { get; init; } = DateTime.UtcNow;
}

internal sealed record AgentToolResult
{
    public string ToolCallId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public JsonNode? Data { get; init; }
    public string? ArtifactId { get; init; }
    public DateTime CompletedAtUtc { get; init; } = DateTime.UtcNow;
}

internal sealed record AgentArtifact
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = "file";
    public string RelativePath { get; init; } = string.Empty;
    public string MimeType { get; init; } = "application/octet-stream";
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public Dictionary<string, string> Provenance { get; init; } = [];
}

internal sealed class AgentSessionState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkspacePath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<AgentConversationTurn> RecentTurns { get; set; } = [];
    public AgentPendingAction? PendingAction { get; set; }
}

internal sealed record AgentPendingAction
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public AgentPendingActionKind Kind { get; init; }
    public string Label { get; init; } = string.Empty;
    public string TargetDocumentPath { get; init; } = string.Empty;
    public string? TargetHeading { get; init; }
    public string PreviewMarkdown { get; init; } = string.Empty;
    public string? OriginalDocumentHash { get; init; }
    public string? RunId { get; init; }
    public List<AgentPendingSource> Sources { get; init; } = [];
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

internal sealed record AgentPendingSource(
    string Id,
    string DisplayPath,
    int StartLine,
    int EndLine,
    string? Locator);

internal sealed record AgentConversationTurn
{
    public string Role { get; init; } = "user";
    public string Content { get; init; } = string.Empty;
    public string? TargetDocumentPath { get; init; }
    public string? RunId { get; init; }
    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
}

internal sealed class AgentRunState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string? TargetDocumentPath { get; set; }
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Queued;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? Error { get; set; }
    public List<AgentMessagePart> Parts { get; set; } = [];
    public List<AgentToolCall> ToolCalls { get; set; } = [];
    public List<AgentArtifact> Artifacts { get; set; } = [];
}

internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
