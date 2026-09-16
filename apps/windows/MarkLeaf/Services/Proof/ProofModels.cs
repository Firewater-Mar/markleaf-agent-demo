namespace MarkLeaf.Services.Proof;

internal sealed class ProofProject
{
    public int SchemaVersion { get; set; } = 1;
    public string Title { get; set; } = "未命名可信文档项目";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<ProofRequirement> Requirements { get; set; } = [];
    public List<ProofSource> Sources { get; set; } = [];
    public List<ProofAuditEvent> AuditTrail { get; set; } = [];
    public ProofRunSummary? LastRun { get; set; }
}

internal sealed class ProofRequirement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Points { get; set; }
    public bool Required { get; set; } = true;
    public bool IsCovered { get; set; }
    public string MatchedHeading { get; set; } = string.Empty;
}

internal sealed class ProofSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Kind { get; set; } = "文档";
    public string TrustLevel { get; set; } = "一般";
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FileModifiedAtUtc { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public string ExtractionStatus { get; set; } = "等待索引";
}

internal sealed class ProofAuditEvent
{
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public bool HumanConfirmed { get; set; }
}

internal sealed class ProofRunSummary
{
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public int RequirementCount { get; set; }
    public int CoveredRequirementCount { get; set; }
    public int EvidenceCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
}

internal enum ProofIssueSeverity
{
    Error,
    Warning,
    Info,
    Passed,
}

internal sealed record ProofIssue(
    ProofIssueSeverity Severity,
    string Category,
    string Title,
    string Detail,
    int? Line = null);

internal enum ProofEvidenceState
{
    Linked,
    NeedsReview,
    Missing,
    PossibleConflict,
}

internal sealed record ProofClaimAssessment(
    int Line,
    string Text,
    ProofEvidenceState State,
    IReadOnlyList<string> CitationIds);

internal sealed record ProofCiResult(
    IReadOnlyList<ProofIssue> Issues,
    IReadOnlyList<ProofClaimAssessment> Claims,
    int RequirementCount,
    int CoveredRequirementCount,
    int EvidenceCount,
    int ClaimCount,
    int SupportedClaimCount)
{
    public int ErrorCount => Issues.Count(issue => issue.Severity == ProofIssueSeverity.Error);
    public int WarningCount => Issues.Count(issue => issue.Severity == ProofIssueSeverity.Warning);
    public int InfoCount => Issues.Count(issue => issue.Severity == ProofIssueSeverity.Info);
    public int PassedCount => Issues.Count(issue => issue.Severity == ProofIssueSeverity.Passed);
    public int CoveragePercent => RequirementCount == 0
        ? 0
        : (int)Math.Round(CoveredRequirementCount * 100d / RequirementCount);
    public int EvidencePercent => ClaimCount == 0
        ? 100
        : (int)Math.Round(SupportedClaimCount * 100d / ClaimCount);
}
