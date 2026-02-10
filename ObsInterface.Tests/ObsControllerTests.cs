using Newtonsoft.Json.Linq;
using ObsInterface;

namespace ObsInterface.Tests;

public sealed class ObsControllerTests
{
    [Fact]
    public async Task SetTextAsync_ReturnsTypeMismatch_WhenTextKeyMissing()
    {
        var adapter = new FakeObsAdapter
        {
            IsConnectedState = true,
            Inputs = [new ObsInputInfo("Image Source", "image_source")]
        };
        adapter.InputSettings["Image Source"] = new JObject { ["file"] = "c:/image.png" };

        var controller = new ObsController(adapter, new ObsInterfaceOptions { StrictMode = false });

        var result = await controller.SetTextAsync("Image Source", "hello");

        Assert.False(result.Ok);
        Assert.Equal(ResultCodes.TypeMismatch, result.Code);
    }

    [Fact]
    public async Task GetInputKindAsync_ReturnsNotFound_WhenInputMissing()
    {
        var adapter = new FakeObsAdapter { IsConnectedState = true };
        var controller = new ObsController(adapter, new ObsInterfaceOptions { StrictMode = false });

        var result = await controller.GetInputKindAsync("Missing");

        Assert.False(result.Ok);
        Assert.Equal(ResultCodes.NotFound, result.Code);
    }

    [Fact]
    public async Task SetImageFileAsync_UsesFileKey_WhenSupported()
    {
        var adapter = new FakeObsAdapter
        {
            IsConnectedState = true,
            Inputs = [new ObsInputInfo("Logo", "image_source")]
        };
        adapter.InputSettings["Logo"] = new JObject { ["file"] = "old.png" };

        var controller = new ObsController(adapter, new ObsInterfaceOptions { StrictMode = false });
        var result = await controller.SetImageFileAsync("Logo", "new.png");

        Assert.True(result.Ok);
        Assert.Equal("new.png", adapter.LastSetSettings?["file"]?.ToString());
    }

    [Fact]
    public async Task StrictMode_Throws_OnFailure()
    {
        var adapter = new FakeObsAdapter { IsConnectedState = false };
        var controller = new ObsController(adapter, new ObsInterfaceOptions { StrictMode = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.GetInputKindAsync("Any"));
    }
}

internal sealed class FakeObsAdapter : IObsWebsocketAdapter
{
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<Exception>? Error;

    public bool IsConnected => IsConnectedState;
    public bool IsConnectedState { get; set; }
    public IReadOnlyList<ObsInputInfo> Inputs { get; set; } = [];
    public Dictionary<string, JObject> InputSettings { get; } = new(StringComparer.Ordinal);
    public IReadOnlyList<ObsSceneItemInfo> SceneItems { get; set; } = [];
    public IReadOnlyList<string> SceneNames { get; set; } = [];
    public JObject? LastSetSettings { get; private set; }

    public Task ConnectAsync(string url, string password, CancellationToken cancellationToken = default)
    {
        IsConnectedState = true;
        Connected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnectedState = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ObsInputInfo>> GetInputListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Inputs);

    public Task<JObject> GetInputSettingsAsync(string inputName, CancellationToken cancellationToken = default)
        => Task.FromResult(InputSettings[inputName]);

    public Task SetInputSettingsAsync(string inputName, JObject settings, bool overlay, CancellationToken cancellationToken = default)
    {
        LastSetSettings = settings;
        InputSettings[inputName] = settings;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ObsSceneItemInfo>> GetSceneItemListAsync(string sceneName, CancellationToken cancellationToken = default)
        => Task.FromResult(SceneItems);

    public Task SetSceneItemEnabledAsync(string sceneName, int sceneItemId, bool enabled, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetCurrentProgramSceneAsync(string sceneName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetSceneNamesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SceneNames);
}
