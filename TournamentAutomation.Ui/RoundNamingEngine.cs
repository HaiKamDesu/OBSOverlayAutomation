namespace TournamentAutomation.Ui;

public sealed class RoundNamingPreviewSource
{
    public long MatchId { get; init; }
    public string MatchNumber { get; init; } = string.Empty;
    public string Side { get; init; } = "Winners";
    public int Round { get; init; } = 1;
    public int? SuggestedPlayOrder { get; init; }
}

public sealed class RoundNamingResult
{
    public string AppLabel { get; init; } = string.Empty;
    public string ObsLabel { get; init; } = string.Empty;
    public int Ft { get; init; } = 2;
    public int MatchedRuleIndex { get; init; } = -1;
}

public static class RoundNamingEngine
{
    public static List<RoundNamingRuleSetting> BuildDefaultRules()
        =>
        [
            new()
            {
                SideFilter = "winners",
                SelectorType = "relative_from_end",
                SelectorValue = 1,
                GrandFinalsResetCondition = "reset_only",
                AppTemplate = "Grand Finals Reset",
                ObsTemplate = "Grand Finals Reset",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 3
            },
            new()
            {
                SideFilter = "winners",
                SelectorType = "relative_from_end",
                SelectorValue = 1,
                GrandFinalsResetCondition = "any",
                AppTemplate = "Grand Finals",
                ObsTemplate = "Grand Finals",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 3
            },
            new()
            {
                SideFilter = "winners",
                SelectorType = "relative_from_end",
                SelectorValue = 2,
                GrandFinalsResetCondition = "any",
                AppTemplate = "Winners Finals",
                ObsTemplate = "Winners Finals",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 3
            },
            new()
            {
                SideFilter = "winners",
                SelectorType = "relative_from_end",
                SelectorValue = 3,
                GrandFinalsResetCondition = "any",
                AppTemplate = "Winners Semi Finals",
                ObsTemplate = "Winners Semi Finals",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 2
            },
            new()
            {
                SideFilter = "winners",
                SelectorType = "relative_from_end",
                SelectorValue = 4,
                GrandFinalsResetCondition = "any",
                AppTemplate = "Winners Quarter Finals",
                ObsTemplate = "Winners Quarter Finals",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 2
            },
            new()
            {
                SideFilter = "losers",
                SelectorType = "relative_from_end",
                SelectorValue = 1,
                GrandFinalsResetCondition = "any",
                AppTemplate = "Losers Finals",
                ObsTemplate = "Losers Finals",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 3
            },
            new()
            {
                SideFilter = "losers",
                SelectorType = "relative_from_end",
                SelectorValue = 2,
                GrandFinalsResetCondition = "any",
                AppTemplate = "Losers Semi Finals",
                ObsTemplate = "Losers Semi Finals",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 2
            },
            new()
            {
                SideFilter = "losers",
                SelectorType = "relative_from_end",
                SelectorValue = 3,
                GrandFinalsResetCondition = "any",
                AppTemplate = "Losers Quarter Finals",
                ObsTemplate = "Losers Quarter Finals",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 2
            },
            new()
            {
                SideFilter = "both",
                SelectorType = "fallback",
                SelectorValue = 1,
                GrandFinalsResetCondition = "any",
                AppTemplate = "{Side} side - Round {Round}",
                ObsTemplate = "{Side} side - Round {Round}",
                IncludeMatchNumberInAppTitle = false,
                IncludeMatchNumberInObsTitle = false,
                Ft = 2
            }
        ];

    public static Dictionary<long, RoundNamingResult> ApplyRules(
        IReadOnlyList<RoundNamingRuleSetting> rules,
        IReadOnlyList<RoundNamingPreviewSource> sources,
        bool useRuleset)
    {
        var results = new Dictionary<long, RoundNamingResult>();
        if (sources.Count == 0)
            return results;

        var work = sources
            .Select(source => new WorkingEntry
            {
                MatchId = source.MatchId,
                MatchNumber = source.MatchNumber,
                Side = source.Side,
                Round = Math.Max(1, source.Round),
                SuggestedPlayOrder = source.SuggestedPlayOrder
            })
            .ToList();

        var winnersMax = work.Where(entry => entry.Side == "Winners").Select(entry => entry.Round).DefaultIfEmpty(0).Max();
        var losersMax = work.Where(entry => entry.Side == "Losers").Select(entry => entry.Round).DefaultIfEmpty(0).Max();

        foreach (var entry in work)
        {
            entry.RoundFromEnd = entry.Side switch
            {
                "Winners" when winnersMax > 0 => winnersMax - entry.Round + 1,
                "Losers" when losersMax > 0 => losersMax - entry.Round + 1,
                _ => 0
            };
        }

        var winnersLast = work
            .Where(entry => entry.Side == "Winners" && entry.Round == winnersMax)
            .OrderBy(entry => entry.SuggestedPlayOrder ?? int.MaxValue)
            .ThenBy(entry => entry.MatchId)
            .ToList();
        if (winnersLast.Count >= 2)
            winnersLast[^1].IsGrandFinalReset = true;

        foreach (var entry in work)
        {
            var originalApp = $"Match {entry.MatchNumber} - {entry.Side} side - Round {entry.Round}";
            var originalObs = $"{entry.Side} side - Round {entry.Round}";
            if (!useRuleset)
            {
                results[entry.MatchId] = new RoundNamingResult
                {
                    AppLabel = originalApp,
                    ObsLabel = originalObs,
                    Ft = 2,
                    MatchedRuleIndex = -1
                };
                continue;
            }

            var matchedIndex = -1;
            RoundNamingRuleSetting? matchedRule = null;
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (string.Equals(rule.SelectorType, "fallback", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!RuleMatches(rule, entry))
                    continue;

                matchedIndex = i;
                matchedRule = rule;
                break;
            }

            if (matchedRule is null)
            {
                for (var i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    if (!string.Equals(rule.SelectorType, "fallback", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!rule.Enabled)
                        continue;

                    matchedIndex = i;
                    matchedRule = rule;
                    break;
                }
            }

            if (matchedRule is null)
            {
                results[entry.MatchId] = new RoundNamingResult
                {
                    AppLabel = originalApp,
                    ObsLabel = originalObs,
                    Ft = 2,
                    MatchedRuleIndex = -1
                };
                continue;
            }

            var ft = matchedRule.Ft <= 0 ? 2 : matchedRule.Ft;
            var app = BuildLabel(matchedRule.AppTemplate, matchedRule.IncludeMatchNumberInAppTitle, entry, ft);
            var obs = BuildLabel(matchedRule.ObsTemplate, matchedRule.IncludeMatchNumberInObsTitle, entry, ft);
            results[entry.MatchId] = new RoundNamingResult
            {
                AppLabel = app,
                ObsLabel = obs,
                Ft = ft,
                MatchedRuleIndex = matchedIndex
            };
        }

        return results;
    }

    public static string BuildOriginalAppLabel(RoundNamingPreviewSource source)
        => $"Match {source.MatchNumber} - {source.Side} side - Round {Math.Max(1, source.Round)}";

    public static string BuildOriginalObsLabel(RoundNamingPreviewSource source)
        => $"{source.Side} side - Round {Math.Max(1, source.Round)}";

    private static bool RuleMatches(RoundNamingRuleSetting rule, WorkingEntry entry)
    {
        if (!rule.Enabled)
            return false;

        var sideFilter = (rule.SideFilter ?? "both").Trim().ToLowerInvariant();
        if (sideFilter == "winners" && entry.Side != "Winners")
            return false;
        if (sideFilter == "losers" && entry.Side != "Losers")
            return false;

        var selectorType = (rule.SelectorType ?? "relative_from_end").Trim().ToLowerInvariant();
        if (selectorType == "fallback")
            return true;
        var selectorValue = rule.SelectorValue <= 0 ? 1 : rule.SelectorValue;
        if (selectorType == "explicit_round")
        {
            if (entry.Round != selectorValue)
                return false;
        }
        else
        {
            if (entry.RoundFromEnd != selectorValue)
                return false;
        }

        var resetCondition = (rule.GrandFinalsResetCondition ?? "any").Trim().ToLowerInvariant();
        return resetCondition switch
        {
            "reset_only" => entry.IsGrandFinalReset,
            "non_reset_only" => !entry.IsGrandFinalReset,
            _ => true
        };
    }

    private static string BuildLabel(string template, bool includeMatchNumber, WorkingEntry entry, int ft)
    {
        var parsedTemplate = string.IsNullOrWhiteSpace(template)
            ? "{Side} side - Round {Round}"
            : template;
        var value = parsedTemplate
            .Replace("{MatchNumber}", entry.MatchNumber, StringComparison.OrdinalIgnoreCase)
            .Replace("{Side}", entry.Side, StringComparison.OrdinalIgnoreCase)
            .Replace("{Round}", entry.Round.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{RoundFromEnd}", entry.RoundFromEnd.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{FT}", ft.ToString(), StringComparison.OrdinalIgnoreCase);
        if (includeMatchNumber && !value.Contains(entry.MatchNumber, StringComparison.OrdinalIgnoreCase))
            value = $"Match {entry.MatchNumber} - {value}";

        return value;
    }

    private sealed class WorkingEntry
    {
        public long MatchId { get; init; }
        public string MatchNumber { get; init; } = string.Empty;
        public string Side { get; init; } = "Winners";
        public int Round { get; init; }
        public int RoundFromEnd { get; set; }
        public bool IsGrandFinalReset { get; set; }
        public int? SuggestedPlayOrder { get; init; }
    }
}
