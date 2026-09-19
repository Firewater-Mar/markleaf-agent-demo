using System.Text.Json;

namespace MarkLeaf.Services.Proof;

internal sealed class ProofProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string GetProjectDirectory(string workspaceRoot)
        => Path.Combine(workspaceRoot, ".markleaf");

    public string GetProjectPath(string workspaceRoot)
        => Path.Combine(GetProjectDirectory(workspaceRoot), "proof-project.json");

    public string GetExportDirectory(string workspaceRoot)
        => Path.Combine(GetProjectDirectory(workspaceRoot), "exports");

    public async Task<ProofProject> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var path = GetProjectPath(workspaceRoot);
        if (!File.Exists(path))
        {
            return new ProofProject
            {
                Title = Path.GetFileName(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            };
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var project = await JsonSerializer.DeserializeAsync<ProofProject>(stream, JsonOptions, cancellationToken);
            return Normalize(project ?? new ProofProject(), workspaceRoot);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"无法读取项目资料：{exception.Message}", exception);
        }
    }

    public async Task SaveAsync(string workspaceRoot, ProofProject project, CancellationToken cancellationToken = default)
    {
        var directory = GetProjectDirectory(workspaceRoot);
        Directory.CreateDirectory(directory);
        var path = GetProjectPath(workspaceRoot);
        var temporaryPath = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        project.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            await MoveIntoPlaceAsync(temporaryPath, path, cancellationToken);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task MoveIntoPlaceAsync(string temporaryPath, string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(40 * (attempt + 1), cancellationToken);
            }
        }
    }

    private static ProofProject Normalize(ProofProject project, string workspaceRoot)
    {
        project.Title = string.IsNullOrWhiteSpace(project.Title)
            ? Path.GetFileName(workspaceRoot)
            : project.Title.Trim();
        project.Requirements ??= [];
        project.Sources ??= [];
        project.AuditTrail ??= [];
        foreach (var requirement in project.Requirements)
        {
            requirement.Id = string.IsNullOrWhiteSpace(requirement.Id)
                ? Guid.NewGuid().ToString("N")
                : requirement.Id;
            requirement.Title = requirement.Title?.Trim() ?? string.Empty;
            requirement.Description ??= string.Empty;
            requirement.MatchedHeading ??= string.Empty;
            requirement.CoverageState = requirement.CoverageState is
                "quick-covered" or "quick-missing" or "covered" or "partial" or "missing"
                ? requirement.CoverageState
                : "unchecked";
            requirement.EvidenceText ??= string.Empty;
            requirement.CoverageReason ??= string.Empty;
        }
        foreach (var source in project.Sources)
        {
            source.Id = string.IsNullOrWhiteSpace(source.Id)
                ? Guid.NewGuid().ToString("N")
                : source.Id;
            source.DisplayName = string.IsNullOrWhiteSpace(source.DisplayName)
                ? Path.GetFileName(source.FilePath)
                : source.DisplayName;
            source.Kind ??= "文档";
            source.TrustLevel ??= "一般";
            source.ExtractedText ??= string.Empty;
            source.ExtractionStatus ??= "等待索引";
        }
        return project;
    }
}
