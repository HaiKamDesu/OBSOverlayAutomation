using System.Collections.ObjectModel;
using System.Windows;

namespace TournamentAutomation.Ui;

public partial class CountryManagerWindow : Window
{
    private readonly ObservableCollection<CountrySetting> _countries;
    public IReadOnlyList<CountrySetting>? ResultCountries { get; private set; }

    public CountryManagerWindow(IReadOnlyList<CountrySetting> countries)
    {
        InitializeComponent();
        _countries = new ObservableCollection<CountrySetting>(
            countries.Select(country => new CountrySetting
            {
                Code = country.Code ?? string.Empty,
                Name = country.Name ?? string.Empty,
                FlagPath = country.FlagPath ?? string.Empty
            }));
        CountriesList.ItemsSource = _countries;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CountryEditWindow(null) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultCountry is null)
            return;

        _countries.Add(dialog.ResultCountry);
        Resort();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (CountriesList.SelectedItem is not CountrySetting selected)
        {
            MessageBox.Show("Select a country to edit.", "Edit Country", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editTarget = new CountrySetting
        {
            Code = selected.Code,
            Name = selected.Name,
            FlagPath = selected.FlagPath
        };
        var dialog = new CountryEditWindow(editTarget) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultCountry is null)
            return;

        selected.Code = dialog.ResultCountry.Code;
        selected.Name = dialog.ResultCountry.Name;
        selected.FlagPath = dialog.ResultCountry.FlagPath;
        CountriesList.Items.Refresh();
        Resort();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (CountriesList.SelectedItem is not CountrySetting selected)
        {
            MessageBox.Show("Select a country to remove.", "Remove Country", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Remove country '{selected.Code}'?",
            "Remove Country",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        _countries.Remove(selected);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultCountries = _countries
            .Select(country => new CountrySetting
            {
                Code = (country.Code ?? string.Empty).Trim(),
                Name = (country.Name ?? string.Empty).Trim(),
                FlagPath = (country.FlagPath ?? string.Empty).Trim()
            })
            .Where(country => !string.IsNullOrWhiteSpace(country.Code) || !string.IsNullOrWhiteSpace(country.Name))
            .GroupBy(country => country.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(country => country.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Resort()
    {
        var ordered = _countries.OrderBy(country => country.Code, StringComparer.OrdinalIgnoreCase).ToList();
        _countries.Clear();
        foreach (var country in ordered)
            _countries.Add(country);
    }
}
