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

        foreach (var entry in countries.OrderBy(x => x.Id.ToString()))
            _countries.Add(entry);

        CountryBox.ItemsSource = _countries;
        CountryBox.DisplayMemberPath = nameof(CountryInfo.Id);

        if (profile is not null)
        {
            NameBox.Text = profile.Name;
            TeamBox.Text = profile.Team;
            CharactersBox.Text = profile.Characters;

            if (Enum.TryParse<CountryId>(profile.Country, true, out var id))
                CountryBox.SelectedItem = _countries.FirstOrDefault(x => x.Id == id);
        }
        else
        {
            CountryBox.SelectedItem = _countries.FirstOrDefault(x => x.Id == CountryId.Unknown);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var team = TeamBox.Text?.Trim() ?? string.Empty;
        var characters = CharactersBox.Text?.Trim() ?? string.Empty;
        var country = (CountryBox.SelectedItem as CountryInfo)?.Id.ToString() ?? CountryId.Unknown.ToString();

        Result = new PlayerProfile
        {
            Name = name,
            Team = team,
            Country = country,
            Characters = characters
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
