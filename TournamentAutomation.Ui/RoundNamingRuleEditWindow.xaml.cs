using System.Windows;
using System.Windows.Controls;

namespace TournamentAutomation.Ui;

public partial class RoundNamingRuleEditWindow : Window
{
    public RoundNamingRuleSetting? ResultRule { get; private set; }

    public RoundNamingRuleEditWindow(RoundNamingRuleSetting? rule)
    {
        InitializeComponent();
        ApplyRule(rule ?? new RoundNamingRuleSetting
        {
            Enabled = true,
            SideFilter = "both",
            SelectorType = "relative_from_end",
            SelectorValue = 1,
            GrandFinalsResetCondition = "any",
            AppTemplate = "{Side} side - Round {Round}",
            ObsTemplate = "{Side} side - Round {Round}",
            IncludeMatchNumberInAppTitle = true,
            IncludeMatchNumberInObsTitle = false,
            Ft = 2
        });
    }

    private void ApplyRule(RoundNamingRuleSetting rule)
    {
        SelectComboItem(EnabledModeBox, rule.Enabled ? "Enabled" : "Disabled", "Enabled");
        SelectComboItem(SideFilterBox, rule.SideFilter, "both");
        SelectComboItem(SelectorTypeBox, rule.SelectorType, "relative_from_end");
        SelectorValueBox.Text = rule.SelectorValue.ToString();
        SelectComboItem(ResetConditionBox, rule.GrandFinalsResetCondition, "any");
        FtBox.Text = rule.Ft.ToString();
        AppTemplateBox.Text = rule.AppTemplate ?? string.Empty;
        ObsTemplateBox.Text = rule.ObsTemplate ?? string.Empty;
        SelectComboItem(IncludeMatchNumberInAppTitleModeBox, rule.IncludeMatchNumberInAppTitle ? "Include" : "Exclude", "Include");
        SelectComboItem(IncludeMatchNumberInObsTitleModeBox, rule.IncludeMatchNumberInObsTitle ? "Include" : "Exclude", "Exclude");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selectorValue = int.TryParse(SelectorValueBox.Text?.Trim(), out var parsedSelectorValue)
            ? Math.Max(1, parsedSelectorValue)
            : 1;
        var ft = int.TryParse(FtBox.Text?.Trim(), out var parsedFt)
            ? Math.Max(1, parsedFt)
            : 2;

        ResultRule = new RoundNamingRuleSetting
        {
            Enabled = string.Equals(GetComboValue(EnabledModeBox, "Enabled"), "Enabled", StringComparison.OrdinalIgnoreCase),
            SideFilter = GetComboValue(SideFilterBox, "both"),
            SelectorType = GetComboValue(SelectorTypeBox, "relative_from_end"),
            SelectorValue = selectorValue,
            GrandFinalsResetCondition = GetComboValue(ResetConditionBox, "any"),
            AppTemplate = AppTemplateBox.Text?.Trim() ?? string.Empty,
            ObsTemplate = ObsTemplateBox.Text?.Trim() ?? string.Empty,
            IncludeMatchNumberInAppTitle = string.Equals(GetComboValue(IncludeMatchNumberInAppTitleModeBox, "Include"), "Include", StringComparison.OrdinalIgnoreCase),
            IncludeMatchNumberInObsTitle = string.Equals(GetComboValue(IncludeMatchNumberInObsTitleModeBox, "Exclude"), "Include", StringComparison.OrdinalIgnoreCase),
            Ft = ft
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string GetComboValue(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Content is string content && !string.IsNullOrWhiteSpace(content))
            return content.Trim();

        return fallback;
    }

    private static void SelectComboItem(ComboBox combo, string? value, string fallback)
    {
        var lookup = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Content is string content && string.Equals(content, lookup, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }
}
