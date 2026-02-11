using ObsInterface;
using TournamentAutomation;
using TournamentAutomation.Application.Commands;
using TournamentAutomation.Application.Hotkeys;
using TournamentAutomation.Application.Overlay;
using TournamentAutomation.Domain;
using TournamentAutomation.Infrastructure;
using TournamentAutomation.Presentation;

var logger = new ConsoleAppLogger();
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var config = ConfigScript.Build();
var initialMatch = ConfigScript.BuildInitialMatch(config);
var state = new TournamentState(initialMatch)
{
    CurrentScene = config.Scenes.InMatch
};
ConfigScript.SeedQueue(state);

var adapter = new ObsWebsocketAdapter();
var controller = new ObsController(adapter);
var obs = new ObsGateway(controller, config.Obs, logger);
var overlay = new OverlayUpdater(obs, config.Overlay, config.Metadata, logger);

if (config.Obs.AutoConnect)
{
    var connected = await obs.ConnectAsync(cts.Token);
    if (!connected)
    {
        logger.Warn("OBS connection failed. Hotkeys will still run, but OBS updates will fail.");
    }
}

var dispatcher = new CommandDispatcher(logger);
var context = new CommandContext(state, obs, overlay, logger, config);
var registry = new HotkeyRegistry();
ConfigScript.RegisterHotkeys(registry, dispatcher, config);

IHotkeyListener listener = OperatingSystem.IsWindows()
    ? new KeyboardHookListener(logger)
    : new ConsoleHotkeyListener();
var engine = new HotkeyEngine(
    listener,
    registry,
    dispatcher,
    context,
    logger,
    config.Hotkeys.ModeKey,
    TimeSpan.FromMilliseconds(config.Hotkeys.ModeTimeoutMs));

logger.Info("Tournament automation is running. Press Ctrl+C to exit.");
await engine.RunAsync(cts.Token);
