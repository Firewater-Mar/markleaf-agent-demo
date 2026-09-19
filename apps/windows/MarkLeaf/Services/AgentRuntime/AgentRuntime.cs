namespace MarkLeaf.Services.AgentRuntime;

internal sealed class AgentRuntime
{
    private const int MaxRecentTurns = 24;
    private readonly string _workspacePath;
    private readonly AgentRunArchive _archive;
    private readonly AgentPermissionPolicy _permissionPolicy;

    private AgentRuntime(
        string workspacePath,
        AgentSessionState session,
        AgentRunArchive archive,
        AgentToolRegistry tools,
        AgentPermissionPolicy permissionPolicy)
    {
        _workspacePath = workspacePath;
        Session = session;
        _archive = archive;
        Tools = tools;
        _permissionPolicy = permissionPolicy;
    }

    public AgentSessionState Session { get; }
    public AgentToolRegistry Tools { get; }

    public static async Task<AgentRuntime> OpenAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var fullWorkspacePath = Path.GetFullPath(workspacePath);
        Directory.CreateDirectory(fullWorkspacePath);
        var archive = new AgentRunArchive(fullWorkspacePath);
        var session = await archive.LoadSessionAsync(cancellationToken) ?? new AgentSessionState
        {
            WorkspacePath = fullWorkspacePath,
        };
        session.WorkspacePath = fullWorkspacePath;
        session.UpdatedAtUtc = DateTime.UtcNow;

        var tools = new AgentToolRegistry();
        tools.Register(new ReadWorkspaceTextTool());
        var runtime = new AgentRuntime(fullWorkspacePath, session, archive, tools, new AgentPermissionPolicy());
        await archive.SaveSessionAsync(session, cancellationToken);
        return runtime;
    }

    public async Task<AgentRunState> StartRunAsync(
        string objective,
        string? targetDocumentPath,
        CancellationToken cancellationToken = default)
    {
        var run = new AgentRunState
        {
            SessionId = Session.Id,
            Objective = objective,
            TargetDocumentPath = targetDocumentPath,
            Status = AgentRunStatus.Running,
        };
        await _archive.SaveRunAsync(run, cancellationToken);
        await AddTurnAsync("user", objective, targetDocumentPath, run.Id, cancellationToken);
        return run;
    }

    public async Task AddPartAsync(
        AgentRunState run,
        AgentMessagePart part,
        CancellationToken cancellationToken = default)
    {
        run.Parts.Add(part);
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _archive.AppendMessagePartAsync(run.Id, part, cancellationToken);
        await _archive.SaveRunAsync(run, cancellationToken);
    }

    public async Task<AgentToolResult> ExecuteToolAsync(
        AgentRunState run,
        AgentToolCall call,
        bool userApproved = false,
        CancellationToken cancellationToken = default)
    {
        if (!Tools.TryGet(call.Name, out var tool) || tool is null)
        {
            return new AgentToolResult
            {
                ToolCallId = call.Id,
                Success = false,
                Summary = $"Unknown agent tool: {call.Name}.",
            };
        }

        var decision = _permissionPolicy.Decide(tool.Descriptor, userApproved);
        if (decision == AgentPermissionDecision.RequireApproval)
        {
            run.Status = AgentRunStatus.WaitingForApproval;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await _archive.SaveRunAsync(run, cancellationToken);
            return new AgentToolResult
            {
                ToolCallId = call.Id,
                Success = false,
                Summary = "This tool requires user approval before it can run.",
            };
        }

        if (decision == AgentPermissionDecision.Deny)
        {
            return new AgentToolResult
            {
                ToolCallId = call.Id,
                Success = false,
                Summary = "This tool is blocked by the current safety policy.",
            };
        }

        run.Status = AgentRunStatus.Running;
        run.ToolCalls.Add(call);
        var result = await tool.ExecuteAsync(call, new AgentToolContext(_workspacePath, run, cancellationToken));
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _archive.AppendToolCallAsync(run.Id, call, result, cancellationToken);
        await AddPartAsync(run, new AgentMessagePart
        {
            Type = AgentMessagePartType.Tool,
            Title = tool.Descriptor.Name,
            Content = result.Summary,
            ToolCallId = call.Id,
        }, cancellationToken);
        return result;
    }

    public Task CompleteRunAsync(AgentRunState run, string assistantResponse, CancellationToken cancellationToken = default) =>
        FinishRunAsync(run, AgentRunStatus.Completed, assistantResponse, null, cancellationToken);

    public Task CancelRunAsync(AgentRunState run, CancellationToken cancellationToken = default) =>
        FinishRunAsync(run, AgentRunStatus.Cancelled, string.Empty, null, cancellationToken);

    public Task FailRunAsync(AgentRunState run, Exception error, CancellationToken cancellationToken = default) =>
        FinishRunAsync(run, AgentRunStatus.Failed, string.Empty, error.Message, cancellationToken);

    public Task<IReadOnlyList<AgentRunState>> ListRunsAsync(int limit = 20, CancellationToken cancellationToken = default) =>
        _archive.ListRunsAsync(limit, cancellationToken);

    public async Task SavePendingActionAsync(AgentPendingAction pendingAction, CancellationToken cancellationToken = default)
    {
        Session.PendingAction = pendingAction;
        Session.UpdatedAtUtc = DateTime.UtcNow;
        await _archive.SaveSessionAsync(Session, cancellationToken);
    }

    public async Task ClearPendingActionAsync(CancellationToken cancellationToken = default)
    {
        if (Session.PendingAction is null) return;
        Session.PendingAction = null;
        Session.UpdatedAtUtc = DateTime.UtcNow;
        await _archive.SaveSessionAsync(Session, cancellationToken);
    }

    private async Task FinishRunAsync(
        AgentRunState run,
        AgentRunStatus status,
        string assistantResponse,
        string? error,
        CancellationToken cancellationToken)
    {
        run.Status = status;
        run.Error = error;
        run.UpdatedAtUtc = DateTime.UtcNow;
        run.CompletedAtUtc = DateTime.UtcNow;
        await _archive.SaveRunAsync(run, cancellationToken);
        if (!string.IsNullOrWhiteSpace(assistantResponse))
        {
            await AddTurnAsync("assistant", assistantResponse, run.TargetDocumentPath, run.Id, cancellationToken);
        }
    }

    private async Task AddTurnAsync(
        string role,
        string content,
        string? targetDocumentPath,
        string? runId,
        CancellationToken cancellationToken)
    {
        Session.RecentTurns.Add(new AgentConversationTurn
        {
            Role = role,
            Content = content,
            TargetDocumentPath = targetDocumentPath,
            RunId = runId,
        });
        if (Session.RecentTurns.Count > MaxRecentTurns)
        {
            Session.RecentTurns.RemoveRange(0, Session.RecentTurns.Count - MaxRecentTurns);
        }

        Session.UpdatedAtUtc = DateTime.UtcNow;
        await _archive.SaveSessionAsync(Session, cancellationToken);
    }
}
