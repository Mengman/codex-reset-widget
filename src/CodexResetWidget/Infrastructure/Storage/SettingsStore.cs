using System.IO;
using System.Text.Json;
using CodexResetWidget.Domain;

namespace CodexResetWidget.Infrastructure.Storage;

public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string FilePath => Path.Combine(directory, "settings.json");
    public string? Warning { get; private set; }
    public DesktopSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            if (new FileInfo(FilePath).Length > 64_000) throw new InvalidDataException();
            var settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(FilePath), Json);
            if (settings is null || !PlacementPolicy.Valid(settings)) throw new InvalidDataException();
            return settings;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { Warning = "Error.SettingsRead"; return new(); }
    }
    public void Save(DesktopSettings settings)
    {
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!PlacementPolicy.Valid(settings)) throw new InvalidDataException();
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
            File.Move(temporary, FilePath, true);
            Warning = null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { Warning = "Error.SettingsSave"; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
