using System.Text;
using System.Text.Json;

namespace MarkLeaf.Services.AgentRuntime;

internal sealed class AgentRunArchive
{
    private readonly string _agentRoot;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AgentRunArchive(string workspacePath)
    {
        _agentRoot = Path.Combine(Path.GetFullPath(workspacePath), ".markleaf", "agent");
    }

    public string SessionPath => Path.Combine(_agentRoot, "session.json");

    public async Task<AgentSessionState?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SessionPath)) return null;

        try
        {
            await using var stream = File.OpenRead(SessionPath);
            return await JsonSerializer.DeserializeAsync<AgentSessionState>(stream, AgentJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task SaveSessionAsync(AgentSessionState session, CancellationToken cancellationToken = default) =>
        WriteJsonAtomicAsync(SessionPath, session, cancellationToken);

    public Task SaveRunAsync(AgentRunState run, CancellationToken cancellationToken = default) =>
        WriteJsonAtomicAsync(GetRunPath(run.Id, "meta.json"), run, cancellationToken);

    public Task AppendMessagePartAsync(string runId, AgentMessagePart part, CancellationToken cancellationToken = default) =>
        AppendJsonLineAsync(GetRunPath(runId, "message-parts.jsonl"), part, cancellationToken);

    public Task AppendToolCallAsync(string runId, AgentToolCall call, AgentToolResult result, CancellationToken cancellationToken = default) =>
        AppendJsonLineAsync(GetRunPath(runId, "tool-calls.jsonl"), new { call, result }, cancellationToken);

    public async Task<IReadOnlyList<AgentRunState>> ListRunsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var runsRoot = Path.Combine(_agentRoot, "runs");
        if (!Directory.Exists(runsRoot)) return [];

        var runs = new List<AgentRunState>();
        foreach (var path in Directory.EnumerateFiles(runsRoot, "meta.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var run = await JsonSerializer.DeserializeAsync<AgentRunState>(stream, AgentJson.Options, cancellationToken);
                if (run is not null) runs.Add(run);
            }
            catch (JsonException)
            {
                // A damaged historical record must not prevent the workspace from opening.
            }
            catch (IOException)
            {
                // The file may be temporarily locked by another MarkLeaf window.
            }
        }

        return runs
            .OrderByDescending(run => run.CreatedAtUtc)
            .Take(Math.Max(0, limit))
            .ToArray();
    }

    private string GetRunPath(string runId, string fileName) =>
        Path.Combine(_agentRoot, "runs", runId, fileName);

    private async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                {
                    await JsonSerializer.SerializeAsync(stream, value, AgentJson.Options, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(value, AgentJson.JsonLineOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
