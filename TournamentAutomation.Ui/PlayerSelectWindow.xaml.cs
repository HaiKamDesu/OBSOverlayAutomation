using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TournamentAutomation.Domain;

namespace TournamentAutomation.Ui;

public partial class PlayerSelectWindow : Window
{
    public PlayerProfile? SelectedProfile { get; private set; }
    private readonly ObservableCollection<PlayerProfile> _players;
    private readonly PlayerDatabase _database;
    private readonly string _databasePath;
    private readonly IReadOnlyList<CountryInfo> _countries;
    private readonly bool _doubleClickSelectsPlayer;
    private readonly ICollectionView _playersView;
    private string _activeSortProperty = "Name";
    private ListSortDirection _activeSortDirection = ListSortDirection.Ascending;

    public PlayerSelectWindow(
        ObservableCollection<PlayerProfile> players,
        PlayerDatabase database,
        string databasePath,
        IReadOnlyList<CountryInfo> countries,
        bool doubleClickSelectsPlayer = false)
    {
        InitializeComponent();
        _players = players;
        _database = database;
        _databasePath = databasePath;
        _countries = countries;
        _doubleClickSelectsPlayer = doubleClickSelectsPlayer;
        _playersView = CollectionViewSource.GetDefaultView(players);
        _playersView.Filter = PlayerMatchesSearch;
        PlayersList.ItemsSource = _playersView;
        PlayersList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(PlayersColumnHeader_Click));
        ApplySort("Name", ListSortDirection.Ascending);
        Loaded += (_, _) => AutoSizeColumns();
        PlayersList.SizeChanged += (_, _) => AutoSizeColumns();
        _players.CollectionChanged += (_, _) =>
        {
            _playersView.Refresh();
            ScheduleAutoSizeColumns();
        };
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectedProfile = PlayersList.SelectedItem as PlayerProfile;
        if (SelectedProfile is null)
        {
            MessageBox.Show("Select a player first.", "Set Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PlayerEditWindow(_countries, null);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result is not null)
            AddProfile(dialog.Result);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (PlayersList.SelectedItem is not PlayerProfile selected)
        {
            MessageBox.Show("Select a player to edit.", "Edit Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        EditSelectedProfile(selected);
    }

    private void PlayersList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PlayersList.SelectedItem is not PlayerProfile selected)
            return;

        if (_doubleClickSelectsPlayer)
        {
            SelectedProfile = selected;
            DialogResult = true;
            return;
        }

        EditSelectedProfile(selected);
    }

    private void PlayersColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header)
            return;

        var label = header.Content?.ToString() ?? string.Empty;
        var property = label switch
        {
            "Nickname" => "Name",
            "Team" => "Team",
            "Country" => "Country",
            "Characters" => "Characters",
            "Challonge" => "ChallongeUsername",
            "Aliases" => "AliasesDisplay",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(property))
            return;

        var nextDirection = string.Equals(_activeSortProperty, property, StringComparison.Ordinal)
            && _activeSortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        ApplySort(property, nextDirection);
    }

    private void EditSelectedProfile(PlayerProfile selected)
    {
        var dialog = new PlayerEditWindow(_countries, selected);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            UpdateProfile(selected, dialog.Result);
            _playersView.Refresh();
            AutoSizeColumns();
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (PlayersList.SelectedItem is not PlayerProfile selected)
        {
            MessageBox.Show("Select a player to remove.", "Remove Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Remove '{selected.Name}'?",
            "Remove Player",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        var dbEntry = _database.Players.FirstOrDefault(x =>
            string.Equals(x.Name, selected.Name, StringComparison.OrdinalIgnoreCase));
        if (dbEntry is not null)
            _database.Players.Remove(dbEntry);

        _players.Remove(selected);
        PlayerDatabaseStore.Save(_databasePath, _database);
        AutoSizeColumns();
    }

    private void AddProfile(PlayerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            MessageBox.Show("Name is required.", "Add Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_database.Players.Any(x => string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A player with that name already exists.", "Add Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var aliasConflict = FindAliasConflict(profile, null);
        if (!string.IsNullOrWhiteSpace(aliasConflict))
        {
            MessageBox.Show(aliasConflict, "Add Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var challongeConflict = FindChallongeUsernameConflict(profile, null);
        if (!string.IsNullOrWhiteSpace(challongeConflict))
        {
            MessageBox.Show(challongeConflict, "Add Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _database.Players.Add(profile);
        _players.Add(profile);
        ResortPlayers();
        PlayerDatabaseStore.Save(_databasePath, _database);
        AutoSizeColumns();
    }

    private void UpdateProfile(PlayerProfile original, PlayerProfile updated)
    {
        if (string.IsNullOrWhiteSpace(updated.Name))
        {
            MessageBox.Show("Name is required.", "Edit Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!string.Equals(original.Name, updated.Name, StringComparison.OrdinalIgnoreCase) &&
            _database.Players.Any(x => string.Equals(x.Name, updated.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A player with that name already exists.", "Edit Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var aliasConflict = FindAliasConflict(updated, original);
        if (!string.IsNullOrWhiteSpace(aliasConflict))
        {
            MessageBox.Show(aliasConflict, "Edit Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var challongeConflict = FindChallongeUsernameConflict(updated, original);
        if (!string.IsNullOrWhiteSpace(challongeConflict))
        {
            MessageBox.Show(challongeConflict, "Edit Player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dbEntry = _database.Players.FirstOrDefault(x =>
            string.Equals(x.Name, original.Name, StringComparison.OrdinalIgnoreCase));
        if (dbEntry is null)
        {
            AddProfile(updated);
            return;
        }

        dbEntry.Name = updated.Name;
        dbEntry.Team = updated.Team;
        dbEntry.Country = updated.Country;
        dbEntry.Characters = updated.Characters;
        dbEntry.ChallongeUsername = updated.ChallongeUsername;
        dbEntry.ChallongeStats = updated.ChallongeStats;
        dbEntry.Aliases = updated.Aliases;

        original.Name = updated.Name;
        original.Team = updated.Team;
        original.Country = updated.Country;
        original.Characters = updated.Characters;
        original.ChallongeUsername = updated.ChallongeUsername;
        original.ChallongeStats = updated.ChallongeStats;
        original.Aliases = updated.Aliases;

        ResortPlayers();
        PlayerDatabaseStore.Save(_databasePath, _database);
        AutoSizeColumns();
    }

    private void ResortPlayers()
    {
        var ordered = _players.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _players.Clear();
        foreach (var profile in ordered)
            _players.Add(profile);
        ApplySort(_activeSortProperty, _activeSortDirection);
        ScheduleAutoSizeColumns();
    }

    private string? FindAliasConflict(PlayerProfile candidate, PlayerProfile? existingProfile)
    {
        var normalizedName = candidate.Name.Trim();
        var nameConflict = _database.Players.FirstOrDefault(player =>
            !ReferenceEquals(player, existingProfile) &&
            player.Aliases.Any(existingAlias => string.Equals(existingAlias, normalizedName, StringComparison.OrdinalIgnoreCase)));
        if (nameConflict is not null)
            return $"Player name '{candidate.Name}' conflicts with alias on '{nameConflict.Name}'.";

        foreach (var alias in candidate.Aliases)
        {
            if (string.Equals(alias, normalizedName, StringComparison.OrdinalIgnoreCase))
                return "An alias cannot be the same as the player's own name.";

            var conflict = _database.Players.FirstOrDefault(player =>
                !ReferenceEquals(player, existingProfile) &&
                (string.Equals(player.Name, alias, StringComparison.OrdinalIgnoreCase) ||
                 player.Aliases.Any(existingAlias => string.Equals(existingAlias, alias, StringComparison.OrdinalIgnoreCase))));

            if (conflict is not null)
                return $"Alias '{alias}' conflicts with player '{conflict.Name}'.";
        }

        return null;
    }

    private string? FindChallongeUsernameConflict(PlayerProfile candidate, PlayerProfile? existingProfile)
    {
        var normalized = NormalizeChallongeUsername(candidate.ChallongeUsername);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var conflict = _database.Players.FirstOrDefault(player =>
            !ReferenceEquals(player, existingProfile)
            && string.Equals(NormalizeChallongeUsername(player.ChallongeUsername), normalized, StringComparison.OrdinalIgnoreCase));
        if (conflict is null)
            return null;

        return $"Challonge username '{candidate.ChallongeUsername}' is already assigned to '{conflict.Name}'.";
    }

    private void ScheduleAutoSizeColumns()
    {
        _ = Dispatcher.InvokeAsync(AutoSizeColumns, DispatcherPriority.Background);
    }

    private void AutoSizeColumns()
    {
        if (PlayersList.View is not GridView gridView)
            return;

        foreach (var column in gridView.Columns)
        {
            column.Width = double.NaN;
            var measured = column.ActualWidth;
            if (double.IsNaN(measured) || measured <= 0)
                continue;

            column.Width = measured;
        }
    }

    private void ApplySort(string propertyName, ListSortDirection direction)
    {
        _activeSortProperty = propertyName;
        _activeSortDirection = direction;

        using (_playersView.DeferRefresh())
        {
            _playersView.SortDescriptions.Clear();
            _playersView.SortDescriptions.Add(new SortDescription(propertyName, direction));
        }

        ScheduleAutoSizeColumns();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _playersView.Refresh();
        ScheduleAutoSizeColumns();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchBox.Focus();
    }

    private bool PlayerMatchesSearch(object item)
    {
        if (item is not PlayerProfile profile)
            return false;

        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        if (ContainsIgnoreCase(profile.Name, query))
            return true;

        if (ContainsIgnoreCase(profile.ChallongeUsername, query))
            return true;

        return profile.Aliases.Any(alias => ContainsIgnoreCase(alias, query));
    }

    private static bool ContainsIgnoreCase(string? source, string value)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeChallongeUsername(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return input.Trim().TrimStart('@').Trim('/').Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
