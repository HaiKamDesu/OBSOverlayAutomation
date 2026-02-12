using System.Collections.ObjectModel;
using System.IO;
using ChallongeInterface;
using ChallongeInterface.Models;
using Microsoft.Win32;
using TournamentAutomation.Application;
using TournamentAutomation.Domain;
using TournamentAutomation.Presentation;
using System.Windows;
using System.Net.Http;
using System.Text;
using System.Windows.Input;

namespace TournamentAutomation.Ui;

public partial class MainWindow : Window
{
    private readonly AutomationHost _host;
    private readonly ObservableCollection<CountryInfo> _countries = new();
    private readonly ObservableCollection<PlayerProfile> _playerProfiles = new();
    private readonly ObservableCollection<string> _queueRows = new();
    private readonly List<ChallongeQueueEntry> _challongeQueueEntries = new();
    private readonly string _settingsPath;
    private readonly UserSettings _settings;
    private PlayerDatabase _playerDatabase = new();
    private string _playerDatabasePath = string.Empty;
    private int _currentQueueIndex = -1;

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

        QueueListBox.ItemsSource = _queueRows;
        ChallongeTournamentBox.Text = _settings.ChallongeTournament
            ?? Environment.GetEnvironmentVariable("CHALLONGE_TOURNAMENT")
            ?? string.Empty;

        ChallongeApiKeyBox.Text = _settings.ChallongeApiKey
            ?? Environment.GetEnvironmentVariable("CHALLONGE_API_KEY")
            ?? string.Empty;

        UpdateQueueListVisuals();
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
    {
        await _host.AdjustScoreAsync(true, 1, CancellationToken.None);
        PersistCurrentQueueEntryStateFromHost();
    }

    private async void P1Down_Click(object sender, RoutedEventArgs e)
    {
        await _host.AdjustScoreAsync(true, -1, CancellationToken.None);
        PersistCurrentQueueEntryStateFromHost();
    }

    private async void P2Up_Click(object sender, RoutedEventArgs e)
    {
        await _host.AdjustScoreAsync(false, 1, CancellationToken.None);
        PersistCurrentQueueEntryStateFromHost();
    }

    private async void P2Down_Click(object sender, RoutedEventArgs e)
    {
        await _host.AdjustScoreAsync(false, -1, CancellationToken.None);
        PersistCurrentQueueEntryStateFromHost();
    }

    private async void CommitToChallonge_Click(object sender, RoutedEventArgs e)
        => await CommitCurrentMatchToChallongeAsync();

    private async void Swap_Click(object sender, RoutedEventArgs e)
        => await _host.SwapPlayersAsync(CancellationToken.None);

    private async void Reset_Click(object sender, RoutedEventArgs e)
        => await _host.ResetMatchAsync(CancellationToken.None);

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_challongeQueueEntries.Count == 0)
        {
            await _host.LoadNextAsync(CancellationToken.None);
            return;
        }

        await NavigateQueueAsync(_currentQueueIndex + 1);
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_challongeQueueEntries.Count == 0)
        {
            MessageBox.Show("Load a Challonge queue first.", "Queue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await NavigateQueueAsync(_currentQueueIndex - 1);
    }

    private async void LoadNextQueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_challongeQueueEntries.Count == 0)
        {
            MessageBox.Show("Load a Challonge queue first.", "Queue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await NavigateQueueAsync(_currentQueueIndex + 1);
    }

    private async void QueueListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (QueueListBox.SelectedIndex >= 0)
            await NavigateQueueAsync(QueueListBox.SelectedIndex);
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
        => await _host.UndoAsync(CancellationToken.None);

    private async void Redo_Click(object sender, RoutedEventArgs e)
        => await _host.RedoAsync(CancellationToken.None);

    private async void SetP1_Click(object sender, RoutedEventArgs e)
        => await SelectAndApplyProfileAsync(isPlayerOne: true);

    private async void SetP2_Click(object sender, RoutedEventArgs e)
        => await SelectAndApplyProfileAsync(isPlayerOne: false);

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

    private async void RefreshQueueButton_Click(object sender, RoutedEventArgs e)
        => await RefreshChallongeQueueAsync();

    private async Task RefreshChallongeQueueAsync()
    {
        var tournament = ChallongeTournamentBox.Text.Trim();
        var apiKey = ChallongeApiKeyBox.Text.Trim();
        var selectedMatchId = _currentQueueIndex >= 0 && _currentQueueIndex < _challongeQueueEntries.Count
            ? _challongeQueueEntries[_currentQueueIndex].MatchId
            : (long?)null;

        if (string.IsNullOrWhiteSpace(tournament) || string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                "Enter both Challonge tournament and API key before refreshing.",
                "Challonge Queue",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings.ChallongeTournament = tournament;
        _settings.ChallongeApiKey = apiKey;
        UserSettingsStore.Save(_settingsPath, _settings);

        QueuePositionLabel.Content = "Queue position: loading...";

        try
        {
            using var httpClient = new HttpClient();
            var client = new ChallongeClient(httpClient, new ChallongeClientOptions
            {
                ApiKey = apiKey
            });

            IReadOnlyList<Participant> participants = await client.GetParticipantsAsync(tournament);
            IReadOnlyList<Match> matches = await client.GetMatchesAsync(tournament);
            var participantsById = participants.ToDictionary(p => p.Id);

            var orderedMatches = matches
                .OrderBy(m => m.SuggestedPlayOrder ?? int.MaxValue)
                .ThenBy(m => m.Round ?? int.MaxValue)
                .ThenBy(m => m.Id)
                .ToList();

            _challongeQueueEntries.Clear();
            foreach (var match in orderedMatches)
                _challongeQueueEntries.Add(ToQueueEntry(match, participantsById));

            foreach (var entry in _challongeQueueEntries)
                ResolveQueueEntry(entry, allowPrompt: false);

            if (_challongeQueueEntries.Count == 0)
            {
                _currentQueueIndex = -1;
            }
            else
            {
                _currentQueueIndex = selectedMatchId.HasValue
                    ? _challongeQueueEntries.FindIndex(entry => entry.MatchId == selectedMatchId.Value)
                    : -1;

                if (_currentQueueIndex < 0)
                {
                    var current = _host.State.CurrentMatch;
                    _currentQueueIndex = _challongeQueueEntries.FindIndex(entry => IsSameMatch(entry.GetDisplayMatch(), current));
                }

                if (_currentQueueIndex < 0)
                    _currentQueueIndex = 0;
            }

            UpdateQueueListVisuals();
        }
        catch (Exception ex)
        {
            QueuePositionLabel.Content = "Queue position: failed to refresh";
            MessageBox.Show(ex.Message, "Challonge Queue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task NavigateQueueAsync(int targetIndex)
    {
        PersistCurrentQueueEntryStateFromHost();

        if (targetIndex < 0 || targetIndex >= _challongeQueueEntries.Count)
        {
            MessageBox.Show("No match exists at that position.", "Queue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var entry = _challongeQueueEntries[targetIndex];
        var match = ResolveQueueEntry(entry, allowPrompt: true);
        if (match is null)
            return;

        await _host.SetCurrentMatchAsync(match, CancellationToken.None);

        _currentQueueIndex = targetIndex;
        ApplyMatchToFields(match, entry);
        UpdateQueueListVisuals();
    }

    private void UpdateQueueListVisuals()
    {
        _queueRows.Clear();

        if (_challongeQueueEntries.Count == 0)
        {
            QueuePositionLabel.Content = "Queue position: not loaded";
            return;
        }

        for (var i = 0; i < _challongeQueueEntries.Count; i++)
        {
            var entry = _challongeQueueEntries[i];
            var match = entry.GetDisplayMatch();
            var status = i < _currentQueueIndex
                ? "done"
                : (i == _currentQueueIndex ? "current" : "up next");

            var unresolvedMarker = entry.ResolvedMatch is null ? " [needs mapping]" : string.Empty;
            var challongeMarker = entry.IsReportedToChallonge ? " [Challonge]" : string.Empty;
            var row = BuildQueueRowText(i + 1, status, match, entry);
            row += unresolvedMarker + challongeMarker;
            _queueRows.Add(row);
        }

        if (_currentQueueIndex >= 0 && _currentQueueIndex < _queueRows.Count)
        {
            QueueListBox.SelectedIndex = _currentQueueIndex;
            QueueListBox.ScrollIntoView(QueueListBox.SelectedItem);
            QueuePositionLabel.Content = $"Queue position: {_currentQueueIndex + 1} / {_challongeQueueEntries.Count}";
        }
        else
        {
            QueuePositionLabel.Content = $"Queue position: not selected ({_challongeQueueEntries.Count} matches)";
        }
    }

    private static ChallongeQueueEntry ToQueueEntry(Match match, IReadOnlyDictionary<long, Participant> participantsById)
    {
        var p1Name = ResolveName(participantsById, match.Player1Id);
        var p2Name = ResolveName(participantsById, match.Player2Id);

        return new ChallongeQueueEntry
        {
            MatchId = match.Id,
            ChallongePlayer1Id = match.Player1Id,
            ChallongePlayer2Id = match.Player2Id,
            ChallongeState = match.State ?? string.Empty,
            ChallongeWinnerId = match.WinnerId,
            ChallongeScoresCsv = match.ScoresCsv,
            IsReportedToChallonge = IsReportedByChallonge(match),
            RawPlayer1Name = p1Name,
            RawPlayer2Name = p2Name,
            RoundLabel = BuildRoundLabel(match)
        };
    }

    private static bool IsReportedByChallonge(Match match)
    {
        return !string.IsNullOrWhiteSpace(match.ScoresCsv)
            || match.WinnerId.HasValue
            || string.Equals(match.State, "complete", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildQueueRowText(int position, string status, MatchState match, ChallongeQueueEntry entry)
    {
        if (TryParseDisplayedScores(entry.ChallongeScoresCsv, out var p1Score, out var p2Score))
        {
            return $"{position}. [{status}] {match.Player1.Name} ({p1Score}) vs ({p2Score}) {match.Player2.Name} - {match.RoundLabel}";
        }

        return $"{position}. [{status}] {match.Player1.Name} vs {match.Player2.Name} - {match.RoundLabel}";
    }

    private static bool TryParseDisplayedScores(string? scoresCsv, out int p1Score, out int p2Score)
    {
        p1Score = 0;
        p2Score = 0;

        if (string.IsNullOrWhiteSpace(scoresCsv))
            return false;

        var segments = scoresCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var token = segments[i];
            var separator = token.IndexOf('-', 1);
            if (separator < 0)
                continue;

            var left = token[..separator].Trim();
            var right = token[(separator + 1)..].Trim();

            if (int.TryParse(left, out var parsedP1) && int.TryParse(right, out var parsedP2))
            {
                p1Score = parsedP1;
                p2Score = parsedP2;
                return true;
            }
        }

        return false;
    }

    private static string BuildRoundLabel(Match match)
    {
        var identifier = string.IsNullOrWhiteSpace(match.Identifier)
            ? $"#{match.Id}"
            : match.Identifier;

        var roundText = match.Round is null ? "?" : match.Round.Value.ToString();
        return $"Match {identifier} (Round {roundText})";
    }

    private static string ResolveName(IReadOnlyDictionary<long, Participant> participants, long? participantId)
    {
        if (participantId is null)
            return "TBD";

        if (!participants.TryGetValue(participantId.Value, out var participant))
            return participantId.Value.ToString();

        return participant.DisplayName
            ?? participant.Name
            ?? participant.Username
            ?? participant.Id.ToString();
    }

    private static bool IsSameMatch(MatchState left, MatchState right)
    {
        return string.Equals(left.Player1.Name, right.Player1.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Player2.Name, right.Player2.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.RoundLabel, right.RoundLabel, StringComparison.OrdinalIgnoreCase);
    }

    private MatchState? ResolveQueueEntry(ChallongeQueueEntry entry, bool allowPrompt)
    {
        if (entry.ResolvedMatch is not null)
            return entry.ResolvedMatch;

        var p1 = FindProfileByNameOrAlias(entry.RawPlayer1Name);
        var p2 = FindProfileByNameOrAlias(entry.RawPlayer2Name);

        if (allowPrompt && p1 is null && !IsPlaceholderName(entry.RawPlayer1Name))
            p1 = PromptForPlayerResolution(entry.RawPlayer1Name, "P1");

        if (allowPrompt && p2 is null && !IsPlaceholderName(entry.RawPlayer2Name))
            p2 = PromptForPlayerResolution(entry.RawPlayer2Name, "P2");

        if (!IsPlaceholderName(entry.RawPlayer1Name) && p1 is null)
            return null;

        if (!IsPlaceholderName(entry.RawPlayer2Name) && p2 is null)
            return null;

        entry.ResolvedMatch = new MatchState
        {
            RoundLabel = entry.RoundLabel,
            Format = MatchSetFormat.FT2,
            Player1 = ToPlayerInfo(p1, entry.RawPlayer1Name),
            Player2 = ToPlayerInfo(p2, entry.RawPlayer2Name)
        };
        entry.Player1CharactersText = p1?.Characters;
        entry.Player2CharactersText = p2?.Characters;

        return entry.ResolvedMatch;
    }

    private async Task CommitCurrentMatchToChallongeAsync()
    {
        if (_challongeQueueEntries.Count == 0 || _currentQueueIndex < 0 || _currentQueueIndex >= _challongeQueueEntries.Count)
        {
            MessageBox.Show("Load and select a Challonge queue match first.", "Commit to Challonge", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var tournament = ChallongeTournamentBox.Text.Trim();
        var apiKey = ChallongeApiKeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(tournament) || string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("Tournament and API key are required.", "Commit to Challonge", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PersistCurrentQueueEntryStateFromHost();
        var entry = _challongeQueueEntries[_currentQueueIndex];
        if (entry.ChallongePlayer1Id is null || entry.ChallongePlayer2Id is null)
        {
            MessageBox.Show("This match does not yet have both Challonge participants assigned.", "Commit to Challonge", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var modePrompt = MessageBox.Show(
            "Commit current score to Challonge?\n\nYes = use current score\nNo = commit DQ result using -1 score\nCancel = abort",
            "Commit to Challonge",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (modePrompt == MessageBoxResult.Cancel)
            return;

        var current = _host.State.CurrentMatch;
        string scoreCsv;
        long winnerId;

        if (modePrompt == MessageBoxResult.No)
        {
            var dqPrompt = MessageBox.Show(
                "Who is disqualified?\n\nYes = P1 DQ (-1)\nNo = P2 DQ (-1)\nCancel = abort",
                "Commit DQ Result",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (dqPrompt == MessageBoxResult.Cancel)
                return;

            if (dqPrompt == MessageBoxResult.Yes)
            {
                scoreCsv = "-1-0";
                winnerId = entry.ChallongePlayer2Id.Value;
            }
            else
            {
                scoreCsv = "0--1";
                winnerId = entry.ChallongePlayer1Id.Value;
            }
        }
        else
        {
            var p1Score = current.Player1.Score;
            var p2Score = current.Player2.Score;
            if (p1Score == p2Score)
            {
                MessageBox.Show("Scores are tied. Set a winner before committing.", "Commit to Challonge", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            scoreCsv = $"{p1Score}-{p2Score}";
            winnerId = p1Score > p2Score ? entry.ChallongePlayer1Id.Value : entry.ChallongePlayer2Id.Value;
        }

        try
        {
            using var httpClient = new HttpClient();
            var client = new ChallongeClient(httpClient, new ChallongeClientOptions
            {
                ApiKey = apiKey
            });

            _ = await client.UpdateMatchAsync(tournament, entry.MatchId, scoreCsv, winnerId, CancellationToken.None);
            entry.ChallongeScoresCsv = scoreCsv;
            entry.ChallongeWinnerId = winnerId;
            entry.ChallongeState = "complete";
            entry.IsReportedToChallonge = true;
            UpdateQueueListVisuals();
            MessageBox.Show($"Committed score '{scoreCsv}' to Challonge.", "Commit to Challonge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Commit to Challonge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private PlayerProfile? FindProfileByNameOrAlias(string challongeName)
    {
        if (string.IsNullOrWhiteSpace(challongeName))
            return null;

        var normalized = NormalizeName(challongeName);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return _playerDatabase.Players.FirstOrDefault(profile =>
            string.Equals(NormalizeName(profile.Name), normalized, StringComparison.Ordinal) ||
            profile.Aliases.Any(alias => string.Equals(NormalizeName(alias), normalized, StringComparison.Ordinal)));
    }

    private PlayerProfile? PromptForPlayerResolution(string challongeName, string slotLabel)
    {
        var prompt = MessageBox.Show(
            $"Could not match Challonge player '{challongeName}' for {slotLabel}.\n\nYes = choose existing player (or add from list)\nNo = create a new player now\nCancel = stop loading this match",
            "Unrecognized Challonge Player",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (prompt == MessageBoxResult.Cancel)
            return null;

        if (prompt == MessageBoxResult.No)
            return CreatePlayerFromChallongeName(challongeName);

        var selected = SelectPlayerForName(challongeName, slotLabel);
        if (selected is not null)
            return selected;

        return null;
    }

    private PlayerProfile? SelectPlayerForName(string challongeName, string slotLabel)
    {
        if (_playerProfiles.Count == 0)
        {
            MessageBox.Show("No saved players found. Create one first.", "Set Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return CreatePlayerFromChallongeName(challongeName);
        }

        var dialog = new PlayerSelectWindow(_playerProfiles, _playerDatabase, _playerDatabasePath, _countries)
        {
            Owner = this,
            Title = $"Select {slotLabel} for '{challongeName}'"
        };

        if (dialog.ShowDialog() != true || dialog.SelectedProfile is null)
            return null;

        EnsureAliasForProfile(dialog.SelectedProfile.Name, challongeName);
        LoadPlayerDatabase();
        return FindProfileByNameOrAlias(dialog.SelectedProfile.Name);
    }

    private PlayerProfile? CreatePlayerFromChallongeName(string challongeName)
    {
        var newProfile = new PlayerProfile
        {
            Name = challongeName.Trim()
        };

        var dialog = new PlayerEditWindow(_countries, newProfile)
        {
            Owner = this,
            Title = $"Create Player for '{challongeName}'"
        };

        if (dialog.ShowDialog() != true || dialog.Result is null)
            return null;

        var created = dialog.Result;
        if (string.IsNullOrWhiteSpace(created.Name))
        {
            MessageBox.Show("Name is required.", "Create Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        if (_playerDatabase.Players.Any(x => string.Equals(x.Name, created.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A player with that name already exists. Select it from the list.", "Create Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return FindProfileByNameOrAlias(created.Name);
        }

        _playerDatabase.Players.Add(created);
        EnsureAliasForProfile(created.Name, challongeName);
        PlayerDatabaseStore.Save(_playerDatabasePath, _playerDatabase);
        RefreshPlayerProfiles();
        return created;
    }

    private static PlayerInfo ToPlayerInfo(PlayerProfile? profile, string fallbackName)
    {
        if (profile is null)
        {
            return new PlayerInfo
            {
                Name = fallbackName,
                Team = string.Empty,
                Country = CountryId.Unknown
            };
        }

        var country = Enum.TryParse<CountryId>(profile.Country, true, out var parsedCountry)
            ? parsedCountry
            : CountryId.Unknown;

        return new PlayerInfo
        {
            Name = profile.Name,
            Team = profile.Team,
            Country = country,
            Characters = ParseCharacters(profile.Characters)
        };
    }

    private static bool IsPlaceholderName(string name)
        => string.Equals(name.Trim(), "TBD", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name.Trim(), "BYE", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var builder = new StringBuilder(input.Length);
        foreach (var c in input.Trim())
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    private void ApplyMatchToFields(MatchState match, ChallongeQueueEntry? entry = null)
    {
        P1Name.Text = match.Player1.Name;
        P1Team.Text = match.Player1.Team;
        P1Characters.Text = ResolveCharactersText(match.Player1, entry?.Player1CharactersText);

        P2Name.Text = match.Player2.Name;
        P2Team.Text = match.Player2.Team;
        P2Characters.Text = ResolveCharactersText(match.Player2, entry?.Player2CharactersText);

        P1Country.SelectedItem = _countries.FirstOrDefault(x => x.Id == match.Player1.Country)
            ?? _countries.FirstOrDefault(x => x.Id == CountryId.Unknown);

        P2Country.SelectedItem = _countries.FirstOrDefault(x => x.Id == match.Player2.Country)
            ?? _countries.FirstOrDefault(x => x.Id == CountryId.Unknown);
    }

    private static string ResolveCharactersText(PlayerInfo player, string? entryCharactersText)
    {
        if (!string.IsNullOrWhiteSpace(entryCharactersText))
            return entryCharactersText;

        return ToCharactersText(player.Characters);
    }

    private static string ToCharactersText(IReadOnlyList<FGCharacterId> characters)
        => string.Join(", ", characters);

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

    private async Task SelectAndApplyProfileAsync(bool isPlayerOne)
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
            var selected = FindProfileByNameOrAlias(dialog.SelectedProfile.Name);
            if (selected is null)
                return;

            ApplyProfileToFields(selected, isPlayerOne);
            await _host.SetPlayerAsync(isPlayerOne, ToPlayerInfo(selected, selected.Name), CancellationToken.None);
            ApplyProfileToCurrentQueueSelection(selected, isPlayerOne);
        }
    }

    private void ApplyProfileToCurrentQueueSelection(PlayerProfile selectedProfile, bool isPlayerOne)
    {
        if (_currentQueueIndex < 0 || _currentQueueIndex >= _challongeQueueEntries.Count)
            return;

        var entry = _challongeQueueEntries[_currentQueueIndex];
        var challongeName = isPlayerOne ? entry.RawPlayer1Name : entry.RawPlayer2Name;
        EnsureAliasForProfile(selectedProfile.Name, challongeName);

        var refreshedProfile = FindProfileByNameOrAlias(selectedProfile.Name) ?? selectedProfile;
        var match = entry.ResolvedMatch ?? entry.GetDisplayMatch();
        var updatedPlayer = ToPlayerInfo(refreshedProfile, challongeName) with
        {
            Score = isPlayerOne ? match.Player1.Score : match.Player2.Score
        };

        entry.ResolvedMatch = isPlayerOne
            ? match with { Player1 = updatedPlayer }
            : match with { Player2 = updatedPlayer };
        if (isPlayerOne)
            entry.Player1CharactersText = refreshedProfile.Characters;
        else
            entry.Player2CharactersText = refreshedProfile.Characters;

        UpdateQueueListVisuals();
    }

    private void PersistCurrentQueueEntryStateFromHost()
    {
        if (_currentQueueIndex < 0 || _currentQueueIndex >= _challongeQueueEntries.Count)
            return;

        var entry = _challongeQueueEntries[_currentQueueIndex];
        entry.ResolvedMatch = _host.State.CurrentMatch;
        entry.Player1CharactersText = P1Characters.Text?.Trim() ?? string.Empty;
        entry.Player2CharactersText = P2Characters.Text?.Trim() ?? string.Empty;
    }

    private void EnsureAliasForProfile(string profileName, string challongeName)
    {
        var alias = challongeName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(alias) || IsPlaceholderName(alias))
            return;

        var normalizedAlias = NormalizeName(alias);
        if (string.IsNullOrWhiteSpace(normalizedAlias))
            return;

        var profile = _playerDatabase.Players.FirstOrDefault(player =>
            string.Equals(player.Name, profileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
            return;

        if (string.Equals(NormalizeName(profile.Name), normalizedAlias, StringComparison.Ordinal))
            return;

        if (_playerDatabase.Players.Any(player =>
                !string.Equals(player.Name, profile.Name, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(NormalizeName(player.Name), normalizedAlias, StringComparison.Ordinal)
                    || player.Aliases.Any(existing =>
                        string.Equals(NormalizeName(existing), normalizedAlias, StringComparison.Ordinal)))))
        {
            return;
        }

        if (profile.Aliases.Any(existing =>
                string.Equals(NormalizeName(existing), normalizedAlias, StringComparison.Ordinal)))
        {
            return;
        }

        profile.Aliases.Add(alias);
        profile.Aliases = profile.Aliases
            .Where(existing => !string.IsNullOrWhiteSpace(existing))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(existing => existing, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PlayerDatabaseStore.Save(_playerDatabasePath, _playerDatabase);
        RefreshPlayerProfiles();
    }

    private sealed class ChallongeQueueEntry
    {
        public long MatchId { get; init; }
        public long? ChallongePlayer1Id { get; init; }
        public long? ChallongePlayer2Id { get; init; }
        public string ChallongeState { get; set; } = string.Empty;
        public string? ChallongeScoresCsv { get; set; }
        public long? ChallongeWinnerId { get; set; }
        public bool IsReportedToChallonge { get; set; }
        public string RawPlayer1Name { get; init; } = string.Empty;
        public string RawPlayer2Name { get; init; } = string.Empty;
        public string RoundLabel { get; init; } = string.Empty;
        public string? Player1CharactersText { get; set; }
        public string? Player2CharactersText { get; set; }
        public MatchState? ResolvedMatch { get; set; }

        public MatchState GetDisplayMatch()
            => ResolvedMatch ?? new MatchState
            {
                RoundLabel = RoundLabel,
                Format = MatchSetFormat.FT2,
                Player1 = new PlayerInfo
                {
                    Name = RawPlayer1Name,
                    Team = string.Empty,
                    Country = CountryId.Unknown
                },
                Player2 = new PlayerInfo
                {
                    Name = RawPlayer2Name,
                    Team = string.Empty,
                    Country = CountryId.Unknown
                }
            };
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
