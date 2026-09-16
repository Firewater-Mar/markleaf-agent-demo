using MarkLeaf.Services.Proof;
using MarkLeaf.Services.AI;
using System.IO.Compression;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ProofWorkflowTests
{
    [TestMethod]
    public void RequirementAnalyzer_ExtractsScoredAndRequiredItems()
    {
        const string text = """
            一、创新性（20分）
            1. 必须说明项目的核心创新点。
            2. 应当包含真实用户调研。
            可选：补充未来商业计划。
            """;

        var requirements = RequirementAnalyzer.Analyze(text);

        Assert.IsGreaterThanOrEqualTo(3, requirements.Count);
        Assert.IsTrue(requirements.Any(item => item.Points == 20));
        Assert.IsTrue(requirements.Any(item => item.Title.Contains("用户调研", StringComparison.Ordinal)));
        Assert.IsTrue(requirements.Any(item => !item.Required));
    }

    [TestMethod]
    public void DocumentCiService_FindsMissingRequirementAndUnsupportedClaim()
    {
        var project = new ProofProject
        {
            Requirements =
            [
                new ProofRequirement { Title = "应用价值", Description = "说明应用价值", Required = true },
                new ProofRequirement { Title = "用户调研", Description = "包含真实用户调研", Required = true },
            ],
        };
        const string markdown = """
            # 应用价值

            本项目能够显著提升写作效率，预计达到 80%。
            """;

        var result = DocumentCiService.Analyze(markdown, project, Path.GetTempPath());

        Assert.AreEqual(1, result.CoveredRequirementCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Title.Contains("用户调研", StringComparison.Ordinal)));
        Assert.IsTrue(result.Issues.Any(issue => issue.Title.Contains("缺少来源", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ProofProjectStore_RoundTripsSidecarProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markleaf-proof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new ProofProjectStore();
            var project = new ProofProject
            {
                Title = "测试项目",
                Requirements = [new ProofRequirement { Title = "技术方案" }],
            };

            await store.SaveAsync(root, project);
            var loaded = await store.LoadAsync(root);

            Assert.AreEqual("测试项目", loaded.Title);
            Assert.HasCount(1, loaded.Requirements);
            Assert.IsTrue(File.Exists(Path.Combine(root, ".markleaf", "proof-project.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SourceTextExtractor_ReadsDocxTextWithoutOffice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"markleaf-proof-{Guid.NewGuid():N}.docx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("word/document.xml");
                await using var stream = entry.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync("""
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body><w:p><w:r><w:t>可验证的 Word 资料</w:t></w:r></w:p></w:body>
                    </w:document>
                    """);
            }

            var (text, status) = await SourceTextExtractor.ExtractAsync(path);

            StringAssert.Contains(text, "可验证的 Word 资料");
            StringAssert.Contains(status, "Word");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void BuildPortableAgentMarkdown_ConvertsTemporarySourceIdsToFootnotes()
    {
        var sources = new[]
        {
            new AiSource("S1", "research.md", 8, 14, "source", 3),
        };

        var result = PortableCitationService.ConvertAgentSourcesToFootnotes(
            "关键结论。[S1]",
            sources,
            new DateTime(2026, 9, 16, 12, 0, 0));

        Assert.IsFalse(result.Contains("[S1]", StringComparison.Ordinal));
        StringAssert.Contains(result, "[^ml-");
        StringAssert.Contains(result, "research.md，第 8-14 行");
    }
}
