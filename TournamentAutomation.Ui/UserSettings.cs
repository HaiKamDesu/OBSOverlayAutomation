using System.IO;
using System.Text.Json;

namespace TournamentAutomation.Ui;

public sealed class UserSettings
{
    public string? PlayerDatabasePath { get; set; }
}

public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static UserSettings Load(string path)
    {
        if (!File.Exists(path))
            return new UserSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public static void Save(string path, UserSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }
}
