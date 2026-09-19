using MarkLeaf.Services.AI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class AiKnowledgeRetrieverTests
{
    [TestMethod]
    public async Task RetrieveAsync_RanksMatchingWorkspaceSourceAndKeepsLineReference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markleaf-ai-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "battery.md"),
                "# 电池报告\n\n磷酸铁锂电池循环寿命较长。\n\n这是需要引用的结论。");
            await File.WriteAllTextAsync(
                Path.Combine(root, "other.md"),
                "# 其他资料\n\n这份文件讨论排版主题。");

            var sources = await AiKnowledgeRetriever.RetrieveAsync(
                root,
                currentDocumentPath: null,
                currentMarkdown: string.Empty,
                query: "电池循环寿命",
                includeCurrentDocument: false,
                includeWorkspace: true,
                maxSources: 2);

            Assert.HasCount(2, sources);
            Assert.AreEqual("battery.md", sources[0].DisplayPath);
            Assert.AreEqual("S1", sources[0].Id);
            Assert.IsGreaterThanOrEqualTo(1, sources[0].StartLine);
            StringAssert.Contains(sources[0].Content, "循环寿命");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task RetrieveAsync_SkipsGitAndBuildDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markleaf-ai-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, ".git", "secret.txt"), "secret-token");
            await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "package.md"), "secret-token");
            await File.WriteAllTextAsync(Path.Combine(root, "safe.md"), "公开资料");

            var sources = await AiKnowledgeRetriever.RetrieveAsync(
                root,
                currentDocumentPath: null,
                currentMarkdown: string.Empty,
                query: "secret-token",
                includeCurrentDocument: false,
                includeWorkspace: true,
                maxSources: 8);

            Assert.HasCount(1, sources);
            Assert.AreEqual("safe.md", sources[0].DisplayPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void BuildChatCompletionsUrl_AcceptsBaseAndFullUrls()
    {
        Assert.AreEqual(
            "http://localhost:11434/v1/chat/completions",
            OpenAiCompatibleClient.BuildChatCompletionsUrl("http://localhost:11434/v1").ToString().TrimEnd('/'));
        Assert.AreEqual(
            "https://example.test/v1/chat/completions",
            OpenAiCompatibleClient.BuildChatCompletionsUrl("https://example.test/v1/chat/completions").ToString().TrimEnd('/'));
    }

    [TestMethod]
    public void BuildModelsUrl_AcceptsBaseAndChatUrls()
    {
        Assert.AreEqual(
            "http://localhost:11434/v1/models",
            OpenAiCompatibleClient.BuildModelsUrl("http://localhost:11434/v1").ToString().TrimEnd('/'));
        Assert.AreEqual(
            "https://example.test/v1/models",
            OpenAiCompatibleClient.BuildModelsUrl("https://example.test/v1/chat/completions").ToString().TrimEnd('/'));
    }

    [TestMethod]
    public void ReadModelIds_SupportsOpenAiAndOllamaResponses()
    {
        using var openAi = System.Text.Json.JsonDocument.Parse("""{"data":[{"id":"gpt-test"},{"id":"gpt-test"}]}""");
        using var ollama = System.Text.Json.JsonDocument.Parse("""{"models":[{"name":"qwen3:4b"},{"model":"llama3.2"}]}""");

        CollectionAssert.AreEqual(new[] { "gpt-test" }, OpenAiCompatibleClient.ReadModelIds(openAi.RootElement).ToArray());
        CollectionAssert.AreEqual(new[] { "qwen3:4b", "llama3.2" }, OpenAiCompatibleClient.ReadModelIds(ollama.RootElement).ToArray());
    }

    [TestMethod]
    public void ParseToolCompletionResponse_ReadsOpenAiFunctionCalls()
    {
        var result = OpenAiCompatibleClient.ParseToolCompletionResponse("""
            {
              "choices": [{
                "message": {
                  "content": null,
                  "tool_calls": [{
                    "id": "call_123",
                    "type": "function",
                    "function": {
                      "name": "read_workspace_text",
                      "arguments": "{\"path\":\"notes.md\"}"
                    }
                  }]
                }
              }]
            }
            """);

        Assert.AreEqual(string.Empty, result.Content);
        Assert.HasCount(1, result.ToolCalls);
        Assert.AreEqual("call_123", result.ToolCalls[0].Id);
        Assert.AreEqual("read_workspace_text", result.ToolCalls[0].Name);
        Assert.AreEqual("notes.md", result.ToolCalls[0].Arguments["path"]?.GetValue<string>());
    }

    [TestMethod]
    public void ParseToolCompletionResponse_ReadsOllamaObjectArguments()
    {
        var result = OpenAiCompatibleClient.ParseToolCompletionResponse("""
            {
              "message": {
                "content": "I will inspect the file.",
                "tool_calls": [{
                  "function": {
                    "name": "read_workspace_text",
                    "arguments": { "path": "report.md" }
                  }
                }]
              }
            }
            """);

        Assert.AreEqual("I will inspect the file.", result.Content);
        Assert.HasCount(1, result.ToolCalls);
        Assert.AreEqual("report.md", result.ToolCalls[0].Arguments["path"]?.GetValue<string>());
    }
}
