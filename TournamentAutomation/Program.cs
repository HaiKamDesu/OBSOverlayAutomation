using TournamentAutomation;
using TournamentAutomation.Application;
using TournamentAutomation.Application.Commands;
using TournamentAutomation.Application.Hotkeys;
using TournamentAutomation.Presentation;

var logger = new ConsoleAppLogger();
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var config = ConfigScript.Build();
var host = new AutomationHost(config, logger);

if (config.Obs.AutoConnect)
{
    var connected = await host.ConnectAsync(cts.Token);
    if (!connected)
    {
        logger.Warn("OBS connection failed. Hotkeys will still run, but OBS updates will fail.");
    }
}

var registry = new HotkeyRegistry();
var dispatcher = host.GetDispatcher();
var context = host.GetContext();
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
