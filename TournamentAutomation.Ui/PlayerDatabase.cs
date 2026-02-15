using System.IO;
using System.Text.Json;

namespace TournamentAutomation.Ui;

public sealed class PlayerProfile
{
    public string Name { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Characters { get; set; } = string.Empty;
    public string ChallongeUsername { get; set; } = string.Empty;
    public PlayerChallongeStatsSnapshot? ChallongeStats { get; set; }
    public List<string> Aliases { get; set; } = new();
    public string AliasesDisplay => string.Join(", ", Aliases);
}

public sealed class PlayerChallongeStatsSnapshot
{
    public string Username { get; set; } = string.Empty;
    public string ProfilePageUrl { get; set; } = string.Empty;
    public string ProfilePictureUrl { get; set; } = string.Empty;
    public string BannerImageUrl { get; set; } = string.Empty;
    public DateTimeOffset RetrievedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public decimal? WinRatePercent { get; set; }
    public int? TotalWins { get; set; }
    public int? TotalLosses { get; set; }
    public int? TotalTies { get; set; }
    public int? TotalTournamentsParticipated { get; set; }
    public int? FirstPlaceFinishes { get; set; }
    public int? SecondPlaceFinishes { get; set; }
    public int? ThirdPlaceFinishes { get; set; }
    public int? TopTenFinishes { get; set; }
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
            {
                player.Aliases ??= new List<string>();
                player.ChallongeUsername ??= string.Empty;
            }

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
