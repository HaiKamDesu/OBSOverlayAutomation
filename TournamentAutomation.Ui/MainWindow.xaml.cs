using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ChallongeInterface;
using ChallongeInterface.Models;
using ChallongeProfileScraper.Models;
using ChallongeProfileScraper.Services;
using Microsoft.Win32;
using TournamentAutomation.Application;
using TournamentAutomation.Domain;
using TournamentAutomation.Presentation;
using System.Windows;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ObsInterface;
using TournamentAutomation.Configuration;

namespace TournamentAutomation.Ui;

public partial class MainWindow : Window
{
    private const string DefaultCharacterSplashartFolder = @"D:\User\Videos\Edited videos\BBCF Clips\1- USABLE ASSETS\Splashart";
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
    private readonly Dictionary<string, ChallongeProfileStats?> _challongeProfileStatsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ChallongeProfileScraperService _challongeProfileScraper;
    private int _challongeProfileDisplayVersion;
    private bool _playerDatabaseDirty;
    private string? _toastErrorDetails;
    private string _toastErrorTitle = "Error Details";

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
        _challongeProfileScraper = new ChallongeProfileScraperService(new HttpClient());
        _host = new AutomationHost(BuildRuntimeConfig(), _logger);

        foreach (var entry in GetConfiguredCountries().OrderBy(x => x.Acronym, StringComparer.OrdinalIgnoreCase))
            _countries.Add(entry);

        P1Country.ItemsSource = _countries;
        P2Country.ItemsSource = _countries;

        P1Country.SelectedItem = _countries.FirstOrDefault(IsUnknownCountry) ?? _countries.FirstOrDefault();
        P2Country.SelectedItem = _countries.FirstOrDefault(IsUnknownCountry) ?? _countries.FirstOrDefault();

        LoadPlayerDatabase();
        LoadChallongeStatsCacheFromPlayerDatabase();

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
        MatchRoundLabelBox.Text = _host.State.CurrentMatch.RoundLabel ?? string.Empty;
        MatchSetTypeBox.Text = ToSetTypeText(_host.State.CurrentMatch.Format);
        UpdateDisplayedProfileStats(null);
        MoveNextOnCommitMenuItem.IsChecked = _settings.MoveToNextOpenMatchOnCommitToChallonge;
        _toastTimer.Tick += (_, _) => HideActionToast();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await EnsureObsConnectedOnStartupAsync();
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
        ApplyMatchMetadataFromFieldsToCurrentMatch();

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
            ShowErrorToast($"OBS update failed: {ex.Message}", ex);
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
            ShowErrorToast($"Failed to apply Player 1: {ex.Message}", ex);
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
            ShowErrorToast($"Failed to apply Player 2: {ex.Message}", ex);
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

    private void ManageCharacterCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowCharacterCatalogDialog())
            return;

        RebuildHostFromSettings();
    }

    private void RoundNamingRulesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowRoundNamingRulesDialog())
            return;

        ApplyRoundNamingRules(_challongeQueueEntries);
        UpdateQueueListVisuals();
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
        var dialog = new PlayerSelectWindow(_playerProfiles, _playerDatabase, _playerDatabasePath, _countries, GetConfiguredCharacterCatalog())
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

            var orderedMatches = BuildDisplayOrderedMatches(matches)
                .ToList();

            _challongeQueueEntries.Clear();
            foreach (var orderedMatch in orderedMatches)
                _challongeQueueEntries.Add(ToQueueEntry(orderedMatch.Match, participantsById, orderedMatch.DisplayNumber));
            ApplyRoundNamingRules(_challongeQueueEntries);

            foreach (var entry in _challongeQueueEntries)
                ResolveQueueEntry(entry, allowPrompt: false, persistEnrichment: false);
            SavePlayerDatabaseIfDirty();

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
            ShowErrorToast($"Challonge refresh failed: {ex.Message}", ex);
            if (showErrorDialog)
                MessageBox.Show(ex.Message, "Challonge Queue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task VerifyObsLayoutAsync()
    {
        var connected = await RefreshObsStatusAsync(showToast: true);
        if (!connected)
            return;

        var obs = _host.GetContext().Obs;
        var sceneNames = await obs.GetSceneNamesAsync(CancellationToken.None);
        var expectedScenes = GetConfiguredSceneButtons()
            .Select(scene => scene.SceneName?.Trim() ?? string.Empty)
            .Where(scene => !string.IsNullOrWhiteSpace(scene))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var missingScenes = expectedScenes
            .Where(scene => !sceneNames.Contains(scene, StringComparer.Ordinal))
            .ToList();

        var overlay = _host.Config.Overlay;
        var inputMappings = new[]
        {
            overlay.P1Name, overlay.P1Team, overlay.P1Country, overlay.P1Flag, overlay.P1Score,
            overlay.P1ChallongeProfileImage, overlay.P1ChallongeBannerImage, overlay.P1ChallongeStatsText, overlay.P1CharacterSprite,
            overlay.P2Name, overlay.P2Team, overlay.P2Country, overlay.P2Flag, overlay.P2Score,
            overlay.P2ChallongeProfileImage, overlay.P2ChallongeBannerImage, overlay.P2ChallongeStatsText, overlay.P2CharacterSprite,
            overlay.RoundLabel, overlay.SetType
        }
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.Ordinal)
        .ToList();

        var missingInputs = new List<string>();
        foreach (var input in inputMappings)
        {
            if (!await obs.GetInputExistsAsync(input, CancellationToken.None))
                missingInputs.Add(input);
        }

        if (missingScenes.Count == 0 && missingInputs.Count == 0)
        {
            ShowActionToast("OBS layout verified. All configured scenes and sources exist.", ToastKind.Success);
            return;
        }

        var lines = new List<string>();
        if (missingScenes.Count > 0)
        {
            lines.Add("Missing scenes:");
            lines.AddRange(missingScenes.Select(scene => $"- {scene}"));
        }

        if (missingInputs.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add(string.Empty);
            lines.Add("Missing inputs/sources:");
            lines.AddRange(missingInputs.Select(input => $"- {input}"));
        }

        ShowActionToast("OBS layout verification found missing items.", ToastKind.Warning);
        MessageBox.Show(string.Join(Environment.NewLine, lines), "Verify OBS Layout", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        match = RehydrateMatchFromPlayerDatabase(entry, match);
        entry.ResolvedMatch = match;

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
            UpdateDisplayedProfileStats(null);
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
            UpdateDisplayedProfileStats(null);
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
        var metaText = $"{entry.AppRoundLabel}";

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

    private static ChallongeQueueEntry ToQueueEntry(Match match, IReadOnlyDictionary<long, Participant> participantsById, string displayNumber)
    {
        var p1Participant = ResolveParticipant(participantsById, match.Player1Id);
        var p2Participant = ResolveParticipant(participantsById, match.Player2Id);
        var p1Name = ResolveName(match.Player1Id, p1Participant);
        var p2Name = ResolveName(match.Player2Id, p2Participant);
        var round = match.Round ?? 0;
        var side = round < 0 ? "Losers" : "Winners";
        var roundAbs = Math.Abs(round);
        if (roundAbs == 0)
            roundAbs = 1;

        return new ChallongeQueueEntry
        {
            MatchId = match.Id,
            MatchNumber = displayNumber,
            RawRound = match.Round,
            RoundSide = side,
            RoundAbsolute = roundAbs,
            SuggestedPlayOrder = match.SuggestedPlayOrder,
            ChallongePlayer1Id = match.Player1Id,
            ChallongePlayer2Id = match.Player2Id,
            ChallongeState = match.State ?? string.Empty,
            ChallongeWinnerId = match.WinnerId,
            ChallongeScoresCsv = match.ScoresCsv,
            IsReportedToChallonge = IsReportedByChallonge(match),
            RawPlayer1Name = p1Name,
            RawPlayer2Name = p2Name,
            RawPlayer1ApiChallongeUsername = ResolveParticipantChallongeUsername(p1Participant),
            RawPlayer2ApiChallongeUsername = ResolveParticipantChallongeUsername(p2Participant),
            DefaultFt = 2,
            AppRoundLabel = string.Empty,
            ObsRoundLabel = string.Empty
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

    private static List<OrderedDisplayMatch> BuildDisplayOrderedMatches(IReadOnlyList<Match> matches)
    {
        var ordered = matches
            .Select(match => new
            {
                Match = match,
                ExpectedNumber = TryGetExpectedDisplayNumber(match)
            })
            .OrderBy(entry => entry.ExpectedNumber.HasValue ? 0 : 1)
            .ThenBy(entry => entry.ExpectedNumber ?? int.MaxValue)
            .ThenBy(entry => entry.Match.SuggestedPlayOrder ?? int.MaxValue)
            .ThenBy(entry => entry.Match.Round ?? int.MaxValue)
            .ThenBy(entry => entry.Match.Id)
            .ToList();

        var results = new List<OrderedDisplayMatch>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var sequentialNumber = i + 1;
            results.Add(new OrderedDisplayMatch
            {
                Match = ordered[i].Match,
                DisplayNumber = sequentialNumber.ToString()
            });
        }

        return results;
    }

    private static int? TryGetExpectedDisplayNumber(Match match)
    {
        if (match.SuggestedPlayOrder is > 0)
            return match.SuggestedPlayOrder.Value;

        if (!string.IsNullOrWhiteSpace(match.Identifier) && int.TryParse(match.Identifier.Trim(), out var parsedIdentifier) && parsedIdentifier > 0)
            return parsedIdentifier;

        return null;
    }

    private static Participant? ResolveParticipant(IReadOnlyDictionary<long, Participant> participants, long? participantId)
    {
        if (participantId is null)
            return null;

        participants.TryGetValue(participantId.Value, out var participant);
        return participant;
    }

    private static string ResolveName(long? participantId, Participant? participant)
    {
        if (participantId is null)
            return "TBD";

        if (participant is null)
            return participantId.Value.ToString();

        return participant.DisplayName
            ?? participant.Name
            ?? participant.Username
            ?? participant.Id.ToString();
    }

    private static string? ResolveParticipantChallongeUsername(Participant? participant)
    {
        var fromProfile = NormalizeChallongeUsername(participant?.ChallongeUsername);
        if (!string.IsNullOrWhiteSpace(fromProfile))
            return fromProfile;

        return NormalizeChallongeUsername(participant?.Username);
    }

    private static bool IsSameMatch(MatchState left, MatchState right)
    {
        return string.Equals(left.Player1.Name, right.Player1.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Player2.Name, right.Player2.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.RoundLabel, right.RoundLabel, StringComparison.OrdinalIgnoreCase);
    }

    private MatchState? ResolveQueueEntry(ChallongeQueueEntry entry, bool allowPrompt, bool persistEnrichment = true)
    {
        if (entry.ResolvedMatch is not null)
            return entry.ResolvedMatch;

        var p1 = FindProfileByNameOrAlias(entry.RawPlayer1Name);
        var p2 = FindProfileByNameOrAlias(entry.RawPlayer2Name);

        if (allowPrompt && p1 is null && !IsPlaceholderName(entry.RawPlayer1Name))
            p1 = PromptForPlayerResolution(entry.RawPlayer1Name, "P1", entry.RawPlayer1ApiChallongeUsername);

        if (allowPrompt && p2 is null && !IsPlaceholderName(entry.RawPlayer2Name))
            p2 = PromptForPlayerResolution(entry.RawPlayer2Name, "P2", entry.RawPlayer2ApiChallongeUsername);

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

        _ = EnrichProfileFromParticipant(p1, entry.RawPlayer1Name, entry.RawPlayer1ApiChallongeUsername);
        _ = EnrichProfileFromParticipant(p2, entry.RawPlayer2Name, entry.RawPlayer2ApiChallongeUsername);
        if (persistEnrichment)
            SavePlayerDatabaseIfDirty();

        entry.ResolvedPlayer1ChallongeUsername = ResolveParticipantProfileUsername(entry.RawPlayer1ApiChallongeUsername, p1);
        entry.ResolvedPlayer2ChallongeUsername = ResolveParticipantProfileUsername(entry.RawPlayer2ApiChallongeUsername, p2);
        var cachedP1 = TryGetCachedProfileStats(entry.ResolvedPlayer1ChallongeUsername) ?? ToChallongeProfileStats(p1?.ChallongeStats);
        var cachedP2 = TryGetCachedProfileStats(entry.ResolvedPlayer2ChallongeUsername) ?? ToChallongeProfileStats(p2?.ChallongeStats);
        entry.Player1ProfileStats = cachedP1;
        entry.Player2ProfileStats = cachedP2;
        if (cachedP1 is not null)
            player1 = player1 with { ChallongeProfile = ToChallongeProfileInfo(cachedP1) };
        if (cachedP2 is not null)
            player2 = player2 with { ChallongeProfile = ToChallongeProfileInfo(cachedP2) };

        entry.ResolvedMatch = new MatchState
        {
            RoundLabel = entry.ObsRoundLabel,
            Format = ToMatchFormat(entry.DefaultFt),
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
            ShowErrorToast($"Commit failed: {ex.Message}", ex);
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
            ShowErrorToast($"Set state failed: {ex.Message}", ex);
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
            ShowErrorToast($"DQ submission failed: {ex.Message}", ex);
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

    private PlayerProfile? FindProfileByChallongeUsername(string? challongeUsername)
    {
        var normalized = NormalizeChallongeUsername(challongeUsername);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return _playerDatabase.Players.FirstOrDefault(profile =>
            string.Equals(
                NormalizeChallongeUsername(profile.ChallongeUsername),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    private PlayerProfile? PromptForPlayerResolution(string challongeName, string slotLabel, string? apiChallongeUsername)
    {
        var prompt = MessageBox.Show(
            $"Could not match Challonge player '{challongeName}' for {slotLabel}.\n\nYes = choose existing player (or add from list)\nNo = create a new player now\nCancel = stop loading this match",
            "Unrecognized Challonge Player",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (prompt == MessageBoxResult.Cancel)
            return null;

        if (prompt == MessageBoxResult.No)
            return CreatePlayerFromChallongeName(challongeName, apiChallongeUsername);

        var selected = SelectPlayerForName(challongeName, slotLabel, apiChallongeUsername);
        if (selected is not null)
            return selected;

        return null;
    }

    private PlayerProfile? SelectPlayerForName(string challongeName, string slotLabel, string? apiChallongeUsername)
    {
        if (_playerProfiles.Count == 0)
        {
            MessageBox.Show("No saved players found. Create one first.", "Set Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return CreatePlayerFromChallongeName(challongeName, apiChallongeUsername);
        }

        var dialog = new PlayerSelectWindow(_playerProfiles, _playerDatabase, _playerDatabasePath, _countries, GetConfiguredCharacterCatalog(), doubleClickSelectsPlayer: true)
        {
            Owner = this,
            Title = $"Select {slotLabel} for '{challongeName}'"
        };

        if (dialog.ShowDialog() != true || dialog.SelectedProfile is null)
            return null;

        _ = EnrichProfileFromParticipant(dialog.SelectedProfile, challongeName, apiChallongeUsername);
        SavePlayerDatabaseIfDirty();
        return FindProfileByNameOrAlias(dialog.SelectedProfile.Name);
    }

    private PlayerProfile? CreatePlayerFromChallongeName(string challongeName, string? apiChallongeUsername)
    {
        var newProfile = new PlayerProfile
        {
            Name = challongeName.Trim(),
            ChallongeUsername = NormalizeChallongeUsername(apiChallongeUsername)
        };

        var dialog = new PlayerEditWindow(_countries, GetConfiguredCharacterCatalog(), newProfile)
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
        _playerDatabaseDirty = true;
        _ = EnrichProfileFromParticipant(created, challongeName, apiChallongeUsername);
        SavePlayerDatabaseIfDirty();
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
            Characters = ParseCharacters(profile.Characters),
            Character = profile.Characters ?? string.Empty,
            ChallongeUsername = NormalizeChallongeUsername(profile.ChallongeUsername),
            ChallongeProfile = ToChallongeProfileInfo(profile.ChallongeStats)
        };
    }

    private MatchState RehydrateMatchFromPlayerDatabase(ChallongeQueueEntry entry, MatchState match)
    {
        var p1 = RehydratePlayerFromDatabase(match.Player1, entry.RawPlayer1Name, entry.ResolvedPlayer1ChallongeUsername);
        var p2 = RehydratePlayerFromDatabase(match.Player2, entry.RawPlayer2Name, entry.ResolvedPlayer2ChallongeUsername);
        return match with
        {
            Player1 = p1,
            Player2 = p2
        };
    }

    private PlayerInfo RehydratePlayerFromDatabase(PlayerInfo player, string rawName, string? preferredChallongeUsername)
    {
        var profile = FindProfileByNameOrAlias(player.Name)
            ?? FindProfileByNameOrAlias(rawName)
            ?? FindProfileByChallongeUsername(preferredChallongeUsername)
            ?? FindProfileByChallongeUsername(player.ChallongeUsername);

        if (profile is null)
            return player;

        var normalizedPreferred = NormalizeChallongeUsername(preferredChallongeUsername);
        var normalizedProfileUsername = NormalizeChallongeUsername(profile.ChallongeUsername);
        var targetUsername = !string.IsNullOrWhiteSpace(normalizedPreferred)
            ? normalizedPreferred
            : !string.IsNullOrWhiteSpace(normalizedProfileUsername)
                ? normalizedProfileUsername
                : player.ChallongeUsername;

        var targetProfile = ToChallongeProfileInfo(profile.ChallongeStats) ?? player.ChallongeProfile;
        return player with
        {
            Character = profile.Characters ?? string.Empty,
            Characters = ParseCharacters(profile.Characters ?? string.Empty),
            ChallongeUsername = targetUsername,
            ChallongeProfile = targetProfile
        };
    }

    private static ChallongeProfileInfo? ToChallongeProfileInfo(PlayerChallongeStatsSnapshot? stats)
    {
        if (stats is null)
            return null;

        return new ChallongeProfileInfo
        {
            Username = NormalizeChallongeUsername(stats.Username),
            ProfilePageUrl = stats.ProfilePageUrl ?? string.Empty,
            ProfilePictureUrl = stats.ProfilePictureUrl ?? string.Empty,
            BannerImageUrl = stats.BannerImageUrl ?? string.Empty,
            RetrievedAtUtc = stats.RetrievedAtUtc,
            WinRatePercent = stats.WinRatePercent,
            TotalWins = stats.TotalWins,
            TotalLosses = stats.TotalLosses,
            TotalTies = stats.TotalTies,
            TotalTournamentsParticipated = stats.TotalTournamentsParticipated,
            FirstPlaceFinishes = stats.FirstPlaceFinishes,
            SecondPlaceFinishes = stats.SecondPlaceFinishes,
            ThirdPlaceFinishes = stats.ThirdPlaceFinishes,
            TopTenFinishes = stats.TopTenFinishes
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
        MatchRoundLabelBox.Text = match.RoundLabel ?? string.Empty;
        MatchSetTypeBox.Text = ToSetTypeText(match.Format);

        UpdateCharacterButtonLabels();
        SyncScoreDisplaysFromState();
        UpdateDisplayedProfileStats(entry);
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
            Characters = ids,
            Character = characters ?? string.Empty
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
        _playerDatabaseDirty = false;
        RefreshPlayerProfiles();
    }

    private void LoadChallongeStatsCacheFromPlayerDatabase()
    {
        _challongeProfileStatsCache.Clear();
        foreach (var profile in _playerDatabase.Players)
        {
            var snapshot = profile.ChallongeStats;
            if (snapshot is null)
                continue;

            var stats = ToChallongeProfileStats(snapshot);
            if (stats is null)
                continue;

            var keys = new[]
            {
                NormalizeChallongeUsername(snapshot.Username),
                NormalizeChallongeUsername(profile.ChallongeUsername)
            };

            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    _challongeProfileStatsCache[key] = stats;
            }
        }
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

        var profileText = BuildProfileText(profile.ChallongeUsername, null);
        if (isPlayerOne)
            P1ChallongeProfileText.Text = profileText;
        else
            P2ChallongeProfileText.Text = profileText;
    }

    private async Task SelectAndApplyProfileAsync(bool isPlayerOne)
    {
        if (_playerProfiles.Count == 0)
        {
            MessageBox.Show("No saved players found.", "Set Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new PlayerSelectWindow(_playerProfiles, _playerDatabase, _playerDatabasePath, _countries, GetConfiguredCharacterCatalog());
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
                ShowErrorToast($"Failed to apply player: {ex.Message}", ex);
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
        var apiChallongeUsername = isPlayerOne ? entry.RawPlayer1ApiChallongeUsername : entry.RawPlayer2ApiChallongeUsername;
        _ = EnrichProfileFromParticipant(selectedProfile, challongeName, apiChallongeUsername);
        SavePlayerDatabaseIfDirty();

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
            entry.ResolvedPlayer1ChallongeUsername = ResolveParticipantProfileUsername(entry.RawPlayer1ApiChallongeUsername, refreshedProfile);
        else
            entry.ResolvedPlayer2ChallongeUsername = ResolveParticipantProfileUsername(entry.RawPlayer2ApiChallongeUsername, refreshedProfile);
        if (isPlayerOne)
            entry.Player1CharactersText = refreshedProfile.Characters;
        else
            entry.Player2CharactersText = refreshedProfile.Characters;

        UpdateQueueListVisuals();
        UpdateDisplayedProfileStats(entry);
    }

    private void PersistCurrentQueueEntryStateFromHost()
    {
        if (_currentQueueIndex < 0 || _currentQueueIndex >= _challongeQueueEntries.Count)
            return;

        ApplyMatchMetadataFromFieldsToCurrentMatch();
        var entry = _challongeQueueEntries[_currentQueueIndex];
        entry.ResolvedMatch = _host.State.CurrentMatch;
        entry.Player1CharactersText = P1Characters.Text?.Trim() ?? string.Empty;
        entry.Player2CharactersText = P2Characters.Text?.Trim() ?? string.Empty;
    }

    private void ApplyMatchMetadataFromFieldsToCurrentMatch()
    {
        var current = _host.State.CurrentMatch;
        var roundLabel = MatchRoundLabelBox.Text?.Trim() ?? string.Empty;
        var format = ParseSetTypeText(MatchSetTypeBox.Text, current.Format);
        if (string.Equals(current.RoundLabel, roundLabel, StringComparison.Ordinal)
            && current.Format == format)
        {
            return;
        }

        _host.State.SetCurrentMatch(current with
        {
            RoundLabel = roundLabel,
            Format = format
        });
    }

    private static MatchSetFormat ParseSetTypeText(string? input, MatchSetFormat fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;

        var normalized = new string(input
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToUpperInvariant();

        if (normalized.StartsWith("FT", StringComparison.Ordinal) && int.TryParse(normalized[2..], out var ftValue))
            return ToMatchFormat(ftValue);

        if (normalized.StartsWith("BO", StringComparison.Ordinal) && int.TryParse(normalized[2..], out var boValue))
        {
            return boValue switch
            {
                <= 3 => MatchSetFormat.FT2,
                <= 5 => MatchSetFormat.FT3,
                _ => MatchSetFormat.BO7
            };
        }

        if (int.TryParse(normalized, out var directValue))
            return ToMatchFormat(directValue);

        return fallback;
    }

    private static string ToSetTypeText(MatchSetFormat format)
    {
        return format switch
        {
            MatchSetFormat.FT2 => "FT2",
            MatchSetFormat.FT3 => "FT3",
            MatchSetFormat.BO7 => "BO7",
            _ => "FT2"
        };
    }

    private bool EnrichProfileFromParticipant(PlayerProfile? profile, string challongeName, string? apiChallongeUsername)
    {
        if (profile is null)
            return false;

        var changed = false;
        changed |= EnsureAliasForProfile(profile, challongeName);
        changed |= EnsureChallongeUsernameForProfile(profile, apiChallongeUsername);
        return changed;
    }

    private bool EnsureAliasForProfile(PlayerProfile profile, string challongeName)
    {
        var alias = challongeName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(alias) || IsPlaceholderName(alias))
            return false;

        var normalizedAlias = NormalizeName(alias);
        if (string.IsNullOrWhiteSpace(normalizedAlias))
            return false;

        if (string.Equals(NormalizeName(profile.Name), normalizedAlias, StringComparison.Ordinal))
            return false;

        if (_playerDatabase.Players.Any(player =>
                !string.Equals(player.Name, profile.Name, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(NormalizeName(player.Name), normalizedAlias, StringComparison.Ordinal)
                    || player.Aliases.Any(existing =>
                        string.Equals(NormalizeName(existing), normalizedAlias, StringComparison.Ordinal)))))
        {
            return false;
        }

        if (profile.Aliases.Any(existing =>
                string.Equals(NormalizeName(existing), normalizedAlias, StringComparison.Ordinal)))
        {
            return false;
        }

        profile.Aliases.Add(alias);
        profile.Aliases = profile.Aliases
            .Where(existing => !string.IsNullOrWhiteSpace(existing))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(existing => existing, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _playerDatabaseDirty = true;
        return true;
    }

    private bool EnsureChallongeUsernameForProfile(PlayerProfile profile, string? candidateUsername)
    {
        var normalizedCandidate = NormalizeChallongeUsername(candidateUsername);
        if (string.IsNullOrWhiteSpace(normalizedCandidate))
            return false;

        var current = NormalizeChallongeUsername(profile.ChallongeUsername);
        if (!string.IsNullOrWhiteSpace(current))
            return false;

        var conflict = _playerDatabase.Players.FirstOrDefault(player =>
            !string.Equals(player.Name, profile.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeChallongeUsername(player.ChallongeUsername), normalizedCandidate, StringComparison.OrdinalIgnoreCase));
        if (conflict is not null)
            return false;

        profile.ChallongeUsername = normalizedCandidate;
        _playerDatabaseDirty = true;
        return true;
    }

    private static string? ResolveParticipantProfileUsername(string? apiUsername, PlayerProfile? mappedProfile)
    {
        var normalizedApi = NormalizeChallongeUsername(apiUsername);
        if (!string.IsNullOrWhiteSpace(normalizedApi))
            return normalizedApi;

        return NormalizeChallongeUsername(mappedProfile?.ChallongeUsername);
    }

    private static string NormalizeChallongeUsername(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return input.Trim().TrimStart('@').Trim('/').Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private void SavePlayerDatabaseIfDirty()
    {
        if (!_playerDatabaseDirty)
            return;

        PlayerDatabaseStore.Save(_playerDatabasePath, _playerDatabase);
        RefreshPlayerProfiles();
        _playerDatabaseDirty = false;
    }

    private void EditCharacters(bool isPlayerOne)
    {
        var charactersBox = isPlayerOne ? P1Characters : P2Characters;
        var catalog = GetConfiguredCharacterCatalog();
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
        var picker = new ComboBox
        {
            MinWidth = 180,
            IsEditable = true,
            IsTextSearchEnabled = true,
            ToolTip = "Choose from catalog or type a custom character name"
        };
        foreach (var entry in catalog)
            picker.Items.Add(entry.Name);
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

            picker.Text = string.Empty;
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

    private void UpdateDisplayedProfileStats(ChallongeQueueEntry? entry)
    {
        var requestVersion = ++_challongeProfileDisplayVersion;
        var p1Username = entry?.ResolvedPlayer1ChallongeUsername;
        var p2Username = entry?.ResolvedPlayer2ChallongeUsername;

        P1ChallongeProfileText.Text = BuildProfileText(p1Username, entry?.Player1ProfileStats);
        P2ChallongeProfileText.Text = BuildProfileText(p2Username, entry?.Player2ProfileStats);

        _ = RefreshDisplayedProfileStatsAsync(entry, p1Username, p2Username, requestVersion);
    }

    private async Task RefreshDisplayedProfileStatsAsync(ChallongeQueueEntry? entry, string? p1Username, string? p2Username, int requestVersion)
    {
        var normalizedP1 = NormalizeChallongeUsername(p1Username);
        var normalizedP2 = NormalizeChallongeUsername(p2Username);
        if (string.IsNullOrWhiteSpace(normalizedP1) && string.IsNullOrWhiteSpace(normalizedP2))
            return;

        ChallongeProfileStats? p1Stats = null;
        ChallongeProfileStats? p2Stats = null;

        if (!string.IsNullOrWhiteSpace(normalizedP1))
            p1Stats = await GetOrScrapeProfileStatsAsync(normalizedP1);

        if (!string.IsNullOrWhiteSpace(normalizedP2))
            p2Stats = await GetOrScrapeProfileStatsAsync(normalizedP2);

        if (requestVersion != _challongeProfileDisplayVersion)
            return;

        if (entry is not null)
        {
            entry.Player1ProfileStats = p1Stats;
            entry.Player2ProfileStats = p2Stats;
            var match = entry.ResolvedMatch ?? entry.GetDisplayMatch();
            var updatedP1 = match.Player1 with
            {
                ChallongeProfile = p1Stats is null ? match.Player1.ChallongeProfile : ToChallongeProfileInfo(p1Stats),
                ChallongeUsername = !string.IsNullOrWhiteSpace(normalizedP1) ? normalizedP1 : match.Player1.ChallongeUsername
            };
            var updatedP2 = match.Player2 with
            {
                ChallongeProfile = p2Stats is null ? match.Player2.ChallongeProfile : ToChallongeProfileInfo(p2Stats),
                ChallongeUsername = !string.IsNullOrWhiteSpace(normalizedP2) ? normalizedP2 : match.Player2.ChallongeUsername
            };
            entry.ResolvedMatch = match with
            {
                Player1 = updatedP1,
                Player2 = updatedP2
            };

            if (_currentQueueIndex >= 0 && _currentQueueIndex < _challongeQueueEntries.Count
                && ReferenceEquals(_challongeQueueEntries[_currentQueueIndex], entry))
            {
                await _host.SetCurrentMatchAsync(entry.ResolvedMatch, CancellationToken.None);
            }
        }

        P1ChallongeProfileText.Text = BuildProfileText(normalizedP1, p1Stats);
        P2ChallongeProfileText.Text = BuildProfileText(normalizedP2, p2Stats);
    }

    private async Task<ChallongeProfileStats?> GetOrScrapeProfileStatsAsync(string username)
    {
        if (_challongeProfileStatsCache.TryGetValue(username, out var cached))
            return cached;

        ChallongeProfileStats? stats;
        try
        {
            stats = await _challongeProfileScraper.ScrapeByUsernameAsync(username, CancellationToken.None);
        }
        catch
        {
            stats = null;
        }

        _challongeProfileStatsCache[username] = stats;
        if (stats is not null)
            PersistScrapedProfileStats(username, stats);
        return stats;
    }

    private void PersistScrapedProfileStats(string username, ChallongeProfileStats stats)
    {
        var normalized = NormalizeChallongeUsername(username);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var changed = false;
        var normalizedStatsUser = NormalizeChallongeUsername(stats.Username);
        var snapshot = ToSnapshot(stats);

        foreach (var player in _playerDatabase.Players)
        {
            var playerUsername = NormalizeChallongeUsername(player.ChallongeUsername);
            var matches = string.Equals(playerUsername, normalized, StringComparison.OrdinalIgnoreCase)
                          || (!string.IsNullOrWhiteSpace(normalizedStatsUser)
                              && string.Equals(playerUsername, normalizedStatsUser, StringComparison.OrdinalIgnoreCase));
            if (!matches)
                continue;

            player.ChallongeStats = snapshot;
            if (string.IsNullOrWhiteSpace(player.ChallongeUsername))
                player.ChallongeUsername = normalizedStatsUser;
            changed = true;
        }

        if (!changed)
            return;

        _playerDatabaseDirty = true;
        SavePlayerDatabaseIfDirty();
    }

    private static string BuildProfileText(string? username, ChallongeProfileStats? stats)
    {
        var normalized = NormalizeChallongeUsername(username);
        if (string.IsNullOrWhiteSpace(normalized))
            return "Challonge: unavailable";

        if (stats is null)
            return $"Challonge: {normalized}";

        var winRate = stats.WinRatePercent.HasValue ? $"{stats.WinRatePercent.Value:0.#}%" : "-";
        var wins = stats.TotalWins?.ToString() ?? "-";
        var losses = stats.TotalLosses?.ToString() ?? "-";
        return $"Challonge: {normalized} | W-L {wins}-{losses} | WR {winRate}";
    }

    private static ChallongeProfileInfo ToChallongeProfileInfo(ChallongeProfileStats stats)
    {
        return new ChallongeProfileInfo
        {
            Username = NormalizeChallongeUsername(stats.Username),
            ProfilePageUrl = stats.ProfilePageUrl.ToString(),
            ProfilePictureUrl = stats.ProfilePictureUrl ?? string.Empty,
            BannerImageUrl = stats.BannerImageUrl ?? string.Empty,
            RetrievedAtUtc = stats.RetrievedAtUtc,
            WinRatePercent = stats.WinRatePercent,
            TotalWins = stats.TotalWins,
            TotalLosses = stats.TotalLosses,
            TotalTies = stats.TotalTies,
            TotalTournamentsParticipated = stats.TotalTournamentsParticipated,
            FirstPlaceFinishes = stats.FirstPlaceFinishes,
            SecondPlaceFinishes = stats.SecondPlaceFinishes,
            ThirdPlaceFinishes = stats.ThirdPlaceFinishes,
            TopTenFinishes = stats.TopTenFinishes
        };
    }

    private static ChallongeProfileStats? ToChallongeProfileStats(PlayerChallongeStatsSnapshot? snapshot)
    {
        if (snapshot is null)
            return null;

        var username = NormalizeChallongeUsername(snapshot.Username);
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var profileUrl = Uri.TryCreate(snapshot.ProfilePageUrl, UriKind.Absolute, out var parsedProfileUrl)
            ? parsedProfileUrl
            : new Uri($"https://challonge.com/users/{username}");

        return new ChallongeProfileStats
        {
            Username = username,
            ProfilePageUrl = profileUrl,
            ProfilePictureUrl = snapshot.ProfilePictureUrl,
            BannerImageUrl = snapshot.BannerImageUrl,
            RetrievedAtUtc = snapshot.RetrievedAtUtc,
            WinRatePercent = snapshot.WinRatePercent,
            TotalWins = snapshot.TotalWins,
            TotalLosses = snapshot.TotalLosses,
            TotalTies = snapshot.TotalTies,
            TotalTournamentsParticipated = snapshot.TotalTournamentsParticipated,
            FirstPlaceFinishes = snapshot.FirstPlaceFinishes,
            SecondPlaceFinishes = snapshot.SecondPlaceFinishes,
            ThirdPlaceFinishes = snapshot.ThirdPlaceFinishes,
            TopTenFinishes = snapshot.TopTenFinishes
        };
    }

    private static PlayerChallongeStatsSnapshot ToSnapshot(ChallongeProfileStats stats)
    {
        return new PlayerChallongeStatsSnapshot
        {
            Username = NormalizeChallongeUsername(stats.Username),
            ProfilePageUrl = stats.ProfilePageUrl.ToString(),
            ProfilePictureUrl = stats.ProfilePictureUrl ?? string.Empty,
            BannerImageUrl = stats.BannerImageUrl ?? string.Empty,
            RetrievedAtUtc = stats.RetrievedAtUtc,
            WinRatePercent = stats.WinRatePercent,
            TotalWins = stats.TotalWins,
            TotalLosses = stats.TotalLosses,
            TotalTies = stats.TotalTies,
            TotalTournamentsParticipated = stats.TotalTournamentsParticipated,
            FirstPlaceFinishes = stats.FirstPlaceFinishes,
            SecondPlaceFinishes = stats.SecondPlaceFinishes,
            ThirdPlaceFinishes = stats.ThirdPlaceFinishes,
            TopTenFinishes = stats.TopTenFinishes
        };
    }

    private ChallongeProfileStats? TryGetCachedProfileStats(string? username)
    {
        var normalized = NormalizeChallongeUsername(username);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (_challongeProfileStatsCache.TryGetValue(normalized, out var cached))
            return cached;

        var mapped = _playerDatabase.Players.FirstOrDefault(player =>
            string.Equals(NormalizeChallongeUsername(player.ChallongeUsername), normalized, StringComparison.OrdinalIgnoreCase));
        return ToChallongeProfileStats(mapped?.ChallongeStats);
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

    private List<CharacterCatalogSetting> GetConfiguredCharacterCatalog()
    {
        if (_settings.CharacterCatalog is { Count: > 0 })
        {
            return _settings.CharacterCatalog
                .Select(entry => new CharacterCatalogSetting
                {
                    Name = (entry.Name ?? string.Empty).Trim(),
                    SpritePath = (entry.SpritePath ?? string.Empty).Trim()
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return BuildDefaultCharacterCatalog();
    }

    private static List<CharacterCatalogSetting> BuildDefaultCharacterCatalog()
    {
        if (!Directory.Exists(DefaultCharacterSplashartFolder))
            return new List<CharacterCatalogSetting>();

        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
            ".bmp"
        };

        return Directory
            .EnumerateFiles(DefaultCharacterSplashartFolder)
            .Where(path => supportedExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new CharacterCatalogSetting
            {
                Name = Path.GetFileNameWithoutExtension(path),
                SpritePath = path
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private List<RoundNamingRuleSetting> GetConfiguredRoundNamingRules()
    {
        static List<RoundNamingRuleSetting> EnsureFallback(List<RoundNamingRuleSetting> rules)
        {
            if (rules.Any(rule => string.Equals(rule.SelectorType, "fallback", StringComparison.OrdinalIgnoreCase)))
                return rules;

            rules.Add(new RoundNamingRuleSetting
            {
                Enabled = true,
                SideFilter = "both",
                SelectorType = "fallback",
                SelectorValue = 1,
                GrandFinalsResetCondition = "any",
                AppTemplate = "{Side} side - Round {Round}",
                ObsTemplate = "{Side} side - Round {Round}",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 2
            });
            return rules;
        }

        if (_settings.RoundNamingRules is { Count: > 0 })
        {
            var configured = _settings.RoundNamingRules
                .Select(CloneRule)
                .ToList();
            return EnsureFallback(configured);
        }

        return EnsureFallback(RoundNamingEngine.BuildDefaultRules());
    }

    private void ApplyRoundNamingRules(List<ChallongeQueueEntry> entries)
    {
        if (entries.Count == 0)
            return;

        var rules = GetConfiguredRoundNamingRules();
        var sources = entries.Select(entry => new RoundNamingPreviewSource
        {
            MatchId = entry.MatchId,
            MatchNumber = entry.MatchNumber,
            Side = entry.RoundSide,
            Round = entry.RoundAbsolute,
            SuggestedPlayOrder = entry.SuggestedPlayOrder
        }).ToList();
        var results = RoundNamingEngine.ApplyRules(rules, sources, useRuleset: true);
        foreach (var entry in entries)
        {
            if (!results.TryGetValue(entry.MatchId, out var result))
                continue;

            entry.DefaultFt = result.Ft;
            entry.AppRoundLabel = result.AppLabel;
            entry.ObsRoundLabel = result.ObsLabel;
            entry.MatchedRuleIndex = result.MatchedRuleIndex;

            if (entry.ResolvedMatch is not null)
            {
                entry.ResolvedMatch = entry.ResolvedMatch with
                {
                    RoundLabel = entry.ObsRoundLabel,
                    Format = ToMatchFormat(result.Ft)
                };
            }
        }
    }

    private static RoundNamingRuleSetting CloneRule(RoundNamingRuleSetting rule)
        => new()
        {
            Enabled = rule.Enabled,
            SideFilter = rule.SideFilter,
            SelectorType = rule.SelectorType,
            SelectorValue = rule.SelectorValue,
            GrandFinalsResetCondition = rule.GrandFinalsResetCondition,
            AppTemplate = rule.AppTemplate,
            ObsTemplate = rule.ObsTemplate,
            IncludeMatchNumberInAppTitle = rule.IncludeMatchNumberInAppTitle,
            IncludeMatchNumberInObsTitle = rule.IncludeMatchNumberInObsTitle,
            Ft = rule.Ft
        };

    private static MatchSetFormat ToMatchFormat(int ft)
    {
        return ft switch
        {
            <= 2 => MatchSetFormat.FT2,
            3 => MatchSetFormat.FT3,
            _ => MatchSetFormat.BO7
        };
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
        var envObsUrl = Environment.GetEnvironmentVariable("OBS_WS_URL")?.Trim();
        var obsUrl = string.IsNullOrWhiteSpace(_settings.ObsUrl)
            ? (string.IsNullOrWhiteSpace(envObsUrl) ? config.Obs.Url : envObsUrl)
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
            P1ChallongeProfileImage = _settings.MapP1ChallongeProfileImage is null ? config.Overlay.P1ChallongeProfileImage : _settings.MapP1ChallongeProfileImage.Trim(),
            P1ChallongeBannerImage = _settings.MapP1ChallongeBannerImage is null ? config.Overlay.P1ChallongeBannerImage : _settings.MapP1ChallongeBannerImage.Trim(),
            P1ChallongeStatsText = _settings.MapP1ChallongeStatsText is null ? config.Overlay.P1ChallongeStatsText : _settings.MapP1ChallongeStatsText.Trim(),
            P1CharacterSprite = _settings.MapP1CharacterSprite is null ? config.Overlay.P1CharacterSprite : _settings.MapP1CharacterSprite.Trim(),
            P2Name = _settings.MapP2Name is null ? config.Overlay.P2Name : _settings.MapP2Name.Trim(),
            P2Team = _settings.MapP2Team is null ? config.Overlay.P2Team : _settings.MapP2Team.Trim(),
            P2Country = _settings.MapP2Country is null ? config.Overlay.P2Country : _settings.MapP2Country.Trim(),
            P2Flag = _settings.MapP2Flag is null ? config.Overlay.P2Flag : _settings.MapP2Flag.Trim(),
            P2Score = _settings.MapP2Score is null ? config.Overlay.P2Score : _settings.MapP2Score.Trim(),
            P2ChallongeProfileImage = _settings.MapP2ChallongeProfileImage is null ? config.Overlay.P2ChallongeProfileImage : _settings.MapP2ChallongeProfileImage.Trim(),
            P2ChallongeBannerImage = _settings.MapP2ChallongeBannerImage is null ? config.Overlay.P2ChallongeBannerImage : _settings.MapP2ChallongeBannerImage.Trim(),
            P2ChallongeStatsText = _settings.MapP2ChallongeStatsText is null ? config.Overlay.P2ChallongeStatsText : _settings.MapP2ChallongeStatsText.Trim(),
            P2CharacterSprite = _settings.MapP2CharacterSprite is null ? config.Overlay.P2CharacterSprite : _settings.MapP2CharacterSprite.Trim(),
            RoundLabel = _settings.MapRoundLabel is null ? config.Overlay.RoundLabel : _settings.MapRoundLabel.Trim(),
            SetType = _settings.MapSetType is null ? config.Overlay.SetType : _settings.MapSetType.Trim(),
            ChallongeDefaultProfileImagePath = _settings.ChallongeDefaultProfileImagePath is null ? config.Overlay.ChallongeDefaultProfileImagePath : _settings.ChallongeDefaultProfileImagePath.Trim(),
            ChallongeDefaultBannerImagePath = _settings.ChallongeDefaultBannerImagePath is null ? config.Overlay.ChallongeDefaultBannerImagePath : _settings.ChallongeDefaultBannerImagePath.Trim(),
            ChallongeDefaultStatsText = _settings.ChallongeDefaultStatsText is null ? config.Overlay.ChallongeDefaultStatsText : _settings.ChallongeDefaultStatsText.Trim(),
            ChallongeStatsTemplate = _settings.ChallongeStatsTemplate is null ? config.Overlay.ChallongeStatsTemplate : _settings.ChallongeStatsTemplate
        };
        var metadataCountries = new Dictionary<CountryId, CountryInfo>(config.Metadata.Countries);
        foreach (var country in GetConfiguredCountries())
        {
            if (Enum.TryParse<CountryId>(country.Acronym, true, out var resolvedId) && resolvedId != CountryId.Unknown)
                metadataCountries[resolvedId] = country with { Id = resolvedId };
        }

        if (!metadataCountries.ContainsKey(CountryId.Unknown))
            metadataCountries[CountryId.Unknown] = CreateUnknownCountry();
        var characterSprites = GetConfiguredCharacterCatalog()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .ToDictionary(
                entry => entry.Name.Trim(),
                entry => (entry.SpritePath ?? string.Empty).Trim(),
                StringComparer.OrdinalIgnoreCase);
        var metadata = new OverlayMetadata
        {
            Countries = metadataCountries,
            Characters = config.Metadata.Characters,
            CharacterSpritesByName = characterSprites
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
            ShowErrorToast($"Scene switch failed: {ex.Message}", ex);
        }
    }

    private bool HasObsCredentials()
        => !string.IsNullOrWhiteSpace(_host.Config.Obs.Url);

    private bool HasChallongeCredentials()
        => !string.IsNullOrWhiteSpace(ChallongeTournamentBox.Text?.Trim())
           && !string.IsNullOrWhiteSpace(ChallongeApiKeyBox.Text?.Trim());

    private async Task<bool> RefreshObsStatusAsync(bool showToast = true)
    {
        if (!HasObsCredentials())
        {
            SetObsStatus("No Credentials", "#FACC15");
            return false;
        }

        if (showToast)
            ShowPendingStatus("Waiting for OBS to respond...");
        try
        {
            var connected = await _host.ConnectAsync(CancellationToken.None);
            if (connected)
            {
                SetObsStatus("Connected", "#22C55E");
                if (showToast)
                    ShowActionToast("Connected to OBS.", ToastKind.Success);
                return true;
            }

            SetObsStatus("Disconnected", "#EF4444");
            if (showToast)
            {
                var diagnostics = await RunObsConnectionDiagnosticsAsync(CancellationToken.None);
                ShowActionToast(diagnostics.ToastMessage, ToastKind.Error, errorDetails: diagnostics.Details, errorTitle: "OBS Connection Diagnostics");
            }

            return false;
        }
        catch (Exception ex)
        {
            SetObsStatus("Disconnected", "#EF4444");
            if (showToast)
                ShowErrorToast($"OBS connection failed: {ex.Message}", ex);
            return false;
        }
    }

    private sealed record ObsConnectionDiagnostics(string ToastMessage, string Details);

    private async Task<ObsConnectionDiagnostics> RunObsConnectionDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var details = new StringBuilder();
        var url = _host.Config.Obs.Url?.Trim() ?? string.Empty;
        var password = _host.Config.Obs.Password ?? string.Empty;
        details.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:O}");
        details.AppendLine($"Configured URL: {url}");
        details.AppendLine($"Password configured: {(string.IsNullOrEmpty(password) ? "No" : "Yes")}");
        details.AppendLine();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new ObsConnectionDiagnostics("OBS URL is invalid. Click for diagnostics.", details.Append("URL parse failed. Ensure it looks like ws://127.0.0.1:4455").ToString());

        if (!string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            return new ObsConnectionDiagnostics("OBS URL must use ws:// or wss://. Click for diagnostics.",
                details.Append($"Unsupported URL scheme '{uri.Scheme}'.").ToString());
        }

        var port = uri.IsDefaultPort
            ? (string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : uri.Port;
        details.AppendLine($"Parsed endpoint: {uri.Host}:{port} ({uri.Scheme})");

        var tcpReachable = false;
        try
        {
            using var tcpClient = new TcpClient();
            using var tcpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            tcpCts.CancelAfter(TimeSpan.FromSeconds(3));
            await tcpClient.ConnectAsync(uri.Host, port, tcpCts.Token);
            tcpReachable = true;
            details.AppendLine("TCP reachability: Success");
        }
        catch (Exception ex)
        {
            details.AppendLine("TCP reachability: Failed");
            details.AppendLine(ex.ToString());
        }

        var adapter = new ObsWebsocketAdapter();
        var controller = new ObsController(adapter);
        details.AppendLine("Expected OBS websocket settings:");
        details.AppendLine("- WebSocket server enabled");
        details.AppendLine("- Port 4455");
        details.AppendLine("- SSL disabled when using ws:// URLs");
        details.AppendLine();

        var connectResult = await controller.ConnectAndWaitAsync(url, password, TimeSpan.FromSeconds(20), cancellationToken);
        if (connectResult.Ok)
        {
            _ = await controller.DisconnectAsync(cancellationToken);
            details.AppendLine("OBS websocket handshake: Success");
            return new ObsConnectionDiagnostics("Could not connect from app flow, but endpoint responded. Click for diagnostics.", details.ToString());
        }

        details.AppendLine($"OBS websocket handshake: Failed ({connectResult.Code})");
        details.AppendLine(connectResult.Message);
        if (connectResult.Exception is not null)
        {
            details.AppendLine();
            details.AppendLine(connectResult.Exception.ToString());
        }

        var hint = BuildObsFailureHint(connectResult, tcpReachable, password);
        return new ObsConnectionDiagnostics(hint, details.ToString());
    }

    private static string BuildObsFailureHint(Result<bool> connectResult, bool tcpReachable, string password)
    {
        if (!tcpReachable)
            return "Could not reach OBS websocket host/port. Click for diagnostics.";

        var combined = $"{connectResult.Code} {connectResult.Message} {connectResult.Exception?.Message}".ToLowerInvariant();
        if (combined.Contains("auth", StringComparison.Ordinal) || combined.Contains("password", StringComparison.Ordinal))
            return "OBS rejected authentication credentials. Click for diagnostics.";

        if (combined.Contains("timeout", StringComparison.Ordinal))
            return "OBS websocket connection timed out. Click for diagnostics.";

        if (string.IsNullOrWhiteSpace(password))
            return "OBS connection failed. If websocket authentication is enabled in OBS, set the password. Click for diagnostics.";

        return "Could not connect to OBS. Click for diagnostics.";
    }

    private async Task EnsureObsConnectedOnStartupAsync()
    {
        if (!HasObsCredentials())
        {
            SetObsStatus("No Credentials", "#FACC15");
            return;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var connected = await RefreshObsStatusAsync(showToast: attempt == 1);
            if (connected)
                return;

            if (attempt < maxAttempts)
                await Task.Delay(600);
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
        AddField("p1_challonge_profile_image", "P1 Challonge Profile Image", FieldValue(_settings.MapP1ChallongeProfileImage, config.Overlay.P1ChallongeProfileImage));
        AddField("p1_challonge_banner_image", "P1 Challonge Banner Image", FieldValue(_settings.MapP1ChallongeBannerImage, config.Overlay.P1ChallongeBannerImage));
        AddField("p1_challonge_stats_text", "P1 Challonge Stats Text", FieldValue(_settings.MapP1ChallongeStatsText, config.Overlay.P1ChallongeStatsText));
        AddField("p1_character_sprite", "P1 Character Sprite", FieldValue(_settings.MapP1CharacterSprite, config.Overlay.P1CharacterSprite));
        AddField("p2_name", "P2 Name Source", FieldValue(_settings.MapP2Name, config.Overlay.P2Name));
        AddField("p2_team", "P2 Team Source", FieldValue(_settings.MapP2Team, config.Overlay.P2Team));
        AddField("p2_country", "P2 Country Source", FieldValue(_settings.MapP2Country, config.Overlay.P2Country));
        AddField("p2_flag", "P2 Flag Source", FieldValue(_settings.MapP2Flag, config.Overlay.P2Flag));
        AddField("p2_score", "P2 Score Source", FieldValue(_settings.MapP2Score, config.Overlay.P2Score));
        AddField("p2_challonge_profile_image", "P2 Challonge Profile Image", FieldValue(_settings.MapP2ChallongeProfileImage, config.Overlay.P2ChallongeProfileImage));
        AddField("p2_challonge_banner_image", "P2 Challonge Banner Image", FieldValue(_settings.MapP2ChallongeBannerImage, config.Overlay.P2ChallongeBannerImage));
        AddField("p2_challonge_stats_text", "P2 Challonge Stats Text", FieldValue(_settings.MapP2ChallongeStatsText, config.Overlay.P2ChallongeStatsText));
        AddField("p2_character_sprite", "P2 Character Sprite", FieldValue(_settings.MapP2CharacterSprite, config.Overlay.P2CharacterSprite));
        AddField("round_label", "Round Label Source", FieldValue(_settings.MapRoundLabel, config.Overlay.RoundLabel));
        AddField("set_type", "Set Type Source", FieldValue(_settings.MapSetType, config.Overlay.SetType));
        AddField("challonge_default_profile_image_path", "Default Challonge Profile Image", FieldValue(_settings.ChallongeDefaultProfileImagePath, config.Overlay.ChallongeDefaultProfileImagePath));
        AddField("challonge_default_banner_image_path", "Default Challonge Banner Image", FieldValue(_settings.ChallongeDefaultBannerImagePath, config.Overlay.ChallongeDefaultBannerImagePath));
        AddField("challonge_default_stats_text", "Default Challonge Stats Text", FieldValue(_settings.ChallongeDefaultStatsText, config.Overlay.ChallongeDefaultStatsText));
        AddField("challonge_stats_template", "Challonge Stats Template", FieldValue(_settings.ChallongeStatsTemplate, config.Overlay.ChallongeStatsTemplate));

        var saveButton = new Button { Content = "Save", Width = 88, Margin = new Thickness(4) };
        var cancelButton = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(4) };
        var resetButton = new Button { Content = "Use Defaults", Margin = new Thickness(4) };
        var verifyButton = new Button { Content = "Verify OBS Layout", Margin = new Thickness(4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(verifyButton);
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
            boxes["p1_challonge_profile_image"].Text = config.Overlay.P1ChallongeProfileImage;
            boxes["p1_challonge_banner_image"].Text = config.Overlay.P1ChallongeBannerImage;
            boxes["p1_challonge_stats_text"].Text = config.Overlay.P1ChallongeStatsText;
            boxes["p1_character_sprite"].Text = config.Overlay.P1CharacterSprite;
            boxes["p2_name"].Text = config.Overlay.P2Name;
            boxes["p2_team"].Text = config.Overlay.P2Team;
            boxes["p2_country"].Text = config.Overlay.P2Country;
            boxes["p2_flag"].Text = config.Overlay.P2Flag;
            boxes["p2_score"].Text = config.Overlay.P2Score;
            boxes["p2_challonge_profile_image"].Text = config.Overlay.P2ChallongeProfileImage;
            boxes["p2_challonge_banner_image"].Text = config.Overlay.P2ChallongeBannerImage;
            boxes["p2_challonge_stats_text"].Text = config.Overlay.P2ChallongeStatsText;
            boxes["p2_character_sprite"].Text = config.Overlay.P2CharacterSprite;
            boxes["round_label"].Text = config.Overlay.RoundLabel;
            boxes["set_type"].Text = config.Overlay.SetType;
            boxes["challonge_default_profile_image_path"].Text = config.Overlay.ChallongeDefaultProfileImagePath;
            boxes["challonge_default_banner_image_path"].Text = config.Overlay.ChallongeDefaultBannerImagePath;
            boxes["challonge_default_stats_text"].Text = config.Overlay.ChallongeDefaultStatsText;
            boxes["challonge_stats_template"].Text = config.Overlay.ChallongeStatsTemplate;
        };
        verifyButton.Click += async (_, _) => await VerifyObsLayoutAsync();
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
        _settings.MapP1ChallongeProfileImage = boxes["p1_challonge_profile_image"].Text.Trim();
        _settings.MapP1ChallongeBannerImage = boxes["p1_challonge_banner_image"].Text.Trim();
        _settings.MapP1ChallongeStatsText = boxes["p1_challonge_stats_text"].Text.Trim();
        _settings.MapP1CharacterSprite = boxes["p1_character_sprite"].Text.Trim();
        _settings.MapP2Name = boxes["p2_name"].Text.Trim();
        _settings.MapP2Team = boxes["p2_team"].Text.Trim();
        _settings.MapP2Country = boxes["p2_country"].Text.Trim();
        _settings.MapP2Flag = boxes["p2_flag"].Text.Trim();
        _settings.MapP2Score = boxes["p2_score"].Text.Trim();
        _settings.MapP2ChallongeProfileImage = boxes["p2_challonge_profile_image"].Text.Trim();
        _settings.MapP2ChallongeBannerImage = boxes["p2_challonge_banner_image"].Text.Trim();
        _settings.MapP2ChallongeStatsText = boxes["p2_challonge_stats_text"].Text.Trim();
        _settings.MapP2CharacterSprite = boxes["p2_character_sprite"].Text.Trim();
        _settings.MapRoundLabel = boxes["round_label"].Text.Trim();
        _settings.MapSetType = boxes["set_type"].Text.Trim();
        _settings.ChallongeDefaultProfileImagePath = boxes["challonge_default_profile_image_path"].Text.Trim();
        _settings.ChallongeDefaultBannerImagePath = boxes["challonge_default_banner_image_path"].Text.Trim();
        _settings.ChallongeDefaultStatsText = boxes["challonge_default_stats_text"].Text.Trim();
        _settings.ChallongeStatsTemplate = boxes["challonge_stats_template"].Text;
        PersistSettings();
        RebuildHostFromSettings();
        RenderSceneButtons();
        return true;
    }

    private bool ShowCharacterCatalogDialog()
    {
        var entries = GetConfiguredCharacterCatalog()
            .Select(entry => new CharacterCatalogSetting
            {
                Name = entry.Name,
                SpritePath = entry.SpritePath
            })
            .ToList();

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Character catalog entries drive player assignment and first-character sprite overlays.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var rowsPanel = new StackPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 380,
            Content = rowsPanel
        };
        panel.Children.Add(scroll);

        void RenderRows()
        {
            rowsPanel.Children.Clear();
            for (var i = 0; i < entries.Count; i++)
            {
                var index = i;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBox = new TextBox { Text = entries[index].Name, Margin = new Thickness(0, 0, 6, 0) };
                nameBox.TextChanged += (_, _) => entries[index].Name = nameBox.Text;

                var spriteBox = new TextBox { Text = entries[index].SpritePath, Margin = new Thickness(0, 0, 6, 0) };
                spriteBox.TextChanged += (_, _) => entries[index].SpritePath = spriteBox.Text;

                var removeButton = new Button { Content = "Remove", Padding = new Thickness(8, 4, 8, 4) };
                removeButton.Click += (_, _) =>
                {
                    entries.RemoveAt(index);
                    RenderRows();
                };

                Grid.SetColumn(nameBox, 0);
                Grid.SetColumn(spriteBox, 1);
                Grid.SetColumn(removeButton, 2);
                row.Children.Add(nameBox);
                row.Children.Add(spriteBox);
                row.Children.Add(removeButton);
                rowsPanel.Children.Add(row);
            }
        }

        RenderRows();

        var addButton = new Button { Content = "Add Character", Width = 120, Margin = new Thickness(0, 6, 0, 8) };
        addButton.Click += (_, _) =>
        {
            entries.Add(new CharacterCatalogSetting());
            RenderRows();
        };
        var defaultsButton = new Button { Content = "Use default list", Width = 130, Margin = new Thickness(8, 6, 0, 8) };
        defaultsButton.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(
                "This will overwrite the current character catalog list with the default splashart folder files. Continue?",
                "Overwrite Character Catalog",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            entries.Clear();
            entries.AddRange(BuildDefaultCharacterCatalog());
            RenderRows();
        };
        var topButtons = new StackPanel { Orientation = Orientation.Horizontal };
        topButtons.Children.Add(addButton);
        topButtons.Children.Add(defaultsButton);
        panel.Children.Add(topButtons);

        var saveButton = new Button { Content = "Save", Width = 88, Margin = new Thickness(4) };
        var cancelButton = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Owner = this,
            Title = "Character Catalog",
            Content = panel,
            Width = 720,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#101315")!,
            Foreground = Brushes.White
        };

        saveButton.Click += (_, _) => window.DialogResult = true;
        cancelButton.Click += (_, _) => window.DialogResult = false;

        if (window.ShowDialog() != true)
            return false;

        _settings.CharacterCatalog = entries
            .Select(entry => new CharacterCatalogSetting
            {
                Name = (entry.Name ?? string.Empty).Trim(),
                SpritePath = (entry.SpritePath ?? string.Empty).Trim()
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PersistSettings();
        ShowActionToast("Character catalog updated.", ToastKind.Success);
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

    private bool ShowRoundNamingRulesDialog()
    {
        var window = new RoundNamingRulesWindow(GetConfiguredRoundNamingRules(), BuildRoundNamingPreviewSources())
        {
            Owner = this
        };

        if (window.ShowDialog() != true || window.ResultRules is null)
            return false;

        _settings.RoundNamingRules = window.ResultRules.ToList();
        PersistSettings();
        RebuildHostFromSettings();
        ShowActionToast("Round naming rules updated.", ToastKind.Success);
        return true;
    }

    private List<RoundNamingPreviewSource> BuildRoundNamingPreviewSources()
    {
        return _challongeQueueEntries
            .Select(entry => new RoundNamingPreviewSource
            {
                MatchId = entry.MatchId,
                MatchNumber = entry.MatchNumber,
                Side = entry.RoundSide,
                Round = entry.RoundAbsolute,
                SuggestedPlayOrder = entry.SuggestedPlayOrder
            })
            .ToList();
    }

    private void RebuildHostFromSettings()
    {
        var previousObs = _host.GetContext().Obs;
        var wasConnected = previousObs.IsConnectedAsync().GetAwaiter().GetResult();
        var currentMatch = _host.State.CurrentMatch;
        _host = new AutomationHost(BuildRuntimeConfig(), _logger);
        _host.State.SetCurrentMatch(currentMatch);
        RenderSceneButtons();
        SyncScoreDisplaysFromState();

        if (wasConnected)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await _host.ConnectAsync(CancellationToken.None);
                }
                catch
                {
                    // Keep silent: explicit connect flow shows actionable status.
                }
            });
        }
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
        public string MatchNumber { get; init; } = string.Empty;
        public int? RawRound { get; init; }
        public int? SuggestedPlayOrder { get; init; }
        public string RoundSide { get; set; } = "Winners";
        public int RoundAbsolute { get; set; } = 1;
        public int RoundFromEnd { get; set; } = 1;
        public bool IsGrandFinalReset { get; set; }
        public int MatchedRuleIndex { get; set; } = -1;
        public int DefaultFt { get; set; } = 2;
        public long? ChallongePlayer1Id { get; init; }
        public long? ChallongePlayer2Id { get; init; }
        public string ChallongeState { get; set; } = string.Empty;
        public string? ChallongeScoresCsv { get; set; }
        public long? ChallongeWinnerId { get; set; }
        public bool IsReportedToChallonge { get; set; }
        public string RawPlayer1Name { get; init; } = string.Empty;
        public string RawPlayer2Name { get; init; } = string.Empty;
        public string? RawPlayer1ApiChallongeUsername { get; init; }
        public string? RawPlayer2ApiChallongeUsername { get; init; }
        public string? ResolvedPlayer1ChallongeUsername { get; set; }
        public string? ResolvedPlayer2ChallongeUsername { get; set; }
        public ChallongeProfileStats? Player1ProfileStats { get; set; }
        public ChallongeProfileStats? Player2ProfileStats { get; set; }
        public string AppRoundLabel { get; set; } = string.Empty;
        public string ObsRoundLabel { get; set; } = string.Empty;
        public string? Player1CharactersText { get; set; }
        public string? Player2CharactersText { get; set; }
        public MatchState? ResolvedMatch { get; set; }

        public MatchState GetDisplayMatch()
            => ResolvedMatch ?? new MatchState
            {
                RoundLabel = ObsRoundLabel,
                Format = ToMatchFormat(DefaultFt),
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

    private sealed class OrderedDisplayMatch
    {
        public Match Match { get; init; } = null!;
        public string DisplayNumber { get; init; } = string.Empty;
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

    private void ShowErrorToast(string message, Exception exception, bool autoHide = true)
    {
        ShowActionToast(message, ToastKind.Error, autoHide, exception.ToString(), "Internal Error Details");
    }

    private void ShowActionToast(string message, ToastKind kind, bool autoHide = true, string? errorDetails = null, string? errorTitle = null)
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
        _toastErrorDetails = string.IsNullOrWhiteSpace(errorDetails) ? null : errorDetails;
        _toastErrorTitle = string.IsNullOrWhiteSpace(errorTitle) ? "Error Details" : errorTitle!;
        ActionToast.Cursor = _toastErrorDetails is null ? Cursors.Arrow : Cursors.Hand;
        ActionToast.ToolTip = _toastErrorDetails is null ? null : "Click for details";

        ActionToast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        if (autoHide)
            _toastTimer.Start();
    }

    private void ActionToast_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_toastErrorDetails))
            return;

        ShowErrorDetailsDialog(_toastErrorTitle, _toastErrorDetails);
    }

    private void ShowErrorDetailsDialog(string title, string details)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 920,
            Height = 560,
            MinWidth = 680,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#111827")!,
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#F9FAFB")!
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 90,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(closeButton, Dock.Bottom);
        root.Children.Add(closeButton);

        var detailsBox = new TextBox
        {
            Text = details,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#030712")!,
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#F9FAFB")!,
            FontFamily = new FontFamily("Consolas")
        };
        root.Children.Add(detailsBox);

        dialog.Content = root;
        _ = dialog.ShowDialog();
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
