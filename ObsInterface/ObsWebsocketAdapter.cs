using System.Collections;
using System.Globalization;
using System.Reflection;
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
        TaskCompletionSource connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnConnected(object? _, EventArgs __) => connectedTcs.TrySetResult();
        void OnDisconnected(object? _, EventArgs __)
            => connectedTcs.TrySetException(new InvalidOperationException("OBS disconnected before connection completed."));

        Connected += OnConnected;
        Disconnected += OnDisconnected;
        CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(() => connectedTcs.TrySetCanceled(cancellationToken));

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
            cancellationRegistration.Dispose();
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
            object response = _client.GetInputList();
            IReadOnlyList<object> inputEntries = ExtractEntries(response, "Inputs");
            List<ObsInputInfo> result = new(inputEntries.Count);

            foreach (object input in inputEntries)
            {
                string inputName = GetRequiredStringProperty(input, "InputName", "Name");
                string inputKind = GetRequiredStringProperty(input, "InputKind", "Kind");
                result.Add(new ObsInputInfo(inputName, inputKind));
            }

            return result;
        }, cancellationToken);
    }

    public Task<JObject> GetInputSettingsAsync(string inputName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            object response = _client.GetInputSettings(inputName);
            JObject settings = GetJObjectProperty(response, "InputSettings", "Settings");
            return (JObject)settings.DeepClone();
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
            object response = _client.GetSceneItemList(sceneName);
            IReadOnlyList<object> sceneItems = ExtractEntries(response, "SceneItems", "Items");
            List<ObsSceneItemInfo> result = new(sceneItems.Count);

            foreach (object sceneItem in sceneItems)
            {
                int sceneItemId = GetRequiredIntProperty(sceneItem, "SceneItemId", "SceneItemID", "Id");
                string sourceName = GetRequiredStringProperty(sceneItem, "SourceName", "Name");
                bool sceneItemEnabled = GetRequiredBoolProperty(sceneItem, "SceneItemEnabled", "IsEnabled", "Visible");
                result.Add(new ObsSceneItemInfo(sceneItemId, sourceName, sceneItemEnabled));
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
            object response = _client.GetSceneList();
            IReadOnlyList<object> scenes = ExtractEntries(response, "Scenes");
            List<string> result = new(scenes.Count);

            foreach (object scene in scenes)
            {
                string sceneName = GetRequiredStringProperty(scene, "SceneName", "Name");
                result.Add(sceneName);
            }

            return result;
        }, cancellationToken);
    }

    private static IReadOnlyList<object> ExtractEntries(object response, params string[] collectionPropertyNames)
    {
        if (response is null)
            throw new InvalidOperationException("OBS response was null.");

        if (response is string)
            throw new InvalidOperationException($"OBS response '{response.GetType().FullName}' is not enumerable.");

        if (response is IEnumerable topLevelEnumerable)
        {
            List<object> directEntries = new();
            foreach (object? item in topLevelEnumerable)
            {
                if (item is not null)
                    directEntries.Add(item);
            }

            if (directEntries.Count > 0)
                return directEntries;
        }

        foreach (string propertyName in collectionPropertyNames)
        {
            if (!TryGetPropertyValue(response, propertyName, out object? propertyValue) || propertyValue is null)
                continue;

            if (propertyValue is string)
                continue;

            if (propertyValue is IEnumerable enumerable)
            {
                List<object> entries = new();
                foreach (object? item in enumerable)
                {
                    if (item is not null)
                        entries.Add(item);
                }

                return entries;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate an enumerable collection on OBS response type '{response.GetType().FullName}'.");
    }

    private static JObject GetJObjectProperty(object source, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (!TryGetPropertyValue(source, propertyName, out object? value) || value is null)
                continue;

            if (value is JObject jObject)
                return jObject;
        }

        if (source is JObject directJObject)
            return directJObject;

        throw new InvalidOperationException(
            $"None of [{string.Join(", ", propertyNames)}] exist as JObject properties on '{source.GetType().FullName}'.");
    }

    private static string GetRequiredStringProperty(object source, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (!TryGetPropertyValue(source, propertyName, out object? value) || value is null)
                continue;

            if (value is string text)
                return text;
        }

        throw new InvalidOperationException(
            $"None of [{string.Join(", ", propertyNames)}] exist as string properties on '{source.GetType().FullName}'.");
    }

    private static int GetRequiredIntProperty(object source, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (!TryGetPropertyValue(source, propertyName, out object? value) || value is null)
                continue;

            if (value is int intValue)
                return intValue;

            if (value is long longValue)
                return checked((int)longValue);

            if (value is short shortValue)
                return shortValue;

            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed;
        }

        throw new InvalidOperationException(
            $"None of [{string.Join(", ", propertyNames)}] exist as int-like properties on '{source.GetType().FullName}'.");
    }

    private static bool GetRequiredBoolProperty(object source, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (!TryGetPropertyValue(source, propertyName, out object? value) || value is null)
                continue;

            if (value is bool boolValue)
                return boolValue;

            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bool parsed))
                return parsed;
        }

        throw new InvalidOperationException(
            $"None of [{string.Join(", ", propertyNames)}] exist as bool-like properties on '{source.GetType().FullName}'.");
    }

    private static bool TryGetPropertyValue(object source, string propertyName, out object? value)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
        PropertyInfo? property = source.GetType().GetProperty(propertyName, Flags);
        if (property is null)
        {
            value = null;
            return false;
        }

        value = property.GetValue(source);
        return true;
    }
}
