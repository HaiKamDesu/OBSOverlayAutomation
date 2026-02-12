using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace TournamentAutomation.Ui;

public sealed class UserSettings
{
    public string? SettingsFolderPath { get; set; }
    public string? PlayerDatabasePath { get; set; }
    public string? ChallongeTournament { get; set; }
    public string? ChallongeApiKey { get; set; }
    public string? ObsUrl { get; set; }
    public string? ObsPassword { get; set; }
    public string? SceneInMatch { get; set; }
    public string? SceneDesk { get; set; }
    public string? SceneBreak { get; set; }
    public string? SceneResults { get; set; }
    public string? MapP1Name { get; set; }
    public string? MapP1Team { get; set; }
    public string? MapP1Country { get; set; }
    public string? MapP1Flag { get; set; }
    public string? MapP1Score { get; set; }
    public string? MapP2Name { get; set; }
    public string? MapP2Team { get; set; }
    public string? MapP2Country { get; set; }
    public string? MapP2Flag { get; set; }
    public string? MapP2Score { get; set; }
    public string? MapRoundLabel { get; set; }
    public string? MapSetType { get; set; }
    public bool MoveToNextOpenMatchOnCommitToChallonge { get; set; } = true;
    public List<CountrySetting> Countries { get; set; } = new();
    public List<SceneButtonSetting> SceneButtons { get; set; } = new();
}

public sealed class SceneButtonSetting
{
    public string DisplayName { get; set; } = string.Empty;
    public string SceneName { get; set; } = string.Empty;
}

public sealed class CountrySetting
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FlagPath { get; set; } = string.Empty;
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
