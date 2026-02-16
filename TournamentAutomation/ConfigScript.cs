using TournamentAutomation.Application.Commands;
using TournamentAutomation.Application.Hotkeys;
using TournamentAutomation.Configuration;
using TournamentAutomation.Domain;

namespace TournamentAutomation;

public static class ConfigScript
{
    public static AppConfig Build()
    {
        return new AppConfig
        {
            Obs = new ObsConnectionConfig
            {
                Url = Environment.GetEnvironmentVariable("OBS_WS_URL") ?? string.Empty,
                Password = Environment.GetEnvironmentVariable("OBS_WS_PASSWORD") ?? string.Empty,
                AutoConnect = true,
                ConnectTimeoutMs = 20000
            },
            Scenes = new SceneMapping
            {
                InMatch = "In-Game Match",
                Desk = "Commentary",
                Break = "Break",
                Results = "Results"
            },
            Overlay = new OverlayMapping
            {
                P1Name = "P1 Player Name",
                P1Team = "P1 Team Name",
                P1Country = "P1 Country Name",
                P1Flag = "P1 Flag",
                P1Score = "P1 Score",
                P2Name = "P2 Player Name",
                P2Team = "P2 Team Name",
                P2Country = "P2 Country Name",
                P2Flag = "P2 Flag",
                P2Score = "P2 Score",
                RoundLabel = "Middle Title",
                SetType = "Middle Set Length",
                P1ChallongeProfileImage = "",
                P1ChallongeBannerImage = "",
                P1ChallongeStatsText = "",
                P1CharacterSprite = "",
                P2ChallongeProfileImage = "",
                P2ChallongeBannerImage = "",
                P2ChallongeStatsText = "",
                P2CharacterSprite = "",
                ChallongeDefaultProfileImagePath = "",
                ChallongeDefaultBannerImagePath = "",
                ChallongeDefaultStatsText = "",
                ChallongeStatsTemplate = "W-L {wins}-{losses} | WR {win_rate}%"
            },
            Metadata = new OverlayMetadata
            {
                Countries = new Dictionary<CountryId, CountryInfo>
                {
                    [CountryId.Unknown] = new CountryInfo { Id = CountryId.Unknown, Acronym = "", FlagPath = "" },
                    [CountryId.ARG] = new CountryInfo { Id = CountryId.ARG, Acronym = "ARG", FlagPath = @"D:/User/Videos/Edited videos/BBCF Clips/1- USABLE ASSETS/Stream Overlays/Blazblue Events/Flags/Argentina.png" },
                    [CountryId.CHL] = new CountryInfo { Id = CountryId.CHL, Acronym = "CHL", FlagPath = @"D:/User/Videos/Edited videos/BBCF Clips/1- USABLE ASSETS/Stream Overlays/Blazblue Events/Flags/Chile.png" },
                    [CountryId.USA] = new CountryInfo { Id = CountryId.USA, Acronym = "USA", FlagPath = @"C:\\flags\\USA.png" },
                    [CountryId.JPN] = new CountryInfo { Id = CountryId.JPN, Acronym = "JPN", FlagPath = @"C:\\flags\\JPN.png" },
                    [CountryId.MEX] = new CountryInfo { Id = CountryId.MEX, Acronym = "MEX", FlagPath = @"C:\\flags\\MEX.png" }
                },
                Characters = new Dictionary<FGCharacterId, FGCharacterInfo>
                {
                    [FGCharacterId.Unknown] = new FGCharacterInfo { Id = FGCharacterId.Unknown, ImagePath = "" },
                    [FGCharacterId.Ragna] = new FGCharacterInfo { Id = FGCharacterId.Ragna, ImagePath = @"C:\\chars\\Ragna.png" },
                    [FGCharacterId.Jin] = new FGCharacterInfo { Id = FGCharacterId.Jin, ImagePath = @"C:\\chars\\Jin.png" },
                    [FGCharacterId.Noel] = new FGCharacterInfo { Id = FGCharacterId.Noel, ImagePath = @"C:\\chars\\Noel.png" },
                    [FGCharacterId.Rachel] = new FGCharacterInfo { Id = FGCharacterId.Rachel, ImagePath = @"C:\\chars\\Rachel.png" }
                }
            },
            PlayerProfiles = new Dictionary<string, PlayerInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["P1_Default"] = CreatePlayer("PlayerOne", "TeamA", CountryId.ARG, FGCharacterId.Ragna),
                ["P2_Default"] = CreatePlayer("PlayerTwo", "TeamB", CountryId.USA, FGCharacterId.Jin)
            },
            Defaults = new MatchDefaults
            {
                RoundLabel = "Pools",
                Format = MatchSetFormat.FT2,
                ScoreMin = 0,
                ScoreMax = 3
            },
            Hotkeys = new HotkeyConfig
            {
                ModeKey = "Ctrl+K",
                ModeTimeoutMs = 1500
            }
        };
    }

    public static MatchState BuildInitialMatch(AppConfig config)
    {
        return new MatchState
        {
            RoundLabel = config.Defaults.RoundLabel,
            Format = config.Defaults.Format,
            Player1 = new PlayerInfo(),
            Player2 = new PlayerInfo()
        };
    }

    public static void SeedQueue(TournamentState state)
    {
        state.Queue.Enqueue(CreateMatch(
            roundLabel: "Winners Semi-Finals",
            format: MatchSetFormat.FT2,
            p1: CreatePlayer("Heythan", "LG", CountryId.ARG, FGCharacterId.Ragna),
            p2: CreatePlayer("Guaripolo", "PK", CountryId.CHL, FGCharacterId.Ragna)));

        state.Queue.Enqueue(CreateMatch(
            roundLabel: "Winners Finals",
            format: MatchSetFormat.FT2,
            p1: CreatePlayer("Heythan", "LG", CountryId.ARG, FGCharacterId.Ragna),
            p2: CreatePlayer("Kam", "LG", CountryId.ARG, FGCharacterId.Ragna)));
    }

    public static MatchState CreateMatch(string roundLabel, MatchSetFormat format, PlayerInfo p1, PlayerInfo p2)
    {
        return new MatchState
        {
            RoundLabel = roundLabel,
            Format = format,
            Player1 = p1,
            Player2 = p2
        };
    }

    public static PlayerInfo CreatePlayer(string name, string team, CountryId country, params FGCharacterId[] characters)
    {
        return new PlayerInfo
        {
            Name = name,
            Team = team,
            Country = country,
            Characters = characters.Length == 0 ? Array.Empty<FGCharacterId>() : characters,
            Character = characters.Length == 0 ? string.Empty : string.Join(", ", characters)
        };
    }

    public static void RegisterHotkeys(HotkeyRegistry registry, CommandDispatcher dispatcher, AppConfig config)
    {
        var catalog = new CommandCatalog(config);

        void BindKey(ConsoleKey key, Func<ICommand> commandFactory)
            => registry.Add(key, commandFactory());

        void BindInlineKey(ConsoleKey key, string description,
            Func<CommandContext, CancellationToken, Task<CommandResult>> execute,
            Func<CommandContext, CancellationToken, Task<CommandResult>>? undo = null,
            bool recordInHistory = true)
            => registry.Add(key, new InlineCommand(description, execute, undo, recordInHistory));

        void BindSequence(string keySequence, string actionId)
        {
            var command = CreateCommand(actionId, catalog, dispatcher)
                ?? throw new InvalidOperationException($"Unknown action id '{actionId}'.");

            registry.Add(keySequence, command);
        }

        // Hotkey mode starter is configured in AppConfig.Hotkeys.ModeKey (default Ctrl+K).
        // Update key sequences below to fit your workflow.
        BindKey(ConsoleKey.F1, () => new SwitchSceneCommand(config.Scenes.InMatch));
        BindKey(ConsoleKey.F2, () => new SwitchSceneCommand(config.Scenes.Desk));
        BindKey(ConsoleKey.F3, () => new SwitchSceneCommand(config.Scenes.Break));
        BindKey(ConsoleKey.F4, () => new SwitchSceneCommand(config.Scenes.Results));

        BindKey(ConsoleKey.D1, () => new AdjustScoreCommand(true, 1));
        BindKey(ConsoleKey.D2, () => new AdjustScoreCommand(false, 1));
        BindKey(ConsoleKey.D3, () => new AdjustScoreCommand(true, -1));
        BindKey(ConsoleKey.D4, () => new AdjustScoreCommand(false, -1));

        BindKey(ConsoleKey.S, () => new SwapPlayersCommand());
        BindKey(ConsoleKey.R, () => new ResetMatchCommand());
        BindKey(ConsoleKey.N, () => new LoadNextMatchCommand());

        BindKey(ConsoleKey.Z, () => new UndoCommand(dispatcher));
        BindKey(ConsoleKey.Y, () => new RedoCommand(dispatcher));

        BindKey(ConsoleKey.F6, () => new SetPlayerProfileCommand(true, "P1_Default"));
        BindKey(ConsoleKey.F7, () => new SetPlayerProfileCommand(false, "P2_Default"));

        BindKey(ConsoleKey.F8, () => new EditPlayerCommand(true));
        BindKey(ConsoleKey.F9, () => new EditPlayerCommand(false));

        // Example of an inline command with explicit logic:
        // BindKey(ConsoleKey.F5, "Force Refresh Overlay",
        //     async (ctx, ct) =>
        //     {
        //         var ok = await ctx.Overlay.ApplyMatchAsync(ctx.State.CurrentMatch, ct);
        //         return ok ? CommandResult.Success("Overlay refreshed.") : CommandResult.Fail("Overlay refresh failed.");
        //     });
    }

    private static ICommand? CreateCommand(string actionId, CommandCatalog catalog, CommandDispatcher dispatcher)
    {
        var normalized = actionId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "undo" => new UndoCommand(dispatcher),
            "redo" => new RedoCommand(dispatcher),
            _ => catalog.TryCreate(normalized, out var created) ? created : null
        };
    }
}
