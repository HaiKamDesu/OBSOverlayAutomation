using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace TournamentAutomation.Ui;

public partial class RoundNamingRulesWindow : Window
{
    private readonly ObservableCollection<RoundNamingRuleSetting> _rules;
    private readonly List<RoundNamingPreviewSource> _previewSources;
    private readonly ObservableCollection<RuleListRowViewModel> _ruleRows = new();
    private readonly ObservableCollection<PreviewRowViewModel> _previewRows = new();
    private bool _isDirty;
    private string _rulesSortProperty = "RuleNumberInt";
    private ListSortDirection _rulesSortDirection = ListSortDirection.Ascending;
    private string _previewSortProperty = "MatchNumber";
    private ListSortDirection _previewSortDirection = ListSortDirection.Ascending;
    public IReadOnlyList<RoundNamingRuleSetting>? ResultRules { get; private set; }

    public RoundNamingRulesWindow(IReadOnlyList<RoundNamingRuleSetting> rules, IReadOnlyList<RoundNamingPreviewSource> previewSources)
    {
        InitializeComponent();
        _rules = new ObservableCollection<RoundNamingRuleSetting>(rules.Select(CloneRule));
        _previewSources = previewSources.Select(ClonePreviewSource).ToList();
        RulesList.ItemsSource = _ruleRows;
        PreviewList.ItemsSource = _previewRows;
        RulesList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(RulesColumnHeader_Click));
        PreviewList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(PreviewColumnHeader_Click));
        RulesList.SizeChanged += (_, _) => AutoSizeColumns(RulesList);
        PreviewList.SizeChanged += (_, _) => AutoSizeColumns(PreviewList);
        UpdateBracketSummary();
        UpdatePreviewVisibility();
        RefreshRulesList();
        RefreshPreview();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RoundNamingRuleEditWindow(null) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultRule is null)
            return;

        _rules.Add(dialog.ResultRule);
        MarkDirty();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        _ = TryEditSelectedRule(showSelectionWarning: true);
    }

    private void RulesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _ = TryEditSelectedRule(showSelectionWarning: false);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleListRowViewModel selectedRow)
            return;
        var selected = selectedRow.Rule;

        _rules.Remove(selected);
        MarkDirty();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleListRowViewModel selectedRow)
            return;
        var selected = selectedRow.Rule;

        var index = _rules.IndexOf(selected);
        if (index <= 0)
            return;

        _rules.Move(index, index - 1);
        RulesList.SelectedIndex = index - 1;
        MarkDirty();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleListRowViewModel selectedRow)
            return;
        var selected = selectedRow.Rule;

        var index = _rules.IndexOf(selected);
        if (index < 0 || index >= _rules.Count - 1)
            return;

        _rules.Move(index, index + 1);
        RulesList.SelectedIndex = index + 1;
        MarkDirty();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty)
        {
            var confirm = MessageBox.Show(
                "You have unsaved edits. Restore default ruleset and discard current changes?",
                "Reset to Defaults",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;
        }

        _rules.Clear();
        foreach (var rule in RoundNamingEngine.BuildDefaultRules())
            _rules.Add(CloneRule(rule));

        MarkDirty();
    }

    private void ShowPreviewResultsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreviewVisibility();
    }

    private void RefitColumns_Click(object sender, RoutedEventArgs e)
    {
        AutoSizeColumns(RulesList);
        AutoSizeColumns(PreviewList);
    }

    private void ListView_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not ListView listView)
            return;

        if (FindDescendant<ScrollViewer>(listView) is not ScrollViewer scrollViewer)
            return;

        if (e.Delta > 0)
            scrollViewer.LineUp();
        else
            scrollViewer.LineDown();

        e.Handled = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultRules = _rules.Select(CloneRule).ToList();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        RefreshRulesList();
        RefreshPreview();
    }

    private void RefreshRulesList()
    {
        _ruleRows.Clear();
        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            var selectorType = (rule.SelectorType ?? "relative_from_end").Trim().ToLowerInvariant();
            var selector = selectorType switch
            {
                "explicit_round" => $"Explicit round {Math.Max(1, rule.SelectorValue)}",
                "fallback" => "Fallback (when no rules match)",
                _ => $"{Ordinal(Math.Max(1, rule.SelectorValue))} from end"
            };
            _ruleRows.Add(new RuleListRowViewModel
            {
                Rule = rule,
                RuleNumber = (i + 1).ToString(),
                RuleNumberInt = i + 1,
                Side = rule.SideFilter,
                Selector = selector,
                ResetCondition = rule.GrandFinalsResetCondition,
                Ft = rule.Ft.ToString(),
                FtInt = rule.Ft,
                AppTemplate = rule.AppTemplate,
                ObsTemplate = rule.ObsTemplate
            });
        }

        ApplySort(RulesList, ref _rulesSortProperty, ref _rulesSortDirection, _rulesSortProperty, _rulesSortDirection);
        AutoSizeColumns(RulesList);
    }

    private void RefreshPreview()
    {
        _previewRows.Clear();
        var resolved = RoundNamingEngine.ApplyRules(_rules.ToList(), _previewSources, useRuleset: true);
        foreach (var source in _previewSources.OrderBy(item => item.SuggestedPlayOrder ?? int.MaxValue).ThenBy(item => item.MatchId))
        {
            if (!resolved.TryGetValue(source.MatchId, out var transformed))
                continue;

            var matchedRuleText = transformed.MatchedRuleIndex >= 0
                ? BuildMatchedRuleText(transformed.MatchedRuleIndex)
                : "Fallback rule";
            _previewRows.Add(new PreviewRowViewModel
            {
                MatchLabel = $"Match {source.MatchNumber}",
                MatchNumber = ParseMatchNumber(source.MatchNumber),
                Side = source.Side,
                ActualRound = source.Round.ToString(),
                ActualRoundInt = source.Round,
                OriginalApp = RoundNamingEngine.BuildOriginalAppLabel(source),
                FinalApp = transformed.AppLabel,
                OriginalObs = RoundNamingEngine.BuildOriginalObsLabel(source),
                FinalObs = transformed.ObsLabel,
                Ft = transformed.Ft.ToString(),
                FtInt = transformed.Ft,
                MatchedRuleText = matchedRuleText
            });
        }

        ApplySort(PreviewList, ref _previewSortProperty, ref _previewSortDirection, _previewSortProperty, _previewSortDirection);
        AutoSizeColumns(PreviewList);
    }

    private void RulesColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header)
            return;

        var property = (header.Content?.ToString() ?? string.Empty) switch
        {
            "#" => "RuleNumberInt",
            "Side" => "Side",
            "Selector" => "Selector",
            "Reset Cond." => "ResetCondition",
            "FT" => "FtInt",
            "App Output" => "AppTemplate",
            "OBS Output" => "ObsTemplate",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(property))
            return;

        var direction = property == _rulesSortProperty && _rulesSortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        ApplySort(RulesList, ref _rulesSortProperty, ref _rulesSortDirection, property, direction);
    }

    private void PreviewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header)
            return;

        var property = (header.Content?.ToString() ?? string.Empty) switch
        {
            "Match" => "MatchNumber",
            "Side" => "Side",
            "Actual Round" => "ActualRoundInt",
            "Original (App)" => "OriginalApp",
            "With Ruleset (App)" => "FinalApp",
            "Original (OBS)" => "OriginalObs",
            "With Ruleset (OBS)" => "FinalObs",
            "FT" => "FtInt",
            "Matched Rule" => "MatchedRuleText",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(property))
            return;

        var direction = property == _previewSortProperty && _previewSortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        ApplySort(PreviewList, ref _previewSortProperty, ref _previewSortDirection, property, direction);
    }

    private static void ApplySort(ListView listView, ref string activeProperty, ref ListSortDirection activeDirection, string property, ListSortDirection direction)
    {
        var view = CollectionViewSource.GetDefaultView(listView.ItemsSource);
        if (view is null)
            return;

        activeProperty = property;
        activeDirection = direction;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(property, direction));
        }
    }

    private void AutoSizeColumns(ListView listView)
    {
        if (listView.View is not GridView gridView)
            return;

        _ = Dispatcher.InvokeAsync(() =>
        {
            foreach (var column in gridView.Columns)
            {
                column.Width = 0;
                column.Width = double.NaN;
                var measured = column.ActualWidth;
                if (double.IsNaN(measured) || measured <= 0)
                    continue;

                column.Width = measured;
            }
        }, DispatcherPriority.Background);
    }

    private void UpdatePreviewVisibility()
    {
        var visible = ShowPreviewResultsCheckBox.IsChecked == true;
        PreviewHeaderPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PreviewList.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Height = visible ? 690 : 500;
        SizeToContent = SizeToContent.Manual;
        AutoSizeColumns(RulesList);
        if (visible)
            AutoSizeColumns(PreviewList);
    }

    private void UpdateBracketSummary()
    {
        var winnersRounds = _previewSources
            .Where(source => source.Side == "Winners")
            .Select(source => source.Round)
            .DefaultIfEmpty(0)
            .Max();
        var losersRounds = _previewSources
            .Where(source => source.Side == "Losers")
            .Select(source => source.Round)
            .DefaultIfEmpty(0)
            .Max();
        BracketSummaryText.Text = $"Winners side: {winnersRounds} rounds detected | Losers side: {losersRounds} rounds detected";
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

    private static RoundNamingPreviewSource ClonePreviewSource(RoundNamingPreviewSource source)
        => new()
        {
            MatchId = source.MatchId,
            MatchNumber = source.MatchNumber,
            Side = source.Side,
            Round = source.Round,
            SuggestedPlayOrder = source.SuggestedPlayOrder
        };

    private static string Ordinal(int value)
    {
        var suffix = "th";
        var rem100 = value % 100;
        if (rem100 is < 11 or > 13)
        {
            suffix = (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        }

        return $"{value}{suffix}";
    }

    private static int ParseMatchNumber(string value)
    {
        return int.TryParse(value, out var parsed) ? parsed : int.MaxValue;
    }

    private bool TryEditSelectedRule(bool showSelectionWarning)
    {
        if (RulesList.SelectedItem is not RuleListRowViewModel selectedRow)
        {
            if (showSelectionWarning)
            {
                MessageBox.Show("Select a rule to edit.", "Round Naming Rules", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return false;
        }

        var selected = selectedRow.Rule;
        var dialog = new RoundNamingRuleEditWindow(selected) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultRule is null)
            return false;

        var index = _rules.IndexOf(selected);
        if (index < 0)
            return false;

        _rules[index] = dialog.ResultRule;
        MarkDirty();
        RulesList.SelectedIndex = index;
        return true;
    }

    private string BuildMatchedRuleText(int ruleIndex)
    {
        if (ruleIndex < 0 || ruleIndex >= _rules.Count)
            return $"Matched rule #{ruleIndex + 1}";

        var selectorType = (_rules[ruleIndex].SelectorType ?? string.Empty).Trim().ToLowerInvariant();
        return selectorType == "fallback"
            ? $"Matched fallback rule #{ruleIndex + 1}"
            : $"Matched rule #{ruleIndex + 1}";
    }

    private sealed class RuleListRowViewModel
    {
        public RoundNamingRuleSetting Rule { get; init; } = new();
        public string RuleNumber { get; init; } = string.Empty;
        public int RuleNumberInt { get; init; }
        public string Side { get; init; } = string.Empty;
        public string Selector { get; init; } = string.Empty;
        public string ResetCondition { get; init; } = string.Empty;
        public string Ft { get; init; } = string.Empty;
        public int FtInt { get; init; }
        public string AppTemplate { get; init; } = string.Empty;
        public string ObsTemplate { get; init; } = string.Empty;
    }

    private sealed class PreviewRowViewModel
    {
        public string MatchLabel { get; init; } = string.Empty;
        public int MatchNumber { get; init; }
        public string Side { get; init; } = string.Empty;
        public string ActualRound { get; init; } = string.Empty;
        public int ActualRoundInt { get; init; }
        public string OriginalApp { get; init; } = string.Empty;
        public string FinalApp { get; init; } = string.Empty;
        public string OriginalObs { get; init; } = string.Empty;
        public string FinalObs { get; init; } = string.Empty;
        public string Ft { get; init; } = string.Empty;
        public int FtInt { get; init; }
        public string MatchedRuleText { get; init; } = string.Empty;
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
