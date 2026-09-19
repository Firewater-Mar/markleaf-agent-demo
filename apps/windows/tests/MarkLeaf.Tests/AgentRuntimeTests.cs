using System.Text.Json.Nodes;
using MarkLeaf.Services.AgentRuntime;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class AgentRuntimeTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "markleaf-agent-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true);
    }

    [TestMethod]
    public async Task RunLifecycleIsPersistedAndRestored()
    {
        var runtime = await AgentRuntime.OpenAsync(_workspace);
        var run = await runtime.StartRunAsync("Summarize the document", "notes.md");
        await runtime.AddPartAsync(run, new AgentMessagePart
        {
            Type = AgentMessagePartType.Progress,
            Content = "Reading the target document.",
        });
        await runtime.CompleteRunAsync(run, "Summary complete.");

        var reopened = await AgentRuntime.OpenAsync(_workspace);
        var runs = await reopened.ListRunsAsync();

        Assert.HasCount(1, runs);
        Assert.AreEqual(AgentRunStatus.Completed, runs[0].Status);
        Assert.AreEqual("notes.md", runs[0].TargetDocumentPath);
        Assert.HasCount(1, runs[0].Parts);
        Assert.HasCount(2, reopened.Session.RecentTurns);
        Assert.AreEqual("assistant", reopened.Session.RecentTurns[1].Role);
    }

    [TestMethod]
    public async Task ReadWorkspaceTextToolReadsInsideWorkspaceAndBlocksTraversal()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.md"), "hello MarkLeaf");
        var runtime = await AgentRuntime.OpenAsync(_workspace);
        var run = await runtime.StartRunAsync("Read notes", "notes.md");

        var result = await runtime.ExecuteToolAsync(run, new AgentToolCall
        {
            Name = "read_workspace_text",
            Arguments = new JsonObject { ["path"] = "notes.md" },
        });
        var blocked = await runtime.ExecuteToolAsync(run, new AgentToolCall
        {
            Name = "read_workspace_text",
            Arguments = new JsonObject { ["path"] = "../outside.txt" },
        });

        Assert.IsTrue(result.Success);
        Assert.AreEqual("hello MarkLeaf", result.Data?["content"]?.GetValue<string>());
        Assert.IsFalse(blocked.Success);
        StringAssert.Contains(blocked.Summary, "outside");
    }

    [TestMethod]
    public void PermissionPolicyRequiresApprovalAndBlocksDestructiveTools()
    {
        var policy = new AgentPermissionPolicy();
        var schema = new JsonObject { ["type"] = "object" };

        Assert.AreEqual(
            AgentPermissionDecision.Allow,
            policy.Decide(new AgentToolDescriptor("read", "read", AgentToolRisk.ReadOnly, schema)));
        Assert.AreEqual(
            AgentPermissionDecision.RequireApproval,
            policy.Decide(new AgentToolDescriptor("write", "write", AgentToolRisk.WorkspaceWrite, schema)));
        Assert.AreEqual(
            AgentPermissionDecision.Allow,
            policy.Decide(new AgentToolDescriptor("write", "write", AgentToolRisk.WorkspaceWrite, schema), true));
        Assert.AreEqual(
            AgentPermissionDecision.Deny,
            policy.Decide(new AgentToolDescriptor("delete", "delete", AgentToolRisk.Destructive, schema), true));
    }

    [TestMethod]
    public void RegistryRejectsDuplicateToolNames()
    {
        var registry = new AgentToolRegistry();
        registry.Register(new ReadWorkspaceTextTool());

        Assert.ThrowsExactly<InvalidOperationException>(() => registry.Register(new ReadWorkspaceTextTool()));
    }
}
