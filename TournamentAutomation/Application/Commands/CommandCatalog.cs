using TournamentAutomation.Configuration;

namespace TournamentAutomation.Application.Commands;

public sealed class CommandCatalog
{
    private readonly AppConfig _config;

    public CommandCatalog(AppConfig config)
    {
        _config = config;
    }

    public bool TryCreate(string actionId, out ICommand? command)
    {
        command = actionId switch
        {
            "scene.inmatch" => new SwitchSceneCommand(_config.Scenes.InMatch),
            "scene.desk" => new SwitchSceneCommand(_config.Scenes.Desk),
            "scene.break" => new SwitchSceneCommand(_config.Scenes.Break),
            "scene.results" => new SwitchSceneCommand(_config.Scenes.Results),

            "score.p1+1" => new AdjustScoreCommand(true, 1),
            "score.p1-1" => new AdjustScoreCommand(true, -1),
            "score.p2+1" => new AdjustScoreCommand(false, 1),
            "score.p2-1" => new AdjustScoreCommand(false, -1),

            "players.swap" => new SwapPlayersCommand(),
            "match.reset" => new ResetMatchCommand(),
            "match.next" => new LoadNextMatchCommand(),

            _ => null
        };

        return command is not null;
    }
}
