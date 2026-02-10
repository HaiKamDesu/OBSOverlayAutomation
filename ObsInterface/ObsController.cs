using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace ObsInterface;

public sealed class ObsController
{
    private readonly IObsWebsocketAdapter _obs;
    private readonly ObsInterfaceOptions _options;
    private readonly ILogger<ObsController> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CachedInput> _inputs = new(StringComparer.Ordinal);

    public ObsController(IObsWebsocketAdapter obs, ObsInterfaceOptions? options = null, ILogger<ObsController>? logger = null)
    {
        _obs = obs;
        _options = options ?? new ObsInterfaceOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ObsController>.Instance;

        _obs.Connected += (_, _) => OnConnected?.Invoke(this, EventArgs.Empty);
        _obs.Disconnected += (_, _) => OnDisconnected?.Invoke(this, EventArgs.Empty);
        _obs.Error += (_, ex) => OnError?.Invoke(this, ex);
    }

    public event EventHandler? OnConnected;
    public event EventHandler? OnDisconnected;
    public event EventHandler<Exception>? OnError;

    public bool IsConnected => _obs.IsConnected;

    public async Task<Result<bool>> ConnectAndWaitAsync(string url, string password, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Fail<bool>(ResultCodes.InvalidArgument, "OBS websocket URL is required.");

        var effectiveTimeout = timeout ?? _options.DefaultTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        try
        {
            var connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? _, EventArgs __) => connectedTcs.TrySetResult();

            _obs.Connected += Handler;
            try
            {
                await _obs.ConnectAsync(url, password, cts.Token).ConfigureAwait(false);

                if (_obs.IsConnected)
                    return Result<bool>.Success(true, "Connected immediately.");

                while (!cts.Token.IsCancellationRequested)
                {
                    var finished = await Task.WhenAny(connectedTcs.Task, Task.Delay(100, cts.Token)).ConfigureAwait(false);
                    if (finished == connectedTcs.Task || _obs.IsConnected)
                        return Result<bool>.Success(true, "Connected.");
                }

                return Fail<bool>(ResultCodes.Timeout, $"Connection timed out after {effectiveTimeout}.");
            }
            finally
            {
                _obs.Connected -= Handler;
            }
        }
        catch (OperationCanceledException ex)
        {
            return Fail<bool>(ResultCodes.Timeout, $"Connection timed out after {effectiveTimeout}.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to OBS.");
            return Fail<bool>(ResultCodes.ObsError, "Could not connect to OBS.", ex);
        }
    }

    public async Task<Result<bool>> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _obs.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            InvalidateCache();
            return Result<bool>.Success(true, "Disconnected.");
        }
        catch (Exception ex)
        {
            return Fail<bool>(ResultCodes.ObsError, "Disconnect failed.", ex);
        }
    }

    public async Task<Result<bool>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var precheck = EnsureConnected<bool>();
        if (!precheck.Ok)
            return precheck;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var inputs = await WithTimeout(ct => _obs.GetInputListAsync(ct), cancellationToken).ConfigureAwait(false);
            _inputs.Clear();
            foreach (var input in inputs)
            {
                _inputs[input.InputName] = new CachedInput(input.InputName, input.InputKind, null);
            }

            return Result<bool>.Success(true, $"Cached {_inputs.Count} inputs.");
        }
        catch (OperationCanceledException ex)
        {
            return Fail<bool>(ResultCodes.Timeout, "Refreshing input cache timed out.", ex);
        }
        catch (Exception ex)
        {
            return Fail<bool>(ResultCodes.ObsError, "Refreshing input cache failed.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void InvalidateCache()
    {
        lock (_inputs)
        {
            _inputs.Clear();
        }
    }

    public async Task<Result<bool>> GetInputExistsAsync(string inputName, CancellationToken cancellationToken = default)
    {
        var input = await GetCachedInputAsync(inputName, cancellationToken).ConfigureAwait(false);
        if (!input.Ok)
            return Result<bool>.Fail(input.Code, input.Message, input.Exception);

        return Result<bool>.Success(input.Value is not null);
    }

    public async Task<Result<string>> GetInputKindAsync(string inputName, CancellationToken cancellationToken = default)
    {
        var input = await RequireInputAsync(inputName, cancellationToken).ConfigureAwait(false);
        if (!input.Ok)
            return Result<string>.Fail(input.Code, input.Message, input.Exception);

        return Result<string>.Success(input.Value!.InputKind);
    }

    public async Task<Result<JObject>> GetInputSettingsAsync(string inputName, CancellationToken cancellationToken = default)
    {
        var check = await RequireInputAsync(inputName, cancellationToken).ConfigureAwait(false);
        if (!check.Ok)
            return Result<JObject>.Fail(check.Code, check.Message, check.Exception);

        try
        {
            var settings = await WithTimeout(ct => _obs.GetInputSettingsAsync(inputName, ct), cancellationToken).ConfigureAwait(false);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _inputs[inputName] = check.Value! with { Settings = settings };
            }
            finally
            {
                _gate.Release();
            }

            return Result<JObject>.Success(settings);
        }
        catch (OperationCanceledException ex)
        {
            return Fail<JObject>(ResultCodes.Timeout, "GetInputSettings timed out.", ex);
        }
        catch (Exception ex)
        {
            return Fail<JObject>(ResultCodes.ObsError, "Failed to read input settings.", ex);
        }
    }

    public async Task<Result<bool>> SetInputSettingsAsync(string inputName, JObject settings, bool overlay = true, CancellationToken cancellationToken = default)
    {
        if (settings is null)
            return Fail<bool>(ResultCodes.InvalidArgument, "settings is required.");

        var check = await RequireInputAsync(inputName, cancellationToken).ConfigureAwait(false);
        if (!check.Ok)
            return Result<bool>.Fail(check.Code, check.Message, check.Exception);

        try
        {
            await WithTimeout(ct => _obs.SetInputSettingsAsync(inputName, settings, overlay, ct), cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(true, "Input settings updated.");
        }
        catch (OperationCanceledException ex)
        {
            return Fail<bool>(ResultCodes.Timeout, "SetInputSettings timed out.", ex);
        }
        catch (Exception ex)
        {
            return Fail<bool>(ResultCodes.ObsError, "Failed to set input settings.", ex);
        }
    }

    public async Task<Result<bool>> SetTextAsync(string inputName, string text, CancellationToken cancellationToken = default)
    {
        if (text is null)
            return Fail<bool>(ResultCodes.InvalidArgument, "text is required.");

        var settingsResult = await GetInputSettingsAsync(inputName, cancellationToken).ConfigureAwait(false);
        if (!settingsResult.Ok)
            return Result<bool>.Fail(settingsResult.Code, settingsResult.Message, settingsResult.Exception);

        if (!settingsResult.Value!.ContainsKey("text"))
            return Fail<bool>(ResultCodes.TypeMismatch, $"Input '{inputName}' does not expose a 'text' setting key.");

        var settings = new JObject { ["text"] = text };
        return await SetInputSettingsAsync(inputName, settings, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<bool>> SetImageFileAsync(string inputName, string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Fail<bool>(ResultCodes.InvalidArgument, "filePath is required.");

        var settingsResult = await GetInputSettingsAsync(inputName, cancellationToken).ConfigureAwait(false);
        if (!settingsResult.Ok)
            return Result<bool>.Fail(settingsResult.Code, settingsResult.Message, settingsResult.Exception);

        var settings = settingsResult.Value!;
        var key = settings.ContainsKey("file") ? "file" : settings.ContainsKey("local_file") ? "local_file" : null;
        if (key is null)
            return Fail<bool>(ResultCodes.TypeMismatch, $"Input '{inputName}' does not support a recognized image path setting key.");

        var patch = new JObject { [key] = filePath };
        return await SetInputSettingsAsync(inputName, patch, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<int>> GetSceneItemIdAsync(string sceneName, string sceneItemName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(sceneItemName))
            return Fail<int>(ResultCodes.InvalidArgument, "sceneName and sceneItemName are required.");

        var sceneItems = await GetSceneItemsAsync(sceneName, cancellationToken).ConfigureAwait(false);
        if (!sceneItems.Ok)
            return Result<int>.Fail(sceneItems.Code, sceneItems.Message, sceneItems.Exception);

        var sceneItem = sceneItems.Value!.FirstOrDefault(x => string.Equals(x.SourceName, sceneItemName, StringComparison.Ordinal));
        if (sceneItem is null)
            return Fail<int>(ResultCodes.NotFound, $"Scene item '{sceneItemName}' not found in scene '{sceneName}'.");

        return Result<int>.Success(sceneItem.SceneItemId);
    }

    public Task<Result<bool>> SetVisibilityAsync(string sceneName, int sceneItemId, bool visible, CancellationToken cancellationToken = default)
        => SetVisibilityCoreAsync(sceneName, sceneItemId, visible, cancellationToken);

    public async Task<Result<bool>> SetVisibilityAsync(string sceneName, string sceneItemName, bool visible, CancellationToken cancellationToken = default)
    {
        var idResult = await GetSceneItemIdAsync(sceneName, sceneItemName, cancellationToken).ConfigureAwait(false);
        if (!idResult.Ok)
            return Result<bool>.Fail(idResult.Code, idResult.Message, idResult.Exception);

        return await SetVisibilityCoreAsync(sceneName, idResult.Value, visible, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<bool>> SwitchSceneAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return Fail<bool>(ResultCodes.InvalidArgument, "sceneName is required.");

        var sceneExists = await GetSceneExistsAsync(sceneName, cancellationToken).ConfigureAwait(false);
        if (!sceneExists.Ok)
            return Result<bool>.Fail(sceneExists.Code, sceneExists.Message, sceneExists.Exception);

        if (!sceneExists.Value)
            return Fail<bool>(ResultCodes.NotFound, $"Scene '{sceneName}' was not found.");

        try
        {
            await WithTimeout(ct => _obs.SetCurrentProgramSceneAsync(sceneName, ct), cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(true, $"Switched to scene '{sceneName}'.");
        }
        catch (OperationCanceledException ex)
        {
            return Fail<bool>(ResultCodes.Timeout, "SwitchScene timed out.", ex);
        }
        catch (Exception ex)
        {
            return Fail<bool>(ResultCodes.ObsError, "Failed to switch scene.", ex);
        }
    }

    private async Task<Result<bool>> SetVisibilityCoreAsync(string sceneName, int sceneItemId, bool visible, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return Fail<bool>(ResultCodes.InvalidArgument, "sceneName is required.");

        try
        {
            await WithTimeout(ct => _obs.SetSceneItemEnabledAsync(sceneName, sceneItemId, visible, ct), cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(true, "Visibility updated.");
        }
        catch (OperationCanceledException ex)
        {
            return Fail<bool>(ResultCodes.Timeout, "SetVisibility timed out.", ex);
        }
        catch (Exception ex)
        {
            return Fail<bool>(ResultCodes.ObsError, "Failed to set scene item visibility.", ex);
        }
    }

    private async Task<Result<bool>> GetSceneExistsAsync(string sceneName, CancellationToken cancellationToken)
    {
        var precheck = EnsureConnected<bool>();
        if (!precheck.Ok)
            return precheck;

        try
        {
            var names = await WithTimeout(ct => _obs.GetSceneNamesAsync(ct), cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(names.Contains(sceneName, StringComparer.Ordinal));
        }
        catch (OperationCanceledException ex)
        {
            return Fail<bool>(ResultCodes.Timeout, "Scene lookup timed out.", ex);
        }
        catch (Exception ex)
        {
            return Fail<bool>(ResultCodes.ObsError, "Failed to load scene list.", ex);
        }
    }

    private async Task<Result<IReadOnlyList<ObsSceneItemInfo>>> GetSceneItemsAsync(string sceneName, CancellationToken cancellationToken)
    {
        var precheck = EnsureConnected<IReadOnlyList<ObsSceneItemInfo>>();
        if (!precheck.Ok)
            return precheck;

        try
        {
            var items = await WithTimeout(ct => _obs.GetSceneItemListAsync(sceneName, ct), cancellationToken).ConfigureAwait(false);
            return Result<IReadOnlyList<ObsSceneItemInfo>>.Success(items);
        }
        catch (OperationCanceledException ex)
        {
            return Fail<IReadOnlyList<ObsSceneItemInfo>>(ResultCodes.Timeout, "GetSceneItemList timed out.", ex);
        }
        catch (Exception ex)
        {
            return Fail<IReadOnlyList<ObsSceneItemInfo>>(ResultCodes.ObsError, "Failed to load scene item list.", ex);
        }
    }

    private async Task<Result<CachedInput?>> GetCachedInputAsync(string inputName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputName))
            return Fail<CachedInput?>(ResultCodes.InvalidArgument, "inputName is required.");

        var precheck = EnsureConnected<CachedInput?>();
        if (!precheck.Ok)
            return precheck;

        var needsRefresh = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            needsRefresh = _inputs.Count == 0;
        }
        finally
        {
            _gate.Release();
        }

        if (needsRefresh)
        {
            var refresh = await RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (!refresh.Ok)
                return Result<CachedInput?>.Fail(refresh.Code, refresh.Message, refresh.Exception);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _inputs.TryGetValue(inputName, out var cachedInput);
            return Result<CachedInput?>.Success(cachedInput);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<CachedInput>> RequireInputAsync(string inputName, CancellationToken cancellationToken)
    {
        var input = await GetCachedInputAsync(inputName, cancellationToken).ConfigureAwait(false);
        if (!input.Ok)
            return Result<CachedInput>.Fail(input.Code, input.Message, input.Exception);

        if (input.Value is null)
            return Fail<CachedInput>(ResultCodes.NotFound, $"Input '{inputName}' was not found.");

        return Result<CachedInput>.Success(input.Value);
    }

    private Result<T> EnsureConnected<T>()
    {
        if (_obs.IsConnected)
            return Result<T>.Success(default);

        return Fail<T>(ResultCodes.NotConnected, "Not connected to OBS.");
    }

    private async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.DefaultTimeout);
        return await action(cts.Token).ConfigureAwait(false);
    }

    private async Task WithTimeout(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.DefaultTimeout);
        await action(cts.Token).ConfigureAwait(false);
    }

    private Result<T> Fail<T>(string code, string message, Exception? ex = null)
    {
        _logger.LogWarning(ex, "OBS operation failed: {Code} - {Message}", code, message);
        if (_options.StrictMode)
        {
            throw new InvalidOperationException($"{code}: {message}", ex);
        }

        return Result<T>.Fail(code, message, ex);
    }

    private sealed record CachedInput(string InputName, string InputKind, JObject? Settings);
}
