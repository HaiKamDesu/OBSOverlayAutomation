namespace TournamentAutomation.Infrastructure;

public interface IObsGateway
{
    Task<bool> IsConnectedAsync();
    Task<bool> ConnectAsync(CancellationToken cancellationToken);
    Task<bool> DisconnectAsync(CancellationToken cancellationToken);
    Task<bool> SwitchSceneAsync(string sceneName, CancellationToken cancellationToken);
    Task<string?> GetTextAsync(string inputName, CancellationToken cancellationToken);
    Task<string?> GetImageFileAsync(string inputName, CancellationToken cancellationToken);
    Task<bool> SetTextAsync(string inputName, string text, CancellationToken cancellationToken);
    Task<bool> SetImageFileAsync(string inputName, string filePath, CancellationToken cancellationToken);
    Task<bool> SetVisibilityAsync(string sceneName, string sceneItemName, bool visible, CancellationToken cancellationToken);
}
