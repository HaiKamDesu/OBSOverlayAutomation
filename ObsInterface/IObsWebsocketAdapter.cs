using Newtonsoft.Json.Linq;

namespace ObsInterface;

public interface IObsWebsocketAdapter
{
    event EventHandler? Connected;
    event EventHandler? Disconnected;
    event EventHandler<Exception>? Error;

    bool IsConnected { get; }

    Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObsInputInfo>> GetInputListAsync(CancellationToken cancellationToken = default);
    Task<JObject> GetInputSettingsAsync(string inputName, CancellationToken cancellationToken = default);
    Task SetInputSettingsAsync(string inputName, JObject settings, bool overlay, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObsSceneItemInfo>> GetSceneItemListAsync(string sceneName, CancellationToken cancellationToken = default);
    Task SetSceneItemEnabledAsync(string sceneName, int sceneItemId, bool enabled, CancellationToken cancellationToken = default);
    Task SetCurrentProgramSceneAsync(string sceneName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetSceneNamesAsync(CancellationToken cancellationToken = default);
}

public sealed record ObsInputInfo(string InputName, string InputKind);
public sealed record ObsSceneItemInfo(int SceneItemId, string SourceName, bool SceneItemEnabled);
