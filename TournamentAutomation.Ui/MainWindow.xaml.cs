using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using TournamentAutomation.Application;
using TournamentAutomation.Domain;
using TournamentAutomation.Presentation;

namespace TournamentAutomation.Ui;

public partial class MainWindow : Window
{
    private readonly AutomationHost _host;
    private readonly ObservableCollection<CountryInfo> _countries = new();
    private readonly ObservableCollection<PlayerProfile> _playerProfiles = new();
    private readonly string _settingsPath;
    private readonly UserSettings _settings;
    private PlayerDatabase _playerDatabase = new();
    private string _playerDatabasePath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        var logger = new ConsoleAppLogger();
        _host = new AutomationHost(ConfigScript.Build(), logger);

        foreach (var entry in _host.Config.Metadata.Countries.Values.OrderBy(x => x.Id.ToString()))
            _countries.Add(entry);

        P1Country.ItemsSource = _countries;
        P2Country.ItemsSource = _countries;

        P1Country.SelectedItem = _countries.FirstOrDefault(x => x.Id == CountryId.Unknown);
        P2Country.SelectedItem = _countries.FirstOrDefault(x => x.Id == CountryId.Unknown);

        _settingsPath = GetSettingsPath();
        _settings = UserSettingsStore.Load(_settingsPath);
        _playerDatabasePath = string.IsNullOrWhiteSpace(_settings.PlayerDatabasePath)
            ? GetDefaultPlayerDatabasePath()
            : _settings.PlayerDatabasePath;

        LoadPlayerDatabase();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var ok = await _host.ConnectAsync(CancellationToken.None);
        StatusLabel.Content = ok ? "Connected" : "Disconnected";
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _host.RefreshOverlayAsync(CancellationToken.None);
    }

    private async void ApplyP1_Click(object sender, RoutedEventArgs e)
    {
        var country = (P1Country.SelectedItem as CountryInfo)?.Id ?? CountryId.Unknown;
        var player = BuildPlayer(P1Name.Text, P1Team.Text, country, P1Characters.Text);
        await _host.SetPlayerAsync(true, player, CancellationToken.None);
        SavePlayerProfile(player, P1Characters.Text);
    }

    private void SaveP1_Click(object sender, RoutedEventArgs e)
    {
        var country = (P1Country.SelectedItem as CountryInfo)?.Id ?? CountryId.Unknown;
        var player = BuildPlayer(P1Name.Text, P1Team.Text, country, P1Characters.Text);
        SavePlayerProfile(player, P1Characters.Text);
    }

    private async void ApplyP2_Click(object sender, RoutedEventArgs e)
    {
        var country = (P2Country.SelectedItem as CountryInfo)?.Id ?? CountryId.Unknown;
        var player = BuildPlayer(P2Name.Text, P2Team.Text, country, P2Characters.Text);
        await _host.SetPlayerAsync(false, player, CancellationToken.None);
        SavePlayerProfile(player, P2Characters.Text);
    }

    private void SaveP2_Click(object sender, RoutedEventArgs e)
    {
        var country = (P2Country.SelectedItem as CountryInfo)?.Id ?? CountryId.Unknown;
        var player = BuildPlayer(P2Name.Text, P2Team.Text, country, P2Characters.Text);
        SavePlayerProfile(player, P2Characters.Text);
    }

    private async void SceneInMatch_Click(object sender, RoutedEventArgs e)
        => await _host.SwitchSceneAsync(_host.Config.Scenes.InMatch, CancellationToken.None);

    private async void SceneDesk_Click(object sender, RoutedEventArgs e)
        => await _host.SwitchSceneAsync(_host.Config.Scenes.Desk, CancellationToken.None);

    private async void SceneBreak_Click(object sender, RoutedEventArgs e)
        => await _host.SwitchSceneAsync(_host.Config.Scenes.Break, CancellationToken.None);

    private async void SceneResults_Click(object sender, RoutedEventArgs e)
        => await _host.SwitchSceneAsync(_host.Config.Scenes.Results, CancellationToken.None);

    private async void P1Up_Click(object sender, RoutedEventArgs e)
        => await _host.AdjustScoreAsync(true, 1, CancellationToken.None);

    private async void P1Down_Click(object sender, RoutedEventArgs e)
        => await _host.AdjustScoreAsync(true, -1, CancellationToken.None);

    private async void P2Up_Click(object sender, RoutedEventArgs e)
        => await _host.AdjustScoreAsync(false, 1, CancellationToken.None);

    private async void P2Down_Click(object sender, RoutedEventArgs e)
        => await _host.AdjustScoreAsync(false, -1, CancellationToken.None);

    private async void Swap_Click(object sender, RoutedEventArgs e)
        => await _host.SwapPlayersAsync(CancellationToken.None);

    private async void Reset_Click(object sender, RoutedEventArgs e)
        => await _host.ResetMatchAsync(CancellationToken.None);

    private async void Next_Click(object sender, RoutedEventArgs e)
        => await _host.LoadNextAsync(CancellationToken.None);

    private async void Undo_Click(object sender, RoutedEventArgs e)
        => await _host.UndoAsync(CancellationToken.None);

    private async void Redo_Click(object sender, RoutedEventArgs e)
        => await _host.RedoAsync(CancellationToken.None);

    private void SetP1_Click(object sender, RoutedEventArgs e)
        => SelectAndApplyProfile(isPlayerOne: true);

    private void SetP2_Click(object sender, RoutedEventArgs e)
        => SelectAndApplyProfile(isPlayerOne: false);

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Player Database Location",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "players.json",
            InitialDirectory = Path.GetDirectoryName(_playerDatabasePath)
        };

        if (dialog.ShowDialog() != true)
            return;

        _playerDatabasePath = dialog.FileName;
        _settings.PlayerDatabasePath = _playerDatabasePath;
        UserSettingsStore.Save(_settingsPath, _settings);
        LoadPlayerDatabase();
    }

    private static PlayerInfo BuildPlayer(string name, string team, CountryId country, string characters)
    {
        var ids = ParseCharacters(characters);
        return new PlayerInfo
        {
            Name = name ?? string.Empty,
            Team = team ?? string.Empty,
            Country = country,
            Characters = ids
        };
    }

    private static IReadOnlyList<FGCharacterId> ParseCharacters(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<FGCharacterId>();

        var tokens = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<FGCharacterId>();
        foreach (var token in tokens)
        {
            if (Enum.TryParse<FGCharacterId>(token, true, out var id))
                list.Add(id);
        }

        return list;
    }

    private void LoadPlayerDatabase()
    {
        _playerDatabase = PlayerDatabaseStore.Load(_playerDatabasePath);
        RefreshPlayerProfiles();
    }

    private void SavePlayerProfile(PlayerInfo player, string charactersText)
    {
        if (string.IsNullOrWhiteSpace(player.Name))
            return;

        var existing = _playerDatabase.Players.FirstOrDefault(x =>
            string.Equals(x.Name, player.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new PlayerProfile();
            _playerDatabase.Players.Add(existing);
        }

        existing.Name = player.Name;
        existing.Team = player.Team;
        existing.Country = player.Country.ToString();
        existing.Characters = charactersText ?? string.Empty;

        PlayerDatabaseStore.Save(_playerDatabasePath, _playerDatabase);
        RefreshPlayerProfiles();
    }

    private void RefreshPlayerProfiles()
    {
        var ordered = _playerDatabase.Players
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _playerProfiles.Clear();
        foreach (var profile in ordered)
            _playerProfiles.Add(profile);
    }

    private void ApplyProfileToFields(PlayerProfile profile, bool isPlayerOne)
    {
        var teamBox = isPlayerOne ? P1Team : P2Team;
        var charactersBox = isPlayerOne ? P1Characters : P2Characters;
        var countryBox = isPlayerOne ? P1Country : P2Country;
        var nameBox = isPlayerOne ? P1Name : P2Name;

        nameBox.Text = profile.Name ?? string.Empty;
        teamBox.Text = profile.Team ?? string.Empty;
        charactersBox.Text = profile.Characters ?? string.Empty;

        if (Enum.TryParse<CountryId>(profile.Country, true, out var id))
            countryBox.SelectedItem = _countries.FirstOrDefault(x => x.Id == id);
    }

    private void SelectAndApplyProfile(bool isPlayerOne)
    {
        if (_playerProfiles.Count == 0)
        {
            MessageBox.Show("No saved players found.", "Set Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new PlayerSelectWindow(_playerProfiles, _playerDatabase, _playerDatabasePath, _countries);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.SelectedProfile is not null)
        {
            LoadPlayerDatabase();
            ApplyProfileToFields(dialog.SelectedProfile, isPlayerOne);
        }
    }

    private static string GetSettingsPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "TournamentAutomation", "ui-settings.json");
    }

    private static string GetDefaultPlayerDatabasePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "TournamentAutomation", "players.json");
    }
}
