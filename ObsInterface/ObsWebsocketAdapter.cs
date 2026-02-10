using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace ObsInterface;

public sealed class ObsWebsocketAdapter : IObsWebsocketAdapter
{
    private readonly OBSWebsocket _client;
    private readonly ILogger<ObsWebsocketAdapter> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    public ObsWebsocketAdapter(OBSWebsocket? client = null, ILogger<ObsWebsocketAdapter>? logger = null)
    {
        _client = client ?? new OBSWebsocket();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ObsWebsocketAdapter>.Instance;

        _client.Connected += (_, _) => Connected?.Invoke(this, EventArgs.Empty);
        _client.Disconnected += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
        _client.ObsError += (_, e) => Error?.Invoke(this, new Exception(e.Message));
    }

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<Exception>? Error;

    public bool IsConnected => _client.IsConnected;

    public async Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                return;
            }

            _client.ConnectAsync(url, password);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public Task<IReadOnlyList<ObsInputInfo>> GetInputListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run<IReadOnlyList<ObsInputInfo>>(() =>
        {
            var response = _client.GetInputList();
            var payload = ToJObject(response);
            var inputs = payload["inputs"] as JArray ?? [];

            var result = new List<ObsInputInfo>(inputs.Count);
            foreach (var token in inputs)
            {
                var name = token.Value<string>("inputName") ?? token.Value<string>("InputName") ?? string.Empty;
                var kind = token.Value<string>("inputKind") ?? token.Value<string>("InputKind") ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(new ObsInputInfo(name, kind));
                }
            }

            return result;
        }, cancellationToken);
    }

    public Task<JObject> GetInputSettingsAsync(string inputName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            var response = _client.GetInputSettings(inputName);
            var payload = ToJObject(response);

            if (payload["inputSettings"] is JObject inputSettings)
            {
                return inputSettings;
            }

            if (payload["InputSettings"] is JObject inputSettingsPascal)
            {
                return inputSettingsPascal;
            }

            return new JObject();
        }, cancellationToken);
    }

    public Task SetInputSettingsAsync(string inputName, JObject settings, bool overlay, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => _client.SetInputSettings(inputName, settings, overlay), cancellationToken);
    }

    public Task<IReadOnlyList<ObsSceneItemInfo>> GetSceneItemListAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run<IReadOnlyList<ObsSceneItemInfo>>(() =>
        {
            var response = _client.GetSceneItemList(sceneName);
            var payload = ToJObject(response);
            var items = payload["sceneItems"] as JArray ?? payload["SceneItems"] as JArray ?? [];

            var result = new List<ObsSceneItemInfo>(items.Count);
            foreach (var token in items)
            {
                var id = token.Value<int?>("sceneItemId") ?? token.Value<int?>("SceneItemId");
                var source = token.Value<string>("sourceName") ?? token.Value<string>("SourceName") ?? string.Empty;
                var enabled = token.Value<bool?>("sceneItemEnabled") ?? token.Value<bool?>("SceneItemEnabled") ?? false;

                if (id.HasValue && !string.IsNullOrWhiteSpace(source))
                {
                    result.Add(new ObsSceneItemInfo(id.Value, source, enabled));
                }
            }

            return result;
        }, cancellationToken);
    }

    public Task SetSceneItemEnabledAsync(string sceneName, int sceneItemId, bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => _client.SetSceneItemEnabled(sceneName, sceneItemId, enabled), cancellationToken);
    }

    public Task SetCurrentProgramSceneAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => _client.SetCurrentProgramScene(sceneName), cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetSceneNamesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var response = _client.GetSceneList();
            var payload = ToJObject(response);
            var scenes = payload["scenes"] as JArray ?? payload["Scenes"] as JArray ?? [];

            var result = new List<string>(scenes.Count);
            foreach (var scene in scenes)
            {
                var sceneName = scene.Value<string>("sceneName") ?? scene.Value<string>("name") ?? scene.Value<string>("Name");
                if (!string.IsNullOrWhiteSpace(sceneName))
                {
                    result.Add(sceneName);
                }
            }

            return result;
        }, cancellationToken);
    }

    private static JObject ToJObject(object? value)
    {
        if (value is null)
        {
            return new JObject();
        }

        if (value is JObject jobj)
        {
            return jobj;
        }

        var token = JToken.FromObject(value);
        if (token is JObject result)
        {
            return result;
        }

        return new JObject
        {
            ["value"] = token
        };
    }
}
