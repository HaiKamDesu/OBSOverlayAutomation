using Microsoft.Extensions.Logging;
using ObsInterface;

var wsUrl = Environment.GetEnvironmentVariable("OBS_WS_URL");
var wsPassword = Environment.GetEnvironmentVariable("OBS_WS_PASSWORD") ?? string.Empty;

if (string.IsNullOrWhiteSpace(wsUrl))
{
    Console.WriteLine("OBS_WS_URL is required (for example: ws://192.168.0.21:4455).");
    return;
}

var inputName = Environment.GetEnvironmentVariable("OBS_INPUT_NAME") ?? "P1 Player Name";
var playerText = Environment.GetEnvironmentVariable("OBS_PLAYER_TEXT") ?? "Franco";
var sceneName = Environment.GetEnvironmentVariable("OBS_SCENE_NAME") ?? "In-Game Match";
var sceneItemName = Environment.GetEnvironmentVariable("OBS_SCENE_ITEM_NAME") ?? "In-Game Match Overlay";

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

var setText = await obs.SetTextAsync(inputName, playerText);
Console.WriteLine($"SetText {inputName}: {setText.Ok} ({setText.Code}) {setText.Message}");

var sceneSwitch = await obs.SwitchSceneAsync(sceneName);
Console.WriteLine($"SwitchScene: {sceneSwitch.Ok} ({sceneSwitch.Code}) {sceneSwitch.Message}");

var setVisibility = await obs.SetVisibilityAsync(sceneName, sceneItemName, true);
Console.WriteLine($"SetVisibility: {setVisibility.Ok} ({setVisibility.Code}) {setVisibility.Message}");

await obs.DisconnectAsync();
Console.WriteLine("Done.");
