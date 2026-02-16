using TournamentAutomation.Domain;

namespace TournamentAutomation.Configuration;

public sealed record AppConfig
{
    public ObsConnectionConfig Obs { get; init; } = new();
    public HotkeyConfig Hotkeys { get; init; } = new();
    public SceneMapping Scenes { get; init; } = new();
    public OverlayMapping Overlay { get; init; } = new();
    public OverlayMetadata Metadata { get; init; } = new();
    public Dictionary<string, TournamentAutomation.Domain.PlayerInfo> PlayerProfiles { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
    public MatchDefaults Defaults { get; init; } = new();
}

public sealed record ObsConnectionConfig
{
    public string Url { get; init; } = "ws://127.0.0.1:4455";
    public string Password { get; init; } = "";
    public bool AutoConnect { get; init; } = true;
    public int ConnectTimeoutMs { get; init; } = 20000;
}

public sealed record HotkeyConfig
{
    public string ModeKey { get; init; } = "Ctrl+K";
    public int ModeTimeoutMs { get; init; } = 1500;
}

public sealed record SceneMapping
{
    public string InMatch { get; init; } = "In-Game Match Overlay";
    public string Desk { get; init; } = "Commentary";
    public string Break { get; init; } = "Break";
    public string Results { get; init; } = "Results";
}

public sealed record OverlayMapping
{
    public string P1Name { get; init; } = "P1 Player Name";
    public string P1Team { get; init; } = "P1 Team Name";
    public string P1Country { get; init; } = "P1 Country Name";
    public string P1Flag { get; init; } = "P1 Flag";
    public string P1Score { get; init; } = "P1 Score";
    public string P1ChallongeProfileImage { get; init; } = "";
    public string P1ChallongeBannerImage { get; init; } = "";
    public string P1ChallongeStatsText { get; init; } = "";
    public string P1CharacterSprite { get; init; } = "";

    public string P2Name { get; init; } = "P2 Player Name";
    public string P2Team { get; init; } = "P2 Team Name";
    public string P2Country { get; init; } = "P2 Country Name";
    public string P2Flag { get; init; } = "P2 Flag";
    public string P2Score { get; init; } = "P2 Score";
    public string P2ChallongeProfileImage { get; init; } = "";
    public string P2ChallongeBannerImage { get; init; } = "";
    public string P2ChallongeStatsText { get; init; } = "";
    public string P2CharacterSprite { get; init; } = "";

    public string RoundLabel { get; init; } = "Round Label";
    public string SetType { get; init; } = "Best Of";
    public string ChallongeDefaultProfileImagePath { get; init; } = "";
    public string ChallongeDefaultBannerImagePath { get; init; } = "";
    public string ChallongeDefaultStatsText { get; init; } = "";
    public string ChallongeStatsTemplate { get; init; } = "W-L {wins}-{losses} | WR {win_rate}%";
}

public sealed record MatchDefaults
{
    public string RoundLabel { get; init; } = "Pools";
    public MatchSetFormat Format { get; init; } = MatchSetFormat.FT2;
    public int ScoreMin { get; init; } = 0;
    public int ScoreMax { get; init; } = 3;
}
