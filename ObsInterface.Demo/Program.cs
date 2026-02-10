using Microsoft.Extensions.Logging;
using ObsInterface;

var wsUrl = Environment.GetEnvironmentVariable("OBS_WS_URL");
var wsPassword = Environment.GetEnvironmentVariable("OBS_WS_PASSWORD");

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Information)
        .AddSimpleConsole(options =>
        {
            options.TimestampFormat = "HH:mm:ss ";
            options.SingleLine = true;
        });
});

var adapter = new ObsWebsocketAdapter(logger: loggerFactory.CreateLogger<ObsWebsocketAdapter>());
var obs = new ObsController(
    adapter,
    new ObsInterfaceOptions { StrictMode = false, DefaultTimeout = TimeSpan.FromSeconds(5) },
    loggerFactory.CreateLogger<ObsController>());

obs.OnConnected += (_, _) => Console.WriteLine("Connected event received.");
obs.OnDisconnected += (_, _) => Console.WriteLine("Disconnected event received.");
obs.OnError += (_, ex) => Console.WriteLine($"OBS error event: {ex.Message}");

var connect = await obs.ConnectAndWaitAsync(wsUrl, wsPassword, TimeSpan.FromSeconds(8));
if (!connect.Ok)
{
    Console.WriteLine($"Connect failed: {connect.Code} - {connect.Message}");
    return;
}

var refresh = await obs.RefreshAsync();
Console.WriteLine($"Refresh: {refresh.Ok} ({refresh.Message})");

var inputName = "P1 Player Name";
var setText = await obs.SetTextAsync(inputName, "Franco");
Console.WriteLine($"SetText {inputName}: {setText.Ok} ({setText.Code}) {setText.Message}");

var sceneSwitch = await obs.SwitchSceneAsync("Match");
Console.WriteLine($"SwitchScene: {sceneSwitch.Ok} ({sceneSwitch.Code}) {sceneSwitch.Message}");

var setVisibility = await obs.SetVisibilityAsync("Match", "P1 Panel", true);
Console.WriteLine($"SetVisibility: {setVisibility.Ok} ({setVisibility.Code}) {setVisibility.Message}");

await obs.DisconnectAsync();
Console.WriteLine("Done.");
