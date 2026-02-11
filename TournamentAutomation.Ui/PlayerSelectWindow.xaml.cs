using System.Collections.ObjectModel;
using System.Windows;
using TournamentAutomation.Domain;

namespace TournamentAutomation.Ui;

public partial class PlayerSelectWindow : Window
{
    public PlayerProfile? SelectedProfile { get; private set; }
    private readonly ObservableCollection<PlayerProfile> _players;
    private readonly PlayerDatabase _database;
    private readonly string _databasePath;
    private readonly IReadOnlyList<CountryInfo> _countries;

    public PlayerSelectWindow(
        ObservableCollection<PlayerProfile> players,
        PlayerDatabase database,
        string databasePath,
        IReadOnlyList<CountryInfo> countries)
    {
        InitializeComponent();
        _players = players;
        _database = database;
        _databasePath = databasePath;
        _countries = countries;
        PlayersList.ItemsSource = players;
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

        var dialog = new PlayerEditWindow(_countries, selected);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result is not null)
            UpdateProfile(selected, dialog.Result);
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

        _database.Players.Add(profile);
        _players.Add(profile);
        ResortPlayers();
        PlayerDatabaseStore.Save(_databasePath, _database);
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

        original.Name = updated.Name;
        original.Team = updated.Team;
        original.Country = updated.Country;
        original.Characters = updated.Characters;

        ResortPlayers();
        PlayerDatabaseStore.Save(_databasePath, _database);
    }

    private void ResortPlayers()
    {
        var ordered = _players.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _players.Clear();
        foreach (var profile in ordered)
            _players.Add(profile);
    }
}
