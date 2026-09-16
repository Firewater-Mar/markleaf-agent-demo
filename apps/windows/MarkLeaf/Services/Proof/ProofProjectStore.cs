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
            await using var stream = File.OpenRead(path);
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
        var temporaryPath = path + ".tmp";
        project.UpdatedAtUtc = DateTime.UtcNow;

        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
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
