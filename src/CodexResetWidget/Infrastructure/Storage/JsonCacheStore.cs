using System.IO;
using System.Text.Json;
using CodexResetWidget.Domain;
using CodexResetWidget.Infrastructure.Api;

namespace CodexResetWidget.Infrastructure.Storage;

public sealed record CacheEnvelope(int SchemaVersion, WidgetSnapshot Snapshot, Dictionary<string, HttpDocument> Documents);
public sealed record CacheLoad(CacheEnvelope? Value, string? Warning = null);
public interface ICacheStore
{
    Task<CacheLoad> LoadAsync(CancellationToken token);
    Task SaveAsync(CacheEnvelope envelope, CancellationToken token);
}
public sealed class JsonCacheStore(string directory) : ICacheStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string FilePath => Path.Combine(directory, "snapshot.json");
    public async Task<CacheLoad> LoadAsync(CancellationToken token)
    {
        try
        {
            if (!File.Exists(FilePath)) return new(null);
            if (new FileInfo(FilePath).Length > 16_000_000) throw new InvalidDataException("缓存过大");
            var text = await File.ReadAllTextAsync(FilePath, token);
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(text, Json);
            if (envelope?.SchemaVersion != 1 || envelope.Snapshot is null || envelope.Documents is null
                || envelope.Snapshot.History?.Events is null || envelope.Snapshot.StatusHealth is null
                || envelope.Snapshot.HistoryHealth is null || envelope.Snapshot.Pending is { LastKnownEvent: null })
                throw new InvalidDataException("缓存版本或结构无效");
            foreach (var reset in envelope.Snapshot.History.Events.Concat(new[] { envelope.Snapshot.Status?.ScheduledReset,
                envelope.Snapshot.Status?.LatestReset, envelope.Snapshot.Pending?.LastKnownEvent }.OfType<ResetEvent>()))
                if (reset is null || string.IsNullOrWhiteSpace(reset.Key.Id) || reset.Text is null || reset.Key.Provider != CodexResetsClient.Provider)
                    throw new InvalidDataException("缓存记录无效");
            return new(envelope);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return new(null, "本地缓存无法读取，正在重新获取数据。"); }
    }
    public async Task SaveAsync(CacheEnvelope envelope, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, Json, token);
                await stream.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
