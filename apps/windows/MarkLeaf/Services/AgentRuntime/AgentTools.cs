using System.Text.Json.Nodes;

namespace MarkLeaf.Services.AgentRuntime;

internal sealed record AgentToolContext(
    string WorkspacePath,
    AgentRunState Run,
    CancellationToken CancellationToken);

internal interface IAgentTool
{
    AgentToolDescriptor Descriptor { get; }
    Task<AgentToolResult> ExecuteAsync(AgentToolCall call, AgentToolContext context);
}

internal sealed class AgentToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<AgentToolDescriptor> Descriptors =>
        _tools.Values.Select(tool => tool.Descriptor).ToArray();

    public void Register(IAgentTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!_tools.TryAdd(tool.Descriptor.Name, tool))
        {
            throw new InvalidOperationException($"Agent tool '{tool.Descriptor.Name}' is already registered.");
        }
    }

    public bool TryGet(string name, out IAgentTool? tool) => _tools.TryGetValue(name, out tool);
}

internal sealed class AgentPermissionPolicy
{
    public AgentPermissionDecision Decide(AgentToolDescriptor descriptor, bool userApproved = false)
    {
        return descriptor.Risk switch
        {
            AgentToolRisk.ReadOnly => AgentPermissionDecision.Allow,
            AgentToolRisk.Destructive => AgentPermissionDecision.Deny,
            _ when userApproved => AgentPermissionDecision.Allow,
            _ => AgentPermissionDecision.RequireApproval,
        };
    }
}

internal sealed class ReadWorkspaceTextTool : IAgentTool
{
    private const long MaxFileBytes = 2 * 1024 * 1024;

    public AgentToolDescriptor Descriptor { get; } = new(
        "read_workspace_text",
        "Read a UTF-8 text file inside the current workspace.",
        AgentToolRisk.ReadOnly,
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Workspace-relative file path.",
                },
            },
            ["required"] = new JsonArray("path"),
            ["additionalProperties"] = false,
        });

    public async Task<AgentToolResult> ExecuteAsync(AgentToolCall call, AgentToolContext context)
    {
        var requestedPath = call.Arguments["path"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return Failure(call, "The path argument is required.");
        }

        var workspaceRoot = Path.GetFullPath(context.WorkspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(workspaceRoot, requestedPath));
        var requiredPrefix = workspaceRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(call, "The requested path is outside the current workspace.");
        }

        if (!File.Exists(candidate))
        {
            return Failure(call, "The requested file does not exist.");
        }
        if (ContainsReparsePoint(workspaceRoot, candidate))
        {
            return Failure(call, "Linked files and folders are not readable by Agent tools.");
        }

        var info = new FileInfo(candidate);
        if (info.Length > MaxFileBytes)
        {
            return Failure(call, "The requested file is larger than the 2 MB text limit.");
        }

        var content = await File.ReadAllTextAsync(candidate, context.CancellationToken);
        var relativePath = Path.GetRelativePath(workspaceRoot, candidate).Replace('\\', '/');
        return new AgentToolResult
        {
            ToolCallId = call.Id,
            Success = true,
            Summary = $"Read {relativePath}.",
            Data = new JsonObject
            {
                ["path"] = relativePath,
                ["content"] = content,
            },
        };
    }

    private static AgentToolResult Failure(AgentToolCall call, string summary) => new()
    {
        ToolCallId = call.Id,
        Success = false,
        Summary = summary,
    };

    private static bool ContainsReparsePoint(string workspaceRoot, string candidate)
    {
        var relativePath = Path.GetRelativePath(workspaceRoot, candidate);
        var currentPath = workspaceRoot;
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }
}
