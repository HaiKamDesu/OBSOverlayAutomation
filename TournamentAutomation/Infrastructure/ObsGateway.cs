using ObsInterface;
using TournamentAutomation.Application.Logging;
using TournamentAutomation.Configuration;

namespace TournamentAutomation.Infrastructure;

public sealed class ObsGateway : IObsGateway
{
    private readonly ObsController _controller;
    private readonly ObsConnectionConfig _config;
    private readonly IAppLogger _logger;

    public ObsGateway(ObsController controller, ObsConnectionConfig config, IAppLogger logger)
    {
        _controller = controller;
        _config = config;
        _logger = logger;
    }

    public Task<bool> IsConnectedAsync() => Task.FromResult(_controller.IsConnected);

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_controller.IsConnected)
            return true;

        var result = await _controller.ConnectAndWaitAsync(
            _config.Url,
            _config.Password,
            TimeSpan.FromMilliseconds(_config.ConnectTimeoutMs),
            cancellationToken);

        if (!result.Ok)
            _logger.Error($"OBS connect failed: {result.Message}", result.Exception);

        return result.Ok;
    }

    public async Task<bool> DisconnectAsync(CancellationToken cancellationToken)
    {
        var result = await _controller.DisconnectAsync(cancellationToken);
        if (!result.Ok)
            _logger.Error($"OBS disconnect failed: {result.Message}", result.Exception);

        return result.Ok;
    }

    public async Task<bool> SwitchSceneAsync(string sceneName, CancellationToken cancellationToken)
    {
        var result = await _controller.SwitchSceneAsync(sceneName, cancellationToken);
        if (!result.Ok)
            _logger.Warn($"OBS: Scene switch failed: {result.Message}");

        return result.Ok;
    }

    public async Task<string?> GetTextAsync(string inputName, CancellationToken cancellationToken)
    {
        var result = await _controller.GetTextAsync(inputName, cancellationToken);
        if (!result.Ok)
        {
            _logger.Warn($"OBS: Text read failed for '{inputName}': {result.Message}");
            return null;
        }

        return result.Value;
    }

    public async Task<string?> GetImageFileAsync(string inputName, CancellationToken cancellationToken)
    {
        var result = await _controller.GetImageFileAsync(inputName, cancellationToken);
        if (!result.Ok)
        {
            _logger.Warn($"OBS: Image read failed for '{inputName}': {result.Message}");
            return null;
        }

        return result.Value;
    }

    public async Task<bool> SetTextAsync(string inputName, string text, CancellationToken cancellationToken)
    {
        var result = await _controller.SetTextAsync(inputName, text, cancellationToken);
        if (!result.Ok)
            _logger.Warn($"OBS: Text update failed for '{inputName}': {result.Message}");

        return result.Ok;
    }

    public async Task<bool> SetImageFileAsync(string inputName, string filePath, CancellationToken cancellationToken)
    {
        var result = await _controller.SetImageFileAsync(inputName, filePath, cancellationToken);
        if (!result.Ok)
            _logger.Warn($"OBS: Image update failed for '{inputName}': {result.Message}");

        return result.Ok;
    }

    public async Task<bool> SetMediaSourceAsync(string inputName, string source, CancellationToken cancellationToken)
    {
        var result = await _controller.SetMediaSourceAsync(inputName, source, cancellationToken);
        if (!result.Ok)
            _logger.Warn($"OBS: Media source update failed for '{inputName}': {result.Message}");

        return result.Ok;
    }

    public async Task<bool> SetVisibilityAsync(string sceneName, string sceneItemName, bool visible, CancellationToken cancellationToken)
    {
        var result = await _controller.SetVisibilityAsync(sceneName, sceneItemName, visible, cancellationToken);
        if (!result.Ok)
            _logger.Warn($"OBS: Visibility update failed: {result.Message}");

        return result.Ok;
    }

    public async Task<bool> GetInputExistsAsync(string inputName, CancellationToken cancellationToken)
    {
        var result = await _controller.GetInputExistsAsync(inputName, cancellationToken);
        if (!result.Ok)
        {
            _logger.Warn($"OBS: Input exists lookup failed for '{inputName}': {result.Message}");
            return false;
        }

        return result.Value;
    }

    public async Task<IReadOnlyList<string>> GetSceneNamesAsync(CancellationToken cancellationToken)
    {
        var result = await _controller.GetSceneNamesAsync(cancellationToken);
        if (!result.Ok)
        {
            _logger.Warn($"OBS: Scene list lookup failed: {result.Message}");
            return Array.Empty<string>();
        }

        return result.Value ?? Array.Empty<string>();
    }
}
