using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ChallongeInterface;
using ChallongeInterface.Models;
using Microsoft.Win32;
using TournamentAutomation.Application;
using TournamentAutomation.Domain;
using TournamentAutomation.Presentation;
using System.Windows;
using System.Net.Http;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TournamentAutomation.Configuration;

namespace TournamentAutomation.Ui;

public partial class MainWindow : Window
{
    private readonly ConsoleAppLogger _logger;
    private AutomationHost _host;
    private readonly ObservableCollection<CountryInfo> _countries = new();
    private readonly ObservableCollection<PlayerProfile> _playerProfiles = new();
    private readonly ObservableCollection<QueueRowViewModel> _queueRows = new();
    private readonly List<ChallongeQueueEntry> _challongeQueueEntries = new();
    private string _settingsPath;
    private readonly UserSettings _settings;
    private PlayerDatabase _playerDatabase = new();
    private string _playerDatabasePath = string.Empty;
    private int _currentQueueIndex = -1;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainWindow()
    {
        InitializeComponent();

        _settingsPath = GetSettingsPath();
        _settings = UserSettingsStore.Load(_settingsPath);
        _settings.SettingsFolderPath ??= Path.GetDirectoryName(_settingsPath);
        _playerDatabasePath = string.IsNullOrWhiteSpace(_settings.PlayerDatabasePath)
            ? GetDefaultPlayerDatabasePath(_settingsPath)
            : _settings.PlayerDatabasePath;
        _settings.PlayerDatabasePath ??= _playerDatabasePath;
        PersistSettings();

        _logger = new ConsoleAppLogger();
        _host = new AutomationHost(BuildRuntimeConfig(), _logger);

        foreach (var entry in GetConfiguredCountries().OrderBy(x => x.Acronym, StringComparer.OrdinalIgnoreCase))
            _countries.Add(entry);

        P1Country.ItemsSource = _countries;
        P2Country.ItemsSource = _countries;

        P1Country.SelectedItem = _countries.FirstOrDefault(IsUnknownCountry) ?? _countries.FirstOrDefault();
        P2Country.SelectedItem = _countries.FirstOrDefault(IsUnknownCountry) ?? _countries.FirstOrDefault();

        LoadPlayerDatabase();

        QueueListBox.ItemsSource = _queueRows;
        ChallongeTournamentBox.Text = _settings.ChallongeTournament
            ?? Environment.GetEnvironmentVariable("CHALLONGE_TOURNAMENT")
            ?? string.Empty;

        ChallongeApiKeyBox.Text = _settings.ChallongeApiKey
            ?? Environment.GetEnvironmentVariable("CHALLONGE_API_KEY")
            ?? string.Empty;

        RenderSceneButtons();
        UpdateQueueListVisuals();
        UpdateCharacterButtonLabels();
        SyncScoreDisplaysFromState();
        MoveNextOnCommitMenuItem.IsChecked = _settings.MoveToNextOpenMatchOnCommitToChallonge;
        _toastTimer.Tick += (_, _) => HideActionToast();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshObsStatusAsync();
        await RefreshChallongeQueueAsync(showMissingCredentialsWarning: false, showErrorDialog: false);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasObsCredentials())
        {
            SetObsStatus("No Credentials", "#FACC15");
            MessageBox.Show("Configure OBS credentials first.", "OBS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RefreshObsStatusAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var p1Country = P1Country.SelectedItem as CountryInfo;
        var p2Country = P2Country.SelectedItem as CountryInfo;
        var p1 = BuildPlayer(P1Name.Text, P1Team.Text, p1Country, P1Characters.Text);
        var p2 = BuildPlayer(P2Name.Text, P2Team.Text, p2Country, P2Characters.Text);

        ShowPendingStatus("Waiting for OBS to respond...");
        try
        {
            await _host.SetPlayerAsync(true, p1, CancellationToken.None);
            await _host.SetPlayerAsync(false, p2, CancellationToken.None);
            await _host.RefreshOverlayAsync(CancellationToken.None);
            ShowActionToast("OBS overlay updated.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowActionToast($"OBS update failed: {ex.Message}", ToastKind.Error);
        }
    }

    private async void ApplyP1_Click(object sender, RoutedEventArgs e)
    {
        var country = P1Country.SelectedItem as CountryInfo;
        var player = BuildPlayer(P1Name.Text, P1Team.Text, country, P1Characters.Text);
        ShowPendingStatus("Waiting for OBS to respond...");
        try
        {
            await _host.SetPlayerAsync(true, player, CancellationToken.None);
            SavePlayerProfile(player, P1Characters.Text, country?.Acronym);
            ShowActionToast("Player 1 applied to OBS.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowActionToast($"Failed to apply Player 1: {ex.Message}", ToastKind.Error);
        }
    }

    private void SaveP1_Click(object sender, RoutedEventArgs e)
    {
        var country = P1Country.SelectedItem as CountryInfo;
        var player = BuildPlayer(P1Name.Text, P1Team.Text, country, P1Characters.Text);
        SavePlayerProfile(player, P1Characters.Text, country?.Acronym);
    }

    private async void ApplyP2_Click(object sender, RoutedEventArgs e)
    {
        var country = P2Country.SelectedItem as CountryInfo;
        var player = BuildPlayer(P2Name.Text, P2Team.Text, country, P2Characters.Text);
        ShowPendingStatus("Waiting for OBS to respond...");
        try
        {
            await _host.SetPlayerAsync(false, player, CancellationToken.None);
            SavePlayerProfile(player, P2Characters.Text, country?.Acronym);
            ShowActionToast("Player 2 applied to OBS.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowActionToast($"Failed to apply Player 2: {ex.Message}", ToastKind.Error);
        }
    }

    private void SaveP2_Click(object sender, RoutedEventArgs e)
    {
        var country = P2Country.SelectedItem as CountryInfo;
        var player = BuildPlayer(P2Name.Text, P2Team.Text, country, P2Characters.Text);
        SavePlayerProfile(player, P2Characters.Text, country?.Acronym);
    }

    private async void SceneInMatch_Click(object sender, RoutedEventArgs e)
        => await SwitchSceneIfMappedAsync(_host.Config.Scenes.InMatch);

    private async void SceneDesk_Click(object sender, RoutedEventArgs e)
        => await SwitchSceneIfMappedAsync(_host.Config.Scenes.Desk);

    private async void SceneBreak_Click(object sender, RoutedEventArgs e)
        => await SwitchSceneIfMappedAsync(_host.Config.Scenes.Break);

    private async void SceneResults_Click(object sender, RoutedEventArgs e)
        => await SwitchSceneIfMappedAsync(_host.Config.Scenes.Results);

    private async void P1Up_Click(object sender, RoutedEventArgs e)
    {
        await _host.AdjustScoreAsync(true, 1, CancellationToken.None);
        PersistCurrentQueueEntryStateFromHost();
        SyncScoreDisplaysFromState();
    }

    private async void P1Down_Click(object sender, RoutedEventArgs e)
    {
        await _host.AdjustScoreAsync(true, -1, CancellationToken.None);
        PersistCurrentQueueEntryStateFromHost();
        SyncScoreDisplaysFromState();
    }

    private async void P2Up_Click(object sender, RoutedEventArgs e)
    {
        await _host.AdjustScoreAsync(false, 1, CancellationToken.None);
        PersistCurrentQueueEntryStateFromHost();
        SyncScoreDisplaysFromState();
    }

    private async void P2Down_Click(object sender, RoutedEventArgs e)
    {
        await _host.AdjustScoreAsync(false, -1, CancellationToken.None);
        PersistCurrentQueueEntryStateFromHost();
        SyncScoreDisplaysFromState();
    }

    private async void CommitToChallonge_Click(object sender, RoutedEventArgs e)
        => await CommitCurrentMatchToChallongeAsync();

    private async void Swap_Click(object sender, RoutedEventArgs e)
    {
        var current = _host.State.CurrentMatch;
        var swapped = current with
        {
            Player1 = current.Player2,
            Player2 = current.Player1
        };

        await _host.SetCurrentMatchAsync(swapped, CancellationToken.None);
        SwapPlayerFieldValues();
        PersistCurrentQueueEntryStateFromHost();
        SyncScoreDisplaysFromState();
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        await _host.ResetMatchAsync(CancellationToken.None);
        SyncScoreDisplaysFromState();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_challongeQueueEntries.Count == 0)
        {
            await _host.LoadNextAsync(CancellationToken.None);
            SyncScoreDisplaysFromState();
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

    private void QueueListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        for (var i = 0; i < _queueRows.Count; i++)
            _queueRows[i].IsSelected = i == QueueListBox.SelectedIndex;
    }

    private void QueueListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not ListBoxItem)
            source = VisualTreeHelper.GetParent(source);

        if (source is ListBoxItem item)
            item.IsSelected = true;
    }

    private void QueueListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindDescendant<ScrollViewer>(QueueListBox) is not ScrollViewer scrollViewer)
            return;

        if (e.Delta > 0)
            scrollViewer.LineUp();
        else
            scrollViewer.LineDown();

        e.Handled = true;
    }

    private void QueueListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (QueueListBox.SelectedItem is not QueueRowViewModel row)
            return;

        var menu = new ContextMenu();

        var setStateMenu = new MenuItem { Header = "Set State" };
        setStateMenu.Items.Add(CreateQueueActionMenuItem("Pending", async () => await SetChallongeMatchStateAsync(row.MatchId, "pending")));
        setStateMenu.Items.Add(CreateQueueActionMenuItem("Open", async () => await SetChallongeMatchStateAsync(row.MatchId, "open")));
        setStateMenu.Items.Add(CreateQueueActionMenuItem("Underway", async () => await SetChallongeMatchStateAsync(row.MatchId, "underway")));
        setStateMenu.Items.Add(CreateQueueActionMenuItem("Complete", async () => await SetChallongeMatchStateAsync(row.MatchId, "complete")));

        var dqMenu = new MenuItem { Header = "DQ" };
        dqMenu.Items.Add(CreateQueueActionMenuItem("DQ P1", async () => await SubmitDqToChallongeAsync(row.MatchId, "p1")));
        dqMenu.Items.Add(CreateQueueActionMenuItem("DQ P2", async () => await SubmitDqToChallongeAsync(row.MatchId, "p2")));
        dqMenu.Items.Add(CreateQueueActionMenuItem("DQ Both", async () => await SubmitDqToChallongeAsync(row.MatchId, "both")));

        menu.Items.Add(setStateMenu);
        menu.Items.Add(dqMenu);
        QueueListBox.ContextMenu = menu;
    }

    private static MenuItem CreateQueueActionMenuItem(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        await _host.UndoAsync(CancellationToken.None);
        SyncScoreDisplaysFromState();
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        await _host.RedoAsync(CancellationToken.None);
        SyncScoreDisplaysFromState();
    }

    private async void SetP1_Click(object sender, RoutedEventArgs e)
        => await SelectAndApplyProfileAsync(isPlayerOne: true);

    private async void SetP2_Click(object sender, RoutedEventArgs e)
        => await SelectAndApplyProfileAsync(isPlayerOne: false);

    private void P1CharactersButton_Click(object sender, RoutedEventArgs e)
        => EditCharacters(isPlayerOne: true);

    private void P2CharactersButton_Click(object sender, RoutedEventArgs e)
        => EditCharacters(isPlayerOne: false);

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Set settings folder",
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = "Select this folder",
            Filter = "Folder marker|*.folder",
            InitialDirectory = Path.GetDirectoryName(_settingsPath) ?? AppContext.BaseDirectory,
        };

        if (dialog.ShowDialog() != true)
            return;

        var selectedFolder = Path.GetDirectoryName(dialog.FileName)?.Trim();
        if (string.IsNullOrWhiteSpace(selectedFolder))
            return;
        var previousDatabasePath = _playerDatabasePath;
        var newSettingsPath = Path.Combine(selectedFolder, "ui-settings.json");
        var newDatabasePath = Path.Combine(selectedFolder, "players.json");

        if (!string.Equals(previousDatabasePath, newDatabasePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(previousDatabasePath)
            && !File.Exists(newDatabasePath))
        {
            File.Copy(previousDatabasePath, newDatabasePath);
        }

        _settingsPath = newSettingsPath;
        _settings.SettingsFolderPath = selectedFolder;
        _playerDatabasePath = newDatabasePath;
        _settings.PlayerDatabasePath = _playerDatabasePath;
        PersistSettings();
        LoadPlayerDatabase();
        ShowActionToast($"Settings folder set to '{selectedFolder}'.", ToastKind.Success);
    }

    private void SettingsCogButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsCogButton.ContextMenu is null)
            return;

        SettingsCogButton.ContextMenu.PlacementTarget = SettingsCogButton;
        SettingsCogButton.ContextMenu.IsOpen = true;
    }

    private void MoveNextOnCommitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.MoveToNextOpenMatchOnCommitToChallonge = MoveNextOnCommitMenuItem.IsChecked;
        PersistSettings();
    }

    private void ManageCountriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowCountriesDialog())
            return;

        ReloadCountriesFromSettings();
        RebuildHostFromSettings();
    }

    private async void ObsCredsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowObsCredentialsDialog())
            return;

        await RefreshObsStatusAsync();
    }

    private async void ChallongeCredsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowChallongeCredentialsDialog())
            return;

        await RefreshChallongeQueueAsync(showMissingCredentialsWarning: false, showErrorDialog: true);
    }

    private void ObsMappingsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ShowObsMappingsDialog();
    }

    private void OpenPlayerDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PlayerSelectWindow(_playerProfiles, _playerDatabase, _playerDatabasePath, _countries)
        {
            Owner = this,
            Title = "Manage Player Database"
        };

        _ = dialog.ShowDialog();
        LoadPlayerDatabase();
    }

    private async void RefreshQueueButton_Click(object sender, RoutedEventArgs e)
        => await RefreshChallongeQueueAsync(showMissingCredentialsWarning: true, showErrorDialog: true);

    private async Task RefreshChallongeQueueAsync(bool showMissingCredentialsWarning, bool showErrorDialog)
    {
        var tournament = ChallongeTournamentBox.Text.Trim();
        var apiKey = ChallongeApiKeyBox.Text.Trim();
        var selectedMatchId = _currentQueueIndex >= 0 && _currentQueueIndex < _challongeQueueEntries.Count
            ? _challongeQueueEntries[_currentQueueIndex].MatchId
            : (long?)null;

        if (string.IsNullOrWhiteSpace(tournament) || string.IsNullOrWhiteSpace(apiKey))
        {
            SetChallongeStatus("No Credentials", "#FACC15");
            if (showMissingCredentialsWarning)
            {
                MessageBox.Show(
                    "Enter both Challonge tournament and API key before refreshing.",
                    "Challonge Queue",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return;
        }

        _settings.ChallongeTournament = tournament;
        _settings.ChallongeApiKey = apiKey;
        PersistSettings();

        QueuePositionLabel.Content = "Queue position: loading...";
        SetChallongeStatus("Checking...", "#FACC15");
        ShowPendingStatus("Waiting for Challonge to respond...");

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
            }

            UpdateQueueListVisuals();
            SetChallongeStatus("Connected", "#22C55E");
            ShowActionToast("Challonge queue refreshed.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            QueuePositionLabel.Content = "Queue position: failed to refresh";
            SetChallongeStatus("Disconnected", "#EF4444");
            ShowActionToast($"Challonge refresh failed: {ex.Message}", ToastKind.Error);
            if (showErrorDialog)
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
        SyncScoreDisplaysFromState();
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
            var row = BuildQueueRowViewModel(i, entry, match);
            _queueRows.Add(row);
        }

        if (_currentQueueIndex >= 0 && _currentQueueIndex < _queueRows.Count)
        {
            _queueRows[_currentQueueIndex].IsSelected = true;
            QueueListBox.SelectedIndex = _currentQueueIndex;
            QueueListBox.ScrollIntoView(QueueListBox.SelectedItem);
            QueuePositionLabel.Content = $"Queue position: {_currentQueueIndex + 1} / {_challongeQueueEntries.Count}";
        }
        else
        {
            QueueListBox.SelectedIndex = -1;
            QueuePositionLabel.Content = $"Queue position: not selected ({_challongeQueueEntries.Count} matches)";
            ScrollQueueViewportToFirstOpenMatch();
        }
    }

    private void ScrollQueueViewportToFirstOpenMatch()
    {
        if (_queueRows.Count == 0 || _currentQueueIndex >= 0)
            return;

        var firstOpenIndex = _challongeQueueEntries.FindIndex(entry =>
            string.Equals(entry.ChallongeState?.Trim(), "open", StringComparison.OrdinalIgnoreCase));
        if (firstOpenIndex < 0 || firstOpenIndex >= _queueRows.Count)
            return;

        var row = _queueRows[firstOpenIndex];
        _ = Dispatcher.InvokeAsync(() => QueueListBox.ScrollIntoView(row), DispatcherPriority.Background);
    }

    private QueueRowViewModel BuildQueueRowViewModel(int index, ChallongeQueueEntry entry, MatchState match)
    {
        var unresolvedMarker = entry.ResolvedMatch is null ? " [Needs Mapping]" : string.Empty;
        var metaText = $"{entry.RoundLabel}";

        var status = string.IsNullOrWhiteSpace(entry.ChallongeState)
            ? "pending"
            : entry.ChallongeState.Trim().ToLowerInvariant();
        var statusDisplay = status switch
        {
            "complete" => "Complete",
            "open" => "Open",
            "pending" => "Pending",
            "underway" => "Underway",
            _ => status
        };
        var statusBrush = status switch
        {
            "complete" => Brushes.LimeGreen,
            "open" => Brushes.Gold,
            "underway" => Brushes.Gold,
            "pending" => Brushes.Gray,
            _ => Brushes.Silver
        };

        int p1Score;
        int p2Score;
        if (!TryParseDisplayedScores(entry.ChallongeScoresCsv, out p1Score, out p2Score))
        {
            p1Score = match.Player1.Score;
            p2Score = match.Player2.Score;
        }

        return new QueueRowViewModel
        {
            MatchId = entry.MatchId,
            MetaText = $"{metaText}{unresolvedMarker}",
            ChallongeStatusText = statusDisplay,
            ChallongeStatusBrush = statusBrush,
            MainText = $"{match.Player1.Name} ({p1Score}) vs ({p2Score}) {match.Player2.Name}",
            IsSelected = false,
            IsLoaded = index == _currentQueueIndex
        };
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
        return $"Match {identifier} - Round {roundText}";
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

        var player1 = ToPlayerInfo(p1, entry.RawPlayer1Name);
        var player2 = ToPlayerInfo(p2, entry.RawPlayer2Name);
        if (TryParseDisplayedScores(entry.ChallongeScoresCsv, out var p1Score, out var p2Score))
        {
            player1 = player1 with { Score = p1Score };
            player2 = player2 with { Score = p2Score };
        }

        entry.ResolvedMatch = new MatchState
        {
            RoundLabel = entry.RoundLabel,
            Format = MatchSetFormat.FT2,
            Player1 = player1,
            Player2 = player2
        };
        entry.Player1CharactersText = p1?.Characters;
        entry.Player2CharactersText = p2?.Characters;

        return entry.ResolvedMatch;
    }

    private async Task CommitCurrentMatchToChallongeAsync()
    {
        if (_challongeQueueEntries.Count == 0 || _currentQueueIndex < 0 || _currentQueueIndex >= _challongeQueueEntries.Count)
        {
            ShowActionToast("Load and select a Challonge queue match first.", ToastKind.Warning);
            return;
        }

        var tournament = ChallongeTournamentBox.Text.Trim();
        var apiKey = ChallongeApiKeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(tournament) || string.IsNullOrWhiteSpace(apiKey))
        {
            ShowActionToast("Tournament and API key are required.", ToastKind.Warning);
            return;
        }

        PersistCurrentQueueEntryStateFromHost();
        var entry = _challongeQueueEntries[_currentQueueIndex];
        if (entry.ChallongePlayer1Id is null || entry.ChallongePlayer2Id is null)
        {
            ShowActionToast("This match does not yet have both Challonge participants assigned.", ToastKind.Warning);
            return;
        }

        var current = _host.State.CurrentMatch;
        string scoreCsv;
        long winnerId;
        var p1Score = current.Player1.Score;
        var p2Score = current.Player2.Score;
        if (p1Score == p2Score)
        {
            ShowActionToast("Scores are tied. Set a winner before committing.", ToastKind.Warning);
            return;
        }

        scoreCsv = $"{p1Score}-{p2Score}";
        winnerId = p1Score > p2Score ? entry.ChallongePlayer1Id.Value : entry.ChallongePlayer2Id.Value;

        try
        {
            ShowPendingStatus("Waiting for Challonge to respond...");
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
            var committedMatchId = entry.MatchId;
            await RefreshChallongeQueueAsync(showMissingCredentialsWarning: false, showErrorDialog: false);
            ShowActionToast($"Committed to Challonge ({scoreCsv}).", ToastKind.Success);

            if (_settings.MoveToNextOpenMatchOnCommitToChallonge)
                await MoveToNextOpenMatchAfterCommitAsync(committedMatchId);
        }
        catch (Exception ex)
        {
            ShowActionToast($"Commit failed: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task MoveToNextOpenMatchAfterCommitAsync(long committedMatchId)
    {
        if (_challongeQueueEntries.Count == 0)
            return;

        var committedIndex = _challongeQueueEntries.FindIndex(entry => entry.MatchId == committedMatchId);
        if (committedIndex < 0)
            committedIndex = _currentQueueIndex;

        var nextOpenIndex = FindOpenQueueIndex(committedIndex + 1, _challongeQueueEntries.Count);
        if (nextOpenIndex < 0)
            nextOpenIndex = FindOpenQueueIndex(0, Math.Max(committedIndex, 0));

        if (nextOpenIndex >= 0)
        {
            await NavigateQueueAsync(nextOpenIndex);
            return;
        }

        ShowActionToast("No open matches remaining.", ToastKind.Warning);
    }

    private int FindOpenQueueIndex(int startInclusive, int endExclusive)
    {
        var start = Math.Max(startInclusive, 0);
        var end = Math.Min(endExclusive, _challongeQueueEntries.Count);
        for (var i = start; i < end; i++)
        {
            var state = _challongeQueueEntries[i].ChallongeState?.Trim() ?? string.Empty;
            if (state.Equals("open", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private async Task SetChallongeMatchStateAsync(long matchId, string targetState)
    {
        var tournament = ChallongeTournamentBox.Text.Trim();
        var apiKey = ChallongeApiKeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(tournament) || string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("Tournament and API key are required.", "Set Challonge State", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entry = _challongeQueueEntries.FirstOrDefault(x => x.MatchId == matchId);
        if (entry is null)
        {
            MessageBox.Show("Match not found in queue.", "Set Challonge State", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            ShowPendingStatus("Waiting for Challonge to respond...");
            using var httpClient = new HttpClient();
            var client = new ChallongeClient(httpClient, new ChallongeClientOptions { ApiKey = apiKey });

            switch (targetState)
            {
                case "underway":
                    _ = await client.MatchActionAsync(tournament, matchId, "mark_as_underway", CancellationToken.None);
                    break;
                case "open":
                    _ = await client.MatchActionAsync(tournament, matchId, "reopen", CancellationToken.None);
                    break;
                case "pending":
                    _ = await client.UpdateMatchStateAsync(tournament, matchId, "pending", CancellationToken.None);
                    break;
                case "complete":
                {
                    var match = entry.ResolvedMatch ?? entry.GetDisplayMatch();
                    var p1Score = match.Player1.Score;
                    var p2Score = match.Player2.Score;
                    if (p1Score == p2Score)
                    {
                        MessageBox.Show("Cannot complete with a tied score. Set a winner first.", "Set Challonge State", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (entry.ChallongePlayer1Id is null || entry.ChallongePlayer2Id is null)
                    {
                        MessageBox.Show("Both Challonge participants are required for completion.", "Set Challonge State", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var winnerId = p1Score > p2Score ? entry.ChallongePlayer1Id.Value : entry.ChallongePlayer2Id.Value;
                    var scoreCsv = $"{p1Score}-{p2Score}";
                    _ = await client.UpdateMatchAsync(tournament, matchId, scoreCsv, winnerId, CancellationToken.None);
                    break;
                }
                default:
                    MessageBox.Show("Unsupported state selection.", "Set Challonge State", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
            }

            await RefreshChallongeQueueAsync(showMissingCredentialsWarning: false, showErrorDialog: true);
            ShowActionToast($"State updated to '{targetState}'.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowActionToast($"Set state failed: {ex.Message}", ToastKind.Error);
            MessageBox.Show(ex.Message, "Set Challonge State", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SubmitDqToChallongeAsync(long matchId, string dqMode)
    {
        var tournament = ChallongeTournamentBox.Text.Trim();
        var apiKey = ChallongeApiKeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(tournament) || string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("Tournament and API key are required.", "DQ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entry = _challongeQueueEntries.FirstOrDefault(x => x.MatchId == matchId);
        if (entry is null)
        {
            MessageBox.Show("Match not found in queue.", "DQ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (entry.ChallongePlayer1Id is null || entry.ChallongePlayer2Id is null)
        {
            MessageBox.Show("This match does not yet have both Challonge participants assigned.", "DQ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string scoreCsv;
        long winnerId;

        switch (dqMode)
        {
            case "p1":
                scoreCsv = "-1-0";
                winnerId = entry.ChallongePlayer2Id.Value;
                break;
            case "p2":
                scoreCsv = "0--1";
                winnerId = entry.ChallongePlayer1Id.Value;
                break;
            case "both":
            {
                var prompt = MessageBox.Show(
                    "Both players DQ selected.\n\nYes = P1 advances\nNo = P2 advances\nCancel = abort",
                    "DQ Both",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (prompt == MessageBoxResult.Cancel)
                    return;

                scoreCsv = "-1--1";
                winnerId = prompt == MessageBoxResult.Yes
                    ? entry.ChallongePlayer1Id.Value
                    : entry.ChallongePlayer2Id.Value;
                break;
            }
            default:
                return;
        }

        try
        {
            ShowPendingStatus("Waiting for Challonge to respond...");
            using var httpClient = new HttpClient();
            var client = new ChallongeClient(httpClient, new ChallongeClientOptions { ApiKey = apiKey });
            _ = await client.UpdateMatchAsync(tournament, matchId, scoreCsv, winnerId, CancellationToken.None);
            await RefreshChallongeQueueAsync(showMissingCredentialsWarning: false, showErrorDialog: true);
            ShowActionToast("DQ result submitted.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowActionToast($"DQ submission failed: {ex.Message}", ToastKind.Error);
            MessageBox.Show(ex.Message, "DQ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SwapPlayerFieldValues()
    {
        (P1Team.Text, P2Team.Text) = (P2Team.Text, P1Team.Text);
        (P1Name.Text, P2Name.Text) = (P2Name.Text, P1Name.Text);
        (P1Characters.Text, P2Characters.Text) = (P2Characters.Text, P1Characters.Text);
        (P1Country.SelectedItem, P2Country.SelectedItem) = (P2Country.SelectedItem, P1Country.SelectedItem);
        UpdateCharacterButtonLabels();
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

        var dialog = new PlayerSelectWindow(_playerProfiles, _playerDatabase, _playerDatabasePath, _countries, doubleClickSelectsPlayer: true)
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

    private PlayerInfo ToPlayerInfo(PlayerProfile? profile, string fallbackName)
    {
        if (profile is null)
        {
            return new PlayerInfo
            {
                Name = fallbackName,
                Team = string.Empty,
                Country = CountryId.Unknown,
                CustomCountryCode = string.Empty,
                CustomCountryName = string.Empty,
                CustomFlagPath = string.Empty
            };
        }

        var countryInfo = ResolveCountryInfo(profile.Country);

        return new PlayerInfo
        {
            Name = profile.Name,
            Team = profile.Team,
            Country = countryInfo.Id,
            CustomCountryCode = countryInfo.Acronym,
            CustomCountryName = countryInfo.DisplayName,
            CustomFlagPath = countryInfo.FlagPath,
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

        P1Country.SelectedItem = ResolveCountrySelection(match.Player1) ?? _countries.FirstOrDefault(IsUnknownCountry);
        P2Country.SelectedItem = ResolveCountrySelection(match.Player2) ?? _countries.FirstOrDefault(IsUnknownCountry);

        UpdateCharacterButtonLabels();
        SyncScoreDisplaysFromState();
    }

    private static string ResolveCharactersText(PlayerInfo player, string? entryCharactersText)
    {
        if (!string.IsNullOrWhiteSpace(entryCharactersText))
            return entryCharactersText;

        return ToCharactersText(player.Characters);
    }

    private static string ToCharactersText(IReadOnlyList<FGCharacterId> characters)
        => string.Join(", ", characters);

    private static PlayerInfo BuildPlayer(string name, string team, CountryInfo? country, string characters)
    {
        var ids = ParseCharacters(characters);
        var code = country?.Acronym?.Trim() ?? string.Empty;
        var resolvedCountry = Enum.TryParse<CountryId>(code, true, out var parsedCountry)
            ? parsedCountry
            : CountryId.Unknown;
        return new PlayerInfo
        {
            Name = name ?? string.Empty,
            Team = team ?? string.Empty,
            Country = resolvedCountry,
            CustomCountryCode = code,
            CustomCountryName = country?.DisplayName?.Trim() ?? string.Empty,
            CustomFlagPath = country?.FlagPath?.Trim() ?? string.Empty,
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

    private void SavePlayerProfile(PlayerInfo player, string charactersText, string? countryCode)
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
        existing.Country = countryCode?.Trim() ?? string.Empty;
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
        UpdateCharacterButtonLabels();

        countryBox.SelectedItem = ResolveCountryInfo(profile.Country);
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
            ShowPendingStatus("Waiting for OBS to respond...");
            try
            {
                await _host.SetPlayerAsync(isPlayerOne, ToPlayerInfo(selected, selected.Name), CancellationToken.None);
                ShowActionToast("Player applied from database.", ToastKind.Success);
            }
            catch (Exception ex)
            {
                ShowActionToast($"Failed to apply player: {ex.Message}", ToastKind.Error);
            }
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

    private void EditCharacters(bool isPlayerOne)
    {
        var charactersBox = isPlayerOne ? P1Characters : P2Characters;
        var existing = charactersBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedCharacters = new ObservableCollection<string>(existing);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = isPlayerOne ? "Edit P1 Characters" : "Edit P2 Characters",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var pickerRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var picker = new TextBox { MinWidth = 180, ToolTip = "Enter any character name" };
        var addButton = new Button { Content = "Add", Padding = new Thickness(10, 4, 10, 4) };
        Grid.SetColumn(addButton, 1);
        pickerRow.Children.Add(picker);
        pickerRow.Children.Add(addButton);
        panel.Children.Add(pickerRow);

        var selectedList = new ListBox { Height = 150, ItemsSource = selectedCharacters, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(selectedList);

        var actionRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var removeButton = new Button { Content = "Remove Selected", Padding = new Thickness(10, 4, 10, 4) };
        var clearButton = new Button { Content = "Clear All", Padding = new Thickness(10, 4, 10, 4) };
        actionRow.Children.Add(removeButton);
        actionRow.Children.Add(clearButton);
        panel.Children.Add(actionRow);

        var saveButton = new Button { Content = "Save", Width = 88, Margin = new Thickness(4) };
        var cancelButton = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(4) };
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonRow.Children.Add(saveButton);
        buttonRow.Children.Add(cancelButton);
        panel.Children.Add(buttonRow);

        var window = new Window
        {
            Owner = this,
            Title = "Characters",
            Content = panel,
            Width = 360,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#101315")!,
            Foreground = Brushes.White
        };

        addButton.Click += (_, _) =>
        {
            var choice = picker.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(choice))
                return;

            if (!selectedCharacters.Any(existingCharacter =>
                    string.Equals(existingCharacter, choice, StringComparison.OrdinalIgnoreCase)))
                selectedCharacters.Add(choice);

            picker.Clear();
        };
        removeButton.Click += (_, _) =>
        {
            if (selectedList.SelectedItem is string selected)
                selectedCharacters.Remove(selected);
        };
        clearButton.Click += (_, _) => selectedCharacters.Clear();
        saveButton.Click += (_, _) => window.DialogResult = true;
        cancelButton.Click += (_, _) => window.DialogResult = false;

        if (window.ShowDialog() != true)
            return;

        charactersBox.Text = string.Join(", ", selectedCharacters);
        PersistEditedCharactersToDatabase(isPlayerOne, charactersBox.Text);
        UpdateCharacterButtonLabels();
    }

    private void PersistEditedCharactersToDatabase(bool isPlayerOne, string charactersText)
    {
        var name = (isPlayerOne ? P1Name.Text : P2Name.Text)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return;

        var existing = _playerDatabase.Players.FirstOrDefault(player =>
            string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return;

        existing.Characters = charactersText;
        PlayerDatabaseStore.Save(_playerDatabasePath, _playerDatabase);
        RefreshPlayerProfiles();
    }

    private void UpdateCharacterButtonLabels()
    {
        P1CharactersButton.Content = BuildCharacterButtonText(P1Characters.Text);
        P2CharactersButton.Content = BuildCharacterButtonText(P2Characters.Text);
    }

    private static string BuildCharacterButtonText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Characters";

        var count = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(token => !string.IsNullOrWhiteSpace(token));
        return count <= 0 ? "Characters" : $"Characters ({count})";
    }

    private static CountryInfo CreateUnknownCountry()
        => new()
        {
            Id = CountryId.Unknown,
            Acronym = "???",
            DisplayName = "Unknown",
            FlagPath = string.Empty
        };

    private static bool IsUnknownCountry(CountryInfo? country)
        => country is null
           || string.IsNullOrWhiteSpace(country.Acronym)
           || country.Id == CountryId.Unknown;

    private CountryInfo ResolveCountryInfo(string? codeOrId)
    {
        var key = codeOrId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            return _countries.FirstOrDefault(IsUnknownCountry) ?? CreateUnknownCountry();

        var byCode = _countries.FirstOrDefault(country =>
            string.Equals(country.Acronym, key, StringComparison.OrdinalIgnoreCase));
        if (byCode is not null)
            return byCode;

        if (Enum.TryParse<CountryId>(key, true, out var parsed))
        {
            var byId = _countries.FirstOrDefault(country => country.Id == parsed);
            if (byId is not null)
                return byId;
        }

        return _countries.FirstOrDefault(IsUnknownCountry) ?? CreateUnknownCountry();
    }

    private CountryInfo? ResolveCountrySelection(PlayerInfo player)
    {
        if (!string.IsNullOrWhiteSpace(player.CustomCountryCode))
        {
            var byCode = _countries.FirstOrDefault(country =>
                string.Equals(country.Acronym, player.CustomCountryCode, StringComparison.OrdinalIgnoreCase));
            if (byCode is not null)
                return byCode;
        }

        return _countries.FirstOrDefault(country => country.Id == player.Country);
    }

    private List<CountryInfo> GetConfiguredCountries()
    {
        if (_settings.Countries is { Count: > 0 })
        {
            var custom = _settings.Countries
                .Select(country =>
                {
                    var code = (country.Code ?? string.Empty).Trim();
                    var id = Enum.TryParse<CountryId>(code, true, out var parsed)
                        ? parsed
                        : CountryId.Unknown;
                    return new CountryInfo
                    {
                        Id = id,
                        Acronym = code,
                        DisplayName = (country.Name ?? string.Empty).Trim(),
                        FlagPath = (country.FlagPath ?? string.Empty).Trim()
                    };
                })
                .Where(country => !string.IsNullOrWhiteSpace(country.Acronym) || !string.IsNullOrWhiteSpace(country.DisplayName))
                .GroupBy(country => country.Acronym, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(country => country.Acronym, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!custom.Any(IsUnknownCountry))
                custom.Insert(0, CreateUnknownCountry());

            return custom;
        }

        var configCountries = ConfigScript.Build().Metadata.Countries.Values
            .OrderBy(country => country.Acronym, StringComparer.OrdinalIgnoreCase)
            .Select(country => country with
            {
                DisplayName = string.IsNullOrWhiteSpace(country.DisplayName) ? country.Acronym : country.DisplayName
            })
            .ToList();
        if (!configCountries.Any(IsUnknownCountry))
            configCountries.Insert(0, CreateUnknownCountry());

        return configCountries;
    }

    private void ReloadCountriesFromSettings()
    {
        var p1Code = (P1Country.SelectedItem as CountryInfo)?.Acronym;
        var p2Code = (P2Country.SelectedItem as CountryInfo)?.Acronym;

        _countries.Clear();
        foreach (var country in GetConfiguredCountries())
            _countries.Add(country);

        P1Country.SelectedItem = ResolveCountryInfo(p1Code);
        P2Country.SelectedItem = ResolveCountryInfo(p2Code);
    }

    private sealed class QueueRowViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isLoaded;

        public long MatchId { get; init; }
        public string MetaText { get; init; } = string.Empty;
        public string ChallongeStatusText { get; init; } = string.Empty;
        public Brush ChallongeStatusBrush { get; init; } = Brushes.Silver;
        public string MainText { get; init; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set
            {
                if (_isLoaded == value)
                    return;

                _isLoaded = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private AppConfig BuildRuntimeConfig()
    {
        var config = ConfigScript.Build();
        var obsUrl = string.IsNullOrWhiteSpace(_settings.ObsUrl)
            ? (Environment.GetEnvironmentVariable("OBS_WS_URL") ?? string.Empty)
            : _settings.ObsUrl.Trim();
        var obsPassword = _settings.ObsPassword
            ?? Environment.GetEnvironmentVariable("OBS_WS_PASSWORD")
            ?? string.Empty;
        var configuredScenes = GetConfiguredSceneButtons();
        var scenes = config.Scenes with
        {
            InMatch = configuredScenes.Count > 0 ? configuredScenes[0].SceneName : (_settings.SceneInMatch is null ? config.Scenes.InMatch : _settings.SceneInMatch.Trim()),
            Desk = configuredScenes.Count > 1 ? configuredScenes[1].SceneName : (_settings.SceneDesk is null ? config.Scenes.Desk : _settings.SceneDesk.Trim()),
            Break = configuredScenes.Count > 2 ? configuredScenes[2].SceneName : (_settings.SceneBreak is null ? config.Scenes.Break : _settings.SceneBreak.Trim()),
            Results = configuredScenes.Count > 3 ? configuredScenes[3].SceneName : (_settings.SceneResults is null ? config.Scenes.Results : _settings.SceneResults.Trim())
        };
        var overlay = config.Overlay with
        {
            P1Name = _settings.MapP1Name is null ? config.Overlay.P1Name : _settings.MapP1Name.Trim(),
            P1Team = _settings.MapP1Team is null ? config.Overlay.P1Team : _settings.MapP1Team.Trim(),
            P1Country = _settings.MapP1Country is null ? config.Overlay.P1Country : _settings.MapP1Country.Trim(),
            P1Flag = _settings.MapP1Flag is null ? config.Overlay.P1Flag : _settings.MapP1Flag.Trim(),
            P1Score = _settings.MapP1Score is null ? config.Overlay.P1Score : _settings.MapP1Score.Trim(),
            P2Name = _settings.MapP2Name is null ? config.Overlay.P2Name : _settings.MapP2Name.Trim(),
            P2Team = _settings.MapP2Team is null ? config.Overlay.P2Team : _settings.MapP2Team.Trim(),
            P2Country = _settings.MapP2Country is null ? config.Overlay.P2Country : _settings.MapP2Country.Trim(),
            P2Flag = _settings.MapP2Flag is null ? config.Overlay.P2Flag : _settings.MapP2Flag.Trim(),
            P2Score = _settings.MapP2Score is null ? config.Overlay.P2Score : _settings.MapP2Score.Trim(),
            RoundLabel = _settings.MapRoundLabel is null ? config.Overlay.RoundLabel : _settings.MapRoundLabel.Trim(),
            SetType = _settings.MapSetType is null ? config.Overlay.SetType : _settings.MapSetType.Trim()
        };
        var metadataCountries = new Dictionary<CountryId, CountryInfo>(config.Metadata.Countries);
        foreach (var country in GetConfiguredCountries())
        {
            if (Enum.TryParse<CountryId>(country.Acronym, true, out var resolvedId) && resolvedId != CountryId.Unknown)
                metadataCountries[resolvedId] = country with { Id = resolvedId };
        }

        if (!metadataCountries.ContainsKey(CountryId.Unknown))
            metadataCountries[CountryId.Unknown] = CreateUnknownCountry();
        var metadata = new OverlayMetadata
        {
            Countries = metadataCountries,
            Characters = config.Metadata.Characters
        };

        return config with
        {
            Obs = config.Obs with
            {
                Url = obsUrl,
                Password = obsPassword
            },
            Scenes = scenes,
            Overlay = overlay,
            Metadata = metadata
        };
    }

    private List<SceneButtonSetting> GetConfiguredSceneButtons()
    {
        if (_settings.SceneButtons is { Count: > 0 })
        {
            var saved = _settings.SceneButtons
                .Where(scene => !string.IsNullOrWhiteSpace(scene.DisplayName) || !string.IsNullOrWhiteSpace(scene.SceneName))
                .Select(scene => new SceneButtonSetting
                {
                    DisplayName = (scene.DisplayName ?? string.Empty).Trim(),
                    SceneName = (scene.SceneName ?? string.Empty).Trim()
                })
                .Where(scene => !string.IsNullOrWhiteSpace(scene.SceneName))
                .ToList();
            if (saved.Count > 0)
                return saved;
        }

        var config = ConfigScript.Build();
        return new List<SceneButtonSetting>
        {
            new() { DisplayName = "In-Match", SceneName = config.Scenes.InMatch },
            new() { DisplayName = "Desk", SceneName = config.Scenes.Desk },
            new() { DisplayName = "Break", SceneName = config.Scenes.Break },
            new() { DisplayName = "Results", SceneName = config.Scenes.Results }
        };
    }

    private void RenderSceneButtons()
    {
        SceneButtonsPanel.Children.Clear();
        var labelText = new TextBlock
        {
            Text = "Scenes:",
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#9CA3AF")!,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        SceneButtonsPanel.Children.Add(new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4),
            Padding = new Thickness(10, 6, 10, 6),
            Child = labelText
        });

        foreach (var scene in GetConfiguredSceneButtons())
        {
            var button = new Button
            {
                Content = string.IsNullOrWhiteSpace(scene.DisplayName) ? scene.SceneName : scene.DisplayName,
                ToolTip = scene.SceneName
            };
            button.Click += async (_, _) => await SwitchSceneIfMappedAsync(scene.SceneName);
            SceneButtonsPanel.Children.Add(button);
        }
    }

    private async Task SwitchSceneIfMappedAsync(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        ShowPendingStatus("Waiting for OBS to respond...");
        try
        {
            await _host.SwitchSceneAsync(sceneName, CancellationToken.None);
            ShowActionToast($"Switched scene to '{sceneName}'.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowActionToast($"Scene switch failed: {ex.Message}", ToastKind.Error);
        }
    }

    private bool HasObsCredentials()
        => !string.IsNullOrWhiteSpace(_host.Config.Obs.Url);

    private bool HasChallongeCredentials()
        => !string.IsNullOrWhiteSpace(ChallongeTournamentBox.Text?.Trim())
           && !string.IsNullOrWhiteSpace(ChallongeApiKeyBox.Text?.Trim());

    private async Task RefreshObsStatusAsync()
    {
        if (!HasObsCredentials())
        {
            SetObsStatus("No Credentials", "#FACC15");
            return;
        }

        ShowPendingStatus("Waiting for OBS to respond...");
        try
        {
            var connected = await _host.ConnectAsync(CancellationToken.None);
            SetObsStatus(connected ? "Connected" : "Disconnected", connected ? "#22C55E" : "#EF4444");
            ShowActionToast(connected ? "Connected to OBS." : "Could not connect to OBS.", connected ? ToastKind.Success : ToastKind.Error);
        }
        catch (Exception ex)
        {
            SetObsStatus("Disconnected", "#EF4444");
            ShowActionToast($"OBS connection failed: {ex.Message}", ToastKind.Error);
        }
    }

    private void SyncScoreDisplaysFromState()
    {
        var current = _host.State.CurrentMatch;
        P1ScoreValue.Text = current.Player1.Score.ToString();
        P2ScoreValue.Text = current.Player2.Score.ToString();
    }

    private void SetObsStatus(string text, string colorHex)
    {
        ObsStatusValue.Text = text;
        ObsStatusValue.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
    }

    private void SetChallongeStatus(string text, string colorHex)
    {
        ChallongeStatusValue.Text = text;
        ChallongeStatusValue.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
    }

    private bool ShowObsCredentialsDialog()
    {
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "OBS WebSocket URL", Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 4) });
        var urlBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(_settings.ObsUrl)
                ? _host.Config.Obs.Url
                : _settings.ObsUrl
        };
        panel.Children.Add(urlBox);
        panel.Children.Add(new TextBlock { Text = "OBS Password", Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 4) });
        var passwordBox = new PasswordBox
        {
            Margin = new Thickness(4),
            Padding = new Thickness(6),
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#0B0F12")!,
            Foreground = Brushes.White,
            BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#334155")!
        };
        passwordBox.Password = _settings.ObsPassword ?? string.Empty;
        panel.Children.Add(passwordBox);

        var connectButton = new Button { Content = "Connect to OBS", Margin = new Thickness(4) };
        var saveButton = new Button { Content = "Save", Width = 88, Margin = new Thickness(4) };
        var cancelButton = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(connectButton);
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Owner = this,
            Title = "OBS Credentials",
            Content = panel,
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#101315")!,
            Foreground = Brushes.White
        };

        var connectRequested = false;
        connectButton.Click += (_, _) =>
        {
            connectRequested = true;
            window.DialogResult = true;
        };
        saveButton.Click += (_, _) => window.DialogResult = true;
        cancelButton.Click += (_, _) => window.DialogResult = false;

        if (window.ShowDialog() != true)
            return false;

        _settings.ObsUrl = urlBox.Text.Trim();
        _settings.ObsPassword = passwordBox.Password;
        PersistSettings();
        RebuildHostFromSettings();

        if (connectRequested)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await RefreshObsStatusAsync();
            });
        }

        return true;
    }

    private bool ShowObsMappingsDialog()
    {
        var config = ConfigScript.Build();
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Map app fields to your OBS scene/source names. Leave any field blank to disable it.",
            Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 430 };
        var root = new StackPanel();
        scroll.Content = root;
        panel.Children.Add(scroll);

        root.Children.Add(new TextBlock
        {
            Text = "Scenes (display label + OBS scene name)",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var sceneRowsPanel = new StackPanel();
        root.Children.Add(sceneRowsPanel);

        var sceneList = GetConfiguredSceneButtons()
            .Select(scene => new SceneButtonSetting { DisplayName = scene.DisplayName, SceneName = scene.SceneName })
            .ToList();

        void RenderSceneRows()
        {
            sceneRowsPanel.Children.Clear();
            for (var i = 0; i < sceneList.Count; i++)
            {
                var index = i;
                var rowPanel = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var displayBox = new TextBox
                {
                    Text = sceneList[index].DisplayName,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                displayBox.TextChanged += (_, _) => sceneList[index].DisplayName = displayBox.Text;

                var sceneBox = new TextBox
                {
                    Text = sceneList[index].SceneName,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                sceneBox.TextChanged += (_, _) => sceneList[index].SceneName = sceneBox.Text;

                var removeButton = new Button
                {
                    Content = "Remove",
                    Padding = new Thickness(8, 4, 8, 4)
                };
                removeButton.Click += (_, _) =>
                {
                    sceneList.RemoveAt(index);
                    RenderSceneRows();
                };

                Grid.SetColumn(displayBox, 0);
                Grid.SetColumn(sceneBox, 1);
                Grid.SetColumn(removeButton, 2);
                rowPanel.Children.Add(displayBox);
                rowPanel.Children.Add(sceneBox);
                rowPanel.Children.Add(removeButton);
                sceneRowsPanel.Children.Add(rowPanel);
            }
        }

        RenderSceneRows();
        var addSceneButton = new Button { Content = "Add Scene", Margin = new Thickness(0, 2, 0, 8), Width = 110 };
        addSceneButton.Click += (_, _) =>
        {
            sceneList.Add(new SceneButtonSetting { DisplayName = $"Scene {sceneList.Count + 1}", SceneName = string.Empty });
            RenderSceneRows();
        };
        root.Children.Add(addSceneButton);

        root.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 8) });
        root.Children.Add(new TextBlock
        {
            Text = "Field Mappings (leave blank to disable)",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(grid);

        var boxes = new Dictionary<string, TextBox>();
        var row = 0;
        void AddField(string key, string label, string value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            };
            var box = new TextBox { Text = value, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 0);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            grid.Children.Add(text);
            grid.Children.Add(box);
            boxes[key] = box;
            row++;
        }

        string FieldValue(string? overrideValue, string defaultValue) => overrideValue ?? defaultValue;

        AddField("p1_name", "P1 Name Source", FieldValue(_settings.MapP1Name, config.Overlay.P1Name));
        AddField("p1_team", "P1 Team Source", FieldValue(_settings.MapP1Team, config.Overlay.P1Team));
        AddField("p1_country", "P1 Country Source", FieldValue(_settings.MapP1Country, config.Overlay.P1Country));
        AddField("p1_flag", "P1 Flag Source", FieldValue(_settings.MapP1Flag, config.Overlay.P1Flag));
        AddField("p1_score", "P1 Score Source", FieldValue(_settings.MapP1Score, config.Overlay.P1Score));
        AddField("p2_name", "P2 Name Source", FieldValue(_settings.MapP2Name, config.Overlay.P2Name));
        AddField("p2_team", "P2 Team Source", FieldValue(_settings.MapP2Team, config.Overlay.P2Team));
        AddField("p2_country", "P2 Country Source", FieldValue(_settings.MapP2Country, config.Overlay.P2Country));
        AddField("p2_flag", "P2 Flag Source", FieldValue(_settings.MapP2Flag, config.Overlay.P2Flag));
        AddField("p2_score", "P2 Score Source", FieldValue(_settings.MapP2Score, config.Overlay.P2Score));
        AddField("round_label", "Round Label Source", FieldValue(_settings.MapRoundLabel, config.Overlay.RoundLabel));
        AddField("set_type", "Set Type Source", FieldValue(_settings.MapSetType, config.Overlay.SetType));

        var saveButton = new Button { Content = "Save", Width = 88, Margin = new Thickness(4) };
        var cancelButton = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(4) };
        var resetButton = new Button { Content = "Use Defaults", Margin = new Thickness(4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(resetButton);
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Owner = this,
            Title = "OBS Layout Mapping",
            Content = panel,
            Width = 620,
            Height = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#101315")!,
            Foreground = Brushes.White
        };

        resetButton.Click += (_, _) =>
        {
            sceneList.Clear();
            sceneList.Add(new SceneButtonSetting { DisplayName = "In-Match", SceneName = config.Scenes.InMatch });
            sceneList.Add(new SceneButtonSetting { DisplayName = "Desk", SceneName = config.Scenes.Desk });
            sceneList.Add(new SceneButtonSetting { DisplayName = "Break", SceneName = config.Scenes.Break });
            sceneList.Add(new SceneButtonSetting { DisplayName = "Results", SceneName = config.Scenes.Results });
            RenderSceneRows();
            boxes["p1_name"].Text = config.Overlay.P1Name;
            boxes["p1_team"].Text = config.Overlay.P1Team;
            boxes["p1_country"].Text = config.Overlay.P1Country;
            boxes["p1_flag"].Text = config.Overlay.P1Flag;
            boxes["p1_score"].Text = config.Overlay.P1Score;
            boxes["p2_name"].Text = config.Overlay.P2Name;
            boxes["p2_team"].Text = config.Overlay.P2Team;
            boxes["p2_country"].Text = config.Overlay.P2Country;
            boxes["p2_flag"].Text = config.Overlay.P2Flag;
            boxes["p2_score"].Text = config.Overlay.P2Score;
            boxes["round_label"].Text = config.Overlay.RoundLabel;
            boxes["set_type"].Text = config.Overlay.SetType;
        };
        saveButton.Click += (_, _) => window.DialogResult = true;
        cancelButton.Click += (_, _) => window.DialogResult = false;

        if (window.ShowDialog() != true)
            return false;

        _settings.SceneButtons = sceneList
            .Where(scene => !string.IsNullOrWhiteSpace(scene.DisplayName) || !string.IsNullOrWhiteSpace(scene.SceneName))
            .Select(scene => new SceneButtonSetting
            {
                DisplayName = scene.DisplayName.Trim(),
                SceneName = scene.SceneName.Trim()
            })
            .ToList();
        _settings.MapP1Name = boxes["p1_name"].Text.Trim();
        _settings.MapP1Team = boxes["p1_team"].Text.Trim();
        _settings.MapP1Country = boxes["p1_country"].Text.Trim();
        _settings.MapP1Flag = boxes["p1_flag"].Text.Trim();
        _settings.MapP1Score = boxes["p1_score"].Text.Trim();
        _settings.MapP2Name = boxes["p2_name"].Text.Trim();
        _settings.MapP2Team = boxes["p2_team"].Text.Trim();
        _settings.MapP2Country = boxes["p2_country"].Text.Trim();
        _settings.MapP2Flag = boxes["p2_flag"].Text.Trim();
        _settings.MapP2Score = boxes["p2_score"].Text.Trim();
        _settings.MapRoundLabel = boxes["round_label"].Text.Trim();
        _settings.MapSetType = boxes["set_type"].Text.Trim();
        PersistSettings();
        RebuildHostFromSettings();
        RenderSceneButtons();
        return true;
    }

    private bool ShowCountriesDialog()
    {
        var seedCountries = GetConfiguredCountries()
            .Select(country => new CountrySetting
            {
                Code = country.Acronym,
                Name = country.DisplayName,
                FlagPath = country.FlagPath
            })
            .ToList();

        var window = new CountryManagerWindow(seedCountries)
        {
            Owner = this
        };

        if (window.ShowDialog() != true || window.ResultCountries is null)
            return false;

        _settings.Countries = window.ResultCountries.ToList();

        if (_settings.Countries.All(country => !string.IsNullOrWhiteSpace(country.Code)))
            _settings.Countries.Insert(0, new CountrySetting { Code = "???", Name = "Unknown", FlagPath = string.Empty });

        PersistSettings();
        ShowActionToast("Countries updated.", ToastKind.Success);
        return true;
    }

    private void RebuildHostFromSettings()
    {
        var currentMatch = _host.State.CurrentMatch;
        _host = new AutomationHost(BuildRuntimeConfig(), _logger);
        _host.State.SetCurrentMatch(currentMatch);
        RenderSceneButtons();
        SyncScoreDisplaysFromState();
    }

    private bool ShowChallongeCredentialsDialog()
    {
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "Tournament Slug", Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 4) });
        var tournamentBox = new TextBox { Text = ChallongeTournamentBox.Text };
        panel.Children.Add(tournamentBox);
        panel.Children.Add(new TextBlock { Text = "API Key", Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 4) });
        var apiKeyBox = new PasswordBox
        {
            Margin = new Thickness(4),
            Padding = new Thickness(6),
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#0B0F12")!,
            Foreground = Brushes.White,
            BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#334155")!
        };
        apiKeyBox.Password = ChallongeApiKeyBox.Text;
        panel.Children.Add(apiKeyBox);

        var saveButton = new Button { Content = "Save", Width = 88, Margin = new Thickness(4) };
        var cancelButton = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Owner = this,
            Title = "Challonge Credentials",
            Content = panel,
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#101315")!,
            Foreground = Brushes.White
        };

        saveButton.Click += (_, _) => window.DialogResult = true;
        cancelButton.Click += (_, _) => window.DialogResult = false;

        if (window.ShowDialog() != true)
            return false;

        ChallongeTournamentBox.Text = tournamentBox.Text.Trim();
        ChallongeApiKeyBox.Text = apiKeyBox.Password.Trim();
        _settings.ChallongeTournament = ChallongeTournamentBox.Text;
        _settings.ChallongeApiKey = ChallongeApiKeyBox.Text;
        PersistSettings();

        if (!HasChallongeCredentials())
            SetChallongeStatus("No Credentials", "#FACC15");

        return true;
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

    private enum ToastKind
    {
        Success,
        Error,
        Warning,
        Info
    }

    private void ShowPendingStatus(string message)
    {
        ShowActionToast(message, ToastKind.Info, autoHide: false);
    }

    private void ShowActionToast(string message, ToastKind kind, bool autoHide = true)
    {
        ActionToastText.Text = message;
        ActionToast.Background = kind switch
        {
            ToastKind.Success => (SolidColorBrush)new BrushConverter().ConvertFromString("#166534")!,
            ToastKind.Error => (SolidColorBrush)new BrushConverter().ConvertFromString("#991B1B")!,
            ToastKind.Warning => (SolidColorBrush)new BrushConverter().ConvertFromString("#92400E")!,
            ToastKind.Info => (SolidColorBrush)new BrushConverter().ConvertFromString("#1D4ED8")!,
            _ => (SolidColorBrush)new BrushConverter().ConvertFromString("#1F2937")!
        };

        ActionToast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        if (autoHide)
            _toastTimer.Start();
    }

    private void HideActionToast()
    {
        _toastTimer.Stop();
        ActionToast.Visibility = Visibility.Collapsed;
    }

    private void PersistSettings()
    {
        UserSettingsStore.Save(_settingsPath, _settings);
        PersistSettingsBootstrap();
    }

    private void PersistSettingsBootstrap()
    {
        var bootstrapPath = GetBootstrapSettingsPath();
        var bootstrap = UserSettingsStore.Load(bootstrapPath);
        bootstrap.SettingsFolderPath = Path.GetDirectoryName(_settingsPath);
        UserSettingsStore.Save(bootstrapPath, bootstrap);
    }

    private static string GetSettingsPath()
    {
        var bootstrapPath = GetBootstrapSettingsPath();
        var bootstrapSettings = UserSettingsStore.Load(bootstrapPath);
        if (!string.IsNullOrWhiteSpace(bootstrapSettings.SettingsFolderPath))
            return Path.Combine(bootstrapSettings.SettingsFolderPath, "ui-settings.json");

        return bootstrapPath;
    }

    private static string GetBootstrapSettingsPath()
    {
        return Path.Combine(GetApplicationDirectory(), "ui-settings.json");
    }

    private static string GetDefaultPlayerDatabasePath(string settingsPath)
    {
        var root = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrWhiteSpace(root))
            root = GetApplicationDirectory();

        return Path.Combine(root, "players.json");
    }

    private static string GetApplicationDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return Directory.GetCurrentDirectory();

        return baseDirectory;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
