using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace ObsInterface;

public sealed class ObsWebsocketAdapter : IObsWebsocketAdapter
{
    private readonly OBSWebsocket _client;
    private readonly ILogger<ObsWebsocketAdapter> _logger;
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    public ObsWebsocketAdapter(OBSWebsocket? client = null, ILogger<ObsWebsocketAdapter>? logger = null)
    {
        _client = client ?? new OBSWebsocket();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ObsWebsocketAdapter>.Instance;

        _client.Connected += (_, _) => Connected?.Invoke(this, EventArgs.Empty);
        _client.Disconnected += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<Exception>? Error;

    public bool IsConnected => _client.IsConnected;

    public async Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
                return;

            await ConnectAndAwaitAsync(url, password, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task ConnectAndAwaitAsync(string url, string password, CancellationToken cancellationToken)
    {
        var connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnConnected(object? _, EventArgs __) => connectedTcs.TrySetResult();
        void OnDisconnected(object? _, EventArgs __)
            => connectedTcs.TrySetException(new InvalidOperationException("OBS disconnected before connection completed."));

        Connected += OnConnected;
        Disconnected += OnDisconnected;
        using var cancellationRegistration = cancellationToken.Register(() => connectedTcs.TrySetCanceled(cancellationToken));

        try
        {
            _client.ConnectAsync(url, password);
            await connectedTcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to OBS websocket at {Url}.", url);
            Error?.Invoke(this, ex);
            throw;
        }
        finally
        {
            Connected -= OnConnected;
            Disconnected -= OnDisconnected;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _client.Disconnect();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ObsInputInfo>> GetInputListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run<IReadOnlyList<ObsInputInfo>>(() =>
        {
            var inputs = _client.GetInputList();
            var result = new List<ObsInputInfo>(inputs.Count);
            foreach (var input in inputs)
            {
                result.Add(new ObsInputInfo(input.InputName, input.InputKind));
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
            return (JObject)response.InputSettings.DeepClone();
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
            var items = _client.GetSceneItemList(sceneName);
            var result = new List<ObsSceneItemInfo>(items.Count);
            foreach (var item in items)
            {
                result.Add(new ObsSceneItemInfo(item.SceneItemId, item.SourceName, item.SceneItemEnabled));
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
            var scenes = _client.GetSceneList();
            var result = new List<string>(scenes.Count);
            foreach (var scene in scenes)
            {
                result.Add(scene.Name);
            }

            return result;
        }, cancellationToken);
    }
}
