using System.Collections.ObjectModel;
using System.Windows;
using TournamentAutomation.Domain;

namespace TournamentAutomation.Ui;

public partial class PlayerEditWindow : Window
{
    public PlayerProfile? Result { get; private set; }
    private readonly ObservableCollection<CountryInfo> _countries = new();

    public PlayerEditWindow(IReadOnlyList<CountryInfo> countries, PlayerProfile? profile)
    {
        InitializeComponent();

        foreach (var entry in countries.OrderBy(x => x.Acronym, StringComparer.OrdinalIgnoreCase))
            _countries.Add(entry);

        CountryBox.ItemsSource = _countries;

        if (profile is not null)
        {
            NameBox.Text = profile.Name;
            TeamBox.Text = profile.Team;
            CharactersBox.Text = profile.Characters;
            AliasesBox.Text = string.Join(", ", profile.Aliases);

            CountryBox.SelectedItem = _countries.FirstOrDefault(x =>
                string.Equals(x.Acronym, profile.Country, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Id.ToString(), profile.Country, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            CountryBox.SelectedItem = _countries.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Acronym))
                ?? _countries.FirstOrDefault();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var team = TeamBox.Text?.Trim() ?? string.Empty;
        var characters = CharactersBox.Text?.Trim() ?? string.Empty;
        var aliases = ParseAliases(AliasesBox.Text);
        var country = (CountryBox.SelectedItem as CountryInfo)?.Acronym?.Trim() ?? string.Empty;

        Result = new PlayerProfile
        {
            Name = name,
            Team = team,
            Country = country,
            Characters = characters,
            Aliases = aliases
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static List<string> ParseAliases(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<string>();

        return input
            .Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
