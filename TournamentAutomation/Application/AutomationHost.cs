using ObsInterface;
using TournamentAutomation.Application.Commands;
using TournamentAutomation.Application.Logging;
using TournamentAutomation.Application.Overlay;
using TournamentAutomation.Configuration;
using TournamentAutomation.Domain;
using TournamentAutomation.Infrastructure;

namespace TournamentAutomation.Application;

public sealed class AutomationHost
{
    private readonly IAppLogger _logger;
    private readonly CommandDispatcher _dispatcher;
    private readonly CommandContext _context;
    private readonly IObsGateway _obs;
    private readonly IOverlayUpdater _overlay;

    public AppConfig Config { get; }
    public TournamentState State => _context.State;

    public AutomationHost(AppConfig config, IAppLogger logger)
    {
        Config = config;
        _logger = logger;

        var adapter = new ObsWebsocketAdapter();
        var controller = new ObsController(adapter);
        _obs = new ObsGateway(controller, config.Obs, logger);
        _overlay = new OverlayUpdater(_obs, config.Overlay, config.Metadata, logger);

        var initialMatch = ConfigScript.BuildInitialMatch(config);
        var state = new TournamentState(initialMatch)
        {
            CurrentScene = config.Scenes.InMatch
        };

        ConfigScript.SeedQueue(state);

        _dispatcher = new CommandDispatcher(logger);
        _context = new CommandContext(state, _obs, _overlay, logger, config);
    }

    public Task<bool> ConnectAsync(CancellationToken cancellationToken)
        => _obs.ConnectAsync(cancellationToken);

    public Task<bool> RefreshOverlayAsync(CancellationToken cancellationToken)
        => _overlay.ApplyMatchAsync(State.CurrentMatch, cancellationToken);

    public Task<CommandResult> SwitchSceneAsync(string scene, CancellationToken cancellationToken)
        => ExecuteAsync(new SwitchSceneCommand(scene), cancellationToken);

    public Task<CommandResult> AdjustScoreAsync(bool isP1, int delta, CancellationToken cancellationToken)
        => ExecuteAsync(new AdjustScoreCommand(isP1, delta), cancellationToken);

    public Task<CommandResult> SwapPlayersAsync(CancellationToken cancellationToken)
        => ExecuteAsync(new SwapPlayersCommand(), cancellationToken);

    public Task<CommandResult> ResetMatchAsync(CancellationToken cancellationToken)
        => ExecuteAsync(new ResetMatchCommand(), cancellationToken);

    public Task<CommandResult> LoadNextAsync(CancellationToken cancellationToken)
        => ExecuteAsync(new LoadNextMatchCommand(), cancellationToken);

    public Task<CommandResult> UndoAsync(CancellationToken cancellationToken)
        => _dispatcher.UndoAsync(_context, cancellationToken);

    public Task<CommandResult> RedoAsync(CancellationToken cancellationToken)
        => _dispatcher.RedoAsync(_context, cancellationToken);

    public Task<CommandResult> SetPlayerAsync(bool isP1, PlayerInfo player, CancellationToken cancellationToken)
        => ExecuteAsync(new SetPlayerInfoCommand(isP1, player), cancellationToken);

    public Task<CommandResult> ApplyProfileAsync(bool isP1, string profileId, CancellationToken cancellationToken)
        => ExecuteAsync(new SetPlayerProfileCommand(isP1, profileId), cancellationToken);

    private Task<CommandResult> ExecuteAsync(ICommand command, CancellationToken cancellationToken)
        => _dispatcher.ExecuteAsync(command, _context, cancellationToken);

    public CommandDispatcher GetDispatcher() => _dispatcher;
    public CommandContext GetContext() => _context;
}
