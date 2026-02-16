using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace ObsInterface;

public sealed class ObsWebsocketAdapter : IObsWebsocketAdapter
{
    private static readonly TimeSpan InitialHandshakeTimeout = TimeSpan.FromSeconds(20);
    private readonly ILogger<ObsWebsocketAdapter> _logger;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _pendingRequests = new(StringComparer.Ordinal);
    private readonly object _stateGate = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;
    private TaskCompletionSource<JObject>? _helloTcs;
    private TaskCompletionSource<JObject>? _identifiedTcs;
    private int _requestId;
    private bool _identified;
    private bool _disconnectRaised;
    private bool _firstReceiveLogged;

    public ObsWebsocketAdapter(ILogger<ObsWebsocketAdapter>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ObsWebsocketAdapter>.Instance;
    }

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<Exception>? Error;

    public bool IsConnected
    {
        get
        {
            lock (_stateGate)
            {
                return _identified && _socket is { State: WebSocketState.Open };
            }
        }
    }

    public async Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
                return;

            await DisconnectCoreAsync(raiseDisconnected: false, CancellationToken.None).ConfigureAwait(false);

            var socket = new ClientWebSocket();
            try
            {
                var wsUri = NormalizeWebSocketUri(url);
                socket.Options.Proxy = null;
                socket.Options.UseDefaultCredentials = false;
                await socket.ConnectAsync(wsUri, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("OBS websocket ConnectAsync completed. Uri={Uri} State={State}", wsUri, socket.State);
                AttachConnectedSocket(socket);
                await IdentifyAsync(password ?? string.Empty, cancellationToken).ConfigureAwait(false);
                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                socket.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to OBS websocket at {Url}.", url);
            Error?.Invoke(this, ex);
            await DisconnectCoreAsync(raiseDisconnected: false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private void AttachConnectedSocket(ClientWebSocket socket)
    {
        lock (_stateGate)
        {
            _socket = socket;
            _receiveLoopCts = new CancellationTokenSource();
            _helloTcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _identifiedTcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _identified = false;
            _disconnectRaised = false;
            _firstReceiveLogged = false;
            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_receiveLoopCts.Token));
        }
    }

    private async Task IdentifyAsync(string password, CancellationToken cancellationToken)
    {
        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeCts.CancelAfter(InitialHandshakeTimeout);

        var hello = await WaitForHandshakeMessageAsync(() => _helloTcs, "OBS hello", handshakeCts.Token).ConfigureAwait(false);
        int rpcVersion = hello["d"]?["rpcVersion"]?.Value<int>() ?? 1;

        string? auth = null;
        var authPayload = hello["d"]?["authentication"] as JObject;
        if (authPayload is not null)
        {
            string? challenge = authPayload["challenge"]?.Value<string>();
            string? salt = authPayload["salt"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(challenge) || string.IsNullOrWhiteSpace(salt))
                throw new InvalidOperationException("OBS hello authentication payload is incomplete.");

            auth = BuildIdentifyAuthentication(password, challenge, salt);
        }

        var identify = new JObject
        {
            ["op"] = 1,
            ["d"] = new JObject
            {
                ["rpcVersion"] = rpcVersion
            }
        };

        if (!string.IsNullOrWhiteSpace(auth))
            identify["d"]!["authentication"] = auth;

        await SendRawMessageAsync(identify, handshakeCts.Token).ConfigureAwait(false);

        var identified = await WaitForHandshakeMessageAsync(() => _identifiedTcs, "OBS identify", handshakeCts.Token).ConfigureAwait(false);
        if (identified["op"]?.Value<int>() != 2)
        {
            throw new InvalidOperationException("OBS identify failed: missing Identified response.");
        }

        lock (_stateGate)
            _identified = true;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return DisconnectCoreAsync(raiseDisconnected: true, cancellationToken);
    }

    public Task<IReadOnlyList<ObsInputInfo>> GetInputListAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteRequestAsync("GetInputList", null, cancellationToken, responseData =>
        {
            var inputEntries = responseData["inputs"] as JArray ?? new JArray();
            List<ObsInputInfo> result = new(inputEntries.Count);
            foreach (var token in inputEntries.OfType<JObject>())
            {
                string inputName = token["inputName"]?.Value<string>() ?? string.Empty;
                string inputKind = token["inputKind"]?.Value<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(inputName))
                    continue;

                result.Add(new ObsInputInfo(inputName, inputKind));
            }

            return (IReadOnlyList<ObsInputInfo>)result;
        });
    }

    public Task<JObject> GetInputSettingsAsync(string inputName, CancellationToken cancellationToken = default)
    {
        return ExecuteRequestAsync("GetInputSettings", new JObject { ["inputName"] = inputName }, cancellationToken, responseData =>
        {
            var settings = responseData["inputSettings"] as JObject;
            if (settings is null)
                throw new InvalidOperationException($"OBS response for '{inputName}' did not contain inputSettings.");

            return (JObject)settings.DeepClone();
        });
    }

    public Task SetInputSettingsAsync(string inputName, JObject settings, bool overlay, CancellationToken cancellationToken = default)
    {
        return ExecuteRequestAsync("SetInputSettings", new JObject
        {
            ["inputName"] = inputName,
            ["inputSettings"] = settings,
            ["overlay"] = overlay
        }, cancellationToken, _ => 0);
    }

    public Task<IReadOnlyList<ObsSceneItemInfo>> GetSceneItemListAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        return ExecuteRequestAsync("GetSceneItemList", new JObject { ["sceneName"] = sceneName }, cancellationToken, responseData =>
        {
            var sceneItems = responseData["sceneItems"] as JArray ?? new JArray();
            List<ObsSceneItemInfo> result = new(sceneItems.Count);
            foreach (var token in sceneItems.OfType<JObject>())
            {
                int? sceneItemId = token["sceneItemId"]?.Value<int?>();
                if (!sceneItemId.HasValue)
                    continue;

                string sourceName = token["sourceName"]?.Value<string>() ?? string.Empty;
                bool sceneItemEnabled = token["sceneItemEnabled"]?.Value<bool?>() ?? false;
                result.Add(new ObsSceneItemInfo(sceneItemId.Value, sourceName, sceneItemEnabled));
            }

            return (IReadOnlyList<ObsSceneItemInfo>)result;
        });
    }

    public Task SetSceneItemEnabledAsync(string sceneName, int sceneItemId, bool enabled, CancellationToken cancellationToken = default)
    {
        return ExecuteRequestAsync("SetSceneItemEnabled", new JObject
        {
            ["sceneName"] = sceneName,
            ["sceneItemId"] = sceneItemId,
            ["sceneItemEnabled"] = enabled
        }, cancellationToken, _ => 0);
    }

    public Task SetCurrentProgramSceneAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        return ExecuteRequestAsync("SetCurrentProgramScene", new JObject
        {
            ["sceneName"] = sceneName
        }, cancellationToken, _ => 0);
    }

    public Task<IReadOnlyList<string>> GetSceneNamesAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteRequestAsync("GetSceneList", null, cancellationToken, responseData =>
        {
            var scenes = responseData["scenes"] as JArray ?? new JArray();
            List<string> result = new(scenes.Count);
            foreach (var token in scenes.OfType<JObject>())
            {
                string sceneName = token["sceneName"]?.Value<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(sceneName))
                    result.Add(sceneName);
            }

            return (IReadOnlyList<string>)result;
        });
    }

    private async Task<T> ExecuteRequestAsync<T>(string requestType, JObject? requestData, CancellationToken cancellationToken, Func<JObject, T> projector)
    {
        EnsureConnected();

        string requestId = Interlocked.Increment(ref _requestId).ToString(CultureInfo.InvariantCulture);
        var pending = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(requestId, pending))
            throw new InvalidOperationException("Could not register OBS request.");

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (_pendingRequests.TryRemove(requestId, out var tcs))
                tcs.TrySetCanceled(cancellationToken);
        });

        var message = new JObject
        {
            ["op"] = 6,
            ["d"] = new JObject
            {
                ["requestType"] = requestType,
                ["requestId"] = requestId
            }
        };

        if (requestData is not null)
            message["d"]!["requestData"] = requestData;

        await SendRawMessageAsync(message, cancellationToken).ConfigureAwait(false);

        JObject responseEnvelope;
        try
        {
            responseEnvelope = await pending.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }

        var payload = responseEnvelope["d"] as JObject
            ?? throw new InvalidOperationException("OBS request response did not include a data payload.");
        var status = payload["requestStatus"] as JObject
            ?? throw new InvalidOperationException("OBS request response did not include requestStatus.");
        bool ok = status["result"]?.Value<bool?>() ?? false;
        if (!ok)
        {
            int code = status["code"]?.Value<int?>() ?? -1;
            string comment = status["comment"]?.Value<string>() ?? "Unknown OBS error.";
            throw new InvalidOperationException($"{requestType} failed ({code}): {comment}");
        }

        var responseData = payload["responseData"] as JObject ?? new JObject();
        return projector(responseData);
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("OBS websocket is not connected.");
    }

    private async Task SendRawMessageAsync(JObject message, CancellationToken cancellationToken)
    {
        ClientWebSocket? socket;
        lock (_stateGate)
            socket = _socket;

        if (socket is null || socket.State != WebSocketState.Open)
            throw new InvalidOperationException("OBS websocket is not connected.");

        string payload = message.ToString(Newtonsoft.Json.Formatting.None);
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<JObject> WaitForHandshakeMessageAsync(Func<TaskCompletionSource<JObject>?> tcsSelector, string name, CancellationToken cancellationToken)
    {
        TaskCompletionSource<JObject>? tcs;
        lock (_stateGate)
            tcs = tcsSelector();

        if (tcs is null)
            throw new InvalidOperationException($"OBS {name} waiter was not initialized.");

        using var cancellationRegistration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        ClientWebSocket? socket;
        lock (_stateGate)
            socket = _socket;

        if (socket is null)
            return;

        var buffer = new byte[16 * 1024];
        using var messageBuffer = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var json = await ReceiveTextMessageAsync(socket, buffer, messageBuffer, cancellationToken).ConfigureAwait(false);
                if (json is null)
                    continue;

                HandleReceivedMessage(json);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OBS receive loop failed.");
            Error?.Invoke(this, ex);
            await DisconnectCoreAsync(raiseDisconnected: true, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<string?> ReceiveTextMessageAsync(
        ClientWebSocket socket,
        byte[] buffer,
        MemoryStream messageBuffer,
        CancellationToken cancellationToken)
    {
        messageBuffer.SetLength(0);
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (!_firstReceiveLogged)
            {
                _firstReceiveLogged = true;
                _logger.LogInformation(
                    "OBS first receive frame. MessageType={MessageType} Count={Count} EndOfMessage={EndOfMessage}",
                    result.MessageType,
                    result.Count,
                    result.EndOfMessage);
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogWarning(
                    "OBS websocket close frame received. Status={Status} Description={Description}",
                    result.CloseStatus?.ToString() ?? "None",
                    result.CloseStatusDescription ?? string.Empty);
                throw new InvalidOperationException(
                    $"OBS websocket closed during receive. Status={result.CloseStatus}; Description={result.CloseStatusDescription ?? string.Empty}");
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                if (result.EndOfMessage)
                    return null;
                continue;
            }

            messageBuffer.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
        }
    }

    private static Uri NormalizeWebSocketUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Invalid OBS websocket URL '{url}'.");

        if (!string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OBS websocket URL must use ws:// or wss://.");

        var builder = new UriBuilder(uri);
        if (string.IsNullOrEmpty(builder.Path) || builder.Path == "/")
            builder.Path = "/";

        return builder.Uri;
    }

    private void HandleReceivedMessage(string json)
    {
        JObject envelope = JObject.Parse(json);
        int op = envelope["op"]?.Value<int?>() ?? -1;
        switch (op)
        {
            case 0:
                _helloTcs?.TrySetResult(envelope);
                break;
            case 2:
                _identifiedTcs?.TrySetResult(envelope);
                break;
            case 7:
            {
                var requestId = envelope["d"]?["requestId"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(requestId) && _pendingRequests.TryGetValue(requestId, out var pending))
                    pending.TrySetResult(envelope);
                break;
            }
            default:
                break;
        }
    }

    private async Task DisconnectCoreAsync(bool raiseDisconnected, CancellationToken cancellationToken)
    {
        ClientWebSocket? socket;
        CancellationTokenSource? receiveLoopCts;
        Task? receiveLoopTask;

        lock (_stateGate)
        {
            socket = _socket;
            receiveLoopCts = _receiveLoopCts;
            receiveLoopTask = _receiveLoopTask;
            _socket = null;
            _receiveLoopCts = null;
            _receiveLoopTask = null;
            _helloTcs?.TrySetException(new InvalidOperationException("OBS websocket disconnected."));
            _identifiedTcs?.TrySetException(new InvalidOperationException("OBS websocket disconnected."));
            _helloTcs = null;
            _identifiedTcs = null;
            _identified = false;
        }

        foreach (var pending in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(pending.Key, out var tcs))
                tcs.TrySetException(new InvalidOperationException("OBS websocket disconnected before request completed."));
        }

        receiveLoopCts?.Cancel();
        if (socket is not null)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            socket.Dispose();
        }

        _ = receiveLoopTask;

        if (raiseDisconnected)
        {
            bool shouldRaise;
            lock (_stateGate)
            {
                shouldRaise = !_disconnectRaised;
                if (shouldRaise)
                    _disconnectRaised = true;
            }

            if (shouldRaise)
                Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string BuildIdentifyAuthentication(string password, string challenge, string salt)
    {
        static string Sha256Base64(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToBase64String(hash);
        }

        string secret = Sha256Base64(password + salt);
        return Sha256Base64(secret + challenge);
    }
}
