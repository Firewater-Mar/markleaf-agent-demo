using MarkLeaf.Services.Proof;
using MarkLeaf.Services.AI;
using MarkLeaf.Services.Export;
using System.IO.Compression;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

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
    public async Task SourceTextExtractor_ReadsPdfTextLayerAndKeepsPageLocator()
    {
        var path = Path.Combine(Path.GetTempPath(), $"markleaf-proof-{Guid.NewGuid():N}.pdf");
        try
        {
            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            var first = builder.AddPage(PageSize.A4);
            first.AddText("Evidence on page one", 12, new PdfPoint(50, 750), font);
            var second = builder.AddPage(PageSize.A4);
            second.AddText("Market evidence on page two", 12, new PdfPoint(50, 750), font);
            await File.WriteAllBytesAsync(path, builder.Build());

            var (text, status) = await SourceTextExtractor.ExtractAsync(path);

            StringAssert.Contains(text, "Market evidence on page two");
            StringAssert.Contains(text, "\u001epage:2\u001e");
            StringAssert.Contains(status, "PDF");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ProofSourceQueryService_RanksChineseRequirementFile()
    {
        var project = new ProofProject
        {
            Sources =
            [
                new ProofSource
                {
                    DisplayName = "要求/比赛要求.md",
                    Kind = "Markdown",
                    ExtractedText = "应用价值：说明目标用户、真实场景与社会效益。",
                },
                new ProofSource
                {
                    DisplayName = "资料/无关资料.md",
                    Kind = "Markdown",
                    ExtractedText = "这里是普通的项目背景。",
                },
            ],
        };

        var sources = ProofSourceQueryService.BuildSources(
            project,
            "# 项目方案\n\n正文",
            "项目方案.md",
            null,
            "根据比赛要求检查应用价值",
            3);

        Assert.AreEqual("要求/比赛要求.md", sources[0].DisplayPath);
    }

    [TestMethod]
    public void AgentDocumentEditService_DefaultsToReadOnlyAndBuildsSectionPreview()
    {
        Assert.AreEqual(
            AgentTaskBehavior.ReadOnly,
            AgentDocumentEditService.Classify("概括当前文档，不要修改正文", "auto"));

        var created = AgentDocumentEditService.TryCreateSectionEdit(
            "# 项目方案\n\n## 应用价值\n\n旧内容\n\n## 技术路线\n\n内容",
            "改写“应用价值”章节，先展示修改预览",
            "修改预览：\n\n```markdown\n## 应用价值\n\n新内容 [S1]\n```",
            out var edit);

        Assert.IsTrue(created);
        Assert.IsNotNull(edit);
        Assert.AreEqual("应用价值", edit.TargetHeading);
        StringAssert.Contains(edit.ReplacementMarkdown, "新内容");
    }

    [TestMethod]
    public void AgentDocumentEditService_AcceptsSingleMislabeledFenceAndNaturalConfirmation()
    {
        var created = AgentDocumentEditService.TryCreateSectionEdit(
            "# 项目方案\n\n## 应用价值\n\n旧内容",
            "改写应用价值章节",
            "预览：\n```css\n## 应用价值\n\n新内容\n```",
            out var edit);

        Assert.IsTrue(created);
        Assert.IsNotNull(edit);
        Assert.IsTrue(AgentDocumentEditService.IsApplyConfirmation("可以写入了"));
        Assert.IsTrue(AgentDocumentEditService.IsApplyCancellation("取消修改"));
        Assert.IsTrue(AgentDocumentEditService.TryGetExportFormat("把当前文档导出为 PDF", out var format));
        Assert.AreEqual("pdf", format);
        Assert.IsTrue(AgentDocumentEditService.TryGetExportFormat("另存为 Word", out format));
        Assert.AreEqual("docx", format);
    }

    [TestMethod]
    public void AgentDocumentEditService_RejectsCopyAndAppendAsMaterialRewrite()
    {
        const string markdown = """
            # 项目方案

            ## 一、项目背景

            高校团队需要整合课程资料、调研记录和参考文献，并形成结构清晰的项目文档。
            传统编辑器可以完成排版，但难以同时处理资料检索、要求核对和证据追踪。

            ## 二、应用价值

            其他内容。
            """;
        var edit = new AgentSectionEdit(
            "一、项目背景",
            """
            ## 一、项目背景

            高校团队需要整合课程资料、调研记录和参考文献，并形成结构清晰的项目文档。
            传统编辑器可以完成排版，但难以同时处理资料检索、要求核对和证据追踪。
            因此，本项目计划开发文档 Agent。
            """);

        var quality = AgentDocumentEditService.EvaluateSectionEditQuality(markdown, edit);

        Assert.IsFalse(quality.Passed);
        Assert.IsGreaterThanOrEqualTo(0.7, quality.ReusedTextRatio);
        StringAssert.Contains(quality.Message, "大段沿用");
    }

    [TestMethod]
    public void AgentDocumentEditService_AcceptsMateriallyReorganizedSection()
    {
        const string markdown = """
            # 项目方案

            ## 一、项目背景

            高校团队需要整合课程资料、调研记录和参考文献，并形成结构清晰的项目文档。
            传统编辑器可以完成排版，但难以同时处理资料检索、要求核对和证据追踪。

            ## 二、应用价值

            其他内容。
            """;
        var edit = new AgentSectionEdit(
            "一、项目背景",
            """
            ## 一、项目背景

            高校项目写作的核心困难已经从文字录入转向跨文件的信息组织。课程材料、访谈与行业报告分散保存，使团队必须反复寻找依据并人工核对提交要求。

            现有编辑工具擅长排版，却没有把检索、核验和引用追踪连接成连续流程。研页智写因此将 Markdown 编辑、项目资料和文档 Agent 放入同一工作空间，让正文修改能够被审阅，关键结论能够追溯来源。
            """);

        var quality = AgentDocumentEditService.EvaluateSectionEditQuality(markdown, edit);

        Assert.IsTrue(quality.Passed, quality.Message);
        Assert.IsTrue(AgentDocumentEditService.RequiresMaterialRewrite("改写“项目背景”章节"));
        Assert.IsFalse(AgentDocumentEditService.RequiresMaterialRewrite("只修改错别字并轻微润色"));
    }

    [TestMethod]
    public void AgentDocumentEditService_NormalizesMislabeledPreviewFence()
    {
        var answer = "预览：\n```shell\n## 项目背景\n\n新内容\n```\n说明。";

        var normalized = AgentDocumentEditService.NormalizePreviewFence(answer);

        StringAssert.Contains(normalized, "```markdown\n## 项目背景");
        Assert.IsFalse(normalized.Contains("```shell", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AgentDocumentEditService_MatchesNumberedHeadingFromNaturalRequest()
    {
        var created = AgentDocumentEditService.TryCreateSectionEdit(
            "# 项目方案\n\n## 一、项目背景\n\n旧内容\n\n## 二、应用价值\n\n其他内容",
            "改写项目背景给我看，但不写入",
            "预览：\n```shell\n## 一、项目背景\n\n完全重新组织后的内容。\n```",
            out var edit);

        Assert.IsTrue(created);
        Assert.IsNotNull(edit);
        Assert.AreEqual("一、项目背景", edit.TargetHeading);
        Assert.AreEqual(
            AgentTaskBehavior.EditPreview,
            AgentDocumentEditService.Classify("改写项目背景给我看，但不写入", "auto"));
    }

    [TestMethod]
    public async Task DocxExportService_CreatesOpenXmlDocumentWithHeadingsAndBody()
    {
        var path = Path.Combine(Path.GetTempPath(), $"markleaf-export-{Guid.NewGuid():N}.docx");
        try
        {
            await DocxExportService.ExportAsync("# 项目标题\n\n这是**重点内容**。\n\n- 第一项", path);

            using var archive = ZipFile.OpenRead(path);
            var document = archive.GetEntry("word/document.xml");
            Assert.IsNotNull(document);
            using var reader = new StreamReader(document.Open());
            var xml = await reader.ReadToEndAsync();
            StringAssert.Contains(xml, "项目标题");
            StringAssert.Contains(xml, "重点内容");
            StringAssert.Contains(xml, "第一项");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AgentDocumentEditService_CreatesSafeMarkdownFileDraftInsideWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markleaf-draft-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.AreEqual(
                AgentTaskBehavior.CreateFilePreview,
                AgentDocumentEditService.Classify("在配套成果中新建答辩提纲.md", "auto"));

            var created = AgentDocumentEditService.TryCreateFileDraft(
                root,
                "在配套成果中新建答辩提纲.md",
                "```markdown\n# 答辩提纲\n\n内容\n```",
                out var draft);

            Assert.IsTrue(created);
            Assert.IsNotNull(draft);
            Assert.IsTrue(draft.TargetPath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("配套成果/答辩提纲.md", draft.DisplayPath);
            StringAssert.Contains(draft.Markdown, "答辩提纲");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void AgentDocumentEditService_RecognizesFileCreationFollowedByMoreInstructions()
    {
        const string request = "在配套成果中新建答辩提纲.md，根据当前文档生成完整内容。";

        Assert.AreEqual(
            AgentTaskBehavior.CreateFilePreview,
            AgentDocumentEditService.Classify(request, "auto"));

        var guidance = AgentDocumentEditService.BuildCreateFileGuidance(request);
        StringAssert.Contains(guidance, "不是原文目录或项目摘要");
        StringAssert.Contains(guidance, "现场演示路径");
        StringAssert.Contains(guidance, "没有证据的百分比");
    }

    [TestMethod]
    public void AgentDocumentEditService_RemovesTemporarySessionSourcesFromNewFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markleaf-draft-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var created = AgentDocumentEditService.TryCreateFileDraft(
                root,
                "新建答辩提纲.md，根据当前文档生成",
                "```markdown\n# 答辩提纲 [S1]\n\n项目定位 **[S2]**\n```",
                out var draft);

            Assert.IsTrue(created);
            Assert.IsNotNull(draft);
            Assert.IsFalse(draft.Markdown.Contains("[S1]", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(draft.Markdown.Contains("[S2]", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ProofSourceQueryService_CurrentDocumentRequestDoesNotLeakWorkspaceSources()
    {
        var currentPath = Path.Combine(Path.GetTempPath(), "市场分析.md");
        var project = new ProofProject
        {
            Sources =
            [
                new ProofSource { FilePath = currentPath, DisplayName = "市场分析.md", ExtractedText = "重复索引内容" },
                new ProofSource { FilePath = "other.md", DisplayName = "README.md", ExtractedText = "整个项目说明" },
            ],
        };

        var sources = ProofSourceQueryService.BuildSources(
            project,
            "# 市场分析\n\n只属于当前文件的内容",
            "市场分析.md",
            currentPath,
            "简述这个文件的内容",
            8,
            ProofSourceQueryService.ShouldUseOnlyCurrentDocument("简述这个文件的内容"));

        Assert.HasCount(1, sources);
        Assert.AreEqual("市场分析.md", sources[0].DisplayPath);
    }

    [TestMethod]
    public void DocumentCiService_DoesNotMarkRequirementCoveredFromLooseBodyKeyword()
    {
        var project = new ProofProject
        {
            Requirements = [new ProofRequirement { Title = "AI 使用说明", Description = "说明 AI 使用范围" }],
        };

        var result = DocumentCiService.Analyze(
            "# 项目方案\n\n正文只提到 AI，但没有 AI 使用说明章节。",
            project,
            Path.GetTempPath());

        Assert.AreEqual(0, result.CoveredRequirementCount);
    }

    [TestMethod]
    public void DocumentCiService_MatchesChineseRequirementDescriptionToNumberedHeading()
    {
        var project = new ProofProject
        {
            Requirements =
            [
                new ProofRequirement
                {
                    Title = "R-01",
                    Description = "说明项目背景和真实问题。",
                },
            ],
        };

        var result = DocumentCiService.Analyze(
            "# 项目方案\n\n## 一、项目背景\n\n高校团队需要整合分散资料，并核对比赛要求。",
            project,
            Path.GetTempPath());

        Assert.AreEqual(1, result.CoveredRequirementCount);
        Assert.IsTrue(project.Requirements[0].IsCovered);
        Assert.AreEqual("一、项目背景", project.Requirements[0].MatchedHeading);
        Assert.AreEqual("quick-covered", project.Requirements[0].CoverageState);
        Assert.IsFalse(result.SemanticVerified);
    }

    [TestMethod]
    public void RequirementSemanticReviewService_RequiresValidDocumentLinesForCoverage()
    {
        var requirements = new[]
        {
            new ProofRequirement { Id = "r1", Title = "R-01", Description = "说明项目背景和真实问题" },
            new ProofRequirement { Id = "r2", Title = "R-02", Description = "明确目标用户" },
        };
        var lines = new[]
        {
            "# 项目方案",
            "## 一、项目背景",
            "高校团队需要反复整理分散资料，容易遗漏比赛要求。",
        };

        var assessments = RequirementSemanticReviewService.ParseResponse(
            """
            ```json
            {"assessments":[
              {"id":"r1","status":"covered","heading":"项目背景","startLine":2,"endLine":3,"reason":"说明了背景与问题","confidence":0.92},
              {"id":"r2","status":"covered","heading":"目标用户","startLine":99,"endLine":100,"reason":"已覆盖","confidence":0.9}
            ]}
            ```
            """,
            requirements,
            lines);

        Assert.AreEqual("covered", assessments[0].State);
        Assert.AreEqual(2, assessments[0].StartLine);
        StringAssert.Contains(assessments[0].Evidence, "高校团队");
        Assert.AreEqual("missing", assessments[1].State);
        Assert.IsNull(assessments[1].StartLine);
    }

    [TestMethod]
    public void DocumentCiService_AppliesSemanticCoveredPartialAndMissingStates()
    {
        var project = new ProofProject
        {
            Requirements =
            [
                new ProofRequirement { Id = "r1", Title = "R-01", Description = "项目背景" },
                new ProofRequirement { Id = "r2", Title = "R-02", Description = "目标用户" },
            ],
        };
        var quick = DocumentCiService.Analyze(
            "# 项目方案\n\n## 项目背景\n\n存在真实资料整理问题。",
            project,
            Path.GetTempPath());
        var semantic = DocumentCiService.ApplySemanticAssessments(
            project,
            quick,
            [
                new RequirementCoverageAssessment("r1", "covered", "项目背景", 3, 5, "真实资料整理问题", "完整说明", 0.9),
                new RequirementCoverageAssessment("r2", "partial", "项目背景", 5, 5, "团队", "只笼统提到团队", 0.7),
            ]);

        Assert.IsTrue(semantic.SemanticVerified);
        Assert.AreEqual(1, semantic.CoveredRequirementCount);
        Assert.AreEqual("covered", project.Requirements[0].CoverageState);
        Assert.AreEqual("partial", project.Requirements[1].CoverageState);
        Assert.IsTrue(semantic.Issues.Any(issue => issue.Title == "部分覆盖：R-02"));
    }

    [TestMethod]
    public async Task ProofProjectStore_ConcurrentSavesDoNotShareTemporaryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markleaf-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new ProofProjectStore();
            var saves = Enumerable.Range(1, 6).Select(index => store.SaveAsync(
                root,
                new ProofProject
                {
                    Title = $"项目 {index}",
                    Requirements = [new ProofRequirement { Title = $"R-{index:00}" }],
                }));

            await Task.WhenAll(saves);
            var loaded = await store.LoadAsync(root);

            Assert.IsFalse(string.IsNullOrWhiteSpace(loaded.Title));
            Assert.HasCount(1, loaded.Requirements);
            Assert.HasCount(0, Directory.GetFiles(Path.Combine(root, ".markleaf"), "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ProofProjectStore_NewWorkspaceStartsWithoutRequirements()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markleaf-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var project = await new ProofProjectStore().LoadAsync(root);

            Assert.HasCount(0, project.Requirements);
            Assert.AreEqual(Path.GetFileName(root), project.Title);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void AiCredentialStore_RoundTripsWithWindowsAccountEncryption()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markleaf-credential-{Guid.NewGuid():N}");
        try
        {
            var store = new AiCredentialStore(root);
            store.Save("sk-test-secret");

            Assert.AreEqual("sk-test-secret", store.Load());
            var bytes = File.ReadAllBytes(Path.Combine(root, "ai-credential.bin"));
            Assert.IsFalse(System.Text.Encoding.UTF8.GetString(bytes).Contains("sk-test-secret", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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
