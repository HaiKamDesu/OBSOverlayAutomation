using Microsoft.Win32;
using System.Windows;

namespace TournamentAutomation.Ui;

public partial class CountryEditWindow : Window
{
    public CountrySetting? ResultCountry { get; private set; }

    public CountryEditWindow(CountrySetting? country)
    {
        InitializeComponent();
        if (country is null)
            return;

        CodeBox.Text = country.Code ?? string.Empty;
        NameBox.Text = country.Name ?? string.Empty;
        FlagPathBox.Text = country.FlagPath ?? string.Empty;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Flag Image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;

        FlagPathBox.Text = dialog.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultCountry = new CountrySetting
        {
            Code = CodeBox.Text.Trim(),
            Name = NameBox.Text.Trim(),
            FlagPath = FlagPathBox.Text.Trim()
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
