using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChallongeProfileScraper.Models;
using ChallongeProfileScraper.Services;
using TournamentAutomation.Domain;

namespace TournamentAutomation.Ui;

public partial class PlayerEditWindow : Window
{
    public PlayerProfile? Result { get; private set; }
    private readonly ObservableCollection<CountryInfo> _countries = new();
    private readonly ChallongeProfileScraperService _profileScraper = new(new HttpClient());
    private readonly string _initialChallongeUsername;
    private PlayerChallongeStatsSnapshot? _currentStats;

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
            ChallongeUsernameBox.Text = profile.ChallongeUsername;
            _currentStats = profile.ChallongeStats;

            CountryBox.SelectedItem = _countries.FirstOrDefault(x =>
                string.Equals(x.Acronym, profile.Country, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Id.ToString(), profile.Country, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            CountryBox.SelectedItem = _countries.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Acronym))
                ?? _countries.FirstOrDefault();
        }

        _initialChallongeUsername = NormalizeChallongeUsername(ChallongeUsernameBox.Text);
        UpdateStatsDisplay();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var team = TeamBox.Text?.Trim() ?? string.Empty;
        var characters = CharactersBox.Text?.Trim() ?? string.Empty;
        var aliases = ParseAliases(AliasesBox.Text);
        var country = (CountryBox.SelectedItem as CountryInfo)?.Acronym?.Trim() ?? string.Empty;
        var challongeUsername = NormalizeChallongeUsername(ChallongeUsernameBox.Text);
        var usernameChanged = !string.Equals(challongeUsername, _initialChallongeUsername, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(challongeUsername) && usernameChanged)
        {
            if (!await TryUpdateStatsAsync(showSuccessToast: false))
            {
                MessageBox.Show(
                    "Could not validate that Challonge profile username. Please fix the username or leave it blank.",
                    "Challonge Profile",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        Result = new PlayerProfile
        {
            Name = name,
            Team = team,
            Country = country,
            Characters = characters,
            ChallongeUsername = challongeUsername,
            ChallongeStats = _currentStats,
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

    private static string NormalizeChallongeUsername(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return input.Trim().TrimStart('@').Trim('/').Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private void ChallongeUsernameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var username = NormalizeChallongeUsername(ChallongeUsernameBox.Text);
        if (_currentStats is not null &&
            !string.Equals(username, NormalizeChallongeUsername(_currentStats.Username), StringComparison.OrdinalIgnoreCase))
        {
            _currentStats = null;
        }

        UpdateStatsDisplay();
    }

    private async void UpdateStatsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = await TryUpdateStatsAsync(showSuccessToast: true);
    }

    private void ViewStatsButton_Click(object sender, RoutedEventArgs e)
    {
        var username = NormalizeChallongeUsername(ChallongeUsernameBox.Text);
        if (string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show("Set a Challonge username first.", "Challonge Stats", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_currentStats is null)
        {
            MessageBox.Show("No cached stats yet. Click Update Stats first.", "Challonge Stats", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ShowStatsViewer(_currentStats);
    }

    private async Task<bool> TryUpdateStatsAsync(bool showSuccessToast)
    {
        var username = NormalizeChallongeUsername(ChallongeUsernameBox.Text);
        if (string.IsNullOrWhiteSpace(username))
        {
            _currentStats = null;
            UpdateStatsDisplay();
            return false;
        }

        UpdateStatsButton.IsEnabled = false;
        ViewStatsButton.IsEnabled = false;
        try
        {
            var stats = await _profileScraper.ScrapeByUsernameAsync(username, CancellationToken.None);
            _currentStats = ToSnapshot(stats);
            UpdateStatsDisplay();
            if (showSuccessToast)
            {
                MessageBox.Show("Challonge stats updated.", "Challonge Stats", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return true;
        }
        catch (Exception ex)
        {
            _currentStats = null;
            UpdateStatsDisplay();
            MessageBox.Show(ex.Message, "Challonge Stats", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            UpdateStatsButton.IsEnabled = true;
            ViewStatsButton.IsEnabled = true;
        }
    }

    private void UpdateStatsDisplay()
    {
        var username = NormalizeChallongeUsername(ChallongeUsernameBox.Text);
        if (string.IsNullOrWhiteSpace(username))
        {
            StatsSummaryText.Text = "Stats: not loaded";
            StatsUpdatedText.Text = "Last updated: never";
            return;
        }

        if (_currentStats is null || !string.Equals(username, NormalizeChallongeUsername(_currentStats.Username), StringComparison.OrdinalIgnoreCase))
        {
            StatsSummaryText.Text = $"Stats for {username}: not loaded";
            StatsUpdatedText.Text = "Last updated: never";
            return;
        }

        StatsSummaryText.Text = $"Stats for {username}: W-L {FormatInt(_currentStats.TotalWins)}-{FormatInt(_currentStats.TotalLosses)}, WR {FormatPercent(_currentStats.WinRatePercent)}";
        StatsUpdatedText.Text = $"Last updated: {_currentStats.RetrievedAtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
    }

    private static PlayerChallongeStatsSnapshot ToSnapshot(ChallongeProfileStats stats)
    {
        return new PlayerChallongeStatsSnapshot
        {
            Username = stats.Username,
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

    private static string FormatInt(int? value) => value?.ToString() ?? "-";

    private static string FormatPercent(decimal? value) => value.HasValue ? $"{value.Value:0.#}%" : "-";

    private void ShowStatsViewer(PlayerChallongeStatsSnapshot stats)
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var banner = new Border
        {
            Height = 140,
            CornerRadius = new CornerRadius(8),
            BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#33405A")!,
            BorderThickness = new Thickness(1),
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#1B2230")!,
            Child = BuildImageOrPlaceholder(stats.BannerImageUrl, "No banner", Stretch.UniformToFill)
        };
        root.Children.Add(banner);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 10)
        };
        Grid.SetRow(header, 1);
        header.Children.Add(new Border
        {
            Width = 84,
            Height = 84,
            CornerRadius = new CornerRadius(42),
            BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#4B5563")!,
            BorderThickness = new Thickness(1),
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#0F172A")!,
            ClipToBounds = true,
            Child = BuildImageOrPlaceholder(stats.ProfilePictureUrl, "No pfp", Stretch.UniformToFill)
        });
        var summary = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(new TextBlock { Text = stats.Username, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        summary.Children.Add(new TextBlock { Text = $"Updated: {stats.RetrievedAtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}", Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.LightGray });
        summary.Children.Add(new TextBlock { Text = $"Profile: {stats.ProfilePageUrl}", Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.LightGray });
        header.Children.Add(summary);
        root.Children.Add(header);

        var statsGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(statsGrid, 2);
        for (var i = 0; i < 2; i++)
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
            statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddStatRow(statsGrid, 0, 0, "Win Rate", FormatPercent(stats.WinRatePercent));
        AddStatRow(statsGrid, 0, 1, "Tournaments", FormatInt(stats.TotalTournamentsParticipated));
        AddStatRow(statsGrid, 1, 0, "Wins", FormatInt(stats.TotalWins));
        AddStatRow(statsGrid, 1, 1, "Losses", FormatInt(stats.TotalLosses));
        AddStatRow(statsGrid, 2, 0, "Ties", FormatInt(stats.TotalTies));
        AddStatRow(statsGrid, 2, 1, "Top 10", FormatInt(stats.TopTenFinishes));
        AddStatRow(statsGrid, 3, 0, "1st / 2nd / 3rd", $"{FormatInt(stats.FirstPlaceFinishes)} / {FormatInt(stats.SecondPlaceFinishes)} / {FormatInt(stats.ThirdPlaceFinishes)}");
        root.Children.Add(statsGrid);

        var closeButton = new Button
        {
            Content = "Close",
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(closeButton, 3);
        root.Children.Add(closeButton);

        var window = new Window
        {
            Owner = this,
            Title = "Challonge Profile Stats",
            Width = 720,
            Height = 620,
            MinWidth = 680,
            MinHeight = 560,
            Content = root,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#101315")!,
            Foreground = Brushes.White
        };

        closeButton.Click += (_, _) => window.Close();
        _ = window.ShowDialog();
    }

    private static void AddStatRow(Grid parent, int row, int column, string label, string value)
    {
        var panel = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#33405A")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#1B2230")!,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 12 },
                    new TextBlock { Text = value, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) }
                }
            }
        };

        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
        parent.Children.Add(panel);
    }

    private static FrameworkElement BuildImageOrPlaceholder(string? imageUrl, string fallbackText, Stretch stretchMode)
    {
        var url = imageUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return new TextBlock
            {
                Text = fallbackText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };
        }

        try
        {
            var image = new Image
            {
                Stretch = stretchMode,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var source = new BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri(url, UriKind.Absolute);
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.EndInit();
            image.Source = source;
            return image;
        }
        catch
        {
            return new TextBlock
            {
                Text = fallbackText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };
        }
    }
}
