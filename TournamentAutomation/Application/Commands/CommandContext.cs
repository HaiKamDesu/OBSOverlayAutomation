using TournamentAutomation.Application.Logging;
using TournamentAutomation.Application.Overlay;
using TournamentAutomation.Configuration;
using TournamentAutomation.Domain;
using TournamentAutomation.Infrastructure;

namespace TournamentAutomation.Application.Commands;

public sealed class CommandContext
{
    public TournamentState State { get; }
    public IObsGateway Obs { get; }
    public IOverlayUpdater Overlay { get; }
    public IAppLogger Logger { get; }
    public AppConfig Config { get; }

    public CommandContext(
        TournamentState state,
        IObsGateway obs,
        IOverlayUpdater overlay,
        IAppLogger logger,
        AppConfig config)
    {
        State = state;
        Obs = obs;
        Overlay = overlay;
        Logger = logger;
        Config = config;
    }
}
