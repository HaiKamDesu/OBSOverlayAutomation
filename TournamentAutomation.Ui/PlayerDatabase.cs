using System.IO;
using System.Text.Json;

namespace TournamentAutomation.Ui;

public sealed class PlayerProfile
{
    public string Name { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Characters { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string AliasesDisplay => string.Join(", ", Aliases);
}

public sealed class PlayerDatabase
{
    public List<PlayerProfile> Players { get; set; } = new();
}

public static class PlayerDatabaseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static PlayerDatabase Load(string path)
    {
        if (!File.Exists(path))
            return new PlayerDatabase();

        try
        {
            var json = File.ReadAllText(path);
            var database = JsonSerializer.Deserialize<PlayerDatabase>(json, JsonOptions) ?? new PlayerDatabase();

            foreach (var player in database.Players)
                player.Aliases ??= new List<string>();

            return database;
        }
        catch
        {
            return new PlayerDatabase();
        }
    }

    public static void Save(string path, PlayerDatabase database)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(database, JsonOptions);
        File.WriteAllText(path, json);
    }
}
